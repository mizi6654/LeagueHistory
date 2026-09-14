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

        // 快照状态
        private string _lastTeamStructureSnapshot = "";
        private string _lastChampSelectSnapshotString = ""; // 用于英雄变化判断

        public TeamCardDisplayService(FormMain form, PlayerCardManager cardManager)
        {
            _form = form;
            _cardManager = cardManager;
        }

        /// <summary>
        /// 双方加载 + 强化面板准备
        /// </summary>
        /// <returns></returns>
        public async Task EnterChampSelectAsync()
        {
            FormUiStateManager.SafeInvoke(_form, () =>
            {
                // 清掉「正在检测连接 / 等待游戏」一类状态 Panel
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

            // 重置快照
            _lastTeamStructureSnapshot = "";
            _lastChampSelectSnapshotString = "";
            _form.lastChampSelectSnapshotString = "";

            await Task.CompletedTask;
        }

        public async Task ShowBothTeamsFromGameSessionAsync()
        {
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

                var sessionData = await Globals.lcuClient.GetGameSession();
                if (sessionData == null)
                {
                    Debug.WriteLine("[ShowBothTeams] GameSession 为空");
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

                Debug.WriteLine($"[ShowBothTeams] 我方 row={myRow} count={myTeam.Count} | 敌方 row={enemyRow} count={enemyTeam.Count}");

                await _cardManager.CreateBasicCardsOnly(myTeam, isMyTeam: true, row: myRow);
                await _cardManager.FillPlayerMatchInfoAsync(myTeam, isMyTeam: true, row: myRow);
                _form._cachedMyTeam = myTeam;

                await _cardManager.CreateBasicCardsOnly(enemyTeam, isMyTeam: false, row: enemyRow);
                await _cardManager.FillPlayerMatchInfoAsync(enemyTeam, isMyTeam: false, row: enemyRow);
                _form._cachedEnemyTeam = enemyTeam;

                await Task.Delay(500);
                await _cardManager.ValidateAndCompleteAllCards(teamOne, teamTwo);

                Debug.WriteLine("[ShowBothTeams] 双方卡片加载完成");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShowBothTeams] 异常: {ex}");
            }
        }

        public async Task ShowMyTeamCards()
        {
            var session = await Globals.lcuClient.GetChampSelectSession();
            if (session == null) return;

            var myTeam = session["myTeam"] as JArray;
            if (myTeam == null || myTeam.Count == 0) return;

            int row = myTeam[0]?["team"]?.Value<int>() == 1 ? 0 : 1;

            // 结构快照（是否有人进出）
            var structureSnapshot = string.Join("|", myTeam.Select(p =>
                p["summonerId"]?.ToString() ?? "0"));

            // 英雄快照（是否换英雄）
            var heroSnapshot = string.Join("|", myTeam.Select(p =>
                $"{p["summonerId"]?.ToString() ?? "0"}_{p["championId"]?.ToString() ?? "0"}"));

            bool structureChanged = structureSnapshot != _lastTeamStructureSnapshot;
            bool heroChanged = heroSnapshot != _lastChampSelectSnapshotString;

            if (!structureChanged && !heroChanged)
                return;

            _lastTeamStructureSnapshot = structureSnapshot;
            _lastChampSelectSnapshotString = heroSnapshot;

            _form.lastChampSelectSnapshotString = heroSnapshot;

            Debug.WriteLine($"[TeamCardDisplay] 我方更新 | 结构变化:{structureChanged} | 英雄变化:{heroChanged}");

            if (structureChanged)
            {
                await _cardManager.CreateBasicCardsOnly(myTeam, isMyTeam: true, row: row);
                await _cardManager.FillPlayerMatchInfoAsync(myTeam, isMyTeam: true, row: row);
                // 一次补全即可（延迟可缩短至 500ms）
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
            try
            {
                var currentSummoner = await Globals.lcuClient.GetCurrentSummoner();
                if (currentSummoner == null) return;

                string myPuuid = currentSummoner["puuid"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(myPuuid)) return;

                var sessionData = await Globals.lcuClient.GetGameSession();
                if (sessionData == null) return;

                int queueId = sessionData["gameData"]?["queue"]?["id"]?.Value<int>() ?? 0;
                Globals.CurrGameMod = queueId.ToString();

                var teamOne = sessionData["gameData"]?["teamOne"] as JArray;
                var teamTwo = sessionData["gameData"]?["teamTwo"] as JArray;
                var selections = sessionData["gameData"]?["playerChampionSelections"] as JArray;

                // 🔥 核心：补全缺失的玩家（基于 puuid）
                if (selections != null && selections.Count == 10)
                {
                    (teamOne, teamTwo) = _cardManager.EnsureAllPlayersPresent(teamOne, teamTwo, selections);
                    Debug.WriteLine($"[EnsurePlayers] 补全后 → Team1:{teamOne?.Count ?? 0} | Team2:{teamTwo?.Count ?? 0}");
                }

                if (teamOne == null || teamTwo == null) return;

                bool isInTeamOne = teamOne.Any(t => t["puuid"]?.ToString() == myPuuid);
                JArray enemyTeam = isInTeamOne ? teamTwo : teamOne;
                int enemyRow = isInTeamOne ? 1 : 0;

                await _cardManager.CreateBasicCardsOnly(enemyTeam, isMyTeam: false, row: enemyRow);
                await _cardManager.FillPlayerMatchInfoAsync(enemyTeam, isMyTeam: false, row: enemyRow);
                _form._cachedEnemyTeam = enemyTeam;

                // 单次补全 + 较短延迟
                await Task.Delay(500);
                await _cardManager.ValidateAndCompleteAllCards(teamOne, teamTwo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShowEnemyTeamCards] 异常: {ex.Message}");
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