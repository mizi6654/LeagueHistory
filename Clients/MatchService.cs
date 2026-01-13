using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace League.Clients
{
    /// <summary>
    /// 对战记录服务
    /// </summary>
    public class MatchService
    {
        private readonly LcuClient _client;
        private readonly Queue<long> _responseTimeHistory = new();
        private const int MaxHistorySize = 5;
        private readonly SemaphoreSlim _concurrencySemaphore = new(6);

        public MatchService(LcuClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// 分页查询历史战绩
        /// </summary>
        public async Task<JArray> FetchMatchesWithRetryAsync(string puuid, int begIndex, int endIndex, bool isPreheat = false)
        {
            int maxRetries = isPreheat ? 1 : 3;
            int attempt = 0;

            while (attempt < maxRetries)
            {
                attempt++;

                Debug.WriteLine($"[{puuid}] 第{attempt}次尝试，等待信号量...");
                await _concurrencySemaphore.WaitAsync();

                try
                {
                    var timeoutSeconds = attempt == 1 ? 2 : 4;
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

                    var path = $"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex={begIndex}&endIndex={endIndex}";
                    Debug.WriteLine($"[正常请求] {path}");

                    var watch = Stopwatch.StartNew();
                    var response = await _client.GetAsync(path);
                    watch.Stop();

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();

                        if (!isPreheat)
                        {
                            UpdateResponseTimeHistory(watch.ElapsedMilliseconds);
                            Debug.WriteLine($"[正常请求] （默认返回{endIndex}场）耗时: {watch.ElapsedMilliseconds}ms");
                        }

                        var json = JObject.Parse(content);
                        return json["games"]?["games"] as JArray;
                    }

                    Debug.WriteLine($"[响应失败] {response.StatusCode}");
                }
                catch (TaskCanceledException)
                {
                    Debug.WriteLine($"第{attempt}次请求超时");
                    if (isPreheat) break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"请求异常: {ex.Message}");
                }
                finally
                {
                    _concurrencySemaphore.Release();
                }
            }

            return null;
        }

        /// <summary>
        /// 查询最新对战记录（不分页）
        /// </summary>
        public async Task<JArray> FetchLatestMatchesAsync(string puuid, bool isPreheat = false)
        {
            int[] retryDelays = { 10, 15 };

            for (int i = 0; i < retryDelays.Length; i++)
            {
                var timeout = TimeSpan.FromSeconds(retryDelays[i]);

                try
                {
                    using var cts = new CancellationTokenSource(timeout);
                    var response = await _client.GetAsync($"/lol-match-history/v1/products/lol/{puuid}/matches");

                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(content);

                    return json["games"]?["games"] as JArray;
                }
                catch (TaskCanceledException)
                {
                    Debug.WriteLine($"[Fetch超时] {puuid} 超过 {retryDelays[i]} 秒未响应");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[第{i + 1}次尝试失败] 异常: {ex.Message}");
                }

                if (i < retryDelays.Length - 1)
                {
                    Debug.WriteLine($"[等待重试] 即将进行第{i + 2}次尝试");
                    await Task.Delay(1000);
                }
            }

            return null;
        }

        /// <summary>
        /// 获取完整对战详情
        /// </summary>
        public async Task<JObject> GetFullMatchByGameIdAsync(long gameId)
        {
            try
            {
                var response = await _client.GetAsync($"/lol-match-history/v1/games/{gameId}");

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                return JObject.Parse(content);
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"[LCU] 请求超时: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取完整对战信息失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 更新响应时间历史记录
        /// </summary>
        private void UpdateResponseTimeHistory(long elapsedMs)
        {
            _responseTimeHistory.Enqueue(elapsedMs);
            if (_responseTimeHistory.Count > MaxHistorySize)
            {
                _responseTimeHistory.Dequeue();
            }
        }

        /// <summary>
        /// 获取平均响应时间
        /// </summary>
        public double GetAverageResponseTime()
        {
            if (_responseTimeHistory.Count == 0) return 0;
            return _responseTimeHistory.Average();
        }
    }
}