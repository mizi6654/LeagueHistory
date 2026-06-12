using League.Managers;
using League.Parsers;
using League.UIState;
using Newtonsoft.Json;
using System.Diagnostics;
using static League.FormMain;

namespace League.Services
{
    /// <summary>
    /// 游戏流程协调者（精简版）
    /// </summary>
    public class GameFlowWatcher
    {
        private readonly FormMain _form;
        private readonly FormUiStateManager _uiManager;
        private readonly PlayerCardManager _cardManager;
        private readonly MatchQueryProcessor _matchQueryProcessor;

        // 新分离的服务
        private readonly GameFlowPhaseMonitor _phaseMonitor;
        private readonly AutoQueueAccepter _autoAccepter;
        private readonly AutoPickService _autoPickService;
        private readonly TeamCardDisplayService _cardDisplayService;
        private readonly GameEndCleanupService _cleanupService;

        private CancellationTokenSource? _watcherCts;

        public GameFlowWatcher(FormMain form, FormUiStateManager uiManager,
            PlayerCardManager cardManager, MatchQueryProcessor matchQueryProcessor)
        {
            _form = form;
            _uiManager = uiManager;
            _cardManager = cardManager;
            _matchQueryProcessor = matchQueryProcessor;

            // 初始化各服务
            _phaseMonitor = new GameFlowPhaseMonitor();
            _autoAccepter = new AutoQueueAccepter(form);
            _autoPickService = new AutoPickService(form);
            _cardDisplayService = new TeamCardDisplayService(form, cardManager);
            _cleanupService = new GameEndCleanupService(form, cardManager, uiManager);

            // 订阅阶段变化事件
            _phaseMonitor.PhaseChanged += OnGameflowPhaseChanged;
            // 订阅断线事件,调用主窗体的重连逻辑
            _phaseMonitor.OnLcuDisconnected += HandleLcuDisconnected;
        }

        #region 启动与停止
        public async void StartGameflowWatcher()
        {
            if (!_uiManager.LcuReady) return;

            _watcherCts = new CancellationTokenSource();

            // 确保旧的已停止
            _phaseMonitor.Stop();
            _autoPickService.Stop();
            await _phaseMonitor.StartMonitoringAsync(_watcherCts.Token);
        }

        public void StopGameflowWatcher()
        {
            _watcherCts?.Cancel();
            _watcherCts?.Dispose();
            _watcherCts = null;

            _phaseMonitor.Stop();

            _autoPickService.Stop();
            _cleanupService.Cleanup();
        }

        // <summary>
        /// LCU 断开连接处理
        /// </summary>
        private void HandleLcuDisconnected()
        {
            Debug.WriteLine("[GameFlowWatcher] 检测到 LCU 断开，准备重启连接轮询");

            _uiManager.LcuReady = false;
            _uiManager.IsGame = false;
            _uiManager.SetLcuUiState(false, false);

            // 清理当前监听
            _phaseMonitor.Stop();
            _autoPickService.Stop();

            // 通知主窗体重新启动等待连接的轮询
            _form.StartLcuConnectPolling();
        }
        #endregion

        /// <summary>
        /// 核心：游戏阶段变化时分发处理
        /// </summary>
        public async Task OnGameflowPhaseChanged(string phase, string? previousPhase)
        {
            try
            {
                // 🔥 最重要的调试日志
                Debug.WriteLine($"[Phase Changed] {previousPhase ?? "null"} → {phase} | 时间: {DateTime.Now:HH:mm:ss.fff}");

                switch (phase)
                {
                    case "Matchmaking":
                    case "ReadyCheck":
                        await HandleQueuePhase(phase);
                        break;

                    case "ChampSelect":
                        await HandleChampSelectStart();
                        break;

                    case "InProgress":
                        await HandleGameInProgress();
                        break;

                    // 🔥 扩大触发范围 + 放宽 previousPhase 条件
                    case "EndOfGame":
                    case "PreEndOfGame":
                    case "WaitingForStats":
                        Debug.WriteLine($"[GameEnd Trigger] 检测到结束阶段: {phase}, previous={previousPhase}");
                        await HandleGameEnd(previousPhase);  // 即使 previousPhase 为空也尝试处理
                        break;
                    case "Lobby":
                    case "None":
                        if (_cleanupService.ShouldReturnToLobby())
                        {
                            Debug.WriteLine(
                                $"[Lobby] 检测到刚结束游戏，开始返回模式 {Globals.CurrGameMod}");

                            await _cleanupService.ReturnToSpecificLobbyAsync();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameFlowWatcher] 阶段处理异常: {ex}");
            }
        }

        private async Task HandleQueuePhase(string phase)
        {
            _uiManager.IsGame = false;
            _cardManager.ClearAllCaches();
            _cardManager.ClearGameState();
            _matchQueryProcessor.ClearPlayerMatchCache();
            _cleanupService.Reset();

            // 🔥 重置自动接受状态
            _autoAccepter.Reset();

            if (phase == "ReadyCheck" && _form.GetAppConfig()?.EnableAutoAcceptQueue == true)
            {
                await _autoAccepter.TryAcceptAsync();
            }

            FormUiStateManager.SafeInvoke(_form.imageTabControl1, () =>
            {
                _uiManager.SetLcuUiState(_uiManager.LcuReady, false);
                _form.imageTabControl1.SelectedIndex = 1;
            });
        }

        private async Task HandleChampSelectStart()
        {
            _uiManager.IsGame = true;
            await _cardDisplayService.EnterChampSelectAsync();

            // 🔥 重要：同时启动卡片更新和自动抢英雄
            _autoPickService.StartChampSelectMonitoring(_cardDisplayService);
        }

        private async Task HandleGameInProgress()
        {
            _autoPickService.Stop();
            await _cardDisplayService.ShowEnemyTeamCardsAsync();
        }

        private async Task HandleGameEnd(string? previousPhase)
        {
            Debug.WriteLine($"[HandleGameEnd] 被调用 | previousPhase={previousPhase}");

            // 更宽松的判断逻辑（兼容各种跳变情况）
            bool shouldHandle = previousPhase == "InProgress"
                             || previousPhase == "WaitingForStats"
                             || previousPhase == "ChampSelect"
                             || previousPhase == "EndOfGame"
                             || previousPhase == "PreEndOfGame";

            if (shouldHandle)
            {
                // 给游戏结束打一个标记
                if (!string.IsNullOrEmpty(Globals.CurrGameMod))
                {
                    _cleanupService.MarkGameJustEnded();

                    Debug.WriteLine(
                        $"[HandleGameEnd] 已标记游戏结束，等待Lobby后返回模式 {Globals.CurrGameMod}");
                }

                await _cleanupService.HandleGameEndAsync(previousPhase);
                _autoAccepter.Reset();
                _autoPickService.Reset();
            }
            else
            {
                Debug.WriteLine($"[HandleGameEnd] 条件不满足，跳过处理");
            }
        }

        public void ClearGameState()
        {
            _cardDisplayService.ClearSnapshots();
            _cardManager.ClearGameState();
        }
    }
}