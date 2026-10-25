using League.Controls;
using League.Models;
using League.Networking;
using League.Parsers;
using League.UIState;
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

        public readonly PlayerCardUIManager _uiManager;
        private readonly PlayerCardCache _cache;
        public readonly PlayerCardFactory _factory;
        private readonly PlayerCardValidator _validator;

        // 分队节流：我方 / 敌方互不影响
        private DateTime _lastFillMyTeamTime = DateTime.MinValue;
        private DateTime _lastFillEnemyTeamTime = DateTime.MinValue;
        private const int FillThrottleMs = 1200;

        // 隐藏会话日志（进程内每种只存一次；新一局 Reset）
        private static readonly object HiddenLogLock = new object();
        private static bool _hiddenLogMyTeamSaved;
        private static bool _hiddenLogEnemyTeamSaved;
        private static bool _hiddenLogFull10Saved;

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

        public void ResetHiddenSessionLogFlags()
        {
            lock (HiddenLogLock)
            {
                _hiddenLogMyTeamSaved = false;
                _hiddenLogEnemyTeamSaved = false;
                _hiddenLogFull10Saved = false;
            }
        }

        /// <summary>
        /// 在完整 session JSON 里判断是否存在隐藏玩家（champ-select / gameflow 结构都兼容）
        /// </summary>
        public static bool SessionHasHiddenPlayer(JToken session)
        {
            if (session == null) return false;

            // champ-select
            if (TeamArrayHasHiddenPlayer(session["myTeam"] as JArray)) return true;
            if (TeamArrayHasHiddenPlayer(session["theirTeam"] as JArray)) return true;

            // gameflow
            var gameData = session["gameData"];
            if (gameData != null)
            {
                if (TeamArrayHasHiddenPlayer(gameData["teamOne"] as JArray)) return true;
                if (TeamArrayHasHiddenPlayer(gameData["teamTwo"] as JArray)) return true;
            }

            // 有人直接传队伍数组时也兼容
            if (session is JArray arr && TeamArrayHasHiddenPlayer(arr)) return true;

            return false;
        }

        private static bool TeamArrayHasHiddenPlayer(JArray team)
        {
            if (team == null || team.Count == 0) return false;
            foreach (var p in team)
            {
                if (p == null) continue;
                string vis = p["nameVisibilityType"]?.ToString() ?? "";
                string puuid = p["puuid"]?.ToString() ?? "";
                long sid = p["summonerId"]?.Value<long>() ?? 0;

                if (string.Equals(vis, "HIDDEN", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (!MatchQueryProcessor.IsUsablePuuid(puuid) && sid == 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// side:
        ///   "my_team"         — 选人 ChampSelect 完整 session，仅当我方有隐藏时存
        ///   "full10_gameflow" — 读秒 GameSession 完整 session，本局任意侧有隐藏时存
        /// 每种 side 全局只存一次（带锁）
        /// </summary>
        public void TrySaveHiddenFullSessionOnce(string side, JToken fullSession)
        {
            if (fullSession == null) return;

            lock (HiddenLogLock)
            {
                if (side == "my_team")
                {
                    if (_hiddenLogMyTeamSaved) return;

                    // 只看我方 myTeam 是否有隐藏（不要用 theirTeam 误触发）
                    var myTeam = fullSession["myTeam"] as JArray;
                    if (!TeamArrayHasHiddenPlayer(myTeam))
                        return;

                    WriteHiddenSessionFile("my_team", fullSession);
                    _hiddenLogMyTeamSaved = true;
                    return;
                }

                if (side == "full10_gameflow")
                {
                    if (_hiddenLogFull10Saved) return;

                    // 读秒完整包：当前 session 任一侧有隐藏，或本局已为「我方隐藏」存过
                    bool hasHiddenNow = SessionHasHiddenPlayer(fullSession);
                    if (!hasHiddenNow && !_hiddenLogMyTeamSaved)
                        return;

                    WriteHiddenSessionFile("full10_gameflow", fullSession);
                    _hiddenLogFull10Saved = true;
                    return;
                }
            }
        }

        private static void WriteHiddenSessionFile(string side, JToken fullSession)
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
                Directory.CreateDirectory(logDir);

                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = side switch
                {
                    "my_team" => $"hidden_my_team_{stamp}.json",
                    "full10_gameflow" => $"hidden_full10_gameflow_{stamp}.json",
                    _ => $"hidden_{side}_{stamp}.json"
                };

                string path = Path.Combine(logDir, fileName);
                File.WriteAllText(path, fullSession.ToString(Formatting.Indented));
                Debug.WriteLine($"[HiddenLog] 已保存 {side} → {path}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HiddenLog] 保存失败: {ex.Message}");
            }
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

        /// <summary>
        /// 填充队伍战绩卡片。
        /// forceFill=true 时跳过节流（进游戏补敌方、重连补全等必须成功的路径）。
        /// CreateBasicCardsOnly + ApplyOneCardUiAsync 优先更新已有占位卡，不会重复叠卡。
        /// </summary>
        public async Task FillPlayerMatchInfoAsync(JArray team, bool isMyTeam, int row, bool forceFill = false)
        {
            if (team == null || team.Count == 0) return;

            if (!forceFill)
            {
                DateTime lastFill = isMyTeam ? _lastFillMyTeamTime : _lastFillEnemyTeamTime;
                if ((DateTime.Now - lastFill).TotalMilliseconds < FillThrottleMs)
                {
                    //Debug.WriteLine($"[Fill] 节流跳过 isMyTeam={isMyTeam}（距上次 {(DateTime.Now - lastFill).TotalMilliseconds:F0}ms）");
                    return;
                }
                if (isMyTeam) _lastFillMyTeamTime = DateTime.Now;
                else _lastFillEnemyTeamTime = DateTime.Now;
            }
            else
            {
                if (isMyTeam) _lastFillMyTeamTime = DateTime.Now;
                else _lastFillEnemyTeamTime = DateTime.Now;
                Debug.WriteLine($"[Fill] forceFill 绕过节流 isMyTeam={isMyTeam}");
            }

            var teamList = team.ToList();
            int count = teamList.Count;
            var fetchedInfos = new PlayerMatchInfo[count];

            var semaphore = new SemaphoreSlim(3);
            var tasks = new List<Task>(count);

            for (int i = 0; i < count; i++)
            {
                int col = i;
                var playerData = teamList[i];

                await semaphore.WaitAsync();

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        long sid = playerData["summonerId"]?.Value<long>() ?? 0;
                        int cid = playerData["championId"]?.Value<int>() ?? 0;
                        string puuidFromData = playerData["puuid"]?.ToString() ?? "";

                        PlayerMatchInfo info;

                        if (sid == 0 && string.IsNullOrEmpty(puuidFromData))
                        {
                            info = _factory.CreateHiddenPlayerInfo(0, cid);
                        }
                        else
                        {
                            int retries = forceFill ? 3 : 2;
                            info = await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(playerData, retries);
                        }

                        if (info == null || info.Player == null)
                        {
                            info = _factory.CreateFailedPlayerInfo(sid, cid, puuidFromData);
                            Debug.WriteLine($"[Fill] 强制失败兜底: col={col}, sid={sid}, cid={cid}");
                        }

                        fetchedInfos[col] = info;

                        if (info.Player?.SummonerId > 0)
                            _cache.AddOrUpdateCache(info.Player.SummonerId, info);

                        await ApplyOneCardUiAsync(info, isMyTeam, row, col, puuidFromData);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Fill progressive] col={col} 异常: {ex.Message}");
                        try
                        {
                            long sid = playerData["summonerId"]?.Value<long>() ?? 0;
                            int cid = playerData["championId"]?.Value<int>() ?? 0;
                            string puuidFromData = playerData["puuid"]?.ToString() ?? "";
                            var fallback = _factory.CreateFailedPlayerInfo(sid, cid, puuidFromData);
                            fetchedInfos[col] = fallback;
                            await ApplyOneCardUiAsync(fallback, isMyTeam, row, col, puuidFromData);
                        }
                        catch { }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            try
            {
                var detector = new PartyDetector();
                detector.Detect(
                    fetchedInfos
                        .Where(f => f?.Player != null && !string.IsNullOrEmpty(f.Player.Puuid))
                        .ToList()
                );

                foreach (var info in fetchedInfos)
                {
                    if (info?.Player == null) continue;
                    if (info.Player.SummonerId == 0) continue;
                    _uiManager.UpdatePlayerNameColor(info.Player.SummonerId, info.Player.NameColor, _cache);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Fill] 组队染色异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 统计某行已存在的有效卡片数量
        /// </summary>
        public int CountCardsInRow(int row, int expectedCols = 5)
        {
            int count = 0;
            try
            {
                FormUiStateManager.SafeInvokeSync(_form.tableLayoutPanel1, () =>
                {
                    for (int col = 0; col < expectedCols; col++)
                    {
                        var panel = _form.tableLayoutPanel1.GetControlFromPosition(col, row) as BorderPanel;
                        var card = panel?.Controls.OfType<PlayerCardControl>().FirstOrDefault();
                        if (card != null && !card.IsDisposed)
                            count++;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CountCardsInRow] row={row} 异常: {ex.Message}");
            }
            return count;
        }

        private async Task ApplyOneCardUiAsync(
            PlayerMatchInfo info,
            bool isMyTeam,
            int row,
            int col,
            string puuidFallback)
        {
            if (info?.Player == null) return;

            await _uiManager._uiLock.WaitAsync();
            try
            {
                string puuid = info.Player.Puuid ?? puuidFallback ?? "";

                PlayerCardControl? existing = null;
                FormUiStateManager.SafeInvokeSync(_form.tableLayoutPanel1, () =>
                {
                    var panel = _form.tableLayoutPanel1.GetControlFromPosition(col, row) as BorderPanel;
                    existing = panel?.Controls.OfType<PlayerCardControl>().FirstOrDefault();
                });

                if (existing != null && !existing.IsDisposed)
                {
                    _uiManager.UpdateCardUI(existing, info);
                    if (info.Player.SummonerId > 0)
                        _cache.RegisterCard(info.Player.SummonerId, existing);
                }
                else
                {
                    _uiManager.CreateLoadingPlayerMatch(info, isMyTeam, row, col, puuid);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ApplyOneCardUi] row={row} col={col} 异常: {ex.Message}");
            }
            finally
            {
                _uiManager._uiLock.Release();
            }
        }

        public async Task ValidateAndCompleteAllCards(JArray? teamOne = null, JArray? teamTwo = null)
        {
            const int MaxRetry = 3;
            int retryCount = 0;

            while ((teamOne == null || teamTwo == null || teamOne.Count != 5 || teamTwo.Count != 5) && retryCount <= MaxRetry)
            {
                if (retryCount > 0)
                    //Debug.WriteLine($"[Validate] 第 {retryCount} 次重试获取队伍数据...");

                //Debug.WriteLine("[Validate] 队伍数据不完整，尝试重新获取最新数据...");
                (teamOne, teamTwo) = await TryGetLatestFullTeamsAsync();

                if (teamOne != null && teamTwo != null && teamOne.Count == 5 && teamTwo.Count == 5)
                    break;

                retryCount++;
                await Task.Delay(600);
            }

            if (teamOne == null || teamTwo == null || teamOne.Count != 5 || teamTwo.Count != 5)
            {
                //Debug.WriteLine("[Validate] 多次重试后仍无法得到有效队伍数据，补全中止");
                return;
            }

            await _uiManager._uiLock.WaitAsync();
            try
            {
                var cardsNeedFix = _validator.ForceGetAllCardsForCompletion();

                if (cardsNeedFix.Count == 0)
                {
                    //Debug.WriteLine("[Validate] 无需补全，所有卡片正常");
                    return;
                }

                Debug.WriteLine($"[Validate] 发现 {cardsNeedFix.Count} 个需要补全的卡片");

                foreach (var cardInfo in cardsNeedFix)
                {
                    Debug.WriteLine(
                        $"[补全尝试] Col={cardInfo.Column} Name={cardInfo.CurrentName} " +
                        $"SID={cardInfo.SummonerId} PUUID={cardInfo.Puuid ?? "空"} " +
                        $"NameOnly={cardInfo.NameOnly}");

                    // 真正隐藏：直接修 UI，不查
                    bool noIdentity = cardInfo.SummonerId == 0 &&
                        (string.IsNullOrEmpty(cardInfo.Puuid) || !MatchQueryProcessor.IsUsablePuuid(cardInfo.Puuid));

                    if (noIdentity || cardInfo.CurrentName?.Contains("隐藏") == true && noIdentity)
                    {
                        _validator.FixHiddenPlayerCard(cardInfo.Card);
                        continue;
                    }

                    JToken? playerData = FindPlayerDataByPuuid(teamOne, teamTwo, cardInfo.Puuid)
                                      ?? FindPlayerDataInSession(teamOne, teamTwo, cardInfo.SummonerId);

                    if (playerData == null)
                    {
                        Debug.WriteLine($"[补全失败] 找不到玩家数据 Puuid={cardInfo.Puuid}");
                        continue;
                    }

                    if (cardInfo.NameOnly)
                    {
                        // ========== 只补名字 ==========
                        bool nameOk = await TryFixPlayerNameOnlyAsync(cardInfo, playerData);
                        if (!nameOk)
                        {
                            // 名字仍失败，降级为完整重查一次
                            Debug.WriteLine($"[补全] 仅补名字失败，降级完整重查: {cardInfo.CurrentName}");
                            var matchInfo = await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(playerData, retryTimes: 2);
                            if (matchInfo?.Player != null)
                            {
                                _uiManager.UpdateCardUI(cardInfo.Card, matchInfo);
                                if (matchInfo.Player.SummonerId != 0)
                                    _cache.AddOrUpdateCache(matchInfo.Player.SummonerId, matchInfo);
                                Debug.WriteLine($"[补全成功-降级] {matchInfo.Player.GameName}");
                            }
                            else
                            {
                                Debug.WriteLine($"[补全失败] 名字+战绩均未恢复: {cardInfo.CurrentName}");
                            }
                        }
                    }
                    else
                    {
                        // ========== 完整补全（名字 + 战绩） ==========
                        var matchInfo = await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(playerData, retryTimes: 3);

                        if (matchInfo?.Player != null &&
                            (matchInfo.MatchItems?.Count > 0 ||
                             !string.IsNullOrEmpty(matchInfo.Player.GameName) &&
                             !matchInfo.Player.GameName.Contains("未知") &&
                             !matchInfo.Player.GameName.Contains("失败")))
                        {
                            // 即使战绩为空（很久没打），只要名字正常也算成功
                            _uiManager.UpdateCardUI(cardInfo.Card, matchInfo);
                            if (matchInfo.Player.SummonerId != 0)
                                _cache.AddOrUpdateCache(matchInfo.Player.SummonerId, matchInfo);
                            Debug.WriteLine(
                                $"[补全成功] {matchInfo.Player.GameName} 战绩项:{matchInfo.MatchItems?.Count ?? 0}");
                        }
                        else
                        {
                            var failedInfo = _factory.CreateFailedPlayerInfo(cardInfo.SummonerId, cardInfo.ChampionId);
                            _uiManager.UpdateCardUI(cardInfo.Card, failedInfo);
                            Debug.WriteLine($"[补全失败] {cardInfo.CurrentName}");
                        }
                    }

                    await Task.Delay(250);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ValidateAndCompleteAllCards] 整体异常: {ex.Message}");
            }
            finally
            {
                _uiManager._uiLock.Release();
            }
        }

        /// <summary>
        /// 只刷新玩家显示名称（不重打 SGP 战绩）
        /// </summary>
        private async Task<bool> TryFixPlayerNameOnlyAsync(PlayerCardValidationInfo cardInfo, JToken playerData)
        {
            try
            {
                long sid = cardInfo.SummonerId;
                if (sid == 0)
                    sid = playerData["summonerId"]?.Value<long>() ?? 0;

                string puuid = cardInfo.Puuid;
                if (string.IsNullOrEmpty(puuid))
                    puuid = playerData["puuid"]?.ToString() ?? "";

                JObject summonerInfo = null;

                if (sid != 0)
                    summonerInfo = await Globals.lcuClient.GetGameNameBySummonerId(sid.ToString());
                else if (MatchQueryProcessor.IsUsablePuuid(puuid))
                    summonerInfo = await Globals.lcuClient.GetGameNameByPuuid(puuid);
                else
                    return false;

                string gameName = summonerInfo?["gameName"]?.ToString();
                string tagLine = summonerInfo?["tagLine"]?.ToString() ?? "";

                if (string.IsNullOrWhiteSpace(gameName) ||
                    gameName.Contains("未知") ||
                    gameName == "未知玩家")
                {
                    Debug.WriteLine($"[仅补名字] 仍失败 SID={sid} PUUID={puuid?.Substring(0, Math.Min(8, puuid?.Length ?? 0))}...");
                    return false;
                }

                string fullName = string.IsNullOrEmpty(tagLine) ? gameName : $"{gameName}#{tagLine}";

                FormUiStateManager.SafeInvoke(cardInfo.Card, () =>
                {
                    cardInfo.Card.lblPlayerName.Text = fullName;
                    string privacy = summonerInfo?["privacy"]?.ToString()
                        ?.Equals("PUBLIC", StringComparison.OrdinalIgnoreCase) == true
                        ? "公开" : "隐藏";
                    if (cardInfo.Card.lblPrivacyStatus != null)
                        cardInfo.Card.lblPrivacyStatus.Text = privacy;
                });

                // 缓存同步（用现有 API）
                long cacheKey = sid != 0 ? sid : (summonerInfo?["summonerId"]?.Value<long>() ?? 0);
                if (cacheKey != 0)
                {
                    var all = _cache.GetAllCachedPlayerInfos();
                    if (all.TryGetValue(cacheKey, out var cached) && cached?.Player != null)
                    {
                        cached.Player.GameName = gameName;
                        cached.Player.FullName = fullName;
                        _cache.AddOrUpdateCache(cacheKey, cached);
                    }
                }

                Debug.WriteLine($"[仅补名字成功] {fullName}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[仅补名字异常] {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 进游戏后：用 GameSession 补全的真 puuid，刷新我方「隐藏/未知/空战绩」卡片。
        /// myTeam / myRow 由调用方按当前红蓝方传入。
        /// </summary>
        public async Task CompleteMyTeamHiddenOrUnknownAsync(JArray myTeam, int myRow)
        {
            if (myTeam == null || myTeam.Count == 0)
            {
                Debug.WriteLine("[CompleteMyTeam] 我方队伍为空，跳过");
                return;
            }

            try
            {
                var needFix = new List<PlayerCardValidationInfo>();

                FormUiStateManager.SafeInvokeSync(_form.tableLayoutPanel1, () =>
                {
                    for (int col = 0; col < _form.tableLayoutPanel1.ColumnCount; col++)
                    {
                        var panel = _form.tableLayoutPanel1.GetControlFromPosition(col, myRow) as BorderPanel;
                        if (panel == null || panel.Controls.Count == 0) continue;

                        var card = panel.Controls[0] as PlayerCardControl;
                        if (card == null || card.IsDisposed) continue;

                        string name = card.lblPlayerName.Text?.Trim() ?? "";
                        int listCount = card.ListViewControl?.Items.Count ?? 0;

                        if (name.Contains("加载中") || name.Contains("查询中"))
                            continue;

                        bool isHiddenLike = name.Contains("隐藏");
                        bool isUnknownOrFailed =
                            name.Contains("未知") ||
                            name == "未知玩家" ||
                            name.Contains("失败") ||
                            string.IsNullOrWhiteSpace(name);
                        bool emptyMatches = listCount == 0;

                        if (!isHiddenLike && !isUnknownOrFailed && !emptyMatches)
                            continue;

                        needFix.Add(new PlayerCardValidationInfo
                        {
                            SummonerId = card.CurrentSummonerId,
                            ChampionId = card.CurrentChampionId,
                            Puuid = card.CurrentPuuId ?? "",
                            Row = myRow,
                            Column = col,
                            Card = card,
                            CurrentName = name,
                            NameOnly = !emptyMatches && (isUnknownOrFailed || isHiddenLike),
                            RetryCount = 0
                        });
                    }
                });

                if (needFix.Count == 0)
                {
                    Debug.WriteLine("[CompleteMyTeam] 我方无需补全");
                    return;
                }

                Debug.WriteLine($"[CompleteMyTeam] 发现 {needFix.Count} 张我方卡需要补全 | myRow={myRow}");

                await _uiManager._uiLock.WaitAsync();
                try
                {
                    foreach (var cardInfo in needFix)
                    {
                        JToken playerData = null;

                        if (!string.IsNullOrEmpty(cardInfo.Puuid) &&
                            MatchQueryProcessor.IsUsablePuuid(cardInfo.Puuid))
                        {
                            playerData = myTeam.FirstOrDefault(p =>
                                p["puuid"]?.ToString() == cardInfo.Puuid);
                        }

                        if (playerData == null && cardInfo.ChampionId > 0)
                        {
                            playerData = myTeam.FirstOrDefault(p =>
                                (p["championId"]?.Value<int>() ?? 0) == cardInfo.ChampionId);
                        }

                        if (playerData == null && cardInfo.SummonerId != 0)
                        {
                            playerData = myTeam.FirstOrDefault(p =>
                                (p["summonerId"]?.Value<long>() ?? 0) == cardInfo.SummonerId);
                        }

                        if (playerData == null)
                        {
                            Debug.WriteLine(
                                $"[CompleteMyTeam] 找不到玩家数据 Col={cardInfo.Column} " +
                                $"Champ={cardInfo.ChampionId} Name={cardInfo.CurrentName}");
                            continue;
                        }

                        string realPuuid = playerData["puuid"]?.ToString() ?? "";
                        long realSid = playerData["summonerId"]?.Value<long>() ?? 0;

                        if (!MatchQueryProcessor.IsUsablePuuid(realPuuid) && realSid == 0)
                        {
                            _validator.FixHiddenPlayerCard(cardInfo.Card);
                            Debug.WriteLine($"[CompleteMyTeam] 仍无身份，保持隐藏 Col={cardInfo.Column}");
                            continue;
                        }

                        Debug.WriteLine(
                            $"[CompleteMyTeam] 补全 Col={cardInfo.Column} Name={cardInfo.CurrentName} " +
                            $"SID={realSid} PUUID={(realPuuid.Length >= 8 ? realPuuid.Substring(0, 8) : realPuuid)}... " +
                            $"NameOnly={cardInfo.NameOnly}");

                        if (cardInfo.NameOnly)
                        {
                            bool ok = await TryFixPlayerNameOnlyAsync(cardInfo, playerData);
                            if (!ok)
                            {
                                var matchInfo = await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(
                                    playerData, retryTimes: 2);
                                if (matchInfo?.Player != null)
                                {
                                    _uiManager.UpdateCardUI(cardInfo.Card, matchInfo);
                                    if (matchInfo.Player.SummonerId != 0)
                                        _cache.AddOrUpdateCache(matchInfo.Player.SummonerId, matchInfo);
                                    Debug.WriteLine($"[CompleteMyTeam] 降级完整成功 {matchInfo.Player.GameName}");
                                }
                            }
                        }
                        else
                        {
                            var matchInfo = await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(
                                playerData, retryTimes: 3);
                            if (matchInfo?.Player != null)
                            {
                                _uiManager.UpdateCardUI(cardInfo.Card, matchInfo);
                                if (matchInfo.Player.SummonerId != 0)
                                    _cache.AddOrUpdateCache(matchInfo.Player.SummonerId, matchInfo);
                                Debug.WriteLine(
                                    $"[CompleteMyTeam] 完整成功 {matchInfo.Player.GameName} " +
                                    $"战绩:{matchInfo.MatchItems?.Count ?? 0}");
                            }
                            else
                            {
                                Debug.WriteLine($"[CompleteMyTeam] 完整失败 Col={cardInfo.Column}");
                            }
                        }

                        await Task.Delay(250);
                    }
                }
                finally
                {
                    _uiManager._uiLock.Release();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CompleteMyTeam] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 串行补全某一行：空战绩 / 失败 / 未知 / 隐藏（有可用身份时）
        /// 慢网下比并发 Fill 更稳，专门兜底超时空卡。
        /// </summary>
        public async Task CompleteTeamEmptyOrFailedAsync(JArray team, int row)
        {
            if (team == null || team.Count == 0) return;

            var needFix = new List<PlayerCardValidationInfo>();

            FormUiStateManager.SafeInvokeSync(_form.tableLayoutPanel1, () =>
            {
                for (int col = 0; col < _form.tableLayoutPanel1.ColumnCount; col++)
                {
                    var panel = _form.tableLayoutPanel1.GetControlFromPosition(col, row) as BorderPanel;
                    if (panel == null || panel.Controls.Count == 0) continue;

                    var card = panel.Controls[0] as PlayerCardControl;
                    if (card == null || card.IsDisposed) continue;

                    string name = card.lblPlayerName.Text?.Trim() ?? "";
                    int listCount = card.ListViewControl?.Items.Count ?? 0;

                    // 加载中也纳入：慢网时 Fill 失败后常卡在加载中
                    bool isLoading = name.Contains("加载中") || name.Contains("查询中");
                    bool isBadName =
                        name.Contains("隐藏") ||
                        name.Contains("未知") ||
                        name.Contains("失败") ||
                        string.IsNullOrWhiteSpace(name);
                    bool emptyMatches = listCount == 0;

                    // 只要战绩空，或名字异常，就补（有身份的前提下）
                    if (!emptyMatches && !isBadName && !isLoading)
                        continue;

                    needFix.Add(new PlayerCardValidationInfo
                    {
                        SummonerId = card.CurrentSummonerId,
                        ChampionId = card.CurrentChampionId,
                        Puuid = card.CurrentPuuId ?? "",
                        Row = row,
                        Column = col,
                        Card = card,
                        CurrentName = name,
                        NameOnly = !emptyMatches && isBadName, // 有战绩只补名
                        RetryCount = 0
                    });
                }
            });

            if (needFix.Count == 0)
            {
                Debug.WriteLine($"[CompleteTeam] row={row} 无需补全");
                return;
            }

            Debug.WriteLine($"[CompleteTeam] row={row} 发现 {needFix.Count} 张卡需要补全");

            await _uiManager._uiLock.WaitAsync();
            try
            {
                foreach (var cardInfo in needFix)
                {
                    JToken playerData = null;

                    if (MatchQueryProcessor.IsUsablePuuid(cardInfo.Puuid))
                        playerData = team.FirstOrDefault(p => p["puuid"]?.ToString() == cardInfo.Puuid);

                    if (playerData == null && cardInfo.ChampionId > 0)
                        playerData = team.FirstOrDefault(p =>
                            (p["championId"]?.Value<int>() ?? 0) == cardInfo.ChampionId);

                    if (playerData == null && cardInfo.SummonerId != 0)
                        playerData = team.FirstOrDefault(p =>
                            (p["summonerId"]?.Value<long>() ?? 0) == cardInfo.SummonerId);

                    if (playerData == null)
                    {
                        Debug.WriteLine($"[CompleteTeam] 找不到数据 Col={cardInfo.Column}");
                        continue;
                    }

                    string realPuuid = playerData["puuid"]?.ToString() ?? "";
                    long realSid = playerData["summonerId"]?.Value<long>() ?? 0;

                    if (!MatchQueryProcessor.IsUsablePuuid(realPuuid) && realSid == 0)
                    {
                        _validator.FixHiddenPlayerCard(cardInfo.Card);
                        continue;
                    }

                    // 慢网：串行 + 多试几次
                    var matchInfo = await _matchQueryProcessor.SafeFetchPlayerMatchInfoAsync(
                        playerData, retryTimes: 4);

                    if (matchInfo?.Player != null)
                    {
                        _uiManager.UpdateCardUI(cardInfo.Card, matchInfo);
                        if (matchInfo.Player.SummonerId != 0)
                            _cache.AddOrUpdateCache(matchInfo.Player.SummonerId, matchInfo);
                        Debug.WriteLine(
                            $"[CompleteTeam] 成功 {matchInfo.Player.GameName} 战绩:{matchInfo.MatchItems?.Count ?? 0}");
                    }
                    else
                    {
                        Debug.WriteLine($"[CompleteTeam] 仍失败 Col={cardInfo.Column} Name={cardInfo.CurrentName}");
                    }

                    await Task.Delay(400); // 比 250 略长，减轻超时
                }
            }
            finally
            {
                _uiManager._uiLock.Release();
            }
        }

        public async Task<(JArray? myTeam, JArray? enemyTeam)> TryGetLatestFullTeamsAsync()
        {
            JArray? myTeam = null;
            JArray? enemyTeam = null;

            try
            {
                var champSession = await Globals.lcuClient.GetChampSelectSession();
                if (champSession != null)
                {
                    myTeam = champSession["myTeam"] as JArray;
                    if (myTeam != null && myTeam.Count > 0)
                    {
                        _form._cachedMyTeam = myTeam;
                        //Debug.WriteLine($"[TryGetLatest] 我方从 ChampSelect 获取 ({myTeam.Count}人)");
                    }
                }

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

                        //Debug.WriteLine($"[TryGetLatest] GameSession 补全成功 | 我方:{myTeam?.Count ?? 0} | 敌方:{enemyTeam?.Count ?? 0}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TryGetLatestFullTeamsAsync] 异常: {ex.Message}");
            }

            return (myTeam, enemyTeam);
        }

        public (JArray teamOne, JArray teamTwo) EnsureAllPlayersPresent(
            JArray? teamOne, JArray? teamTwo, JArray selections)
        {
            var finalTeamOne = teamOne?.DeepClone() as JArray ?? new JArray();
            var finalTeamTwo = teamTwo?.DeepClone() as JArray ?? new JArray();

            var existingPuuids = new HashSet<string>();
            foreach (var p in finalTeamOne) existingPuuids.Add(p["puuid"]?.ToString() ?? "");
            foreach (var p in finalTeamTwo) existingPuuids.Add(p["puuid"]?.ToString() ?? "");

            foreach (var sel in selections)
            {
                string? puuid = sel["puuid"]?.ToString();
                if (string.IsNullOrEmpty(puuid) || existingPuuids.Contains(puuid))
                    continue;

                var missingPlayer = new JObject
                {
                    ["puuid"] = puuid,
                    ["championId"] = sel["championId"],
                    ["summonerId"] = 0,
                    ["summonerName"] = "",
                    ["profileIconId"] = 0,
                    ["selectedPosition"] = "NONE"
                };

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