using Newtonsoft.Json;
using System.Diagnostics;

namespace League.Clients
{
    /// <summary>
    /// 消息服务管理器 - 负责选人阶段和游戏内消息发送
    /// </summary>
    public class MessageServiceManager
    {
        private ChatService _chatService;
        private League.Services.GameInputService _gameInputService;

        public MessageServiceManager(ChatService chatService)
        {
            _chatService = chatService;
            _gameInputService = new League.Services.GameInputService();
        }

        /// <summary>
        /// 接受 Ready Check（自动确认匹配）
        /// </summary>
        public async Task<bool> AcceptReadyCheckAsync(LcuClient client)
        {
            try
            {
                var response = await client.PostAsync("/lol-matchmaking/v1/ready-check/accept", new StringContent(""));
                bool success = response.IsSuccessStatusCode;

                Debug.WriteLine($"[自动接受] 执行结果: {(success ? "成功" : "失败")} | Status: {response.StatusCode}");
                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[自动接受] 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 选人阶段发送消息
        /// </summary>
        public async Task<bool> SendChampSelectMessageAsync(string message)
        {
            try
            {
                var chatId = await _chatService.FindConversationIdAsync("championSelect");
                if (string.IsNullOrEmpty(chatId)) return false;

                string[] lines = message.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    await _chatService.SendMessageAsync(chatId, line);
                    await Task.Delay(400);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"选人界面发送失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 游戏内发送消息
        /// </summary>
        public async Task<bool> SendInGameMessageAsync(string message)
        {
            if (_gameInputService == null) return false;
            return await _gameInputService.SendInGameMessageAsync(message);
        }
    }
}