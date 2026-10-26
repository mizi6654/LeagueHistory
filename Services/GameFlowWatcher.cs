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

        private readonly AutoQueueAccepter _autoAccepter;
        private readonly AutoPickService _autoPickService;
        private readonly TeamCardDisplayService _cardDisplayService;
        private readonly GameEndCleanupService _cleanupService;

        private string? _lastHandledPhase;

        // 断线前是否在对局中（用于重连后补卡）
        private bool _wasInGameBeforeDisconnect;

        public GameFlowWatcher(FormMain form, FormUiStateManager uiManager,
            PlayerCardManager cardManager, MatchQueryProcessor matchQueryProcessor)
        {
            _form = form;
            _uiManager = uiManager;
            _cardManager = cardManager;
            _matchQueryProcessor = matchQueryProcessor;

            _autoAccepter = new AutoQueueAccepter(form);
            _autoPickService = new AutoPickService(form);
            _cardDisplayService = new TeamCardDisplayService(form, cardManager);
            _cleanupService = new GameEndCleanupService(form, cardManager, uiManager);

            LcuManager.Instance.PhaseChanged += OnGameflowPhaseChanged;
            LcuManager.Instance.Disconnected += HandleLcuDisconnected;
        }

        #region 启动与停止
        public async void StartGameflowWatcher()
        {
            if (!_uiManager.LcuReady) return;
            //Debug.WriteLine("[GameFlowWatcher] StartGameflowWatcher 就绪（事件已由 LcuManager 驱动）");
        }

        public void StopGameflowWatcher()
        {
            _autoPickService.Stop();
            _cleanupService.Cleanup();
        }

        private void HandleLcuDisconnected()
        {
            if (!_uiManager.LcuReady)
            {
                //Debug.WriteLine("[GameFlowWatcher] 收到 Disconnected，但 LcuReady=false，忽略");
                return;
            }

            Debug.WriteLine("[GameFlowWatcher] 检测到 LCU 断开");

            _wasInGameBeforeDisconnect = _uiManager.IsGame
                || (_form._cachedMyTeam != null && _form._cachedMyTeam.Count > 0)
                || (_form._cachedEnemyTeam != null && _form._cachedEnemyTeam.Count > 0);

            _lastHandledPhase = null;
            _uiManager.LcuReady = false;
            _uiManager.IsGame = false;
            _uiManager.SetLcuUiState(false, false);

            _autoPickService.Stop();
            // 只清快照，不清空已画好的卡片 UI，重连后可直接补敌方
            _cardDisplayService.ClearSnapshots();
        }
        #endregion

        public async Task OnGameflowPhaseChanged(string phase, string? previousPhase)
        {
            if (!_uiManager.LcuReady)
            {
                //Debug.WriteLine($"[Phase Changed] 忽略（LcuReady=false）: {previousPhase ?? "null"} → {phase}");
                return;
            }

            // 刚重连且曾在对局、敌方明显不足时，允许对 InProgress 再处理一次
            bool allowInProgressRetry =
                phase == "InProgress"
                && _wasInGameBeforeDisconnect
                && (_form._cachedEnemyTeam == null || _form._cachedEnemyTeam.Count == 0
                    || _cardManager.CountCardsInRow(1) < 3);

            if (phase == _lastHandledPhase && !allowInProgressRetry)
            {
                Debug.WriteLine($"[Phase Changed] 忽略重复: {phase}");
                return;
            }
            _lastHandledPhase = phase;

            try
            {
                //Debug.WriteLine($"[Phase Changed] {previousPhase ?? "null"} → {phase} | 时间: {DateTime.Now:HH:mm:ss.fff}");

                switch (phase)
                {
                    case "Matchmaking":
                    case "ReadyCheck":
                        _wasInGameBeforeDisconnect = false;

                        // 查找匹配中
                        _form._statusBar?.SetMatching(GetRegionDisplayName());

                        await HandleQueuePhase(phase);
                        break;

                    case "ChampSelect":
                        _cardManager.ResetHiddenSessionLogFlags();

                        // 选人中
                        _form._statusBar?.SetChampSelect(GetRegionDisplayName());

                        await HandleChampSelectStart();
                        break;

                    case "GameStart":
                        _autoPickService.Stop();
                        Debug.WriteLine("[GameFlowWatcher] GameStart：停止自动选人");
                        break;

                    case "InProgress":
                        // 游戏中
                        _form._statusBar?.SetInGame(GetRegionDisplayName());

                        await HandleGameInProgress(previousPhase);
                        _wasInGameBeforeDisconnect = false;
                        break;

                    case "EndOfGame":
                    case "PreEndOfGame":
                    case "WaitingForStats":
                        _wasInGameBeforeDisconnect = false;

                        // 游戏已结束
                        _form._statusBar?.SetGameEnded(GetRegionDisplayName());

                        Debug.WriteLine($"[GameEnd Trigger] 检测到结束阶段: {phase}, previous={previousPhase}");
                        await HandleGameEnd(previousPhase);
                        break;

                    case "Lobby":
                    case "None":
                        _wasInGameBeforeDisconnect = false;

                        // 空闲中
                        _form._statusBar?.SetIdle(GetRegionDisplayName());

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

        /// <summary>
        /// 更新窗体状态条信息
        /// </summary>
        /// <returns></returns>
        private static string? GetRegionDisplayName()
        {
            // 优先 SgpSession（JWT/URL 反查后才有值），再退回 LcuManager
            string? id = FormMain.Globals.sgpClient.SgpServerId;
            if (string.IsNullOrWhiteSpace(id))
                id = LcuManager.Instance.SgpServerId;
            if (string.IsNullOrWhiteSpace(id))
                return null;
            return SgpServerConfig.GetDisplayName(id);
        }

        private async Task HandleQueuePhase(string phase)
        {
            _uiManager.IsGame = false;
            _cardManager.ClearAllCaches();
            _cardManager.ClearGameState();
            _matchQueryProcessor.ClearPlayerMatchCache();
            _cleanupService.Reset();
            _autoAccepter.Reset();

            if (phase == "ReadyCheck" && _form.GetAppConfig()?.EnableAutoAcceptQueue == true)
            {
                int delay = _form.GetAppConfig()?.AutoAcceptDelaySeconds ?? 0;
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

            bool needFullReload =
                _wasInGameBeforeDisconnect
                && (_form._cachedEnemyTeam == null || _form._cachedEnemyTeam.Count == 0
                    || _cardManager.CountCardsInRow(0) + _cardManager.CountCardsInRow(1) < 6);

            if (fromNormalFlow && !needFullReload)
            {
                Debug.WriteLine("[GameFlowWatcher] InProgress（正常流程）：仅加载敌方卡片");
                await _cardDisplayService.ShowEnemyTeamCardsAsync();
            }
            else
            {
                Debug.WriteLine("[GameFlowWatcher] InProgress（中途打开/重连补全）：准备面板并加载双方");
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

            bool shouldHandle = previousPhase == "InProgress"
                             || previousPhase == "WaitingForStats"
                             || previousPhase == "ChampSelect"
                             || previousPhase == "EndOfGame"
                             || previousPhase == "PreEndOfGame";

            if (shouldHandle)
            {
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