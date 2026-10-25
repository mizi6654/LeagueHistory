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
        Reconnecting,
        Connected
    }

    /// <summary>
    /// 国服向 LCU 连接管理（永久循环）：
    /// - 凭证：仅 LeagueClientUx 命令行 + 本地缓存（无 lockfile、无进程存活判定断线）
    /// - 已连接：以 WebSocket 存活为主；约每 10 秒 HTTP 心跳，连续失败才软重连
    /// - 单次心跳失败绝不 Abort WS，避免误杀连接导致面板卡在「检测中」
    /// - 兼容「进游戏关闭客户端」：UX 没了仍可用缓存尝试保持/恢复 HTTP+WS
    /// </summary>
    public class LcuManager : IDisposable
    {
        public static LcuManager Instance { get; } = new LcuManager();

        public LcuClient HttpClient { get; private set; }
        public LcuConnectionState State { get; private set; } = LcuConnectionState.Disconnected;

        /// <summary>真正可用（HTTP+WS+summoner 就绪）</summary>
        public event Action Connected;

        /// <summary>确认断开（UI 可显示检测中并清面板）</summary>
        public event Action Disconnected;

        /// <summary>软重连中（UI 不要 Clear 战绩面板）</summary>
        public event Action Reconnecting;

        /// <summary>游戏阶段变化 (newPhase, oldPhase)</summary>
        public event Func<string, string, Task> PhaseChanged;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private string _port;
        private string _token;
        private string _lastPhase;
        private bool _isRunning;
        private readonly object _lock = new object();

        // ---------- 心跳 ----------
        private int _heartbeatFailCount;
        private const int HeartbeatIntervalSeconds = 10;
        private const int HeartbeatTimeoutSeconds = 4;
        private const int HeartbeatFailThreshold = 2;   // 连续 2 次心跳失败 → 软重连

        // ---------- 硬断开：不要再 6 轮 ----------
        private int _reconnectFailCount;
        private const int HardDisconnectAfterReconnectFails = 2; // 连续 2 轮建连失败 → 检测中

        // 软重连最长等待（从进入 Reconnecting 起算）
        private DateTime _reconnectingSinceUtc = DateTime.MinValue;
        private static readonly TimeSpan SoftReconnectMax = TimeSpan.FromSeconds(15);

        private readonly string _cacheFilePath;
        private const int CacheValidMinutes = 3600;         // 进游戏关客户端时缓存要能撑住

        // 心跳用的轻量接口（国服可用；建连/心跳都用，避免 current-summoner 误判）
        private const string HeartbeatPath = "/riotclient/auth-token";
        private const string HeartbeatFallbackPath = "/riotclient/app-name";

        public string Region { get; private set; }
        public string RsoPlatformId { get; private set; }
        public string SgpServerId { get; private set; }

        public LcuManager()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            _cacheFilePath = Path.Combine(appDir, "last_lcu_cache.json");
        }

        public Task StartAsync()
        {
            lock (_lock)
            {
                if (_isRunning) return Task.CompletedTask;
                _isRunning = true;
            }

            _cts = new CancellationTokenSource();
            _ = Task.Run(ConnectionLoopAsync, _cts.Token);
            Debug.WriteLine("[LcuManager] 启动永久连接循环（WS 为主 + 10s 心跳）");
            return Task.CompletedTask;
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning) return;
                _isRunning = false;
            }

            try { _cts?.Cancel(); } catch { }
            DisconnectSocketOnly();
            State = LcuConnectionState.Disconnected;
            Debug.WriteLine("[LcuManager] 已停止");
        }

        // ==================== 主循环 ====================
        
        private async Task ConnectionLoopAsync()
        {
            while (_isRunning && _cts != null && !_cts.IsCancellationRequested)
            {
                try
                {
                    // 凭证：Ux 命令行 或 缓存（有凭证 ≠ 已连接，只是可以去试）
                    if (!await TryGetCredentialsAsync())
                    {
                        await EnterDisconnectedAsync("无可用凭证");
                        await SafeDelay(2500);
                        continue;
                    }

                    bool soft = State == LcuConnectionState.Connected
                             || State == LcuConnectionState.Reconnecting;

                    State = soft ? LcuConnectionState.Reconnecting : LcuConnectionState.Connecting;
                    if (soft)
                    {
                        if (_reconnectingSinceUtc == DateTime.MinValue)
                            _reconnectingSinceUtc = DateTime.UtcNow;

                        try { Reconnecting?.Invoke(); } catch { }
                        Debug.WriteLine($"[LcuManager] 软重连 Port={_port}");
                    }
                    else
                    {
                        _reconnectingSinceUtc = DateTime.MinValue;
                        Debug.WriteLine($"[LcuManager] 连接中 Port={_port}");
                    }

                    // 软重连少试几次，更快得出「这轮失败」
                    int attempts = soft ? 4 : 8;
                    bool ok = await TryConnectWithRetryAsync(maxAttempts: attempts, delayMs: 600);

                    if (!ok)
                    {
                        _reconnectFailCount++;
                        Debug.WriteLine($"[LcuManager] 建连失败 count={_reconnectFailCount}");

                        bool tooManyRounds = _reconnectFailCount >= HardDisconnectAfterReconnectFails;
                        bool tooLong = soft
                            && _reconnectingSinceUtc != DateTime.MinValue
                            && (DateTime.UtcNow - _reconnectingSinceUtc) >= SoftReconnectMax;

                        // 真实连不上：轮数到了 或 软重连超过约 15 秒 → 才算断开（出检测中）
                        if (tooManyRounds || tooLong)
                        {
                            _reconnectingSinceUtc = DateTime.MinValue;
                            await EnterDisconnectedAsync(
                                tooLong ? "软重连超时（约15秒仍连不上）" : "连续建连失败");
                        }
                        else if (soft)
                        {
                            try { Reconnecting?.Invoke(); } catch { }
                        }
                        else
                        {
                            // 从未连上过的首次连接失败
                            await EnterDisconnectedAsync("建连失败");
                        }

                        await SafeDelay(1500);
                        continue;
                    }

                    // summoner 尽量等，不作为唯一成败条件
                    bool summonerOk = await WaitForSummonerReadyAsync(maxAttempts: 20, delayMs: 500);
                    if (!summonerOk)
                        Debug.WriteLine("[LcuManager] summoner 未完全就绪，HTTP/WS 已通，继续");

                    // ★ 真正连上：清零所有失败计数
                    State = LcuConnectionState.Connected;
                    _heartbeatFailCount = 0;
                    _reconnectFailCount = 0;
                    _reconnectingSinceUtc = DateTime.MinValue;
                    SaveCache(_port, _token);

                    try { Connected?.Invoke(); }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[LcuManager] Connected 回调异常: {ex.Message}");
                    }

                    Debug.WriteLine("[LcuManager] ★ Connected（进入 WS 接收 + 心跳）");
                    await Task.Delay(200);
                    await QueryCurrentPhaseAsync();

                    await ReceiveLoopWithHeartbeatAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LcuManager] 主循环异常: {ex.Message}");
                }

                // WS/心跳判定链路断了 → 先软重连，不立刻当「客户端退出」
                DisconnectSocketOnly();
                if (State == LcuConnectionState.Connected)
                {
                    State = LcuConnectionState.Reconnecting;
                    _reconnectingSinceUtc = DateTime.UtcNow;
                    try { Reconnecting?.Invoke(); } catch { }
                    Debug.WriteLine("[LcuManager] 链路中断 → Reconnecting");
                }

                await SafeDelay(1500);
            }
        }

        private async Task EnterDisconnectedAsync(string reason)
        {
            DisconnectSocketOnly();
            _reconnectingSinceUtc = DateTime.MinValue;

            if (State != LcuConnectionState.Disconnected)
            {
                State = LcuConnectionState.Disconnected;
                Debug.WriteLine($"[LcuManager] Disconnected: {reason}");
                try { Disconnected?.Invoke(); } catch { }
            }

            await Task.CompletedTask;
        }

        // ==================== 凭证（国服：命令行 + 缓存，无 lockfile）====================
        private Task<bool> TryGetCredentialsAsync()
        {
            try
            {
                var ux = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
                if (ux != null)
                {
                    string cmd = GetCommandLine(ux);
                    if (!string.IsNullOrEmpty(cmd))
                    {
                        string port = Clean(ExtractArgument(cmd, "--app-port="));
                        string token = Clean(ExtractArgument(cmd, "--remoting-auth-token="));

                        // 新增区服
                        string region = Clean(ExtractArgument(cmd, "--region="));
                        string rso = Clean(ExtractArgument(cmd, "--rso-platform-id="));
                        if (string.IsNullOrEmpty(rso))
                            rso = Clean(ExtractArgument(cmd, "--platform-id="));

                        if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token))
                        {
                            _port = port;
                            _token = token;

                            if (!string.IsNullOrEmpty(region)) Region = region;
                            if (!string.IsNullOrEmpty(rso)) RsoPlatformId = rso;
                            SgpServerId = SgpServerConfig.BuildSgpServerId(Region, RsoPlatformId);

                            Debug.WriteLine(
                                $"[LcuManager] 凭证←Ux Port={_port}, Region={Region}, Rso={RsoPlatformId}, SgpId={SgpServerId}");
                            return Task.FromResult(true);
                        }
                    }
                }

                if (LoadCache(out string cp, out string ct))
                {
                    _port = cp;
                    _token = ct;
                    // 缓存里若以后存了 region 可一并恢复；现在至少 port/token 能连
                    Debug.WriteLine($"[LcuManager] 凭证←缓存 Port={_port}");
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LcuManager] 取凭证异常: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        private async Task<bool> TryConnectWithRetryAsync(int maxAttempts, int delayMs)
        {
            for (int i = 1; i <= maxAttempts; i++)
            {
                if (_cts == null || _cts.IsCancellationRequested) return false;
                try
                {
                    await TryGetCredentialsAsync();
                    await ConnectHttpAndWsAsync();
                    Debug.WriteLine($"[LcuManager] 第 {i} 次建连成功");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LcuManager] 第 {i}/{maxAttempts} 建连失败: {ex.Message}");
                    DisconnectSocketOnly();
                    if (i < maxAttempts) await SafeDelay(delayMs);
                }
            }
            return false;
        }

        private async Task ConnectHttpAndWsAsync()
        {
            var newClient = new LcuClient(_port, _token);

            // 建连探测（仅此时用 HTTP，带超时）
            if (!await PingHttpAsync(newClient, HeartbeatTimeoutSeconds))
                throw new Exception("HTTP 建连探测失败");

            var old = HttpClient;
            HttpClient = newClient;
            try { old?.HttpClient?.Dispose(); } catch { }

            DisconnectSocketOnly();
            _ws = new ClientWebSocket();
            _ws.Options.RemoteCertificateValidationCallback = (s, c, ch, e) => true;
            _ws.Options.SetRequestHeader("Authorization",
                "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{_token}")));

            var uri = new Uri($"wss://127.0.0.1:{_port}/");
            await _ws.ConnectAsync(uri, _cts.Token);

            // 订阅 phase（可按需加更多事件）
            await SendWsAsync("[5,\"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase\"]");
        }

        private async Task<bool> WaitForSummonerReadyAsync(int maxAttempts, int delayMs)
        {
            for (int i = 1; i <= maxAttempts; i++)
            {
                if (_cts == null || _cts.IsCancellationRequested) return false;
                try
                {
                    var response = await HttpClient.GetAsync("/lol-summoner/v1/current-summoner");
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            var obj = JObject.Parse(json);
                            if (!string.IsNullOrEmpty(obj["puuid"]?.ToString()))
                            {
                                Debug.WriteLine($"[LcuManager] summoner 就绪，验证用户内容获取成功 ({i})次!");
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LcuManager] 等 summoner ({i}): {ex.Message}");
                }
                await SafeDelay(delayMs);
            }
            return false;
        }

        // ==================== WS 接收 + 心跳（心跳失败不立刻杀 WS）====================
        private async Task ReceiveLoopWithHeartbeatAsync()
        {
            var buffer = new byte[16384];
            var messageBuffer = new StringBuilder();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

            // 后台心跳：只计数，达到阈值才 Abort，让 Receive 退出并走软重连
            var heartbeatTask = Task.Run(async () =>
            {
                while (!linked.Token.IsCancellationRequested && State == LcuConnectionState.Connected)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(HeartbeatIntervalSeconds), linked.Token);
                        if (State != LcuConnectionState.Connected) break;

                        bool ok = await PingHttpAsync(HttpClient, HeartbeatTimeoutSeconds);
                        if (ok)
                        {
                            if (_heartbeatFailCount > 0)
                                Debug.WriteLine("[LcuManager] 心跳恢复");
                            _heartbeatFailCount = 0;
                            continue;
                        }

                        _heartbeatFailCount++;
                        Debug.WriteLine($"[LcuManager] 心跳失败 {_heartbeatFailCount}/{HeartbeatFailThreshold}");

                        // 关键：只有连续失败才认为链路死了，避免偶发超时误杀
                        if (_heartbeatFailCount >= HeartbeatFailThreshold)
                        {
                            Debug.WriteLine("[LcuManager] 心跳连续失败 → 结束接收以软重连");
                            try { _ws?.Abort(); } catch { }
                            break;
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[LcuManager] 心跳任务异常: {ex.Message}");
                    }
                }
            }, linked.Token);

            try
            {
                while (_ws != null
                       && _ws.State == WebSocketState.Open
                       && _cts != null
                       && !_cts.IsCancellationRequested
                       && State == LcuConnectionState.Connected)
                {
                    try
                    {
                        var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Debug.WriteLine("[LcuManager] WS Close 帧");
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
                    catch (OperationCanceledException) { break; }
                    catch (WebSocketException ex)
                    {
                        Debug.WriteLine($"[LcuManager] WS 异常: {ex.Message}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[LcuManager] 接收异常: {ex.Message}");
                    }
                }
            }
            finally
            {
                try { linked.Cancel(); } catch { }
                try { await heartbeatTask; } catch { }
            }
        }

        /// <summary>
        /// 轻量心跳/建连探测。成功返回 true。不抛到外层。
        /// </summary>
        private async Task<bool> PingHttpAsync(LcuClient client, int timeoutSeconds)
        {
            if (client == null) return false;
            try
            {
                if (await PingOneAsync(client, HeartbeatPath, timeoutSeconds))
                    return true;
                return await PingOneAsync(client, HeartbeatFallbackPath, timeoutSeconds);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LcuManager] Ping 异常: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> PingOneAsync(LcuClient client, string path, int timeoutSeconds)
        {
            try
            {
                var getTask = client.GetAsync(path);
                var finished = await Task.WhenAny(getTask, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
                if (finished != getTask) return false;
                var resp = await getTask;
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task ProcessWsMessageAsync(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
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
            catch (JsonReaderException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LcuManager] WS 消息处理: {ex.Message}");
            }
        }

        private async Task QueryCurrentPhaseAsync()
        {
            try
            {
                if (HttpClient == null) return;
                var response = await HttpClient.GetAsync("/lol-gameflow/v1/gameflow-phase");
                if (!response.IsSuccessStatusCode) return;
                string content = (await response.Content.ReadAsStringAsync()).Trim().Trim('"');
                if (!string.IsNullOrEmpty(content) && content != _lastPhase)
                {
                    string old = _lastPhase;
                    _lastPhase = content;
                    if (PhaseChanged != null)
                        await PhaseChanged.Invoke(content, old);
                }
            }
            catch { }
        }

        private async Task SendWsAsync(string json)
        {
            if (_ws == null || _ws.State != WebSocketState.Open || _cts == null) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        /// <summary>只关 WS，不改 State、不 Dispose HttpClient（重连时再替换）</summary>
        private void DisconnectSocketOnly()
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
                               .Wait(400);
                        }
                        catch { }
                    }
                    _ws.Dispose();
                }
            }
            catch { }
            _ws = null;
        }

        private async Task SafeDelay(int ms)
        {
            try
            {
                if (_cts != null)
                    await Task.Delay(ms, _cts.Token);
                else
                    await Task.Delay(ms);
            }
            catch (OperationCanceledException) { }
        }

        // ==================== 缓存 ====================
        private void SaveCache(string port, string token)
        {
            try
            {
                var data = new { Port = port, Token = token, Time = DateTime.Now };
                File.WriteAllText(_cacheFilePath, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LcuManager] 写缓存失败: {ex.Message}");
            }
        }

        private bool LoadCache(out string port, out string token)
        {
            port = null;
            token = null;
            try
            {
                if (!File.Exists(_cacheFilePath)) return false;
                var obj = JObject.Parse(File.ReadAllText(_cacheFilePath));
                var time = obj["Time"]?.ToObject<DateTime>() ?? DateTime.MinValue;
                if ((DateTime.Now - time).TotalMinutes > CacheValidMinutes) return false;
                port = obj["Port"]?.ToString();
                token = obj["Token"]?.ToString();
                return !string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token);
            }
            catch
            {
                return false;
            }
        }

        private static string Clean(string value) =>
            value?.Trim().Trim('"', '\'', ' ', '\r', '\n');

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
                Debug.WriteLine($"[LcuManager] 读命令行失败: {ex.Message}");
            }
            return "";
        }

        private string ExtractArgument(string cmdLine, string key)
        {
            if (string.IsNullOrEmpty(cmdLine)) return null;
            var match = Regex.Match(cmdLine, Regex.Escape(key) + "([^ ]+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        public void Dispose()
        {
            Stop();
            try { HttpClient?.HttpClient?.Dispose(); } catch { }
        }
    }
}