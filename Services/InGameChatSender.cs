using League.Clients;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Text;

namespace League.Services
{
    // 游戏内发送核心类（多方案尝试）
    public class InGameChatSender
    {
        private readonly LcuSession _lcuSession;
        private readonly ChatService _chatService;

        public InGameChatSender(LcuSession lcuSession)
        {
            _lcuSession = lcuSession ?? throw new ArgumentNullException(nameof(lcuSession));
            _chatService = lcuSession.ChatService;   // 直接拿现成的
        }

        /// <summary>
        /// 尝试发送游戏内消息，返回是否至少有一种方式成功
        /// </summary>
        public async Task<bool> TrySendAsync(string message)
        {
            Debug.WriteLine($"[InGameChatSender] 开始发送，_chatService 是否 null: {_chatService == null}");

            if (string.IsNullOrWhiteSpace(message)) return false;

            Debug.WriteLine($"[InGameChatSender] 尝试发送 ({message.Length} 字符)");

            // 优先级 1：最直接的 Akari 风格 API
            try
            {
                var payload = new { message };
                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var resp = await _lcuSession.Client.PostAsync(
                    "/lol-game-client-chat/v1/instant-messages", content);

                if (resp.IsSuccessStatusCode)
                {
                    Debug.WriteLine("[方案1] 成功，状态码: " + resp.StatusCode);
                    return true;
                }
                else
                {
                    string error = await resp.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[方案1] 失败，状态码: {resp.StatusCode}，响应: {error}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InGameChatSender] 方案1 异常: {ex.Message}");
            }

            // 优先级 2：使用 ChatService 的增强直发
            try
            {
                bool ok = await _lcuSession.SendGameChatDirectly(message);
                if (ok)
                {
                    Debug.WriteLine("[InGameChatSender] 方案2 ChatService 成功");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InGameChatSender] 方案2 异常: {ex.Message}");
            }

            Debug.WriteLine("[InGameChatSender] 所有方案均失败");
            return false;
        }
    }
}