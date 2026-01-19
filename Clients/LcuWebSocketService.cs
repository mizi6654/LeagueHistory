// LcuWebSocketService.cs - 修复版本
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;  // 使用 Newtonsoft.Json 而不是 System.Text.Json
using Newtonsoft.Json.Linq;

namespace League.Clients
{
    /// <summary>
    /// LCU WebSocket服务 - 只负责发送消息，不接收处理
    /// </summary>
    public class LcuWebSocketService : IDisposable
    {
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private readonly object _lock = new object();

        // 连接状态
        public bool IsConnected { get; private set; }

        public LcuWebSocketService()
        {
            _webSocket = new ClientWebSocket();
            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// 初始化WebSocket连接
        /// </summary>
        public async Task<bool> InitializeAsync(string port, string token)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await DisconnectAsync();
                }

                // 配置WebSocket
                _webSocket.Options.RemoteCertificateValidationCallback =
                    (sender, certificate, chain, sslPolicyErrors) => true;

                _webSocket.Options.SetRequestHeader("Authorization",
                    $"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{token}"))}");

                // 建立连接
                var uri = new Uri($"wss://127.0.0.1:{port}/");
                await _webSocket.ConnectAsync(uri, _cts.Token);

                IsConnected = true;
                Debug.WriteLine($"[WebSocket] 连接成功: {uri}");

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] 初始化失败: {ex.Message}");
                IsConnected = false;
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                _cts?.Cancel();

                if (_webSocket?.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "正常关闭",
                        CancellationToken.None
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] 断开连接异常: {ex.Message}");
            }
            finally
            {
                IsConnected = false;
                _webSocket?.Dispose();
                _webSocket = new ClientWebSocket();
            }
        }

        /// <summary>
        /// 发送聊天消息（LCU WebSocket 标准格式）
        /// </summary>
        public async Task<bool> SendChatMessage(string text, string target = "all")
        {
            lock (_lock)
            {
                if (!IsConnected || _webSocket.State != WebSocketState.Open)
                    return false;
            }

            try
            {
                // LCU WebSocket 消息格式：[5, uri, data]
                var message = new object[]
                {
                    5, // LCU WebSocket 操作码
                    "/lol-game-client-chat/v1/instant-messages",
                    new
                    {
                        body = text,
                        summonerName = target,
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }
                };

                string json = JsonConvert.SerializeObject(message);
                Debug.WriteLine($"[WebSocket] 发送消息: {json}");

                var bytes = Encoding.UTF8.GetBytes(json);
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );

                Debug.WriteLine($"[WebSocket] 消息已发送");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] 发送失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 测试连接
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                // 发送一个简单的测试消息
                var testMessage = new object[]
                {
                    5,
                    "/lol-game-client-chat/v1/instant-messages",
                    new { body = "test", summonerName = "all" }
                };

                string json = JsonConvert.SerializeObject(testMessage);
                var bytes = Encoding.UTF8.GetBytes(json);

                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] 测试连接失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            _cts?.Cancel();
            _webSocket?.Dispose();
            _cts?.Dispose();
        }
    }
}