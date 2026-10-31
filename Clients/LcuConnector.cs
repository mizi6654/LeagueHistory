using System.Diagnostics;

namespace League.Clients
{
    /// <summary>
    /// LCU 连接初始化（首次拿 port/token）
    /// 统一走 ProcessCommandLineHelper：Native + WMI，多进程扫描
    /// </summary>
    public class LcuConnector
    {
        private const int MaxRetryCount = 10;
        private const int RetryDelayMs = 1000;

        /// <summary>
        /// 初始化 LCU 连接
        /// </summary>
        public async Task<(bool success, string port, string token)> InitializeAsync()
        {
            for (int i = 0; i < MaxRetryCount; i++)
            {
                if (ProcessCommandLineHelper.TryFindLcuCredentials(
                        out string port, out string token, out _, out _, out int pid))
                {
                    Debug.WriteLine($"[LCU] 找到参数 → PID={pid}, Port={port}, Token={token}");
                    return (true, port, token);
                }

                bool hasUx = false;
                try
                {
                    hasUx = Process.GetProcessesByName("LeagueClientUx").Length > 0;
                }
                catch { }

                if (hasUx)
                    Debug.WriteLine("[LCU] LeagueClientUx 已启动，但尚未解析到有效 port/token，等待重试...");
                else
                    Debug.WriteLine("[LCU] LeagueClientUx 尚未启动，等待...");

                await Task.Delay(RetryDelayMs);
            }

            Debug.WriteLine("[LCU] 多次重试后仍未获取到 LCU 参数");
            return (false, null, null);
        }
    }
}