using League.Managers;
using League.Models;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using static League.FormMain;

namespace League.Parsers
{
    /// <summary>
    /// 处理玩家战绩查询、解析和缓存管理
    /// </summary>
    public class MatchQueryProcessor
    {
        private PlayerCardManager _playerCardManager;  // 去掉 readonly，因为要后期赋值

        // 缓存相关
        private readonly Dictionary<long, PlayerMatchInfo> _playerMatchCache = new();
        private static readonly ConcurrentDictionary<string, Image> _imageCache = new();
        private bool _filterByGameMode = false; // 默认不过滤（查询所有）

        // 新增：让 PlayerCardManager 能反向注入自己
        internal void SetPlayerCardManager(PlayerCardManager manager)
        {
            _playerCardManager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        // 设置筛选模式
        public void SetFilterMode(bool filterByGameMode)
        {
            _filterByGameMode = filterByGameMode;
            Debug.WriteLine($"[MatchQueryProcessor] 筛选模式设置为: {filterByGameMode}");
        }

        #region 战绩查询方法
        /// <summary>
        /// 安全获取玩家战绩信息（带重试机制）
        /// </summary>
        public async Task<PlayerMatchInfo> SafeFetchPlayerMatchInfoAsync(JToken playerData, int retryTimes = 2)
        {
            long sid = playerData["summonerId"]?.Value<long>() ?? 0;
            int cid = playerData["championId"]?.Value<int>() ?? 0;

            for (int attempt = 1; attempt <= retryTimes + 1; attempt++)
            {
                try
                {
                    var info = await FetchPlayerMatchInfoAsync(playerData);
                    if (info != null && info.Player.SummonerId != 0)
                    {
                        return info;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Fetch失败] 第 {attempt} 次尝试失败: {ex.Message}");
                    if (attempt <= retryTimes)
                        await Task.Delay(1000);
                }
            }

            Debug.WriteLine($"[Fetch彻底失败] summonerId={sid}，显示查询失败卡片");

            // 安全检查：如果 _playerCardManager 还没注入（极少情况），直接创建简单失败对象
            if (_playerCardManager == null)
            {
                Debug.WriteLine("[警告] _playerCardManager 为 null，使用备用失败占位");
                return new PlayerMatchInfo
                {
                    Player = new PlayerInfo
                    {
                        SummonerId = sid,
                        ChampionId = cid,
                        ChampionName = "查询失败",
                        GameName = "失败",
                        IsPublic = "[失败]",
                        SoloRank = "失败",
                        FlexRank = "失败",
                        Avatar = Image.FromFile(AppDomain.CurrentDomain.BaseDirectory + "Assets\\Defaults\\Profile.png")
                    },
                    MatchItems = new List<ListViewItem>(),
                    HeroIcons = new ImageList()
                };
            }

            return _playerCardManager.CreateFailedPlayerInfo(sid, cid);
        }

        /// <summary>
        /// 获取玩家战绩信息（核心方法） - 新增对隐藏玩家（obfuscated）的判断
        /// </summary>
        public async Task<PlayerMatchInfo> FetchPlayerMatchInfoAsync(JToken playerData)
        {
            if (playerData == null)
                throw new ArgumentNullException(nameof(playerData));

            long summonerId = playerData["summonerId"]?.Value<long>() ?? 0;
            string nameVisibility = playerData["nameVisibilityType"]?.ToString() ?? "UNHIDDEN";
            string puuid = playerData["puuid"]?.ToString() ?? "";
            int championId = playerData["championId"]?.Value<int>() ?? 0;

            // 隐藏玩家直接返回
            if (nameVisibility == "HIDDEN" || summonerId == 0)
            {
                Debug.WriteLine($"[隐藏玩家] 检测到隐藏玩家，跳过查询 (championId={championId})");
                return new PlayerMatchInfo
                {
                    Player = new PlayerInfo
                    {
                        SummonerId = 0,
                        ChampionId = championId,
                        ChampionName = GetChampionName(championId),
                        Avatar = await GetChampionIconAsync(championId),  // 这里可以直接 await，因为外层是 async
                        GameName = "隐藏玩家",
                        SoloRank = "隐藏",
                        FlexRank = "隐藏",
                        IsPublic = "隐藏"
                    },
                    MatchItems = new List<ListViewItem>(),
                    HeroIcons = new ImageList()
                };
            }

            // 缓存命中
            if (_playerMatchCache.TryGetValue(summonerId, out var cached))
            {
                cached.Player.ChampionId = championId;
                cached.Player.ChampionName = GetChampionName(championId);
                cached.Player.Avatar = await GetChampionIconAsync(championId);
                return cached;
            }

            // 正常玩家逻辑...
            string gameName = "未知玩家";
            string tagLine = "";
            string privacyStatus = "隐藏";

            var summonerInfo = await Globals.lcuClient.GetGameNameBySummonerId(summonerId.ToString());
            if (summonerInfo != null)
            {
                gameName = summonerInfo["gameName"]?.ToString() ?? "未知玩家";
                tagLine = summonerInfo["tagLine"]?.ToString() ?? "";
                privacyStatus = summonerInfo["privacy"]?.ToString().Equals("PUBLIC", StringComparison.OrdinalIgnoreCase) == true
                    ? "公开" : "隐藏";

                var realPuuid = summonerInfo["puuid"]?.ToString();
                if (!string.IsNullOrEmpty(realPuuid))
                    puuid = realPuuid;
            }

            string displayName = string.IsNullOrEmpty(tagLine) ? gameName : $"{gameName}";

            if (string.IsNullOrEmpty(puuid) || puuid.Length < 10)
            {
                Debug.WriteLine($"[战绩查询失败] puuid 无效 (summonerId={summonerId})");
                // 直接在这里创建返回对象，不需要额外局部方法
                return new PlayerMatchInfo
                {
                    Player = new PlayerInfo
                    {
                        SummonerId = summonerId,
                        ChampionId = championId,
                        ChampionName = GetChampionName(championId),
                        Avatar = await GetChampionIconAsync(championId),
                        GameName = displayName,
                        SoloRank = "未知",
                        FlexRank = "未知",
                        IsPublic = "隐藏"
                    },
                    MatchItems = new List<ListViewItem>(),
                    HeroIcons = new ImageList()
                };
            }

            // 后续查询段位、战绩...
            var rankedStats = await GetRankedStatsAsync(puuid);
            string soloRank = GetFormattedRank(rankedStats, "单双排");
            string flexRank = GetFormattedRank(rankedStats, "灵活组排");

            var matchesJson = await GetPlayerMatchesAsync(puuid);
            var result = ParsePlayerMatchInfo(puuid, matchesJson);

            result.Player = new PlayerInfo
            {
                Puuid = puuid,
                SummonerId = summonerId,
                ChampionId = championId,
                ChampionName = GetChampionName(championId),
                Avatar = await GetChampionIconAsync(championId),
                GameName = displayName,
                SoloRank = soloRank,
                FlexRank = flexRank,
                IsPublic = privacyStatus
            };

            _playerMatchCache[summonerId] = result;
            return result;
        }


        /// <summary>
        /// 获取玩家战绩数据，新增是否过滤模式
        /// </summary>
        private async Task<JArray> GetPlayerMatchesAsync(string puuid)
        {
            string currentQueueId = null;

            // 优先尝试从当前 ChampSelect session 实时获取（最准确）
            try
            {
                var session = await Globals.lcuClient.GetChampSelectSession();
                if (session != null)
                {
                    currentQueueId = session["queueId"]?.ToString();
                    //Debug.WriteLine($"[模式过滤] 从当前 session 实时获取 queueId = {currentQueueId}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[模式过滤] 获取 session 失败，使用全局变量: {ex.Message}");
            }

            // 如果实时获取失败，再用全局变量兜底
            if (string.IsNullOrEmpty(currentQueueId))
            {
                currentQueueId = Globals.CurrGameMod;
                //Debug.WriteLine($"[模式过滤] 使用全局 CurrGameMod = {currentQueueId}");
            }

            if (!_filterByGameMode) // 未勾选复选框：查询所有模式
            {
                //Debug.WriteLine("[模式过滤] 未启用过滤，查询所有模式战绩");
                return await Globals.sgpClient.SgpFetchLatestMatches(puuid, 0, 20, "");
            }
            else // 已勾选：按当前模式过滤
            {
                string queueFilter = currentQueueId switch  // ← 关键：改成 currentQueueId！
                {
                    "420" => "q_420",  // 单双排
                    "440" => "q_440",  // 灵活组排
                    "400" => "q_400",  // 普通匹配
                    "430" => "q_430",  // 匹配模式
                    "450" => "q_450",  // 大乱斗
                    "480" => "q_480",  // 快速模式（AI）
                    "890" => "q_890",  // 快速模式（AI）
                    "900" => "q_900",  // 无限火力
                    "1020" => "q_1020",  // 克隆大作战
                    "1300" => "q_1300",  // 极限闪击
                    "1400" => "q_1400",  // 终极魔典
                    "1700" => "q_1700",  // 斗魂竞技场
                    "2400" => "q_2400",  // 海克斯乱斗
                    "3270" => "q_3270",  // 自定义·海克斯乱斗
                    "950" => "q_950",  // 末日人机
                    "960" => "q_960",  // 末日人机
                    "1900" => "q_1900",  // 快速模式
                    "2000" => "q_2000",  // 神木之门
                    "2010" => "q_2010",  // 神木之门
                    "2020" => "q_2020",  // 神木之门
                    "700" => "q_700",  // 云顶之弈
                    "720" => "q_720",  // 云顶之弈（双人）
                    "740" => "q_740",  // 云顶之弈（闪电）
                    "750" => "q_750",  // 云顶之弈
                    "1090" => "q_1090",  // 云顶之弈(快速)
                    "1100" => "q_1100",  // 云顶之弈(排位)
                    _ => ""
                };

                if (string.IsNullOrEmpty(queueFilter))
                {
                    Debug.WriteLine($"[模式过滤] 无法识别 queueId '{currentQueueId}'，查询所有模式战绩");
                    return await Globals.sgpClient.SgpFetchLatestMatches(puuid, 0, 20, "");
                }

                //Debug.WriteLine($"[模式过滤] 启用过滤，使用 {queueFilter} 查询战绩 (queueId={currentQueueId})");
                return await Globals.sgpClient.SgpFetchLatestMatches(puuid, 0, 20, queueFilter);
            }
        }

        /// <summary>
        /// 获取段位信息
        /// </summary>
        private async Task<Dictionary<string, RankedStats>> GetRankedStatsAsync(string puuid)
        {
            var rankedJson = await Globals.lcuClient.GetCurrentRankedStatsAsync(puuid);
            return RankedStats.FromJson(rankedJson);
        }
        #endregion

        #region 战绩解析方法
        /// <summary>
        /// 解析玩家战绩信息
        /// </summary>
        public PlayerMatchInfo ParsePlayerMatchInfo(string puuid, JArray matches)
        {
            var result = new PlayerMatchInfo();
            var matchItems = result.MatchItems;
            var heroIcons = result.HeroIcons;

            if (matches == null || matches.Count == 0)
            {
                Debug.WriteLine("matches 数据为空");
                return result;
            }

            try
            {
                foreach (JObject match in matches.Cast<JObject>())
                {
                    // 提取游戏数据
                    JObject gameJson = match;
                    if (match["json"] != null)
                    {
                        gameJson = match["json"] as JObject;
                    }

                    if (gameJson == null || gameJson["gameId"]?.Value<long>() == 0)
                        continue;

                    // 查找当前玩家数据
                    var participant = FindParticipant(gameJson, puuid);
                    if (participant == null)
                        continue;

                    // 提取比赛数据
                    long gameId = gameJson["gameId"]?.Value<long>() ?? 0;
                    int teamId = participant["teamId"]?.Value<int>() ?? -1;
                    int championId = participant["championId"]?.Value<int>() ?? 0;
                    string championName = participant["championName"]?.ToString() ?? "";

                    int kills = participant["kills"]?.Value<int>() ?? 0;
                    int deaths = participant["deaths"]?.Value<int>() ?? 0;
                    int assists = participant["assists"]?.Value<int>() ?? 0;
                    bool isWin = participant["win"]?.Value<bool>() ?? false;

                    // 获取游戏模式
                    string gameMode = ExtractGameMode(match["metadata"]?["tags"] as JArray, gameJson);

                    // 获取游戏时间
                    long gameStart = gameJson["gameStartTimestamp"]?.Value<long>() ??
                                    gameJson["gameCreation"]?.Value<long>() ?? 0;
                    string gameDate = gameStart > 0 ?
                        DateTimeOffset.FromUnixTimeMilliseconds(gameStart).ToString("yyyy-MM-dd") : "未知";

                    // 添加到结果
                    result.RecentMatches.Add(new MatchStat { Kills = kills, Deaths = deaths, Assists = assists });
                    result.WinHistory.Add(isWin);

                    // 创建列表项
                    var item = new ListViewItem
                    {
                        ImageKey = championName.Replace(" ", "").Replace("'", ""),
                        ForeColor = isWin ? Color.Green : Color.Red,
                        Tag = new MatchMetadata
                        {
                            GameId = gameId,
                            TeamId = teamId
                        }
                    };
                    item.SubItems.AddRange(new[]
                    {
                        gameMode,
                        $"{kills}/{deaths}/{assists}",
                        gameDate
                    });

                    matchItems.Add(item);
                    result.MatchKeys.Add($"{gameId}_{teamId}");

                    // 缓存英雄图标
                    CacheChampionIcon(championName, championId, heroIcons);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"解析比赛数据异常: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }

            result.HeroIcons = heroIcons;
            return result;
        }

        /// <summary>
        /// 查找参与者
        /// </summary>
        private JObject FindParticipant(JObject gameJson, string puuid)
        {
            var participants = gameJson["participants"] as JArray;
            if (participants == null)
                return null;

            return participants
                .Cast<JObject>()
                .FirstOrDefault(p => p["puuid"]?.ToString() == puuid);
        }

        /// <summary>
        /// 提取游戏模式
        /// </summary>
        private string ExtractGameMode(JArray tags, JObject gameJson)
        {
            if (tags != null)
            {
                return MapGameTags(tags);
            }
            else
            {
                int queueId = gameJson["queueId"]?.Value<int>() ?? -1;
                string gameMode = gameJson["gameMode"]?.ToString();
                return GameMod.GetModeName(queueId, gameMode);
            }
        }

        
        private string MapGameTags(JArray tags)
        {
            if (tags == null) return "未知模式";

            var tagList = tags.Select(t => t.ToString()).ToList();

            // 常见的游戏模式映射
            if (tagList.Contains("q_420")) return "单双排";
            if (tagList.Contains("q_440")) return "灵活组排";

            // 普通匹配（自选）
            if (tagList.Contains("q_400") || tagList.Contains("q_430")) return "匹配";

            // 官方 AI 快速模式
            if (tagList.Contains("q_480") || tagList.Contains("q_890")) return "快速模式（AI）";

            // 旧人机
            if (tagList.Contains("q_830") || tagList.Contains("q_840") ||
                tagList.Contains("q_850") || tagList.Contains("q_870")) return "人机对战";

            if (tagList.Contains("q_450")) return "大乱斗";
            if (tagList.Contains("q_900")) return "无限火力";
            if (tagList.Contains("q_1020")) return "克隆大作战";
            if (tagList.Contains("q_1300")) return "极限闪击";
            if (tagList.Contains("q_1400")) return "终极魔典";
            if (tagList.Contains("q_1700")) return "斗魂竞技场";

            // 官方海克斯
            if (tagList.Contains("q_2400")) return "海克斯乱斗";

            // 自定义海克斯（兜底）
            if (tagList.Contains("q_3270")) return "自定义 · 海克斯乱斗";

            // ⚠️ 新增：自定义模式相关
            if (tagList.Contains("mode_practicetool")) return "训练模式";
            if (tagList.Contains("mode_classic")) return "自定义 · 召唤师峡谷";
            if (tagList.Contains("mode_aram")) return "自定义 · 极地大乱斗";
            if (tagList.Contains("mode_cherry") || tagList.Contains("mode_kiwi")) return "自定义 · 海克斯乱斗";

            // 已有但ExtractGameMode中没有的（保留）
            if (tagList.Contains("q_950") || tagList.Contains("q_960")) return "末日人机";
            if (tagList.Contains("q_1900")) return "快速模式";
            if (tagList.Contains("q_2000") || tagList.Contains("q_2010") || tagList.Contains("q_2020")) return "神木之门";
            if (tagList.Contains("q_700") || tagList.Contains("q_720") ||
                tagList.Contains("q_740") || tagList.Contains("q_750")) return "云顶之弈";
            if (tagList.Contains("q_1090")) return "云顶之弈(快速)";
            if (tagList.Contains("q_1100")) return "云顶之弈(排位)";
            if (tagList.Contains("q_610")) return "联盟战棋";
            if (tagList.Contains("q_100")) return "魄罗大乱斗";
            if (tagList.Contains("q_1200")) return "闪击模式";

            // 新增对自定义模式的识别
            if (tagList.Contains("type_CUSTOM_GAME")) return "自定义模式";

            return string.Join(",", tagList);
        }

        /// <summary>
        /// 缓存英雄图标
        /// </summary>
        private void CacheChampionIcon(string championName, int championId, ImageList heroIcons)
        {
            string cleanName = championName.Replace(" ", "").Replace("'", "");

            if (!_imageCache.TryGetValue(cleanName, out var image))
            {
                image = Globals.resLoading.GetChampionIconAsync(championId).GetAwaiter().GetResult();
                if (image != null)
                {
                    _imageCache.TryAdd(cleanName, image);
                }
            }

            if (image != null && !heroIcons.Images.ContainsKey(cleanName))
            {
                heroIcons.Images.Add(cleanName, image);
            }
        }
        #endregion

        #region 辅助方法
        /// <summary>
        /// 获取格式化段位
        /// </summary>
        private string GetFormattedRank(Dictionary<string, RankedStats> rankedStats, string queueType)
        {
            if (rankedStats != null && rankedStats.TryGetValue(queueType, out RankedStats stats))
            {
                return $"{stats.FormattedTier}({stats.LeaguePoints})";
            }
            return "未知";
        }

        /// <summary>
        /// 获取英雄名称
        /// </summary>
        private string GetChampionName(int championId)
        {
            return Globals.resLoading.GetChampionById(championId)?.Name ?? "Unknown";
        }

        /// <summary>
        /// 获取英雄图标
        /// </summary>
        private async Task<Image> GetChampionIconAsync(int championId)
        {
            return await Globals.resLoading.GetChampionIconAsync(championId);
        }
        #endregion
    }
}