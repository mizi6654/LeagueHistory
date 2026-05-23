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

        public async Task EnterChampSelectAsync()
        {
            // 清理等待面板 + 准备对战面板
            FormUiStateManager.SafeInvoke(_form, () =>
            {
                if (_form._waitingPanel != null)
                {
                    _form.BeginInvoke(new Action(() =>
                    {
                        if (_form.penalGameMatchData.Controls.Contains(_form._waitingPanel))
                            _form.penalGameMatchData.Controls.Remove(_form._waitingPanel);

                        _form._waitingPanel.Dispose();
                        _form._waitingPanel = null;
                        _form.penalGameMatchData.Invalidate();
                        _form.penalGameMatchData.Update();
                    }));
                }

                _form.tableLayoutPanel1.Controls.Clear();
                _form.tableLayoutPanel1.Visible = true;
                _form.tableLayoutPanel1.Dock = DockStyle.Fill;

                if (!_form.penalGameMatchData.Controls.Contains(_form.tableLayoutPanel1))
                    _form.penalGameMatchData.Controls.Add(_form.tableLayoutPanel1);
            });

            // 重置快照
            _lastTeamStructureSnapshot = "";
            _lastChampSelectSnapshotString = "";
            _form.lastChampSelectSnapshotString = "";
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

            _form._cachedMyTeam = myTeam;
            _form.lastChampSelectSnapshotString = heroSnapshot;

            Debug.WriteLine($"[TeamCardDisplay] 我方更新 | 结构变化:{structureChanged} | 英雄变化:{heroChanged}");

            if (structureChanged)
            {
                await _cardManager.CreateBasicCardsOnly(myTeam, isMyTeam: true, row: row);
                await _cardManager.FillPlayerMatchInfoAsync(myTeam, isMyTeam: true, row: row);
            }
            else if (heroChanged)
            {
                await _cardManager.CreateBasicCardsOnly(myTeam, isMyTeam: true, row: row);
            }
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

                if (teamOne == null || teamTwo == null) return;

                bool isInTeamOne = teamOne.Any(t => t["puuid"]?.ToString() == myPuuid);
                JArray enemyTeam = isInTeamOne ? teamTwo : teamOne;

                int enemyRow = isInTeamOne ? 1 : 0;

                _form._cachedEnemyTeam = enemyTeam;

                await _cardManager.CreateBasicCardsOnly(enemyTeam, isMyTeam: false, row: enemyRow);
                await _cardManager.FillPlayerMatchInfoAsync(enemyTeam, isMyTeam: false, row: enemyRow);

                await _cardManager.ValidateAndCompleteAllCards(teamOne, teamTwo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeamCardDisplay] 显示敌方卡片异常: {ex.Message}");
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