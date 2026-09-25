using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace League.Clients
{
    /// <summary>
    /// SGP 会话：复用 HttpClient + 全局限流，避免游戏内 10 人卡片把国服 SGP 打爆
    /// </summary>
    public class SgpSession
    {
        private HttpClient _lcuClient;
        private string _accessToken;
        private string _entitlementsToken;
        private string _puuid;
        private string _summonerId;
        private string _sgpUrl;
        private string _leagueSessionToken;

        // 全局复用一个 SGP HttpClient（线程安全）
        private static readonly HttpClient _sharedSgpClient = CreateSharedSgpClient();

        // 国服 SGP 对并发敏感：最多同时 2 个请求
        private static readonly SemaphoreSlim _sgpLimiter = new(2, 2);

        // 单次请求超时（秒）—— 比原来的 30 更合理，失败快速重试
        private const int RequestTimeoutSeconds = 12;

        private static HttpClient CreateSharedSgpClient()
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 8,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                }
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds + 2)
            };

            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "LeagueOfLegendsClient/14.13.596.7996 (rcp-be-lol-match-history)");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Riot-ClientPlatform", "Windows");

            return client;
        }

        public async Task<bool> InitSgpAsync(HttpClient lcu)
        {
            _lcuClient = lcu;
            if (!await LoadSessionAsync()) return false;
            if (!await LoadEntitlementsTokenAsync()) return false;
            if (string.IsNullOrEmpty(_puuid) || string.IsNullOrEmpty(_accessToken)) return false;

            _sgpUrl = await LoadSgpEndpointAsync();
            return !string.IsNullOrEmpty(_sgpUrl);
        }

        public async Task<bool> LoadSessionAsync()
        {
            HttpResponseMessage resp = await _lcuClient.GetAsync("lol-login/v1/session");
            if (!resp.IsSuccessStatusCode) return false;

            JObject obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
            _puuid = obj["puuid"]?.ToString();
            _summonerId = obj["summonerId"]?.ToString();
            _leagueSessionToken = obj["idToken"]?.ToString();

            return !string.IsNullOrEmpty(_puuid) && !string.IsNullOrEmpty(_leagueSessionToken);
        }

        public async Task<bool> LoadEntitlementsTokenAsync()
        {
            HttpResponseMessage resp = await _lcuClient.GetAsync("entitlements/v1/token");
            if (!resp.IsSuccessStatusCode) return false;

            JObject obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
            _accessToken = obj["accessToken"]?.ToString();
            _entitlementsToken = obj["token"]?.ToString();

            return !string.IsNullOrEmpty(_accessToken);
        }

        public async Task<string> LoadSgpEndpointAsync()
        {
            try
            {
                HttpResponseMessage resp = await _lcuClient.GetAsync("lol-platform-config/v1/namespaces");
                if (!resp.IsSuccessStatusCode) return null;

                string json = await resp.Content.ReadAsStringAsync();
                JObject obj = JObject.Parse(json);

                string sgpUrl = obj["PlayerPreferences"]?["ServiceEndpoint"]?.ToString();

                if (string.IsNullOrEmpty(sgpUrl))
                {
                    string capUrl = obj["LcuPurchaseWidget"]?["CapOrdersUrl"]?.ToString();
                    if (!string.IsNullOrEmpty(capUrl))
                    {
                        var match = Regex.Match(capUrl, @"(https?://[^/]+)");
                        if (match.Success)
                            sgpUrl = match.Groups[1].Value;
                    }
                }

                if (!string.IsNullOrEmpty(sgpUrl))
                {
                    Debug.WriteLine($"[SGP] 成功获取 SGP 地址: {sgpUrl}");
                    return sgpUrl.TrimEnd('/');
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SGP] 获取 Endpoint 异常: {ex.Message}");
            }

            Debug.WriteLine("[SGP] 未能获取 SGP 地址");
            return null;
        }

        public async Task<JArray> SgpFetchLatestMatches(
            string puuid,
            int startIndex = 0,
            int count = 19,
            string tag = null)
        {
            if (string.IsNullOrWhiteSpace(_sgpUrl))
            {
                Debug.WriteLine("❌ _sgpUrl 为空");
                return null;
            }

            if (string.IsNullOrWhiteSpace(_accessToken))
            {
                Debug.WriteLine("❌ accessToken 为空，尝试刷新");
                if (!await LoadEntitlementsTokenAsync())
                    return null;
            }

            string path = $"/match-history-query/v1/products/lol/player/{puuid}/SUMMARY";
            var queries = new List<string> { $"startIndex={startIndex}", $"count={count}" };
            if (!string.IsNullOrEmpty(tag)) queries.Add($"tag={tag}");
            string fullUrl = _sgpUrl.TrimEnd('/') + path + "?" + string.Join("&", queries);

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                await _sgpLimiter.WaitAsync();
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", _accessToken);

                    Debug.WriteLine($"SGP 请求地址: {fullUrl} (尝试 {attempt}/3)");

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(RequestTimeoutSeconds));
                    var watch = Stopwatch.StartNew();
                    using HttpResponseMessage response =
                        await _sharedSgpClient.SendAsync(request, cts.Token);
                    watch.Stop();

                    Debug.WriteLine($"SGP 查询状态: {response.StatusCode} 耗时: {watch.ElapsedMilliseconds}ms");

                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        JObject json = JObject.Parse(content);
                        return json["games"] as JArray;
                    }

                    string errorContent = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"请求失败: {response.StatusCode} - {errorContent}");

                    // 401/403 再刷新 token
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        if (!await LoadEntitlementsTokenAsync())
                        {
                            Debug.WriteLine("刷新 Token 失败");
                            return null;
                        }
                    }
                }
                catch (TaskCanceledException tce)
                {
                    Debug.WriteLine($"SGP 查询超时（尝试 {attempt}/3）: {tce.Message}");
                }
                catch (HttpRequestException hre)
                {
                    Debug.WriteLine($"HTTP 请求异常: {hre.Message} (Inner: {hre.InnerException?.Message})");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"其他异常: {ex.Message}");
                }
                finally
                {
                    _sgpLimiter.Release();
                }

                // 重试前短暂停一下，并尝试刷新 token
                if (attempt < 3)
                {
                    await LoadEntitlementsTokenAsync();
                    await Task.Delay(400 * attempt);
                }
            }

            Debug.WriteLine("SGP 多次重试后仍失败");
            return null;
        }
    }
}
