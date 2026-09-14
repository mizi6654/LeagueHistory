using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Management;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;

namespace League.Clients
{
    public enum LcuConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }

    /// <summary>
    /// 专业级 LCU 连接管理器（HTTP + WebSocket 双通道）
    /// 以 WebSocket 事件驱动为主，HTTP 仅用于主动请求
    /// </summary>
    public class LcuManager : IDisposable
    {
        public static LcuManager Instance { get; } = new LcuManager();

        private bool _hasEverConnected = false;  // 只有真正连上过，断线才通知 UI

        /// <summary>HTTP 客户端（供所有 Service 使用）</summary>
        public LcuClient HttpClient { get; private set; }

        public LcuConnectionState State { get; private set; } = LcuConnectionState.Disconnected;

        /// <summary>连接成功</summary>
        public event Action Connected;

        /// <summary>真正断线（进程消失或多次重连失败）</summary>
        public event Action Disconnected;

        /// <summary>游戏阶段变化 (newPhase, oldPhase)</summary>
        public event Func<string, string, Task> PhaseChanged;

        /// <summary>没有 LCU，但 2999 显示正在对局（关客户端模式）</summary>
        public event Action InGameWithoutLcu;

        // 内部状态
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private string _port;
        private string _token;
        private string _lastPhase;
        private bool _isRunning;
        private readonly object _lock = new object();

        // ========== 对外入口 ==========
        public async Task StartAsync()
        {
            lock (_lock)
            {
                if (_isRunning) return;
                _isRunning = true;
            }

            _cts = new CancellationTokenSource();
            _ = Task.Run(ConnectionLoopAsync, _cts.Token);
            Debug.WriteLine("[LcuManager] 启动连接循环");
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning) return;
                _isRunning = false;
            }

            _cts?.Cancel();
            DisconnectInternal();
            Debug.WriteLine("[LcuManager] 已停止");
        }

        // ========== 主连接循环 ==========

        private async Task ConnectionLoopAsync()
        {
            while (_isRunning && !_cts.IsCancellationRequested)
            {
                try
                {
                    // 1. 获取凭证
                    if (!await TryGetCredentialsAsync())
                    {
                        await SafeDelay(2000);
                        continue;
                    }

                    // 2. 尝试建立连接（带重试，应对客户端刚启动 API 未就绪）
                    State = LcuConnectionState.Connecting;
                    Debug.WriteLine($"[LcuManager] 尝试连接 → Port:{_port}");

                    bool connected = await TryConnectWithRetryAsync(maxAttempts: 15, delayMs: 1500);

                    if (!connected)
                    {
                        Debug.WriteLine("[LcuManager] 多次尝试后仍无法连接，稍后重试...");
                        await SafeDelay(3000);
                        continue; // 注意：这里不要触发 Disconnected
                    }

                    // 3. 真正连上了
                    State = LcuConnectionState.Connected;
                    _hasEverConnected = true;

                    Connected?.Invoke();
                    await Task.Delay(400); // 给主窗体注入 Service 的时间

                    await QueryCurrentPhaseAsync();

                    Debug.WriteLine("[LcuManager] 已连接，进入接收循环");
                    await ReceiveLoopAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LcuManager] 连接循环异常: {ex.Message}");
                }

                // 走到这里说明连接断了
                DisconnectInternal();

                // 只有曾经成功连接过，才通知 UI 断线
                if (_hasEverConnected)
                {
                    _hasEverConnected = false;
                    Disconnected?.Invoke();
                    Debug.WriteLine("[LcuManager] 已断线，3 秒后重试...");
                }
                else
                {
                    Debug.WriteLine("[LcuManager] 尚未成功连接过，继续静默重试...");
                }

                await SafeDelay(3000);
            }
        }

        // ========== 获取 port / token ==========
        private async Task<bool> TryGetCredentialsAsync()
        {
            try
            {
                var process = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
                if (process == null) return false;

                string cmdLine = GetCommandLine(process);
                if (string.IsNullOrEmpty(cmdLine)) return false;

                string port = ExtractArgument(cmdLine, "--app-port=");
                string token = ExtractArgument(cmdLine, "--remoting-auth-token=");

                if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
                    return false;

                _port = port.Trim().Trim('"', '\'', ' ', '\r', '\n');
                _token = token.Trim().Trim('"', '\'', ' ', '\r', '\n');
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LcuManager] 获取凭证失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 带重试的连接：专门应对「进程已启动但 API 还没就绪」
        /// </summary>
        private async Task<bool> TryConnectWithRetryAsync(int maxAttempts, int delayMs)
        {
            for (int i = 1; i <= maxAttempts; i++)
            {
                if (_cts.IsCancellationRequested) return false;

                try
                {
                    // 每次重试都重新读一次凭证（防止客户端重启后 port 变了）
                    await TryGetCredentialsAsync();

                    await ConnectHttpAndWsAsync();
                    Debug.WriteLine($"[LcuManager] 第 {i} 次尝试连接成功");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LcuManager] 第 {i}/{maxAttempts} 次连接失败: {ex.Message}");

                    // 清理半成品连接
                    try
                    {
                        _ws?.Abort();
                        _ws?.Dispose();
                    }
                    catch { }
                    _ws = null;

                    if (i < maxAttempts)
                        await SafeDelay(delayMs);
                }
            }
            return false;
        }

        private async Task ConnectHttpAndWsAsync()
        {
            // --- HTTP ---
            HttpClient?.HttpClient?.Dispose();
            HttpClient = new LcuClient(_port, _token);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var response = await HttpClient.GetAsync("/lol-summoner/v1/current-summoner");
            // 注意：刚登录时 current-summoner 可能暂时 404/空，只要不是「连接被拒绝」就算 HTTP 通了
            // 这里用一个更稳的探活端点
            if (!response.IsSuccessStatusCode)
            {
                // 再试一个几乎总会有的端点
                response = await HttpClient.GetAsync("/riotclient/app-name");
                if (!response.IsSuccessStatusCode)
                    throw new Exception($"HTTP 测试失败: {response.StatusCode}");
            }

            // --- WebSocket ---
            _ws?.Dispose();
            _ws = new ClientWebSocket();
            _ws.Options.RemoteCertificateValidationCallback = (s, c, ch, e) => true;
            _ws.Options.SetRequestHeader("Authorization",
                "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{_token}")));

            var uri = new Uri($"wss://127.0.0.1:{_port}/");
            await _ws.ConnectAsync(uri, _cts.Token);

            // 订阅 phase
            await SendWsAsync("[5,\"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase\"]");

            State = LcuConnectionState.Connected;
        }

        // ========== 接收循环 ==========
        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[16384];
            var messageBuffer = new StringBuilder();

            while (_ws != null && _ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.WriteLine("[LcuManager] WebSocket 收到关闭帧");
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                        if (result.EndOfMessage)
                        {
                            string raw = messageBuffer.ToString();
                            messageBuffer.Clear();
                            await ProcessWsMessageAsync(raw);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException ex)
                {
                    Debug.WriteLine($"[LcuManager] WebSocket 异常: {ex.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LcuManager] 接收异常: {ex.Message}");
                    // 不立刻退出，继续尝试接收
                }
            }
        }


        private async Task ProcessWsMessageAsync(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return; // 空消息直接忽略

            try
            {
                var arr = JArray.Parse(raw);
                if (arr.Count < 3 || arr[0].Value<int>() != 8) return;

                var payload = arr[2] as JObject;
                if (payload == null) return;

                string uri = payload["uri"]?.ToString();
                var data = payload["data"];

                if (uri == "/lol-gameflow/v1/gameflow-phase" && data != null)
                {
                    string phase = data.Type == JTokenType.String
                        ? data.Value<string>()?.Trim('"')
                        : data.ToString();

                    if (!string.IsNullOrEmpty(phase) && phase != _lastPhase)
                    {
                        string oldPhase = _lastPhase;
                        _lastPhase = phase;

                        Debug.WriteLine($"[LcuManager] Phase: {oldPhase ?? "null"} → {phase}");

                        if (PhaseChanged != null)
                            await PhaseChanged.Invoke(phase, oldPhase);
                    }
                }
            }
            catch (JsonReaderException)
            {
                // 非 JSON 或空帧，直接忽略
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LcuManager] 解析消息失败: {ex.Message}");
            }
        }

        // ========== 主动查询当前 phase（连接成功后补一次） ==========
        private async Task QueryCurrentPhaseAsync()
        {
            try
            {
                var response = await HttpClient.GetAsync("/lol-gameflow/v1/gameflow-phase");
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    string phase = content.Trim().Trim('"');
                    if (!string.IsNullOrEmpty(phase) && phase != _lastPhase)
                    {
                        string old = _lastPhase;
                        _lastPhase = phase;
                        if (PhaseChanged != null)
                            await PhaseChanged.Invoke(phase, old);
                    }
                }
            }
            catch { /* 忽略 */ }
        }

        private async Task SendWsAsync(string json)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        private void DisconnectInternal()
        {
            try
            {
                if (_ws != null)
                {
                    if (_ws.State == WebSocketState.Open)
                    {
                        try
                        {
                            _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None)
                               .Wait(500);
                        }
                        catch { }
                    }
                    _ws.Dispose();
                }
            }
            catch { }

            _ws = null;
            State = LcuConnectionState.Disconnected;
        }

        private async Task SafeDelay(int ms)
        {
            try { await Task.Delay(ms, _cts.Token); }
            catch (OperationCanceledException) { }
        }

        // ========== 辅助方法（从原 LcuConnector 复制） ==========
        private string GetCommandLine(Process process)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
                using var collection = searcher.Get();
                foreach (ManagementObject obj in collection)
                    return obj["CommandLine"]?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LcuManager] 获取命令行失败: {ex.Message}");
            }
            return "";
        }

        private string ExtractArgument(string cmdLine, string key)
        {
            var match = Regex.Match(cmdLine, key + "([^ ]+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        public void Dispose()
        {
            Stop();
            HttpClient?.HttpClient?.Dispose();
        }
    }
}