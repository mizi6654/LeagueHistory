using League.Extensions;
using League.Managers;
using League.States;
using League.UIState;
using System.Diagnostics;

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

        private bool _gameEndHandled = false;

        public GameEndCleanupService(FormMain form, PlayerCardManager cardManager, FormUiStateManager uiManager)
        {
            _form = form;
            _cardManager = cardManager;
            _uiManager = uiManager;
        }

        public async Task HandleGameEndAsync(string? previousPhase)
        {
            // 放宽条件
            if (previousPhase == "InProgress" ||
                previousPhase == "WaitingForStats" ||
                previousPhase == "ChampSelect" ||
                previousPhase == "EndOfGame" ||
                previousPhase == "PreEndOfGame")
            {
                if (_gameEndHandled)
                {
                    Debug.WriteLine("[HandleGameEndAsync] 已处理过，跳过重复执行");
                    return;
                }

                _gameEndHandled = true;
                await OnGameEnd();
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

            await Task.Delay(1000);

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
        }

        public void Reset() => _gameEndHandled = false;
    }
}