using League.Models;
using League.Networking;
using League.Parsers;
using League.Controls;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

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

        private System.Windows.Forms.Timer? _finalSweepTimer;

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

                        var fallback = _factory.CreateFailedPlayerInfo(sid, cid);
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
                    if (info?.Player != null)
                        _uiManager.UpdatePlayerNameColor(info.Player.SummonerId, info.Player.NameColor, _cache);
                }
            }
            finally
            {
                _uiManager._uiLock.Release();
            }
        }

        public async Task ValidateAndCompleteAllCards(JArray teamOne, JArray teamTwo)
        {
            if (teamOne == null || teamTwo == null) return;

            await _uiManager._uiLock.WaitAsync();
            try
            {
                var cardsNeedFix = _validator.ForceGetAllCardsForCompletion();
                if (cardsNeedFix.Count == 0)
                {
                    Debug.WriteLine("[Validate] 无需补全");
                    return;
                }

                Debug.WriteLine($"[Validate] 发现 {cardsNeedFix.Count} 个需要补全的卡片（含隐藏/空白）");

                foreach (var cardInfo in cardsNeedFix)
                {
                    // 隐藏玩家直接修复
                    if (cardInfo.SummonerId == 0 ||
                        string.IsNullOrEmpty(cardInfo.Puuid) ||
                        cardInfo.CurrentName?.Contains("隐藏") == true)
                    {
                        _validator.FixHiddenPlayerCard(cardInfo.Card);
                        continue;
                    }

                    JToken? playerData = FindPlayerDataByPuuid(teamOne, teamTwo, cardInfo.Puuid)
                                      ?? FindPlayerDataInSession(teamOne, teamTwo, cardInfo.SummonerId);

                    if (playerData == null)
                    {
                        Debug.WriteLine($"[补全] 找不到玩家数据 Puuid={cardInfo.Puuid}");
                        continue;
                    }

                    // 重试查询
                    var matchInfo = await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(playerData);
                    if (matchInfo?.Player != null && matchInfo.MatchItems?.Count > 0)
                    {
                        _uiManager.UpdateCardUI(cardInfo.Card, matchInfo);
                        _cache.AddOrUpdateCache(matchInfo.Player.SummonerId, matchInfo);
                        Debug.WriteLine($"[补全成功] {matchInfo.Player.GameName} 战绩项:{matchInfo.MatchItems.Count}");
                    }
                    else
                    {
                        var failedInfo = _factory.CreateFailedPlayerInfo(cardInfo.SummonerId, cardInfo.ChampionId);
                        _uiManager.UpdateCardUI(cardInfo.Card, failedInfo);
                        Debug.WriteLine($"[补全失败] {cardInfo.CurrentName}");
                    }

                    await Task.Delay(200); // 防API限流
                }
            }
            finally
            {
                _uiManager._uiLock.Release();
            }
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

        #region 最终兜底补全卡片
        public async Task StartFinalCardSweep()
        {
            _finalSweepTimer?.Stop();
            _finalSweepTimer?.Dispose();

            _finalSweepTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            _finalSweepTimer.Tick += async (s, e) =>
            {
                _finalSweepTimer.Stop();
                _finalSweepTimer.Dispose();
                _finalSweepTimer = null;

                await ForceFixRemainingLoadingCards();
            };

            _finalSweepTimer.Start();
            Debug.WriteLine("[FinalSweep] 已启动最终卡片兜底定时器（2.5秒后执行）");
        }

        private async Task ForceFixRemainingLoadingCards()
        {
            await _uiManager._uiLock.WaitAsync();
            try
            {
                // 直接使用 validator 返回的结果（已经过滤好了）
                var stillLoading = _validator.ForceGetAllCardsForCompletion();

                if (stillLoading.Count == 0)
                {
                    Debug.WriteLine("[FinalSweep] ✅ 所有卡片状态正常，无需强制修复");
                    return;
                }

                Debug.WriteLine($"[FinalSweep] ⚠️ 发现 {stillLoading.Count} 个残留问题卡片，正在强制修复...");

                foreach (var info in stillLoading)
                {
                    var failedInfo = _factory.CreateFailedPlayerInfo(
                        info.SummonerId,
                        info.ChampionId);

                    _uiManager.UpdateCardUI(info.Card, failedInfo);

                    Debug.WriteLine($"[FinalSweep] 已强制修复 → {info.CurrentName ?? "未知"} (SID:{info.SummonerId})");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FinalSweep] 异常: {ex.Message}");
            }
            finally
            {
                _uiManager._uiLock.Release();
            }
        }
        #endregion
    }
}