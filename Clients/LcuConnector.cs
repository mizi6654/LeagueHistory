using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;

namespace League.Clients
{
    /// <summary>
    /// LCU连接管理器
    /// </summary>
    public class LcuConnector
    {
        private const int MaxRetryCount = 10;
        private const int RetryDelayMs = 1000;

        /// <summary>
        /// 初始化LCU连接
        /// </summary>
        /// <returns>连接是否成功</returns>
        public async Task<(bool success, string port, string token)> InitializeAsync()
        {
            for (int i = 0; i < MaxRetryCount; i++)
            {
                var process = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();

                if (process != null)
                {
                    var cmdLine = GetCommandLine(process);
                    var port = ExtractArgument(cmdLine, "--app-port=");
                    var token = ExtractArgument(cmdLine, "--remoting-auth-token=");

                    if (!string.IsNullOrEmpty(port))
                    {
                        port = port.Trim().Trim('"', '\'', ' ', '\r', '\n');
                    }

                    if (!string.IsNullOrEmpty(token))
                    {
                        token = token.Trim().Trim('"', '\'', ' ', '\r', '\n');
                    }

                    if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token))
                    {
                        Debug.WriteLine($"[LCU] 找到参数 → Port: {port}, Token: {token}");
                        return (true, port, token);
                    }

                    Debug.WriteLine("[LCU] LeagueClientUx 启动了，但参数暂时为空，等待重试...");
                }
                else
                {
                    Debug.WriteLine("[LCU] LeagueClientUx 尚未启动，等待...");
                }

                await Task.Delay(RetryDelayMs);
            }

            Debug.WriteLine("[LCU] 多次重试后仍未获取到 LCU 参数");
            return (false, null, null);
        }

        /// <summary>
        /// 获取进程命令行
        /// </summary>
        private string GetCommandLine(Process process)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
                using var collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    return obj["CommandLine"]?.ToString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LCU] 获取命令行失败: {ex.Message}");
            }

            return "";
        }

        /// <summary>
        /// 从命令行提取参数值
        /// </summary>
        private string ExtractArgument(string cmdLine, string key)
        {
            var match = Regex.Match(cmdLine, key + "([^ ]+)");
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}