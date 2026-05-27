using League.Managers;
using League.Models;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using static League.FormMain;

namespace League.Parsers
{
    public class MatchQueryProcessor
    {
        // 静态图片缓存（保持原有行为）
        internal static readonly ConcurrentDictionary<string, Image> _imageCache = new();

        // 服务依赖（设为 readonly）
        private readonly PlayerMatchDataFetcher _fetcher;
        private readonly MatchDataParser _parser;
        private readonly PlayerMatchCacheManager _cacheManager;

        // PlayerInfoFactory 不设为 readonly，因为需要延迟初始化
        private PlayerInfoFactory _factory;

        private PlayerCardManager _playerCardManager;
        private bool _filterByGameMode = false;

        public MatchQueryProcessor()
        {
            _fetcher = new PlayerMatchDataFetcher();
            _parser = new MatchDataParser();
            _cacheManager = new PlayerMatchCacheManager();

            // 构造函数中暂不创建 Factory，等 SetPlayerCardManager 时再初始化
        }

        /// <summary>
        /// 必须在外部调用此方法注入 PlayerCardManager
        /// </summary>
        internal void SetPlayerCardManager(PlayerCardManager manager)
        {
            _playerCardManager = manager ?? throw new ArgumentNullException(nameof(manager));

            // 在此处初始化 Factory
            _factory = new PlayerInfoFactory(_playerCardManager);

            Debug.WriteLine("[MatchQueryProcessor] PlayerCardManager 已注入，Factory 初始化完成");
        }

        public void SetFilterMode(bool filterByGameMode)
        {
            _filterByGameMode = filterByGameMode;
            Debug.WriteLine($"[MatchQueryProcessor] 筛选模式设置为: {filterByGameMode}");
        }

        //public async Task<PlayerMatchInfo> SafeFetchPlayerMatchInfoAsync(JToken playerData, int retryTimes = 2)
        //{
        //    long sid = playerData["summonerId"]?.Value<long>() ?? 0;
        //    int cid = playerData["championId"]?.Value<int>() ?? 0;
        //    string nameVisibility = playerData["nameVisibilityType"]?.ToString() ?? "UNHIDDEN";

        //    if (nameVisibility == "HIDDEN" || sid == 0)
        //    {
        //        EnsureFactoryInitialized();
        //        return _factory.CreateHiddenPlayerInfo(sid, cid);
        //    }

        //    for (int attempt = 1; attempt <= retryTimes + 1; attempt++)
        //    {
        //        try
        //        {
        //            var info = await FetchPlayerMatchInfoAsync(playerData);
        //            if (info?.Player?.SummonerId != 0)
        //                return info;
        //        }
        //        catch (Exception ex)
        //        {
        //            Debug.WriteLine($"[Fetch失败] 第 {attempt} 次尝试失败: {ex.Message}");
        //            if (attempt <= retryTimes)
        //                await Task.Delay(1000);
        //        }
        //    }

        //    EnsureFactoryInitialized();
        //    return _factory.CreateFailedPlayerInfo(sid, cid);
        //}

        public async Task<PlayerMatchInfo> SafeFetchPlayerMatchInfoAsync(JToken playerData, int retryTimes = 2)
        {
            long sid = playerData["summonerId"]?.Value<long>() ?? 0;
            string puuid = playerData["puuid"]?.ToString() ?? "";
            int cid = playerData["championId"]?.Value<int>() ?? 0;

            string nameVisibility = playerData["nameVisibilityType"]?.ToString() ?? "UNHIDDEN";

            // 🔥 真正隐藏的玩家才直接返回 Hidden
            if (nameVisibility == "HIDDEN")
            {
                Debug.WriteLine($"[HiddenPlayer] nameVisibility=HIDDEN → 标记为隐藏");
                return _factory.CreateHiddenPlayerInfo(sid, cid);
            }

            // summonerId 为0，但有 puuid → 数据不完整，使用 puuid 兜底查询
            if (sid == 0 && !string.IsNullOrEmpty(puuid))
            {
                Debug.WriteLine($"[Puuid兜底] summonerId=0 但有 puuid，使用 puuid 查询: {puuid.Substring(0, 8)}...");

                var tempData = new JObject
                {
                    ["puuid"] = puuid,
                    ["championId"] = cid,
                    ["summonerId"] = 0
                };

                return await FetchPlayerMatchInfoAsync(tempData);
            }

            // 正常情况
            for (int attempt = 1; attempt <= retryTimes + 1; attempt++)
            {
                try
                {
                    var info = await FetchPlayerMatchInfoAsync(playerData);
                    if (info?.Player != null &&
                        (!string.IsNullOrEmpty(info.Player.Puuid) || info.Player.SummonerId != 0))
                    {
                        return info;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SafeFetch] 第{attempt}次失败: {ex.Message}");
                    if (attempt <= retryTimes)
                        await Task.Delay(800);
                }
            }

            // 最终失败
            Debug.WriteLine($"[SafeFetch] 多次尝试后仍失败 → SID:{sid} Puuid:{puuid.Substring(0, 8)}...");
            return _factory.CreateFailedPlayerInfo(sid, cid);
        }

        public async Task<PlayerMatchInfo> FetchPlayerMatchInfoAsync(JToken playerData)
        {
            if (playerData == null) throw new ArgumentNullException(nameof(playerData));

            EnsureFactoryInitialized(); // 确保 Factory 已初始化

            long summonerId = playerData["summonerId"]?.Value<long>() ?? 0;
            string puuid = playerData["puuid"]?.ToString() ?? "";
            int championId = playerData["championId"]?.Value<int>() ?? 0;

            string nameVisibility = playerData["nameVisibilityType"]?.ToString() ?? "UNHIDDEN";
            bool isHidden = nameVisibility == "HIDDEN" ||
                           string.IsNullOrEmpty(puuid) ||
                           summonerId == 0 ||
                           puuid.StartsWith("OBFUSCATED_") ||
                           puuid.StartsWith("HIDDEN_");

            if (isHidden)
                return _factory.CreateHiddenPlayerInfo(summonerId, championId);

            // 缓存检查
            if (summonerId != 0 && _cacheManager.TryGetPlayerMatch(summonerId, out var cached))
            {
                cached.Player.ChampionId = championId;
                cached.Player.ChampionName = Globals.resLoading.GetChampionById(championId)?.Name ?? "Unknown";
                cached.Player.Avatar = await Globals.resLoading.GetChampionIconAsync(championId);
                return cached;
            }

            // 正常查询流程...
            var summonerInfo = summonerId != 0
                ? await _fetcher.GetGameNameBySummonerIdAsync(summonerId.ToString())
                : null;

            string gameName = summonerInfo?["gameName"]?.ToString() ?? "未知玩家";
            string tagLine = summonerInfo?["tagLine"]?.ToString() ?? "";
            string privacyStatus = summonerInfo?["privacy"]?.ToString().Equals("PUBLIC", StringComparison.OrdinalIgnoreCase) == true
                ? "公开" : "隐藏";
            puuid = summonerInfo?["puuid"]?.ToString() ?? puuid;

            if (string.IsNullOrEmpty(puuid) || puuid.Length < 10)
            {
                return _factory.CreateHiddenPlayerInfo(0, championId);
            }

            var rankedStats = await _fetcher.GetRankedStatsAsync(puuid);
            string soloRank = GetFormattedRank(rankedStats, "单双排");
            string flexRank = GetFormattedRank(rankedStats, "灵活组排");

            var matchesJson = await _fetcher.GetPlayerMatchesAsync(puuid, _filterByGameMode);
            var result = _parser.ParsePlayerMatchInfo(puuid, matchesJson);

            result.Player = new PlayerInfo
            {
                Puuid = puuid,
                SummonerId = summonerId,
                ChampionId = championId,
                ChampionName = Globals.resLoading.GetChampionById(championId)?.Name ?? "Unknown",
                Avatar = await Globals.resLoading.GetChampionIconAsync(championId),
                GameName = string.IsNullOrEmpty(tagLine) ? gameName : gameName,
                SoloRank = soloRank,
                FlexRank = flexRank,
                IsPublic = privacyStatus
            };

            _cacheManager.CachePlayerMatch(summonerId, result);
            return result;
        }

        /// <summary>
        /// 确保 Factory 已初始化，防止空引用
        /// </summary>
        private void EnsureFactoryInitialized()
        {
            if (_factory == null)
            {
                if (_playerCardManager == null)
                    throw new InvalidOperationException("PlayerCardManager 未注入，请先调用 SetPlayerCardManager 方法");

                _factory = new PlayerInfoFactory(_playerCardManager);
            }
        }

        private string GetFormattedRank(Dictionary<string, RankedStats> rankedStats, string queueType)
        {
            if (rankedStats?.TryGetValue(queueType, out var stats) == true)
            {
                return $"{stats.FormattedTier}({stats.LeaguePoints})";
            }
            return "未知";
        }

        public void ClearPlayerMatchCache()
        {
            _cacheManager.ClearCache();
        }
    }
}