using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;

namespace League.Clients
{
    /// <summary>
    /// 聊天服务（选人 / 游戏内）- 增强版，支持多种发送方式
    /// </summary>
    public class ChatService
    {
        private readonly LcuClient _client;

        public ChatService(LcuClient client)
        {
            _client = client;
        }

        /// <summary>
        /// 获取所有聊天会话
        /// </summary>
        public async Task<JArray> GetConversationsAsync()
        {
            var resp = await _client.GetAsync("/lol-chat/v1/conversations");
            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine("[Chat] 获取会话失败");
                return new JArray();
            }

            return JArray.Parse(await resp.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// 向指定会话发送消息
        /// </summary>
        public async Task<bool> SendMessageAsync(string conversationId, string message)
        {
            var payload = new
            {
                body = message
            };

            string json = JsonConvert.SerializeObject(payload);

            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var resp = await _client.PostAsync(
                $"/lol-chat/v1/conversations/{conversationId}/messages",
                content
            );

            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync();
                Debug.WriteLine($"[Chat] 发送失败: {resp.StatusCode} - {errorBody}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 增强版：查找会话（支持 game / championSelect / custom / practiceTool）
        /// </summary>
        //public async Task<string?> FindConversationIdAsync(string type, int maxRetries = 10, int delayMs = 500)
        //{
        //    string[] targetTypes = type.Equals("game", StringComparison.OrdinalIgnoreCase)
        //        ? new[] { "game", "customGame", "practiceTool", "championSelect", "inProgress", ""}
        //        : new[] { type };

        //    for (int attempt = 1; attempt <= maxRetries; attempt++)
        //    {
        //        try
        //        {
        //            var resp = await _client.GetAsync("/lol-chat/v1/conversations");
        //            if (!resp.IsSuccessStatusCode)
        //            {
        //                Debug.WriteLine($"[Chat] 获取会话失败: {resp.StatusCode}");
        //                return null;
        //            }

        //            string rawJson = await resp.Content.ReadAsStringAsync();
        //            var conversations = JArray.Parse(rawJson);

        //            Debug.WriteLine($"[Chat] 第 {attempt} 次查询，会话总数: {conversations.Count}");

        //            if (attempt == 1 || conversations.Count == 0)
        //            {
        //                foreach (var c in conversations)
        //                {
        //                    Debug.WriteLine(
        //                        $"[会话] id={c["id"]}, type={c["type"]}, name={c["name"] ?? c["gameName"] ?? "无"}"
        //                    );
        //                }
        //            }

        //            foreach (var target in targetTypes)
        //            {
        //                var match = conversations.FirstOrDefault(c =>
        //                {
        //                    string convType = c["type"]?.ToString() ?? "";
        //                    string convName = c["gameName"]?.ToString() ?? c["name"]?.ToString() ?? "";

        //                    return convType.Equals(target, StringComparison.OrdinalIgnoreCase)
        //                           || convName.Contains("召唤师峡谷")
        //                           || convName.Contains("扭曲丛林")
        //                           || convName.Contains("极地大乱斗")
        //                           || convName.Contains("海克斯大乱斗")
        //                           || convName.Contains("游戏")
        //                           || convName.Contains("ARAM")
        //                           || convName.Contains("大乱斗")
        //                           || convName.Contains("匹配")
        //                           || convName.Contains("排位");
        //                });

        //                if (match != null)
        //                {
        //                    string? id = match["id"]?.ToString();
        //                    if (!string.IsNullOrEmpty(id))
        //                    {
        //                        Debug.WriteLine($"[Chat] 找到会话 type={target}, id={id}");
        //                        return id;
        //                    }
        //                }
        //            }

        //            if (attempt < maxRetries)
        //            {
        //                Debug.WriteLine($"[Chat] 未找到 {type} 会话，{delayMs}ms 后重试...");
        //                await Task.Delay(delayMs);
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            Debug.WriteLine($"[Chat] 查找会话异常: {ex}");
        //        }
        //    }

        //    Debug.WriteLine($"[Chat] 重试 {maxRetries} 次后仍未找到 {type} 会话");
        //    return null;
        //}

        /// <summary>
        /// 增强版：查找会话（支持多种状态）
        /// </summary>
        public async Task<string?> FindConversationIdAsync(string type, int maxRetries = 3, int delayMs = 500)
        {
            string[] targetTypes = type.ToLower() switch
            {
                "game" => new[] { "game", "champSelect", "customGame", "practiceTool", "inProgress" },
                "champselect" => new[] { "champSelect", "championSelect", "inProgress" },
                "ingame" => new[] { "inProgress", "game", "champSelect" },
                _ => new[] { type }
            };

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var resp = await _client.GetAsync("/lol-chat/v1/conversations");
                    if (!resp.IsSuccessStatusCode)
                    {
                        Debug.WriteLine($"[Chat] 获取会话失败: {resp.StatusCode}");
                        return null;
                    }

                    string rawJson = await resp.Content.ReadAsStringAsync();
                    var conversations = JArray.Parse(rawJson);

                    Debug.WriteLine($"[Chat] 第 {attempt} 次查询，会话总数: {conversations.Count}");

                    if (attempt == 1 || conversations.Count == 0)
                    {
                        foreach (var c in conversations)
                        {
                            Debug.WriteLine(
                                $"[会话] id={c["id"]}, type={c["type"]}, gameStatus={c["gameStatus"]}, state={c["state"]}, name={c["name"] ?? c["gameName"] ?? "无"}"
                            );
                        }
                    }

                    foreach (var conv in conversations)
                    {
                        string convType = conv["type"]?.ToString() ?? "";
                        string gameStatus = conv["gameStatus"]?.ToString() ?? "";
                        string convState = conv["state"]?.ToString() ?? "";
                        string convName = conv["gameName"]?.ToString() ?? conv["name"]?.ToString() ?? "";

                        // 检查是否匹配目标类型
                        foreach (var target in targetTypes)
                        {
                            bool match = false;

                            // 1. 直接匹配 type
                            if (convType.Equals(target, StringComparison.OrdinalIgnoreCase))
                            {
                                match = true;
                            }
                            // 2. 匹配 gameStatus
                            else if (gameStatus.Equals(target, StringComparison.OrdinalIgnoreCase))
                            {
                                match = true;
                            }
                            // 3. 匹配游戏状态（inProgress 特殊处理）
                            else if (target.Equals("inprogress", StringComparison.OrdinalIgnoreCase))
                            {
                                // 检查是否是游戏进行中状态
                                if (gameStatus.Equals("inProgress", StringComparison.OrdinalIgnoreCase) ||
                                    convState.Equals("active", StringComparison.OrdinalIgnoreCase) ||
                                    convName.Contains("游戏进行中") ||
                                    convName.Contains("正在游戏中"))
                                {
                                    match = true;
                                }
                            }
                            // 4. 匹配游戏名称关键词
                            else if (convName.Contains("召唤师峡谷") ||
                                     convName.Contains("扭曲丛林") ||
                                     convName.Contains("极地大乱斗") ||
                                     convName.Contains("ARAM") ||
                                     convName.Contains("匹配") ||
                                     convName.Contains("排位") ||
                                     convName.Contains("自定义"))
                            {
                                match = true;
                            }

                            if (match)
                            {
                                string? id = conv["id"]?.ToString();
                                if (!string.IsNullOrEmpty(id))
                                {
                                    Debug.WriteLine($"[Chat] 找到会话: type={convType}, gameStatus={gameStatus}, state={convState}, name={convName}, id={id}");
                                    return id;
                                }
                            }
                        }
                    }

                    if (attempt < maxRetries)
                    {
                        Debug.WriteLine($"[Chat] 未找到 {type} 会话，{delayMs}ms 后重试...");
                        await Task.Delay(delayMs);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Chat] 查找会话异常: { ex}");
                }
            }

            Debug.WriteLine($"[Chat] 重试 {maxRetries} 次后仍未找到 {type} 会话");
            return null;
        }
    }
}