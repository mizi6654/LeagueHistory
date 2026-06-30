using League.Controls;
using League.Models;
using League.Networking;
using League.Parsers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.Managers
{
    public class PlayerCardManager
    {
        private readonly FormMain _form;
        private readonly MatchQueryProcessor _matchQueryProcessor;

        // 新拆分出的服务
        public readonly PlayerCardUIManager _uiManager;
        private readonly PlayerCardCache _cache;
        public readonly PlayerCardFactory _factory;
        private readonly PlayerCardValidator _validator;

        private DateTime _lastFillTime = DateTime.MinValue;

        // 添加到 PlayerCardManager 类中
        public SemaphoreSlim UiLock => _uiManager._uiLock;

        public PlayerCardManager(FormMain form, MatchQueryProcessor matchQueryProcessor)
        {
            _form = form;
            _matchQueryProcessor = matchQueryProcessor;

            _uiManager = new PlayerCardUIManager(form);
            _cache = new PlayerCardCache();
            _factory = new PlayerCardFactory();
            _validator = new PlayerCardValidator(form);

            matchQueryProcessor.SetPlayerCardManager(this);
        }

        public PlayerMatchInfo CreateFailedPlayerInfo(long summonerId, int championId)
        {
            return _factory.CreateFailedPlayerInfo(summonerId, championId);
        }

        public void RegisterCard(long summonerId, PlayerCardControl card)
        {
            _cache.RegisterCard(summonerId, card);
        }

        public async Task CreateBasicCardsOnly(JArray team, bool isMyTeam, int row)
        {
            await _uiManager.CreateBasicCardsOnly(team, isMyTeam, row, _factory, _cache);
        }


        public async Task FillPlayerMatchInfoAsync(JArray team, bool isMyTeam, int row)
        {
            if (team == null || team.Count == 0) return;

            if ((DateTime.Now - _lastFillTime).TotalMilliseconds < 1200)
                return;

            _lastFillTime = DateTime.Now;

            await _uiManager._uiLock.WaitAsync();
            try
            {
                // 并行查询（保持原逻辑）
                var fetchedInfos = await RunWithLimitedConcurrency(
                    team,
                    async p =>
                    {
                        long sid = p["summonerId"]?.Value<long>() ?? 0;
                        return sid == 0
                            ? _factory.CreateHiddenPlayerInfo(0, p["championId"]?.Value<int>() ?? 0)
                            : await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(p);
                    },
                    maxConcurrency: 3);

                // 缓存正常玩家
                foreach (var info in fetchedInfos)
                {
                    if (info?.Player?.SummonerId > 0)
                        _cache.AddOrUpdateCache(info.Player.SummonerId, info);
                }

                // 【关键修复】UI 更新循环 - 按原始 team 顺序处理
                for (int col = 0; col < team.Count; col++)
                {
                    var info = fetchedInfos[col];           // 对应位置的数据
                    var playerData = team[col];             // 原始 JSON 数据（永远可用）

                    if (info == null || info.Player == null)
                    {
                        // 🔥 从原始 team JSON 中取出 id
                        long sid = playerData["summonerId"]?.Value<long>() ?? 0;
                        int cid = playerData["championId"]?.Value<int>() ?? 0;
                        string puuidFromData = playerData["puuid"]?.ToString() ?? "";

                        var fallback = _factory.CreateFailedPlayerInfo(sid, cid, puuidFromData);
                        _uiManager.CreateLoadingPlayerMatch(fallback, isMyTeam, row, col, "");
                        Debug.WriteLine($"[Fill] 强制失败兜底: sid={sid}, cid={cid}");
                        continue;
                    }

                    if (info.Player?.SummonerId == 0)  // 隐藏玩家
                    {
                        _uiManager.CreateLoadingPlayerMatch(info, isMyTeam, row, col, "");
                        continue;
                    }

                    // 正常玩家
                    string puuid = info.Player.Puuid ?? "";
                    _uiManager.CreateLoadingPlayerMatch(info, isMyTeam, row, col, puuid);
                }

                // 组队检测 & 名字颜色
                var detector = new PartyDetector();
                detector.Detect(fetchedInfos.Where(f => f != null).ToList());

                foreach (var info in fetchedInfos)
                {
                    if (info?.Player?.SummonerId == 0) continue; // 跳过隐藏玩家
                    if (info?.Player != null)
                        _uiManager.UpdatePlayerNameColor(info.Player.SummonerId, info.Player.NameColor, _cache);
                }
            }
            finally
            {
                _uiManager._uiLock.Release();
            }
        }

        /// <summary>
        /// 验证并补全所有卡片（优化版）
        /// - 自动检测缺陷并重试获取队伍数据
        /// - 补全过程中保存详细日志到 Debug_teams 目录（文件名使用 yyyyMMdd_HHmmssfff）
        /// </summary>
        public async Task ValidateAndCompleteAllCards(JArray? teamOne = null, JArray? teamTwo = null)
        {
            const int MaxRetry = 2;
            int retryCount = 0;

            // ==================== 队伍数据检查与重试刷新 ====================
            while ((teamOne == null || teamTwo == null || teamOne.Count != 5 || teamTwo.Count != 5) && retryCount <= MaxRetry)
            {
                if (retryCount > 0)
                    Debug.WriteLine($"[Validate] 第 {retryCount} 次重试获取队伍数据...");

                Debug.WriteLine("[Validate] 队伍数据不完整，尝试重新获取最新数据...");
                (teamOne, teamTwo) = await TryGetLatestFullTeamsAsync();

                if (teamOne != null && teamTwo != null && teamOne.Count == 5 && teamTwo.Count == 5)
                    break;

                retryCount++;
                await Task.Delay(600); // 重试间隔
            }

            if (teamOne == null || teamTwo == null || teamOne.Count != 5 || teamTwo.Count != 5)
            {
                Debug.WriteLine("[Validate] 多次重试后仍无法得到有效队伍数据，补全中止");
                return;
            }

            // ==================== 日志文件准备 ====================
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
            string logFileName = $"{timestamp}.txt";
            string DebugPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Debug_teams");
            Directory.CreateDirectory(DebugPath);
            string logFullPath = Path.Combine(DebugPath, logFileName);

            // ==================== 卡片补全逻辑 ====================
            await _uiManager._uiLock.WaitAsync();
            try
            {
                var cardsNeedFix = _validator.ForceGetAllCardsForCompletion();

                if (cardsNeedFix.Count == 0)
                {
                    Debug.WriteLine("[Validate] 无需补全，所有卡片正常");
                    File.AppendAllText(logFullPath, $"[Validate] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - 无需补全，所有卡片正常\n");
                    return;
                }

                // 写入日志头部信息
                File.AppendAllText(logFullPath, $"[Validate] 开始补全 - 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n");
                File.AppendAllText(logFullPath, $"[Validate] 发现 {cardsNeedFix.Count} 个需要补全的卡片\n\n");

                Debug.WriteLine($"[Validate] 发现 {cardsNeedFix.Count} 个需要补全的卡片");

                foreach (var cardInfo in cardsNeedFix)
                {
                    string logMsg = $"[补全尝试] Col={cardInfo.Column} Name={cardInfo.CurrentName} SID={cardInfo.SummonerId} PUUID={cardInfo.Puuid ?? "空"}";
                    Debug.WriteLine(logMsg);
                    File.AppendAllText(logFullPath, logMsg + "\n");

                    // 隐藏玩家直接修复
                    if (cardInfo.SummonerId == 0 ||
                        string.IsNullOrEmpty(cardInfo.Puuid) ||
                        cardInfo.CurrentName?.Contains("隐藏") == true)
                    {
                        _validator.FixHiddenPlayerCard(cardInfo.Card);
                        File.AppendAllText(logFullPath, "  → 隐藏玩家修复\n");
                        continue;
                    }

                    // 使用当前队伍数据查找
                    JToken? playerData = FindPlayerDataByPuuid(teamOne, teamTwo, cardInfo.Puuid)
                                      ?? FindPlayerDataInSession(teamOne, teamTwo, cardInfo.SummonerId);

                    if (playerData == null)
                    {
                        string failLog = $"[补全失败] 找不到玩家数据 Puuid={cardInfo.Puuid}";
                        Debug.WriteLine(failLog);
                        File.AppendAllText(logFullPath, failLog + "\n");
                        continue;
                    }

                    // 重试查询战绩
                    var matchInfo = await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(playerData);

                    if (matchInfo?.Player != null && matchInfo.MatchItems?.Count > 0)
                    {
                        _uiManager.UpdateCardUI(cardInfo.Card, matchInfo);
                        _cache.AddOrUpdateCache(matchInfo.Player.SummonerId, matchInfo);

                        string successLog = $"[补全成功] {matchInfo.Player.GameName} 战绩项:{matchInfo.MatchItems.Count}";
                        Debug.WriteLine(successLog);
                        File.AppendAllText(logFullPath, successLog + "\n");
                    }
                    else
                    {
                        var failedInfo = _factory.CreateFailedPlayerInfo(cardInfo.SummonerId, cardInfo.ChampionId);
                        _uiManager.UpdateCardUI(cardInfo.Card, failedInfo);

                        string failLog = $"[补全失败] {cardInfo.CurrentName}";
                        Debug.WriteLine(failLog);
                        File.AppendAllText(logFullPath, failLog + "\n");
                    }

                    await Task.Delay(250); // 补全间隔，防限流
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ValidateAndCompleteAllCards] 整体异常: {ex.Message}");
                File.AppendAllText(logFullPath, $"\n[整体异常] {ex.Message}\n{ex.StackTrace}\n");
            }
            finally
            {
                _uiManager._uiLock.Release();
            }
        }


        /// <summary>
        /// 统一重新获取双方最新队伍数据
        /// 我方优先用 ChampSelect，整体用 GameSession 补全（兼容 Quick Play）
        /// </summary>
        public async Task<(JArray? myTeam, JArray? enemyTeam)> TryGetLatestFullTeamsAsync()
        {
            JArray? myTeam = null;
            JArray? enemyTeam = null;

            try
            {
                // === 我方：优先使用 ChampSelect（和 ShowMyTeamCards 一致）===
                var champSession = await Globals.lcuClient.GetChampSelectSession();
                if (champSession != null)
                {
                    myTeam = champSession["myTeam"] as JArray;
                    if (myTeam != null && myTeam.Count > 0)
                    {
                        _form._cachedMyTeam = myTeam;
                        Debug.WriteLine($"[TryGetLatest] 我方从 ChampSelect 获取 ({myTeam.Count}人)");
                    }
                }

                // === 双方/敌方：使用 GameSession（和 ShowEnemyTeamCardsAsync 一致）===
                var sessionData = await Globals.lcuClient.GetGameSession();
                if (sessionData != null)
                {
                    var teamOne = sessionData["gameData"]?["teamOne"] as JArray;
                    var teamTwo = sessionData["gameData"]?["teamTwo"] as JArray;
                    var selections = sessionData["gameData"]?["playerChampionSelections"] as JArray;

                    if (selections?.Count == 10)
                    {
                        (teamOne, teamTwo) = EnsureAllPlayersPresent(teamOne, teamTwo, selections);
                    }

                    if (teamOne != null && teamTwo != null)
                    {
                        var currentSummoner = await Globals.lcuClient.GetCurrentSummoner();
                        string myPuuid = currentSummoner?["puuid"]?.ToString() ?? "";

                        if (!string.IsNullOrEmpty(myPuuid))
                        {
                            bool isInTeamOne = teamOne.Any(t => t["puuid"]?.ToString() == myPuuid);
                            myTeam = isInTeamOne ? teamOne : teamTwo;
                            enemyTeam = isInTeamOne ? teamTwo : teamOne;
                        }
                        else
                        {
                            myTeam ??= teamOne;
                            enemyTeam ??= teamTwo;
                        }

                        _form._cachedMyTeam = myTeam;
                        _form._cachedEnemyTeam = enemyTeam;

                        Debug.WriteLine($"[TryGetLatest] GameSession 补全成功 | 我方:{myTeam?.Count ?? 0} | 敌方:{enemyTeam?.Count ?? 0}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TryGetLatestFullTeamsAsync] 异常: {ex.Message}");
            }

            return (myTeam, enemyTeam);
        }

        /// <summary>
        /// 确保 teamOne 和 teamTwo 包含所有10个 puuid（使用 playerChampionSelections 补全）
        /// </summary>
        public (JArray teamOne, JArray teamTwo) EnsureAllPlayersPresent(
            JArray? teamOne, JArray? teamTwo, JArray selections)
        {
            var finalTeamOne = teamOne?.DeepClone() as JArray ?? new JArray();
            var finalTeamTwo = teamTwo?.DeepClone() as JArray ?? new JArray();

            // 收集当前已有的 puuid
            var existingPuuids = new HashSet<string>();
            foreach (var p in finalTeamOne) existingPuuids.Add(p["puuid"]?.ToString() ?? "");
            foreach (var p in finalTeamTwo) existingPuuids.Add(p["puuid"]?.ToString() ?? "");

            // 补充缺失的玩家
            foreach (var sel in selections)
            {
                string? puuid = sel["puuid"]?.ToString();
                if (string.IsNullOrEmpty(puuid) || existingPuuids.Contains(puuid))
                    continue;

                // 创建缺失玩家对象
                var missingPlayer = new JObject
                {
                    ["puuid"] = puuid,
                    ["championId"] = sel["championId"],
                    ["summonerId"] = 0,
                    ["summonerName"] = "",
                    ["profileIconId"] = 0,
                    ["selectedPosition"] = "NONE"
                };

                // 优先补到人数少的队伍
                if (finalTeamOne.Count < 5)
                    finalTeamOne.Add(missingPlayer);
                else if (finalTeamTwo.Count < 5)
                    finalTeamTwo.Add(missingPlayer);
            }

            return (finalTeamOne, finalTeamTwo);
        }

        public JToken? FindPlayerDataByPuuid(JArray teamOne, JArray teamTwo, string puuid)
        {
            if (string.IsNullOrEmpty(puuid)) return null;

            var player = teamOne?.FirstOrDefault(p => p["puuid"]?.ToString() == puuid);
            if (player == null)
                player = teamTwo?.FirstOrDefault(p => p["puuid"]?.ToString() == puuid);

            return player;
        }

        public Dictionary<long, PlayerMatchInfo> GetAllCachedPlayerInfos()
            => _cache.GetAllCachedPlayerInfos();

        public void ClearAllCaches() => _cache.ClearAll();
        public void ClearGameState() => _cache.ClearGameState();

        /// <summary>
        /// 限制并发数的任务执行
        /// </summary>
        public async Task<List<TResult>> RunWithLimitedConcurrency<TInput, TResult>(
            IEnumerable<TInput> inputs,
            Func<TInput, Task<TResult>> taskFunc,
            int maxConcurrency = 3)
        {
            var indexedInputs = inputs.Select((input, index) => new { input, index }).ToList();
            var results = new TResult[indexedInputs.Count];
            var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = new List<Task>();

            foreach (var item in indexedInputs)
            {
                await semaphore.WaitAsync();

                var task = Task.Run(async () =>
                {
                    try
                    {
                        var result = await taskFunc(item.input);
                        results[item.index] = result;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[并发异常] Index {item.index}: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            return results.ToList();
        }

        public JToken FindPlayerDataInSession(JArray teamOne, JArray teamTwo, long summonerId)
        {
            var player = teamOne?.FirstOrDefault(p => p["summonerId"]?.Value<long>() == summonerId);
            if (player == null)
                player = teamTwo?.FirstOrDefault(p => p["summonerId"]?.Value<long>() == summonerId);
            return player;
        }

        public void SaveTeamDataForDebug(JObject sessionData)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
                string DebugPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Debug_teams");
                Directory.CreateDirectory(DebugPath);

                // 正确写法
                File.WriteAllText(Path.Combine(DebugPath, $"session_{timestamp}.json"),
                    sessionData.ToString(Formatting.Indented));

                Debug.WriteLine($"[Debug] 已保存 session_{timestamp}.json");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DebugSave] 保存失败: {ex.Message}");
            }
        }
    }
}