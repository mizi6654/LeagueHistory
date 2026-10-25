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
        //public async Task<JObject> GetSummonerByIdAsync(string summonerId)
        //{
        //    if (string.IsNullOrEmpty(summonerId))
        //    {
        //        return null;
        //    }

        //    // 检查缓存
        //    if (_summonerCache.TryGetValue(summonerId, out var cached))
        //    {
        //        return cached;
        //    }

        //    try
        //    {
        //        var response = await _client.GetAsync($"/lol-summoner/v1/summoners/{summonerId}");

        //        if (!response.IsSuccessStatusCode)
        //        {
        //            return null;
        //        }

        //        var content = await response.Content.ReadAsStringAsync();
        //        var summonerData = JObject.Parse(content);

        //        // 更新缓存
        //        _summonerCache[summonerId] = summonerData;

        //        return summonerData;
        //    }
        //    catch (TaskCanceledException ex)
        //    {
        //        Debug.WriteLine($"[LCU] 请求超时: {ex.Message}");
        //        return null;
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.WriteLine($"根据ID查询召唤师异常: {ex}");
        //        return null;
        //    }
        //}

        /// <summary>
        /// 根据召唤师ID查询召唤师信息（带轻量重试，适配不稳定网络）
        /// </summary>
        public async Task<JObject> GetSummonerByIdAsync(string summonerId, int maxRetry = 3)
        {
            if (string.IsNullOrEmpty(summonerId))
                return null;

            // 检查缓存
            if (_summonerCache.TryGetValue(summonerId, out var cached))
                return cached;

            for (int attempt = 1; attempt <= maxRetry; attempt++)
            {
                try
                {
                    var response = await _client.GetAsync($"/lol-summoner/v1/summoners/{summonerId}");

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            var summonerData = JObject.Parse(content);
                            // 必须有 gameName 才算有效
                            if (!string.IsNullOrEmpty(summonerData["gameName"]?.ToString()))
                            {
                                _summonerCache[summonerId] = summonerData;
                                return summonerData;
                            }
                        }
                    }
                }
                catch (TaskCanceledException ex)
                {
                    Debug.WriteLine($"[LCU] GetSummonerById 超时 ({attempt}/{maxRetry}): {ex.Message}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LCU] GetSummonerById 异常 ({attempt}/{maxRetry}): {ex.Message}");
                }

                if (attempt < maxRetry)
                    await Task.Delay(350 + attempt * 250); // 约 600 / 850 / 1100ms
            }

            return null;
        }

        /// <summary>
        /// 根据 PUUID 查询召唤师信息（SID=0 时的名字兜底）
        /// </summary>
        public async Task<JObject> GetSummonerByPuuidAsync(string puuid, int maxRetry = 3)
        {
            if (string.IsNullOrWhiteSpace(puuid) || puuid.Length < 10)
                return null;

            for (int attempt = 1; attempt <= maxRetry; attempt++)
            {
                try
                {
                    // 使用此接口可以根据puuid拿到玩家名称，已验证
                    var response = await _client.GetAsync(
                        $"/lol-summoner/v2/summoners/puuid/{Uri.EscapeDataString(puuid)}");

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            var data = JObject.Parse(content);
                            if (!string.IsNullOrEmpty(data["gameName"]?.ToString()))
                                return data;
                        }
                    }
                }
                catch (TaskCanceledException ex)
                {
                    Debug.WriteLine($"[LCU] GetSummonerByPuuid 超时 ({attempt}/{maxRetry}): {ex.Message}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LCU] GetSummonerByPuuid 异常 ({attempt}/{maxRetry}): {ex.Message}");
                }

                if (attempt < maxRetry)
                    await Task.Delay(350 + attempt * 250);
            }
            return null;
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
                    Debug.WriteLine($"[LCU] current-summoner 状态码: {response.StatusCode}");
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(content))
                    return null;

                var obj = JObject.Parse(content);
                // 必须有 puuid 才算真正登录完成
                if (string.IsNullOrEmpty(obj["puuid"]?.ToString()))
                {
                    Debug.WriteLine("[LCU] current-summoner 无 puuid，视为未就绪");
                    return null;
                }
                return obj;
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"[LCU] 请求超时: {ex.Message}");
                return null;   // 不要 new JObject()
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取当前召唤师失败: {ex}");
                return null;   // 不要 new JObject()
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