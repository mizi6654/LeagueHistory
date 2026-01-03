// 新增文件：LcuWebSocketClient.cs
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace League.Clients
{
    public class LcuWebSocketClient : IDisposable
    {
        private ClientWebSocket _ws;
        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public event Action<string> OnPhaseChanged;
        public event Action<JObject> OnChampSelectSession;
        public event Action<JObject> OnGameSession;

        public async Task<bool> ConnectAsync(string port, string token)
        {
            try
            {
                _ws = new ClientWebSocket();
                _ws.Options.SetRequestHeader("Authorization",
                    "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"LeagueClient:{token}")));

                await _ws.ConnectAsync(new Uri($"wss://127.0.0.1:{port}/"), CancellationToken.None);

                // 订阅所有事件
                await SendAsync(new object[] { 5, "OnJsonApiEvent" });

                _ = Task.Run(ReceiveLoop);
                Debug.WriteLine($"[WS] WebSocket 连接成功！Port={port}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WS] 连接失败: " + ex.Message);
                return false;
            }
        }

        private async Task SendAsync(object obj)
        {
            var json = JsonSerializer.Serialize(obj);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[1024 * 1024];
            while (_ws.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var array = JArray.Parse(json);

                    if (array.Count >= 3)
                    {
                        var uri = array[2]["uri"]?.ToString();
                        var data = array[2]["data"] as JObject;

                        if (uri == "/lol-gameflow/v1/gameflow-phase")
                        {
                            var phase = data?.ToString().Trim('"');
                            OnPhaseChanged?.Invoke(phase ?? "None");
                        }
                        else if (uri == "/lol-champ-select/v1/session")
                        {
                            OnChampSelectSession?.Invoke(data);
                        }
                        else if (uri == "/lol-gameflow/v1/session")
                        {
                            OnGameSession?.Invoke(data);
                        }
                    }
                }
                catch { }
            }
        }

        public void Dispose() => _ws?.Dispose();
    }
}