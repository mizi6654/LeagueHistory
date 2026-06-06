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
                var fetchedInfos = await RunWithLimitedConcurrency(
                    team,
                    async p =>
                    {
                        long sid = p["summonerId"]?.Value<long>() ?? 0;
                        string puuid = p["puuid"]?.ToString() ?? "";
                        return sid == 0
                            ? _factory.CreateHiddenPlayerInfo(0, p["championId"]?.Value<int>() ?? 0)
                            : await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(p);
                    },
                    maxConcurrency: 3);

                // 缓存
                foreach (var info in fetchedInfos)
                {
                    if (info?.Player?.SummonerId > 0)
                        _cache.AddOrUpdateCache(info.Player.SummonerId, info);
                }

                // 更新UI
                int col = 0;
                foreach (var info in fetchedInfos)
                {
                    if (info?.Player != null)
                    {
                        string puuid = info.Player.Puuid ?? "";
                        _uiManager.CreateLoadingPlayerMatch(info, isMyTeam, row, col, puuid); // 注意：这里其实是更新
                    }
                    col++;
                }

                // 组队检测
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
                // 使用更强的 Force 方法
                var cardsNeedFix = _validator.ForceGetAllCardsForCompletion();
                if (cardsNeedFix.Count == 0)
                {
                    Debug.WriteLine("[Validate] 无需补全");
                    return;
                }

                Debug.WriteLine($"[Validate] 发现 {cardsNeedFix.Count} 个需要补全的卡片");

                foreach (var cardInfo in cardsNeedFix)
                {
                    // 隐藏玩家直接跳过/修复
                    if (cardInfo.SummonerId == 0 || string.IsNullOrEmpty(cardInfo.Puuid))
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
                        // 仍失败则设为失败状态
                        var failedInfo = _factory.CreateFailedPlayerInfo(cardInfo.SummonerId, cardInfo.ChampionId);
                        _uiManager.UpdateCardUI(cardInfo.Card, failedInfo);
                        Debug.WriteLine($"[补全失败] {cardInfo.CurrentName}");
                    }

                    await Task.Delay(150); // 降低频率避免API限流
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
    }
}