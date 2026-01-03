using System.IO;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;
using League.Extensions;
using League.Managers;
using League.States;
using League.UIState;

namespace League.Services
{
    /// <summary>
    /// 负责监听游戏流程阶段（Gameflow Phase），并在不同阶段执行对应操作
    /// 主要功能：选人阶段显示队伍卡片、自动预选/抢英雄、游戏结束清理等
    /// </summary>
    public class GameFlowWatcher
    {
        private readonly FormMain _form;
        private readonly FormUiStateManager _uiManager;
        private readonly PlayerCardManager _cardManager;

        // 取消令牌：用于控制后台轮询任务
        private CancellationTokenSource? _watcherCts;     // 全局游戏流程监听
        private CancellationTokenSource? _champSelectCts; // 选人阶段专用轮询

        // 状态标志
        private bool _champSelectMessageSent = false; // 防止重复启动热键发送战绩
        private bool _gameEndHandled = false; // 防止重复处理游戏结束
        private bool _hasAutoPreliminated = false; // 只保留这个标志，用于普通模式的预选（ARAM 不需要停止）
        private bool _hasSwappedInAram = false; // 恢复：ARAM 已抢过英雄标志（每局重置，一抢就停）

        public GameFlowWatcher(FormMain form, FormUiStateManager uiManager, PlayerCardManager cardManager)
        {
            _form = form;
            _uiManager = uiManager;
            _cardManager = cardManager;
        }

        #region 1. 全局游戏流程监听（Lobby → Matchmaking → ChampSelect → InProgress → End）

        /// <summary>
        /// 启动后台轮询，监听游戏阶段变化（每秒检查一次）
        /// </summary>
        public async void StartGameflowWatcher()
        {
            if (!_uiManager.LcuReady) return;

            _watcherCts = new CancellationTokenSource();
            var token = _watcherCts.Token;

            try
            {
                await Task.Run(async () =>
                {
                    string? lastPhase = null;

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            string? phase = await Globals.lcuClient.GetGameflowPhase();

                            if (string.IsNullOrEmpty(phase))
                            {
                                OnLcuDisconnected();
                                return; // 退出任务
                            }

                            if (phase != lastPhase)
                            {
                                await HandleGameflowPhase(phase, lastPhase);
                                lastPhase = phase;
                            }

                            await Task.Delay(1000, token); // 正常延迟
                        }
                        catch (TaskCanceledException)
                        {
                            // 正常取消：窗体关闭或手动停止，静默退出
                            return;
                        }
                        catch (Exception ex)
                        {
                            // 真正的网络或其他异常，才记录日志
                            Debug.WriteLine($"[GameflowWatcher] 轮询内部异常：{ex}");
                            // 可选：短暂等待后继续，避免异常轰炸
                            await Task.Delay(2000, token);
                        }
                    }
                }, token);
            }
            catch (TaskCanceledException)
            {
                // 外部也可能收到取消（极少情况），静默忽略
                // 不打印任何日志
            }
            catch (Exception ex)
            {
                // 只有非取消的严重异常才记录
                Debug.WriteLine($"[GameflowWatcher] 严重异常：{ex}");
            }
        }

        /// <summary>
        /// 停止所有后台轮询任务
        /// </summary>
        public void StopGameflowWatcher()
        {
            _watcherCts?.Cancel();
            _watcherCts?.Dispose();
            _watcherCts = null;

            _champSelectCts?.Cancel();
            _champSelectCts?.Dispose();
            _champSelectCts = null;
        }

        /// <summary>
        /// 根据游戏阶段执行对应逻辑
        /// </summary>
        public async Task HandleGameflowPhase(string phase, string? previousPhase)
        {
            switch (phase)
            {
                case "Matchmaking":
                case "ReadyCheck":
                    _uiManager.IsGame = false;
                    _cardManager.ClearGameState();
                    FormUiStateManager.SafeInvoke(_form.imageTabControl1, () =>
                    {
                        _uiManager.SetLcuUiState(_uiManager.LcuReady, _uiManager.IsGame);
                        _form.imageTabControl1.SelectedIndex = 1;
                    });
                    _champSelectMessageSent = false;
                    break;

                case "ChampSelect":
                    _uiManager.IsGame = true;
                    await OnChampSelectStart(); // 进入选人核心逻辑
                    break;

                case "InProgress":
                    _champSelectCts?.Cancel();
                    _champSelectMessageSent = false;
                    await ShowEnemyTeamCards(); // 显示敌方战绩卡片
                    break;

                case "EndOfGame":
                case "PreEndOfGame":
                case "WaitingForStats":
                case "Lobby":
                case "None":
                    _champSelectMessageSent = false;
                    await HandleGameEndPhase(previousPhase);
                    break;
            }
        }

        /// <summary>
        /// 游戏结束后的统一清理（只执行一次）
        /// </summary>
        private async Task HandleGameEndPhase(string? previousPhase)
        {
            if (!_gameEndHandled &&
                (previousPhase == "InProgress" || previousPhase == "WaitingForStats" || previousPhase == "ChampSelect"))
            {
                _gameEndHandled = true;
                await OnGameEnd();
            }
        }

        #endregion

        #region 2. 自动预选 / 抢英雄核心逻辑
        /// <summary>
        /// 每轮选人阶段调用一次：负责普通模式的意图预选 + 随机模式的抢英雄
        /// </summary>
        private async Task TryAutoPreliminaryAsync()
        {
            var preList = await _form.GetPreSelectedHeroesAsync();
            if (!preList.Any()) return;

            var session = await Globals.lcuClient.GetChampSelectSession();
            if (session == null) return;

            int queueId = session["queueId"]?.Value<int>() ?? 0;

            if (queueId == 450) // ARAM
            {
                // 完全移除 _hasSwappedInAram 判断！一直抢！
                await Globals.lcuClient.AutoSwapToHighestPriorityAsync(preList);
                return;
            }

            // 普通模式保持原样
            if (_hasAutoPreliminated) return;
            bool successNormal = await Globals.lcuClient.AutoDeclareIntentAsync(preList);
            if (successNormal) _hasAutoPreliminated = true;
        }
        #endregion

        #region 3. 选人阶段核心轮询（ChampSelect）
        /// <summary>
        /// 进入选人阶段时启动：高频轮询处理队伍显示 + 自动抢英雄
        /// </summary>
        private async Task OnChampSelectStart()
        {
            _champSelectCts?.Cancel();
            _champSelectCts = new CancellationTokenSource();
            var token = _champSelectCts.Token;
            Debug.WriteLine("进入选人阶段");
            // UI 初始化：显示对战数据面板
            FormUiStateManager.SafeInvoke(_form.penalGameMatchData, () =>
            {
                if (_form._waitingPanel != null && _form.penalGameMatchData.Controls.Contains(_form._waitingPanel))
                {
                    _form.penalGameMatchData.Controls.Remove(_form._waitingPanel);
                    _form._waitingPanel.Dispose();
                    _form._waitingPanel = null;
                }
                if (!_form.penalGameMatchData.Controls.Contains(_form.tableLayoutPanel1))
                {
                    _form.tableLayoutPanel1.Dock = DockStyle.Fill;
                    _form.penalGameMatchData.Controls.Add(_form.tableLayoutPanel1);
                }
                _form.tableLayoutPanel1.Visible = true;
                _form.tableLayoutPanel1.Controls.Clear();
            });
            // 重置本局状态
            _gameEndHandled = false;
            _hasAutoPreliminated = false;
            _hasSwappedInAram = false; // 每局重置 ARAM 标志

            // 选人阶段轮询循环
            await Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var currentPhase = await Globals.lcuClient.GetGameflowPhase();
                        if (currentPhase != "ChampSelect") break;
                        await ShowMyTeamCards(); // 更新我方卡片 + 战绩
                        await TryAutoPreliminaryAsync(); // 自动预选或抢英雄

                        // 推荐：恢复高频轮询（随机模式下更快抢英雄）
                        var session = await Globals.lcuClient.GetChampSelectSession();
                        int queueId = session?["queueId"]?.Value<int>() ?? 0;
                        int delay = queueId == 450 ? 250 : 1000;
                        await Task.Delay(delay, token);
                    }
                    catch (TaskCanceledException) { break; }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("选人阶段轮询异常: " + ex.Message);
                    }
                }
            }, token);
        }
        #endregion

        #region 4. 队伍卡片显示
        /// <summary>
        /// 显示我方队伍卡片与战绩（选人阶段每轮更新）
        /// </summary>
        private async Task ShowMyTeamCards()
        {
            var session = await Globals.lcuClient.GetChampSelectSession();
            if (session == null) return;

            Globals.CurrGameMod = session["queueId"]?.ToString() ?? "";
            int queueId = int.TryParse(Globals.CurrGameMod, out int qid) ? qid : 0;

            var myTeam = session["myTeam"] as JArray;
            if (myTeam == null || myTeam.Count == 0) return;

            // 调试：查看队伍英雄选择状态
            //Debug.WriteLine("[session Debug] myTeam summonerId 状态:");
            foreach (var p in myTeam)
            {
                long sid = p["summonerId"]?.Value<long>() ?? 0;
                int champId = p["championId"]?.Value<int>() ?? 0;
                //Debug.WriteLine($" - Player: sid={sid}, championId={champId}");
            }

            // 队伍快照对比，避免重复刷新UI
            var currentSnapshot = myTeam.Select(p =>
                $"{p["summonerId"]?.Value<long>() ?? 0}:{p["championId"]?.Value<int>() ?? 0}").ToList();

            if (_form.lastChampSelectSnapshot.SequenceEqual(currentSnapshot))
                return;

            _form.lastChampSelectSnapshot = currentSnapshot;
            _form._cachedMyTeam = myTeam;

            int row = myTeam[0]?["team"]?.Value<int>() == 1 ? 0 : 1;

            await _cardManager.CreateBasicCardsOnly(myTeam, isMyTeam: true, row: row);
            await _cardManager.FillPlayerMatchInfoAsync(myTeam, isMyTeam: true, row: row);

            // 只启动一次热键发送战绩功能
            if (!_champSelectMessageSent)
            {
                _form.ListenAndSendMessageWhenHotkeyPressed(myTeam);
                _champSelectMessageSent = true;
            }
        }

        /// <summary>
        /// 游戏开始后（InProgress阶段）显示敌方队伍卡片与战绩
        /// </summary>
        private async Task ShowEnemyTeamCards()
        {
            try
            {
                // 获取当前玩家 puuid
                var currentSummoner = await Globals.lcuClient.GetCurrentSummoner();
                if (currentSummoner == null || currentSummoner["puuid"]?.ToString() is not string myPuuid)
                {
                    return;
                }

                // 获取游戏会话数据（包含两队完整信息）
                var sessionData = await Globals.lcuClient.GetGameSession();
                if (sessionData == null) return;

                var teamOne = sessionData["gameData"]?["teamOne"] as JArray;
                var teamTwo = sessionData["gameData"]?["teamTwo"] as JArray;
                if (teamOne == null || teamTwo == null) return;

                // 判断自己在哪一队，避免重复查询
                bool isInTeamOne = teamOne.Any(t =>
                {
                    var puuidToken = t["puuid"];
                    return puuidToken != null && puuidToken.ToString() == myPuuid;
                });

                // 选择敌方队伍和对应行号
                JArray enemyTeam = isInTeamOne ? teamTwo : teamOne;
                int enemyRow = isInTeamOne ? 1 : 0;

                // 缓存并显示敌方卡片
                _form._cachedEnemyTeam = enemyTeam;
                await _cardManager.CreateBasicCardsOnly(enemyTeam, isMyTeam: false, row: enemyRow);
                await _cardManager.FillPlayerMatchInfoAsync(enemyTeam, isMyTeam: false, row: enemyRow);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ShowEnemyTeamCards] 异常: " + ex.ToString());
            }
        }
        #endregion

        #region 5. 游戏结束与清理

        /// <summary>
        /// 游戏结束后清理缓存并重置UI
        /// </summary>
        private async Task OnGameEnd()
        {
            Debug.WriteLine("游戏已结束，正在清空缓存及队伍存储信息，重置UI");

            _cardManager.ClearAllCaches();
            _champSelectCts?.Cancel();
            _form.lastChampSelectSnapshot.Clear();
            RefreshState.ForceMatchRefresh = true;

            _form.InvokeIfRequired(async () =>
            {
                await _form.InitializeDefaultTab();
            });
        }

        #endregion

        #region 6. 辅助方法

        /// <summary>
        /// LCU 断开连接时的处理
        /// </summary>
        private void OnLcuDisconnected()
        {
            _uiManager.LcuReady = false;
            _uiManager.IsGame = false;
            _watcherCts?.Cancel();
            _uiManager.SetLcuUiState(false, false);
            _form.StartLcuConnectPolling();
        }

        /// <summary>
        /// 外部调用：清理游戏相关状态（用于模式切换等）
        /// </summary>
        public void ClearGameState()
        {
            _form.lastChampSelectSnapshot.Clear();
            _cardManager.ClearGameState();
        }

        #endregion
    }
}