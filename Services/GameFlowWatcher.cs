using League.Clients;
using League.Managers;
using League.Parsers;
using League.UIState;
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
        private readonly AutoQueueAccepter _autoAccepter;
        private readonly AutoPickService _autoPickService;
        private readonly TeamCardDisplayService _cardDisplayService;
        private readonly GameEndCleanupService _cleanupService;

        private string? _lastHandledPhase;  // 避免同一 phase 被处理两次

        public GameFlowWatcher(FormMain form, FormUiStateManager uiManager,
            PlayerCardManager cardManager, MatchQueryProcessor matchQueryProcessor)
        {
            _form = form;
            _uiManager = uiManager;
            _cardManager = cardManager;
            _matchQueryProcessor = matchQueryProcessor;

            // 初始化各服务
            _autoAccepter = new AutoQueueAccepter(form);
            _autoPickService = new AutoPickService(form);
            _cardDisplayService = new TeamCardDisplayService(form, cardManager);
            _cleanupService = new GameEndCleanupService(form, cardManager, uiManager);

            // 新订阅
            LcuManager.Instance.PhaseChanged += OnGameflowPhaseChanged;
            LcuManager.Instance.Disconnected += HandleLcuDisconnected;
        }

        #region 启动与停止
        //public async void StartGameflowWatcher()
        //{
        //    if (!_uiManager.LcuReady) return;

        //    _autoPickService.Stop(); // 先停旧的
        //}
        public async void StartGameflowWatcher()
        {
            if (!_uiManager.LcuReady) return;

            // ❌ 删掉这行：_autoPickService.Stop();
            // 选人阶段会在 HandleChampSelectStart 里自行 Start
            // 这里再 Stop 会把刚开的监听杀掉

            Debug.WriteLine("[GameFlowWatcher] StartGameflowWatcher 就绪（事件已由 LcuManager 驱动）");
        }

        public void StopGameflowWatcher()
        {
            _autoPickService.Stop();
            _cleanupService.Cleanup();
        }

        // <summary>
        /// LCU 断开连接处理
        /// </summary>
        private void HandleLcuDisconnected()
        {
            // 如果本来就没就绪，忽略（避免启动阶段误伤）
            if (!_uiManager.LcuReady)
            {
                Debug.WriteLine("[GameFlowWatcher] 收到 Disconnected，但 LcuReady=false，忽略");
                return;
            }

            Debug.WriteLine("[GameFlowWatcher] 检测到 LCU 断开");

            _lastHandledPhase = null;
            _uiManager.LcuReady = false;
            _uiManager.IsGame = false;
            _uiManager.SetLcuUiState(false, false);

            // 只清理业务状态，不要再手动启动轮询
            // LcuManager 内部已经在自动重连
            _autoPickService.Stop();
            ClearGameState();
        }
        #endregion

        /// <summary>
        /// 核心：游戏阶段变化时分发处理
        /// </summary>
        public async Task OnGameflowPhaseChanged(string phase, string? previousPhase)
        {
            // 连接尚未就绪时，只记日志，不处理业务
            if (!_uiManager.LcuReady)
            {
                Debug.WriteLine($"[Phase Changed] 忽略（LcuReady=false）: {previousPhase ?? "null"} → {phase}");
                return;
            }

            // 同一 phase 短时间重复到达则跳过（LcuManager 查询 + 业务里又查了一次）
            if (phase == _lastHandledPhase)
            {
                Debug.WriteLine($"[Phase Changed] 忽略重复: {phase}");
                return;
            }
            _lastHandledPhase = phase;

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

                    case "GameStart":
                        // 选人结束、进加载页：停自动选人即可，卡片可先保留
                        _autoPickService.Stop();
                        Debug.WriteLine("[GameFlowWatcher] GameStart：停止自动选人");
                        break;

                    case "InProgress":
                        await HandleGameInProgress(previousPhase);
                        break;

                    case "EndOfGame":
                    case "PreEndOfGame":
                    case "WaitingForStats":
                        Debug.WriteLine($"[GameEnd Trigger] 检测到结束阶段: {phase}, previous={previousPhase}");
                        await HandleGameEnd(previousPhase);
                        break;

                    case "Lobby":
                    case "None":
                        if (_cleanupService.ShouldReturnToLobby())
                        {
                            Debug.WriteLine($"[Lobby] 检测到刚结束游戏，开始返回模式 {Globals.CurrGameMod}");
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

            // 验证是否自动接受对局，以及接受对局延时时间
            if (phase == "ReadyCheck" && _form.GetAppConfig()?.EnableAutoAcceptQueue == true)
            {
                int delay = _form.GetAppConfig()?.AutoAcceptDelaySeconds ?? 0;
                // 只允许 0 / 5 / 10，防止配置被改乱
                if (delay != 5 && delay != 10) delay = 0;

                await _autoAccepter.TryAcceptAsync(delay);
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

        private async Task HandleGameInProgress(string? previousPhase)
        {
            _autoPickService.Stop();
            _uiManager.IsGame = true;

            bool fromNormalFlow =
                previousPhase == "ChampSelect"
                || previousPhase == "GameStart"
                || (_form._cachedMyTeam != null && _form._cachedMyTeam.Count > 0);

            if (fromNormalFlow)
            {
                // 正常路径：选人已画我方 → 只补敌方，不清空整个面板
                Debug.WriteLine("[GameFlowWatcher] InProgress（正常流程）：仅加载敌方卡片");
                await _cardDisplayService.ShowEnemyTeamCardsAsync();
            }
            else
            {
                // 中途打开：面板可能还是「检测连接」，需要完整初始化 + 双方
                Debug.WriteLine("[GameFlowWatcher] InProgress（中途打开）：准备面板并加载双方");
                await _cardDisplayService.EnterChampSelectAsync();
                await _cardDisplayService.ShowBothTeamsFromGameSessionAsync();
            }

            FormUiStateManager.SafeInvoke(_form, () =>
            {
                _uiManager.SetLcuUiState(true, true);
                if (_form.imageTabControl1.TabPages.Count > 1)
                    _form.imageTabControl1.SelectedIndex = 1;
            });
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