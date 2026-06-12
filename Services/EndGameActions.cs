using League.Clients;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Text;
using static League.FormMain;

namespace League.Services
{
    /// <summary>
    /// 游戏结束后的跳过操作（点赞 + 结算界面）
    /// </summary>
    public class EndGameActions
    {
        private readonly LcuSession _lcuSession;

        public EndGameActions(LcuSession lcuSession)
        {
            _lcuSession = lcuSession ?? throw new ArgumentNullException(nameof(lcuSession));
        }

        private async Task PostAsync(string endpoint, object? body = null)
        {
            try
            {
                var client = _lcuSession.Client;
                if (client == null) return;

                string jsonBody = body == null ? "{}" : JsonConvert.SerializeObject(body);
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(endpoint, content);
                Debug.WriteLine($"[LCU Post] {endpoint} -> {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EndGameActions Post] {endpoint} 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 跳过点赞界面
        /// </summary>
        public async Task SkipHonorAsync()
        {
            try
            {
                await PostAsync("/lol-honor-v2/v1/late-recognition/ack");
                Debug.WriteLine("[EndGameActions] ✅ 已跳过点赞界面");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SkipHonor] 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 跳过结算统计界面
        /// </summary>
        public async Task DismissEndOfGameStatsAsync()
        {
            try
            {
                await PostAsync("/lol-end-of-game/v1/state/dismiss-stats");
                await PostAsync("/lol-gameflow/v1/pre-end-game-transition");
                Debug.WriteLine("[EndGameActions] ✅ 已跳过结算统计界面");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DismissEndOfGameStats] 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 游戏结束 → 直接回到对应模式大厅
        /// </summary>
        public async Task ReturnToSpecificLobbyAsync()
        {
            try
            {
                await Task.Delay(500);

                if (!string.IsNullOrEmpty(Globals.CurrGameMod) && int.TryParse(Globals.CurrGameMod, out int queueId) && queueId > 0)
                {
                    await PostAsync("/lol-lobby/v2/lobby", new { queueId });
                    Debug.WriteLine($"[EndGameActions] ✅ 返回指定模式大厅 queueId = {queueId}");
                }
                else
                {
                    await PostAsync("/lol-gameflow/v1/session/request-lobby");
                    Debug.WriteLine("[EndGameActions] ⚠️ CurrGameMod为空，使用通用返回大厅");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ReturnToSpecificLobby] 异常: {ex.Message}");
            }
        }
    }
}