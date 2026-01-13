using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace League.Clients
{
    /// <summary>
    /// 召唤师信息服务
    /// </summary>
    public class SummonerService
    {
        private readonly LcuClient _client;
        private static readonly ConcurrentDictionary<string, JObject> _summonerCache = new();

        public SummonerService(LcuClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// 根据召唤师名称查询召唤师信息
        /// </summary>
        public async Task<JObject> GetSummonerByNameAsync(string summonerName)
        {
            try
            {
                var response = await _client.GetAsync($"/lol-summoner/v1/summoners?name={Uri.EscapeDataString(summonerName)}");

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
                Debug.WriteLine($"查询召唤师异常: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 根据召唤师ID查询召唤师信息
        /// </summary>
        public async Task<JObject> GetSummonerByIdAsync(string summonerId)
        {
            if (string.IsNullOrEmpty(summonerId))
            {
                return null;
            }

            // 检查缓存
            if (_summonerCache.TryGetValue(summonerId, out var cached))
            {
                return cached;
            }

            try
            {
                var response = await _client.GetAsync($"/lol-summoner/v1/summoners/{summonerId}");

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                var summonerData = JObject.Parse(content);

                // 更新缓存
                _summonerCache[summonerId] = summonerData;

                return summonerData;
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"[LCU] 请求超时: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"根据ID查询召唤师异常: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 获取当前登录召唤师信息
        /// </summary>
        public async Task<JObject> GetCurrentSummonerAsync()
        {
            try
            {
                var response = await _client.GetAsync("/lol-summoner/v1/current-summoner");

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
                return new JObject();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取当前召唤师失败: {ex}");
                return new JObject();
            }
        }

        /// <summary>
        /// 清理缓存
        /// </summary>
        public void ClearCache()
        {
            _summonerCache.Clear();
        }

        /// <summary>
        /// 从缓存中移除指定召唤师
        /// </summary>
        public void RemoveFromCache(string summonerId)
        {
            _summonerCache.TryRemove(summonerId, out _);
        }
    }
}