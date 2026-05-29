using System.Diagnostics;

namespace League.Clients
{
    /// <summary>
    /// LCU 连接管理器 - 负责连接和初始化
    /// </summary>
    public class LcuConnectionManager
    {
        private LcuConnector _connector;
        private LcuClient _lcuClient;

        public LcuClient Client => _lcuClient;

        public async Task<(bool success, LcuClient client)> InitializeAsync()
        {
            _connector = new LcuConnector();

            var (success, port, token) = await _connector.InitializeAsync();

            if (!success)
            {
                Debug.WriteLine("[LCU] 连接初始化失败");
                return (false, null);
            }

            try
            {
                _lcuClient = new LcuClient(port, token);

                // 测试连接
                if (await TestConnectionAsync())
                {
                    Debug.WriteLine("[LCU] 初始化成功，已连接 LCU API");
                    return (true, _lcuClient);
                }

                return (false, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LCU] 初始化异常: {ex.Message}");
                return (false, null);
            }
        }

        private async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _lcuClient.GetAsync("/lol-summoner/v1/current-summoner");

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                Debug.WriteLine($"[LCU] LCU 返回异常状态码: {response.StatusCode}");
                return false;
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"[LCU] 请求超时: {ex.Message}");
                return false;
            }
            catch (HttpRequestException)
            {
                Debug.WriteLine($"[LCU] 由于目标计算机积极拒绝，无法连接。");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LCU] 其他异常: {ex}");
                return false;
            }
        }

        public void Dispose()
        {
            _lcuClient?.HttpClient?.Dispose();
        }
    }
}