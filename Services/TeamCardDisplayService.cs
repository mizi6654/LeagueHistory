using League.Infrastructure;
using League.Managers;
using League.UIState;
using Newtonsoft.Json;
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

            _form.lastChampSelectSnapshotString = heroSnapshot;

            Debug.WriteLine($"[TeamCardDisplay] 我方更新 | 结构变化:{structureChanged} | 英雄变化:{heroChanged}");

            if (structureChanged)
            {
                await _cardManager.CreateBasicCardsOnly(myTeam, isMyTeam: true, row: row);
                await _cardManager.FillPlayerMatchInfoAsync(myTeam, isMyTeam: true, row: row);

                // 【新增】我方队伍也进行补全兜底（重点解决隐藏玩家卡住问题）
                await Task.Delay(800);
                await _cardManager.ValidateAndCompleteAllCards(myTeam, _form._cachedEnemyTeam ?? new JArray());

                // 二次补全（更保险）
                await Task.Delay(600);
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

                SaveTeamDataForDebug(sessionData);

                int queueId = sessionData["gameData"]?["queue"]?["id"]?.Value<int>() ?? 0;
                Globals.CurrGameMod = queueId.ToString();

                var teamOne = sessionData["gameData"]?["teamOne"] as JArray;
                var teamTwo = sessionData["gameData"]?["teamTwo"] as JArray;
                var selections = sessionData["gameData"]?["playerChampionSelections"] as JArray;

                // 🔥 核心：补全缺失的玩家（基于 puuid）
                if (selections != null && selections.Count == 10)
                {
                    (teamOne, teamTwo) = EnsureAllPlayersPresent(teamOne, teamTwo, selections);
                    Debug.WriteLine($"[EnsurePlayers] 补全后 → Team1:{teamOne?.Count ?? 0} | Team2:{teamTwo?.Count ?? 0}");
                }

                if (teamOne == null || teamTwo == null) return;

                bool isInTeamOne = teamOne.Any(t => t["puuid"]?.ToString() == myPuuid);
                JArray enemyTeam = isInTeamOne ? teamTwo : teamOne;
                int enemyRow = isInTeamOne ? 1 : 0;



                await _cardManager.CreateBasicCardsOnly(enemyTeam, isMyTeam: false, row: enemyRow);
                await _cardManager.FillPlayerMatchInfoAsync(enemyTeam, isMyTeam: false, row: enemyRow);

                _form._cachedEnemyTeam = enemyTeam;

                // 增强补全
                await Task.Delay(1000);  // 从700ms 增加到1000ms
                await _cardManager.ValidateAndCompleteAllCards(teamOne, teamTwo);

                // 可选：再等一会再补一次（防网络慢）
                await Task.Delay(800);
                await _cardManager.ValidateAndCompleteAllCards(teamOne, teamTwo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShowEnemyTeamCards] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 确保 teamOne 和 teamTwo 包含所有10个 puuid（使用 playerChampionSelections 补全）
        /// </summary>
        private (JArray teamOne, JArray teamTwo) EnsureAllPlayersPresent(
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

        private void SaveTeamDataForDebug(JObject sessionData)
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

        public void ClearSnapshots()
        {
            _lastTeamStructureSnapshot = "";
            _lastChampSelectSnapshotString = "";
            _form.lastChampSelectSnapshotString = "";
        }
    }
}