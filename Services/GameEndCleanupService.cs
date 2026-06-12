using League.Extensions;
using League.Managers;
using League.States;
using League.UIState;
using System.Diagnostics;
using static League.FormMain;

namespace League.Services
{
    /// <summary>
    /// 游戏结束后的清理服务
    /// </summary>
    public class GameEndCleanupService
    {
        private readonly FormMain _form;
        private readonly PlayerCardManager _cardManager;
        private readonly FormUiStateManager _uiManager;
        public readonly EndGameActions? _endGameActions;

        private bool _gameEndHandled = false;

        private bool _gameJustEnded = false;

        public void MarkGameJustEnded() => _gameJustEnded = true;
        public bool ShouldReturnToLobby() => _gameJustEnded;

        public GameEndCleanupService(FormMain form, PlayerCardManager cardManager, FormUiStateManager uiManager)
        {
            _form = form;
            _cardManager = cardManager;
            _uiManager = uiManager;
            _endGameActions = new EndGameActions(Globals.lcuClient);
        }

        public async Task HandleGameEndAsync(string? previousPhase)
        {
            Debug.WriteLine($"[HandleGameEndAsync] 被调用 | previousPhase = {previousPhase}");

            // ==================== 游戏结束检测 ====================
            bool isGameEndTransition = previousPhase == "InProgress" ||
                                       previousPhase == "WaitingForStats" ||
                                       previousPhase == "ChampSelect" ||
                                       previousPhase == "EndOfGame" ||
                                       previousPhase == "PreEndOfGame";

            if (!isGameEndTransition)
            {
                return;
            }

            if (_gameEndHandled)
            {
                Debug.WriteLine("[HandleGameEndAsync] 已处理过，跳过重复执行");
                return;
            }

            _gameEndHandled = true;

            // ==================== 关键优化：尽早执行跳过 ====================
            await ExecuteSkipActionsAsync();

            // ==================== 原有清理逻辑（异步不阻塞跳过） ====================
            _ = OnGameEnd();   // 使用 fire-and-forget，避免阻塞
        }

        /// <summary>
        /// 尽早跳过点赞和结算界面
        /// </summary>
        private async Task ExecuteSkipActionsAsync()
        {
            var config = _form.GetAppConfig();
            if (config == null) return;

            try
            {
                // 跳过点赞界面
                if (config.EnableSkipHonor && _endGameActions != null)
                {
                    await Task.Delay(300);        // 进一步提前
                    await _endGameActions.SkipHonorAsync();
                }

                // 跳过结算界面
                if (config.EnableSkipEndOfGameStats && _endGameActions != null)
                {
                    await Task.Delay(300);        // 结算界面通常比点赞晚一点
                    await _endGameActions.DismissEndOfGameStatsAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExecuteSkipActionsAsync] 异常: {ex.Message}");
            }
        }

        public async Task ReturnToSpecificLobbyAsync()
        {
            if (_endGameActions == null) return;
            try
            {
                await Task.Delay(600);
                await _endGameActions.ReturnToSpecificLobbyAsync();
                _gameJustEnded = false;   // 执行完重置
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ReturnToSpecificLobbyAsync] 异常: {ex.Message}");
                _gameJustEnded = false;
            }
        }

        private async Task OnGameEnd()
        {
            Debug.WriteLine("游戏结束，执行清理...");

            _cardManager.ClearAllCaches();
            _cardManager.ClearGameState();

            if (!RefreshState.ForceMatchRefresh)
            {
                RefreshState.ForceMatchRefresh = true;
            }

            await Task.Delay(800);   // 适当保留一点延迟给战绩刷新

            _form.InvokeIfRequired(async () =>
            {
                try
                {
                    await _form.InitializeDefaultTab();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GameEndCleanup] InitializeDefaultTab 异常: {ex}");
                }
            });
        }

        public void Cleanup()
        {
            _gameEndHandled = false;
            _gameJustEnded = false;
        }

        //public void Reset() => _gameEndHandled = false;
        public void Reset()
        {
            _gameEndHandled = false;
        }
    }
}