using League.Managers;
using League.Parsers;
using League.UIState;
using System.Diagnostics;

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
        }

        #region 启动与停止
        public async void StartGameflowWatcher()
        {
            if (!_uiManager.LcuReady) return;

            _watcherCts = new CancellationTokenSource();
            await _phaseMonitor.StartMonitoringAsync(_watcherCts.Token);
        }

        public void StopGameflowWatcher()
        {
            _watcherCts?.Cancel();
            _watcherCts?.Dispose();
            _watcherCts = null;

            _autoPickService.Stop();
            _cleanupService.Cleanup();
        }
        #endregion

        /// <summary>
        /// 核心：游戏阶段变化时分发处理
        /// </summary>
        public async Task OnGameflowPhaseChanged(string phase, string? previousPhase)
        {
            try
            {
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

                    case "EndOfGame":
                    case "PreEndOfGame":
                    case "WaitingForStats":
                    case "Lobby":
                    case "None":
                        await HandleGameEnd(previousPhase);
                        break;
                }
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("[GameFlowWatcher] 任务被正常取消");
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
            if (previousPhase == "InProgress" || previousPhase == "WaitingForStats" || previousPhase == "ChampSelect")
            {
                await _cleanupService.HandleGameEndAsync(previousPhase);
                _autoAccepter.Reset();           // 额外保险
                _autoPickService.Reset();        // 新增
            }
        }

        public void ClearGameState()
        {
            _cardDisplayService.ClearSnapshots();
            _cardManager.ClearGameState();
        }
    }
}