// LcuWebSocketManager.cs
using System.Diagnostics;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;

namespace League.Clients
{
    /// <summary>
    /// WebSocket管理器 - 专门负责游戏内消息发送
    /// </summary>
    public class LcuWebSocketManager : IDisposable
    {
        private readonly LcuSession _apiSession;  // 仍然需要API Session来获取连接信息
        private readonly LcuWebSocketService _webSocketService;
        private bool _isInitialized = false;

        public LcuSession ApiSession => _apiSession;
        public LcuWebSocketService WebSocketService => _webSocketService;
        public bool IsWebSocketConnected => _webSocketService?.IsConnected ?? false;
        public bool IsReady => _apiSession != null && _isInitialized;

        public LcuWebSocketManager(LcuSession apiSession)
        {
            _apiSession = apiSession;
            _webSocketService = new LcuWebSocketService();
        }

        /// <summary>
        /// 初始化WebSocket连接
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            try
            {
                Debug.WriteLine("[WebSocket管理器] 开始初始化...");

                // 1. 确保API连接已建立
                if (_apiSession == null || _apiSession.Client == null)
                {
                    Debug.WriteLine("[WebSocket管理器] API会话未初始化");
                    return false;
                }

                // 2. 获取WebSocket连接信息
                var connectionInfo = await GetConnectionInfoFromApi();
                if (connectionInfo == null)
                {
                    Debug.WriteLine("[WebSocket管理器] 无法获取连接信息");
                    return false;
                }

                // 3. 初始化WebSocket连接
                bool success = await _webSocketService.InitializeAsync(
                    connectionInfo.Port,
                    connectionInfo.Token
                );

                if (success)
                {
                    _isInitialized = true;
                    Debug.WriteLine("[WebSocket管理器] WebSocket连接成功");

                    // 测试连接
                    bool testResult = await _webSocketService.TestConnectionAsync();
                    if (testResult)
                    {
                        Debug.WriteLine("[WebSocket管理器] WebSocket连接测试成功");
                    }
                }
                else
                {
                    Debug.WriteLine("[WebSocket管理器] WebSocket连接失败");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket管理器] 初始化异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取连接信息
        /// </summary>
        private async Task<ConnectionInfo> GetConnectionInfoFromApi()
        {
            try
            {
                // 方法1：从API Session的HttpClient中提取
                var client = _apiSession.Client;
                if (client?.BaseAddress != null)
                {
                    // 从BaseAddress获取端口
                    var uri = client.BaseAddress;
                    string port = uri.Port.ToString();

                    // 从Authorization头中提取token
                    string token = ExtractAuthToken(client);

                    if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token))
                    {
                        Debug.WriteLine($"[WebSocket管理器] 从API获取连接信息: 端口={port}");
                        return new ConnectionInfo { Port = port, Token = token };
                    }
                }

                // 方法2：读取进程命令行（备选）
                return GetConnectionInfoFromProcess();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket管理器] 获取连接信息失败: {ex.Message}");
                return GetConnectionInfoFromProcess();
            }
        }

        /// <summary>
        /// 从进程命令行获取连接信息
        /// </summary>
        private ConnectionInfo GetConnectionInfoFromProcess()
        {
            try
            {
                var process = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
                if (process == null) return null;

                string cmdLine = GetCommandLine(process);
                string port = ExtractArgument(cmdLine, "--app-port=");
                string token = ExtractArgument(cmdLine, "--remoting-auth-token=");

                if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token))
                {
                    port = port.Trim('"');
                    token = token.Trim('"');
                    Debug.WriteLine($"[WebSocket管理器] 从进程获取连接信息: 端口={port}");
                    return new ConnectionInfo { Port = port, Token = token };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket管理器] 从进程获取信息失败: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 重新连接WebSocket
        /// </summary>
        private async Task<bool> ReconnectAsync()
        {
            try
            {
                await _webSocketService.DisconnectAsync();

                var connectionInfo = await GetConnectionInfoFromApi();
                if (connectionInfo == null) return false;

                return await _webSocketService.InitializeAsync(
                    connectionInfo.Port,
                    connectionInfo.Token
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket管理器] 重连失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 发送选人阶段消息（仍然使用API，因为已经可用）
        /// </summary>
        public async Task<bool> SendChampSelectMessage(string message)
        {
            return await _apiSession.SendChampSelectMessageAsync(message);
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            _webSocketService?.Dispose();
        }

        #region 辅助方法

        private class ConnectionInfo
        {
            public string Port { get; set; }
            public string Token { get; set; }
        }

        private string ExtractAuthToken(HttpClient client)
        {
            try
            {
                var authHeader = client.DefaultRequestHeaders.Authorization;
                if (authHeader != null && authHeader.Scheme == "Basic")
                {
                    var base64Credentials = authHeader.Parameter;
                    var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(base64Credentials));
                    var parts = credentials.Split(':');
                    if (parts.Length == 2)
                    {
                        return parts[1]; // 返回token部分
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExtractAuthToken] 失败: {ex.Message}");
            }
            return string.Empty;
        }

        private string GetCommandLine(Process process)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"
                );
                using var collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    return obj["CommandLine"]?.ToString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GetCommandLine] 失败: {ex.Message}");
            }
            return "";
        }

        private string ExtractArgument(string cmdLine, string key)
        {
            var match = Regex.Match(cmdLine, key + "([^ ]+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        #endregion
    }
}