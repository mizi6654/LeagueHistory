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
        private int _matchHistoryCount = 20;

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

            //Debug.WriteLine("[MatchQueryProcessor] PlayerCardManager 已注入，Factory 初始化完成");
        }

        public void SetFilterMode(bool filterByGameMode)
        {
            _filterByGameMode = filterByGameMode;
            //Debug.WriteLine($"[MatchQueryProcessor] 筛选模式设置为: {filterByGameMode}");
        }

        public void SetMatchHistoryCount(int count)
        {
            _matchHistoryCount = count is 10 or 20 or 30 or 50 ? count : 20;
            //Debug.WriteLine($"[MatchQueryProcessor] 战绩场数设置为: {_matchHistoryCount}");
        }

        /// <summary>
        /// 是否为可用来查战绩/名片的真实 puuid（排除空、全0、混淆前缀）
        /// </summary>
        internal static bool IsUsablePuuid(string puuid)
        {
            if (string.IsNullOrWhiteSpace(puuid)) return false;
            if (puuid.Length < 10) return false;
            if (string.Equals(puuid, "00000000-0000-0000-0000-000000000000", StringComparison.OrdinalIgnoreCase))
                return false;
            if (puuid.StartsWith("OBFUSCATED_", StringComparison.OrdinalIgnoreCase)) return false;
            if (puuid.StartsWith("HIDDEN_", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary>
        /// 安全查询：只有「没有任何可用身份」才返回隐藏卡。
        /// 有真 puuid 时，即使 nameVisibility=HIDDEN 也继续查（进游戏补全场景）。
        /// </summary>
        public async Task<PlayerMatchInfo> SafeFetchPlayerMatchInfoAsync(JToken playerData, int retryTimes = 2)
        {
            EnsureFactoryInitialized();

            long sid = playerData["summonerId"]?.Value<long>() ?? 0;
            string puuid = playerData["puuid"]?.ToString() ?? "";
            int cid = playerData["championId"]?.Value<int>() ?? 0;
            string nameVisibility = playerData["nameVisibilityType"]?.ToString() ?? "UNHIDDEN";

            // 真正无法查询：无可用 puuid 且 summonerId 无效
            if (!IsUsablePuuid(puuid) && sid == 0)
            {
                Debug.WriteLine(
                    $"[HiddenPlayer] 无法解析 vis={nameVisibility}, sid={sid}, puuid无效 → 标记隐藏");
                return _factory.CreateHiddenPlayerInfo(sid, cid);
            }

            if (string.Equals(nameVisibility, "HIDDEN", StringComparison.OrdinalIgnoreCase)
                && IsUsablePuuid(puuid))
            {
                Debug.WriteLine(
                    $"[HiddenPlayer] vis=HIDDEN 但有可用 puuid={puuid.Substring(0, 8)}... → 继续查询");
            }

            // summonerId 为 0，但有可用 puuid → 用 puuid 兜底
            if (sid == 0 && IsUsablePuuid(puuid))
            {
                Debug.WriteLine($"[Puuid兜底] summonerId=0，使用 puuid 查询: {puuid.Substring(0, 8)}...");
                var tempData = new JObject
                {
                    ["puuid"] = puuid,
                    ["championId"] = cid,
                    ["summonerId"] = 0,
                    ["nameVisibilityType"] = "UNHIDDEN"
                };
                return await FetchPlayerMatchInfoAsync(tempData);
            }

            for (int attempt = 1; attempt <= retryTimes + 1; attempt++)
            {
                try
                {
                    var info = await FetchPlayerMatchInfoAsync(playerData);
                    if (info?.Player != null &&
                        (IsUsablePuuid(info.Player.Puuid) || info.Player.SummonerId != 0))
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

            string puuidHint = IsUsablePuuid(puuid) ? puuid.Substring(0, 8) : "无";
            Debug.WriteLine($"[SafeFetch] 多次尝试后仍失败 → SID:{sid} Puuid:{puuidHint}...");
            return _factory.CreateFailedPlayerInfo(sid, cid);
        }

        /// <summary>
        /// 拉取单名玩家名片+战绩。
        /// 判定隐藏只看「有没有可用身份」，不再把 HIDDEN / sid==0 单独判死。
        /// </summary>
        public async Task<PlayerMatchInfo> FetchPlayerMatchInfoAsync(JToken playerData)
        {
            if (playerData == null) throw new ArgumentNullException(nameof(playerData));

            EnsureFactoryInitialized();

            long summonerId = playerData["summonerId"]?.Value<long>() ?? 0;
            string puuid = playerData["puuid"]?.ToString() ?? "";
            int championId = playerData["championId"]?.Value<int>() ?? 0;

            // 仅当既没有可用 puuid、也没有 summonerId 时，才当隐藏
            if (!IsUsablePuuid(puuid) && summonerId == 0)
                return _factory.CreateHiddenPlayerInfo(summonerId, championId);

            // 缓存（仅 sid 有效时）
            if (summonerId != 0 && _cacheManager.TryGetPlayerMatch(summonerId, out var cached))
            {
                cached.Player.ChampionId = championId;
                cached.Player.ChampionName = Globals.resLoading.GetChampionById(championId)?.Name ?? "Unknown";
                cached.Player.Avatar = await Globals.resLoading.GetChampionIconAsync(championId);
                return cached;
            }

            // 有 summonerId 时走原接口取名字；仅有 puuid 时 name 可能暂时为「未知玩家」
            //var summonerInfo = summonerId != 0
            //    ? await _fetcher.GetGameNameBySummonerIdAsync(summonerId.ToString())
            //    : null;

            JObject summonerInfo = null;

            if (summonerId != 0)
            {
                summonerInfo = await _fetcher.GetGameNameBySummonerIdAsync(summonerId.ToString());
            }
            else if (IsUsablePuuid(puuid))
            {
                // SID=0 但有真 puuid（Ensure 补进来的隐藏位）
                summonerInfo = await _fetcher.GetGameNameByPuuidAsync(puuid);
            }

            string gameName = summonerInfo?["gameName"]?.ToString() ?? "未知玩家";
            string tagLine = summonerInfo?["tagLine"]?.ToString() ?? "";
            int summonerLevel = summonerInfo?["summonerLevel"]?.Value<int>() ?? 0;
            string privacyStatus = summonerInfo?["privacy"]?.ToString().Equals("PUBLIC", StringComparison.OrdinalIgnoreCase) == true ? "公开" : "隐藏";

            if (summonerInfo != null)
            {
                long sidFromApi = summonerInfo["summonerId"]?.Value<long>() ?? 0;
                if (sidFromApi != 0) summonerId = sidFromApi;
            }

            // 接口若返回更好的 puuid 则采用
            string fromApi = summonerInfo?["puuid"]?.ToString();
            if (IsUsablePuuid(fromApi))
                puuid = fromApi;

            string fullName = string.IsNullOrEmpty(tagLine) ? gameName : $"{gameName}#{tagLine}";

            if (!IsUsablePuuid(puuid))
                return _factory.CreateHiddenPlayerInfo(0, championId);

            // 排位 + 熟练度（并行）
            var rankedTask = Globals.lcuClient.GetCurrentRankedStatsAsync(puuid);
            var masteryTask = Globals.lcuClient.GetTopChampionMasteryAsync(puuid, 8);
            await Task.WhenAll(rankedTask, masteryTask);

            var rankedJson = await rankedTask;
            var topMasteries = await masteryTask;

            var rankedStats = RankedStats.FromJson(rankedJson) ?? new Dictionary<string, RankedStats>();
            string soloRank = GetFormattedRank(rankedStats, "单双排");
            string flexRank = GetFormattedRank(rankedStats, "灵活组排");

            // 战绩
            var matchesJson = await _fetcher.GetPlayerMatchesAsync(puuid, _filterByGameMode, _matchHistoryCount);
            var result = _parser.ParsePlayerMatchInfo(puuid, matchesJson);

            // 若仍未知，且有 sid，再按 sid 重试一次（你原来的兜底可保留）
            // 若仍未知，且有 puuid，再按 puuid 重试一次
            if ((string.IsNullOrEmpty(gameName) || gameName == "未知玩家" || gameName.Contains("未知"))
                && IsUsablePuuid(puuid))
            {
                var retryInfo = await _fetcher.GetGameNameByPuuidAsync(puuid);
                string retryName = retryInfo?["gameName"]?.ToString();
                if (!string.IsNullOrEmpty(retryName) && retryName != "未知玩家")
                {
                    gameName = retryName;
                    tagLine = retryInfo?["tagLine"]?.ToString() ?? "";
                    fullName = string.IsNullOrEmpty(tagLine) ? gameName : $"{gameName}#{tagLine}";
                    // 顺便把 sid 补上（若接口返回了）
                    long sidFromApi = retryInfo?["summonerId"]?.Value<long>() ?? 0;
                    if (sidFromApi != 0) summonerId = sidFromApi;
                    // privacy / level 同理
                }
            }

            result.Player = new PlayerInfo
            {
                Puuid = puuid,
                SummonerId = summonerId,
                ChampionId = championId,
                ChampionName = Globals.resLoading.GetChampionById(championId)?.Name ?? "Unknown",
                Avatar = await Globals.resLoading.GetChampionIconAsync(championId),
                GameName = gameName,
                FullName = fullName,
                SoloRank = soloRank,
                FlexRank = flexRank,
                IsPublic = privacyStatus,
                TopMasteries = topMasteries,
                SummonerLevel = summonerLevel
            };

            // 行为标签（压力怪等）
            CalculatePlayerBehavior(result);

            // 仅 sid 有效时写入缓存，避免 key=0 污染
            if (summonerId != 0)
                _cacheManager.CachePlayerMatch(summonerId, result);

            return result;
        }

        private void CalculatePlayerBehavior(PlayerMatchInfo info)
        {
            if (info?.RecentMatches == null || info.RecentMatches.Count == 0)
                return;

            int surrenderCount = 0;
            int totalMissing = 0;
            int totalVision = 0;
            int totalGetBack = 0;
            int totalDanger = 0;
            int shortCount = 0;

            foreach (var m in info.RecentMatches)
            {
                if (m.GameEndedInSurrender || m.GameEndedInEarlySurrender)
                    surrenderCount++;

                if (m.GameDurationMinutes < 18)
                    shortCount++;

                totalMissing += m.EnemyMissingPings;
                totalVision += m.EnemyVisionPings;
                totalGetBack += m.GetBackPings;
                totalDanger += m.DangerPings;
            }

            int count = info.RecentMatches.Count;
            var b = info.Behavior;

            b.SurrenderRate = Math.Round(surrenderCount * 100.0 / count, 1);

            // 平均值（判定标签用）
            b.AvgMissingPings = totalMissing / (double)count;
            b.AvgVisionPings = totalVision / (double)count;
            b.AvgGetBackPings = totalGetBack / (double)count;
            b.AvgDangerPings = totalDanger / (double)count;

            // 总次数（有特征时才显示）
            b.TotalMissingPings = totalMissing;
            b.TotalVisionPings = totalVision;
            b.TotalGetBackPings = totalGetBack;
            b.TotalDangerPings = totalDanger;

            b.ShortGameCount = shortCount;

            string currentQueueId = Globals.CurrGameMod ?? "";
            b.BehaviorSummary = GenerateBehaviorSummary(b, currentQueueId);
        }

        private string GenerateBehaviorSummary(PlayerBehavior b, string queueIdStr = "")
        {
            var tags = new List<string>();

            // 投降
            if (b.SurrenderRate >= 35)
                tags.Add("爱投降");
            else if (b.SurrenderRate >= 22)
                tags.Add("容易投降");

            // 单个信号
            if (b.AvgMissingPings >= 2.6) tags.Add("爱发问号");
            if (b.AvgVisionPings >= 2.2) tags.Add("爱发提醒");
            if (b.AvgGetBackPings >= 3.0) tags.Add("爱发撤退");
            if (b.AvgDangerPings >= 2.2) tags.Add("爱发危险");

            double total = b.AvgMissingPings + b.AvgVisionPings + b.AvgGetBackPings + b.AvgDangerPings;

            // 综合标签
            if (total >= 8.0)
                tags.Add("心态容易崩");

            if (total >= 10.5 || b.AvgGetBackPings >= 3.8)
                tags.Add("玻璃心");

            if (b.AvgGetBackPings >= 4.2 && total >= 10.0)
                tags.Add("爱抱怨");

            if (total >= 12.5)
                tags.Add("压力怪");

            if (total >= 14.5 || b.AvgMissingPings >= 4.2 || b.AvgGetBackPings >= 4.8)
                tags.Add("急性子");

            var uniqueTags = tags.Distinct().ToList();
            // 不带 []，外面消息自己加
            return uniqueTags.Count > 0 ? string.Join("、", uniqueTags) : "";
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