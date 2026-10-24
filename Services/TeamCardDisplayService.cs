using League.Managers;
using League.UIState;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.Services
{
    /// <summary>
    /// 负责我方和敌方队伍卡片的显示与更新
    /// </summary>
    public class TeamCardDisplayService
    {
        private readonly FormMain _form;
        private readonly PlayerCardManager _cardManager;

        private string _lastTeamStructureSnapshot = "";
        private string _lastChampSelectSnapshotString = "";

        // 防止敌方/双方加载并发重入
        private readonly SemaphoreSlim _enemyLoadLock = new(1, 1);
        private readonly SemaphoreSlim _bothLoadLock = new(1, 1);

        public TeamCardDisplayService(FormMain form, PlayerCardManager cardManager)
        {
            _form = form;
            _cardManager = cardManager;
        }

        public async Task EnterChampSelectAsync()
        {
            FormUiStateManager.SafeInvoke(_form, () =>
            {
                var toRemove = _form.penalGameMatchData.Controls
                    .OfType<Panel>()
                    .Where(p => p != _form.tableLayoutPanel1)
                    .ToList();
                foreach (var p in toRemove)
                {
                    _form.penalGameMatchData.Controls.Remove(p);
                    p.Dispose();
                }

                if (_form._waitingPanel != null)
                {
                    if (_form.penalGameMatchData.Controls.Contains(_form._waitingPanel))
                        _form.penalGameMatchData.Controls.Remove(_form._waitingPanel);
                    _form._waitingPanel.Dispose();
                    _form._waitingPanel = null;
                }

                _form.tableLayoutPanel1.Controls.Clear();
                _form.tableLayoutPanel1.Visible = true;
                _form.tableLayoutPanel1.Dock = DockStyle.Fill;

                if (!_form.penalGameMatchData.Controls.Contains(_form.tableLayoutPanel1))
                    _form.penalGameMatchData.Controls.Add(_form.tableLayoutPanel1);

                _form.tableLayoutPanel1.BringToFront();
                _form.penalGameMatchData.Invalidate();
                _form.penalGameMatchData.Update();
            });

            _lastTeamStructureSnapshot = "";
            _lastChampSelectSnapshotString = "";
            _form.lastChampSelectSnapshotString = "";

            await Task.CompletedTask;
        }

        /// <summary>
        /// 带重试获取 GameSession（网络波动时避免直接放弃）
        /// </summary>
        private async Task<JObject?> GetGameSessionWithRetryAsync(int maxAttempts = 4, int delayMs = 700)
        {
            for (int i = 1; i <= maxAttempts; i++)
            {
                try
                {
                    var sessionData = await Globals.lcuClient.GetGameSession();
                    if (sessionData != null)
                    {
                        var teamOne = sessionData["gameData"]?["teamOne"] as JArray;
                        var teamTwo = sessionData["gameData"]?["teamTwo"] as JArray;
                        if ((teamOne != null && teamOne.Count > 0) || (teamTwo != null && teamTwo.Count > 0))
                        {
                            if (i > 1)
                                //Debug.WriteLine($"[GameSession] 第 {i} 次重试成功");
                            return sessionData;
                        }
                    }
                    //Debug.WriteLine($"[GameSession] 第 {i}/{maxAttempts} 次为空或不完整");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GameSession] 第 {i}/{maxAttempts} 次异常: {ex.Message}");
                }

                if (i < maxAttempts)
                    await Task.Delay(delayMs);
            }
            return null;
        }

        public async Task ShowBothTeamsFromGameSessionAsync()
        {
            if (!await _bothLoadLock.WaitAsync(0))
            {
                //Debug.WriteLine("[ShowBothTeams] 已有加载在进行，跳过重复调用");
                return;
            }

            try
            {
                var currentSummoner = await Globals.lcuClient.GetCurrentSummoner();
                if (currentSummoner == null)
                {
                    Debug.WriteLine("[ShowBothTeams] 当前召唤师为空");
                    return;
                }

                string myPuuid = currentSummoner["puuid"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(myPuuid)) return;

                var sessionData = await GetGameSessionWithRetryAsync();
                if (sessionData == null)
                {
                    Debug.WriteLine("[ShowBothTeams] GameSession 多次重试仍为空");
                    return;
                }

                int queueId = sessionData["gameData"]?["queue"]?["id"]?.Value<int>() ?? 0;
                Globals.CurrGameMod = queueId.ToString();

                var teamOne = sessionData["gameData"]?["teamOne"] as JArray;
                var teamTwo = sessionData["gameData"]?["teamTwo"] as JArray;
                var selections = sessionData["gameData"]?["playerChampionSelections"] as JArray;

                if (selections != null && selections.Count >= 1)
                {
                    (teamOne, teamTwo) = _cardManager.EnsureAllPlayersPresent(teamOne, teamTwo, selections);
                    Debug.WriteLine($"[EnsurePlayers] 补全后 → Team1:{teamOne?.Count ?? 0} | Team2:{teamTwo?.Count ?? 0}");
                }

                if (teamOne == null || teamTwo == null)
                {
                    Debug.WriteLine("[ShowBothTeams] teamOne/teamTwo 为空");
                    return;
                }

                bool isInTeamOne = teamOne.Any(t => t["puuid"]?.ToString() == myPuuid);
                JArray myTeam = isInTeamOne ? teamOne : teamTwo;
                JArray enemyTeam = isInTeamOne ? teamTwo : teamOne;
                int myRow = isInTeamOne ? 0 : 1;
                int enemyRow = isInTeamOne ? 1 : 0;

                //Debug.WriteLine($"[ShowBothTeams] 我方 row={myRow} count={myTeam.Count} | 敌方 row={enemyRow} count={enemyTeam.Count}");

                await _cardManager.CreateBasicCardsOnly(myTeam, isMyTeam: true, row: myRow);
                await _cardManager.FillPlayerMatchInfoAsync(myTeam, isMyTeam: true, row: myRow, forceFill: true);
                _form._cachedMyTeam = myTeam;

                await _cardManager.CreateBasicCardsOnly(enemyTeam, isMyTeam: false, row: enemyRow);
                await _cardManager.FillPlayerMatchInfoAsync(enemyTeam, isMyTeam: false, row: enemyRow, forceFill: true);
                _form._cachedEnemyTeam = enemyTeam;

                await Task.Delay(500);
                await _cardManager.ValidateAndCompleteAllCards(teamOne, teamTwo);

                int enemyCards = _cardManager.CountCardsInRow(enemyRow);
                if (enemyCards < Math.Min(5, enemyTeam.Count))
                {
                    Debug.WriteLine($"[ShowBothTeams] 敌方卡片不足 ({enemyCards}/{enemyTeam.Count})，二次强制补全");
                    await _cardManager.CreateBasicCardsOnly(enemyTeam, isMyTeam: false, row: enemyRow);
                    await _cardManager.FillPlayerMatchInfoAsync(enemyTeam, isMyTeam: false, row: enemyRow, forceFill: true);
                    await Task.Delay(400);
                    await _cardManager.ValidateAndCompleteAllCards(teamOne, teamTwo);
                }

                Debug.WriteLine("[ShowBothTeams] 双方卡片加载完成");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShowBothTeams] 异常: {ex}");
            }
            finally
            {
                _bothLoadLock.Release();
            }
        }

        public async Task ShowMyTeamCards()
        {
            var session = await Globals.lcuClient.GetChampSelectSession();
            if (session == null) return;

            var myTeam = session["myTeam"] as JArray;
            if (myTeam == null || myTeam.Count == 0) return;

            // 仅当我方 myTeam 有隐藏 → 存完整 champ-select session（一次）
            _cardManager.TrySaveHiddenFullSessionOnce("my_team", session);

            int row = myTeam[0]?["team"]?.Value<int>() == 1 ? 0 : 1;

            var structureSnapshot = string.Join("|", myTeam.Select(p =>
                p["summonerId"]?.ToString() ?? "0"));

            var heroSnapshot = string.Join("|", myTeam.Select(p =>
                $"{p["summonerId"]?.ToString() ?? "0"}_{p["championId"]?.ToString() ?? "0"}"));

            bool structureChanged = structureSnapshot != _lastTeamStructureSnapshot;
            bool heroChanged = heroSnapshot != _lastChampSelectSnapshotString;

            if (!structureChanged && !heroChanged)
                return;

            _lastTeamStructureSnapshot = structureSnapshot;
            _lastChampSelectSnapshotString = heroSnapshot;
            _form.lastChampSelectSnapshotString = heroSnapshot;

            //Debug.WriteLine($"[TeamCardDisplay] 我方更新 | 结构变化:{structureChanged} | 英雄变化:{heroChanged}");

            if (structureChanged)
            {
                await _cardManager.CreateBasicCardsOnly(myTeam, isMyTeam: true, row: row);
                await _cardManager.FillPlayerMatchInfoAsync(myTeam, isMyTeam: true, row: row, forceFill: false);
                await Task.Delay(500);
                await _cardManager.ValidateAndCompleteAllCards(myTeam, _form._cachedEnemyTeam ?? new JArray());
            }
            else if (heroChanged)
            {
                await _cardManager.CreateBasicCardsOnly(myTeam, isMyTeam: true, row: row);
            }

            _form._cachedMyTeam = myTeam;
        }

        public async Task ShowEnemyTeamCardsAsync()
        {
            if (!await _enemyLoadLock.WaitAsync(0))
            {
                //Debug.WriteLine("[ShowEnemyTeamCards] 已有加载在进行，跳过重复调用");
                return;
            }

            try
            {
                var currentSummoner = await Globals.lcuClient.GetCurrentSummoner();
                if (currentSummoner == null) return;

                string myPuuid = currentSummoner["puuid"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(myPuuid)) return;

                var sessionData = await GetGameSessionWithRetryAsync();
                if (sessionData == null)
                {
                    Debug.WriteLine("[ShowEnemyTeamCards] GameSession 多次重试仍为空，放弃本次");
                    return;
                }

                // ★ 完整 gameflow session（读秒/加载时通常含 teamOne+teamTwo）
                // 我方曾有隐藏 或 当前 10 人里任一侧有隐藏 → 存完整 gameflow（一次）
                _cardManager.TrySaveHiddenFullSessionOnce("full10_gameflow", sessionData);

                int queueId = sessionData["gameData"]?["queue"]?["id"]?.Value<int>() ?? 0;
                Globals.CurrGameMod = queueId.ToString();

                var teamOne = sessionData["gameData"]?["teamOne"] as JArray;
                var teamTwo = sessionData["gameData"]?["teamTwo"] as JArray;
                var selections = sessionData["gameData"]?["playerChampionSelections"] as JArray;

                if (selections != null && selections.Count >= 1)
                {
                    (teamOne, teamTwo) = _cardManager.EnsureAllPlayersPresent(teamOne, teamTwo, selections);
                    //Debug.WriteLine($"[EnsurePlayers] 补全后 → Team1:{teamOne?.Count ?? 0} | Team2:{teamTwo?.Count ?? 0}");
                }

                if (teamOne == null || teamTwo == null)
                {
                    Debug.WriteLine("[ShowEnemyTeamCards] teamOne/teamTwo 为空");
                    return;
                }

                bool isInTeamOne = teamOne.Any(t => t["puuid"]?.ToString() == myPuuid);
                JArray enemyTeam = isInTeamOne ? teamTwo : teamOne;
                int enemyRow = isInTeamOne ? 1 : 0;

                if (enemyTeam == null || enemyTeam.Count == 0)
                {
                    Debug.WriteLine("[ShowEnemyTeamCards] 敌方队伍为空");
                    return;
                }

                await _cardManager.CreateBasicCardsOnly(enemyTeam, isMyTeam: false, row: enemyRow);
                await _cardManager.FillPlayerMatchInfoAsync(enemyTeam, isMyTeam: false, row: enemyRow, forceFill: true);
                _form._cachedEnemyTeam = enemyTeam;

                await Task.Delay(500);
                await _cardManager.ValidateAndCompleteAllCards(teamOne, teamTwo);

                int enemyCards = _cardManager.CountCardsInRow(enemyRow);
                int expect = Math.Min(5, enemyTeam.Count);
                if (enemyCards < expect)
                {
                    Debug.WriteLine($"[ShowEnemyTeamCards] 敌方卡片不足 ({enemyCards}/{expect})，二次强制补全");
                    await Task.Delay(400);
                    await _cardManager.CreateBasicCardsOnly(enemyTeam, isMyTeam: false, row: enemyRow);
                    await _cardManager.FillPlayerMatchInfoAsync(enemyTeam, isMyTeam: false, row: enemyRow, forceFill: true);
                    await Task.Delay(300);
                    await _cardManager.ValidateAndCompleteAllCards(teamOne, teamTwo);
                }

                Debug.WriteLine($"[ShowEnemyTeamCards] 完成 | 敌方卡片数={_cardManager.CountCardsInRow(enemyRow)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShowEnemyTeamCards] 异常: {ex.Message}");
            }
            finally
            {
                _enemyLoadLock.Release();
            }
        }

        public void ClearSnapshots()
        {
            _lastTeamStructureSnapshot = "";
            _lastChampSelectSnapshotString = "";
            _form.lastChampSelectSnapshotString = "";
        }
    }
}