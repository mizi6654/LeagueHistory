//using System.Diagnostics;
//using System.Net;
//using System.Net.Http.Headers;
//using System.Net.Security;
//using System.Text.RegularExpressions;
//using Newtonsoft.Json.Linq;

//namespace League.Clients
//{
//    /// <summary>
//    /// SGP 会话：复用 HttpClient + 全局限流，避免游戏内 10 人卡片把国服 SGP 打爆
//    /// </summary>
//    public class SgpSession
//    {
//        private HttpClient _lcuClient;
//        private string _accessToken;
//        private string _entitlementsToken;
//        private string _puuid;
//        private string _summonerId;
//        private string _sgpUrl;
//        private string _leagueSessionToken;

//        // 全局复用一个 SGP HttpClient（线程安全）
//        private static readonly HttpClient _sharedSgpClient = CreateSharedSgpClient();

//        // 国服 SGP 对并发敏感：最多同时 2 个请求
//        private static readonly SemaphoreSlim _sgpLimiter = new(2, 2);

//        // 单次请求超时（秒）—— 比原来的 30 更合理，失败快速重试
//        private const int RequestTimeoutSeconds = 12;

//        private static HttpClient CreateSharedSgpClient()
//        {
//            var handler = new SocketsHttpHandler
//            {
//                UseProxy = false,
//                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
//                MaxConnectionsPerServer = 8,
//                ConnectTimeout = TimeSpan.FromSeconds(5),
//                SslOptions = new SslClientAuthenticationOptions
//                {
//                    RemoteCertificateValidationCallback = (_, _, _, _) => true
//                }
//            };

//            var client = new HttpClient(handler)
//            {
//                Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds + 2)
//            };

//            client.DefaultRequestHeaders.TryAddWithoutValidation(
//                "User-Agent",
//                "LeagueOfLegendsClient/14.13.596.7996 (rcp-be-lol-match-history)");
//            client.DefaultRequestHeaders.Accept.Add(
//                new MediaTypeWithQualityHeaderValue("application/json"));
//            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Riot-ClientPlatform", "Windows");

//            return client;
//        }

//        public async Task<bool> InitSgpAsync(HttpClient lcu)
//        {
//            _lcuClient = lcu;
//            if (!await LoadSessionAsync()) return false;
//            if (!await LoadEntitlementsTokenAsync()) return false;
//            if (string.IsNullOrEmpty(_puuid) || string.IsNullOrEmpty(_accessToken)) return false;

//            _sgpUrl = await LoadSgpEndpointAsync();
//            return !string.IsNullOrEmpty(_sgpUrl);
//        }

//        public async Task<bool> LoadSessionAsync()
//        {
//            HttpResponseMessage resp = await _lcuClient.GetAsync("lol-login/v1/session");
//            if (!resp.IsSuccessStatusCode) return false;

//            JObject obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
//            _puuid = obj["puuid"]?.ToString();
//            _summonerId = obj["summonerId"]?.ToString();
//            _leagueSessionToken = obj["idToken"]?.ToString();

//            return !string.IsNullOrEmpty(_puuid) && !string.IsNullOrEmpty(_leagueSessionToken);
//        }

//        public async Task<bool> LoadEntitlementsTokenAsync()
//        {
//            HttpResponseMessage resp = await _lcuClient.GetAsync("entitlements/v1/token");
//            if (!resp.IsSuccessStatusCode) return false;

//            JObject obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
//            _accessToken = obj["accessToken"]?.ToString();
//            _entitlementsToken = obj["token"]?.ToString();

//            return !string.IsNullOrEmpty(_accessToken);
//        }

//        public async Task<string> LoadSgpEndpointAsync()
//        {
//            try
//            {
//                HttpResponseMessage resp = await _lcuClient.GetAsync("lol-platform-config/v1/namespaces");
//                if (!resp.IsSuccessStatusCode) return null;

//                string json = await resp.Content.ReadAsStringAsync();
//                JObject obj = JObject.Parse(json);

//                string sgpUrl = obj["PlayerPreferences"]?["ServiceEndpoint"]?.ToString();

//                if (string.IsNullOrEmpty(sgpUrl))
//                {
//                    string capUrl = obj["LcuPurchaseWidget"]?["CapOrdersUrl"]?.ToString();
//                    if (!string.IsNullOrEmpty(capUrl))
//                    {
//                        var match = Regex.Match(capUrl, @"(https?://[^/]+)");
//                        if (match.Success)
//                            sgpUrl = match.Groups[1].Value;
//                    }
//                }

//                if (!string.IsNullOrEmpty(sgpUrl))
//                {
//                    Debug.WriteLine($"[SGP] 成功获取 SGP 地址: {sgpUrl}");
//                    return sgpUrl.TrimEnd('/');
//                }
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[SGP] 获取 Endpoint 异常: {ex.Message}");
//            }

//            Debug.WriteLine("[SGP] 未能获取 SGP 地址");
//            return null;
//        }

//        public async Task<JArray> SgpFetchLatestMatches(
//        string puuid,
//        int startIndex = 0,
//        int count = 19,
//        string tag = null)
//        {
//            if (string.IsNullOrWhiteSpace(_sgpUrl))
//            {
//                Debug.WriteLine("❌ _sgpUrl 为空");
//                return null; // 配置/初始化失败，视为不可用
//            }

//            if (string.IsNullOrWhiteSpace(_accessToken))
//            {
//                Debug.WriteLine("❌ accessToken 为空，尝试刷新");
//                if (!await LoadEntitlementsTokenAsync())
//                    return null;
//            }

//            string path = $"/match-history-query/v1/products/lol/player/{puuid}/SUMMARY";
//            var queries = new List<string> { $"startIndex={startIndex}", $"count={count}" };
//            if (!string.IsNullOrEmpty(tag)) queries.Add($"tag={tag}");
//            string fullUrl = _sgpUrl.TrimEnd('/') + path + "?" + string.Join("&", queries);

//            for (int attempt = 1; attempt <= 3; attempt++)
//            {
//                await _sgpLimiter.WaitAsync();
//                try
//                {
//                    using var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
//                    request.Headers.Authorization =
//                        new AuthenticationHeaderValue("Bearer", _accessToken);

//                    Debug.WriteLine($"SGP 请求地址: {fullUrl} (尝试 {attempt}/3)");

//                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(RequestTimeoutSeconds));
//                    var watch = Stopwatch.StartNew();
//                    using HttpResponseMessage response =
//                        await _sharedSgpClient.SendAsync(request, cts.Token);
//                    watch.Stop();

//                    Debug.WriteLine($"SGP 查询状态: {response.StatusCode} 耗时: {watch.ElapsedMilliseconds}ms");

//                    if (response.IsSuccessStatusCode)
//                    {
//                        string content = await response.Content.ReadAsStringAsync();
//                        JObject json = JObject.Parse(content);

//                        // ★ 成功：有 games 就返回，没有就返回空数组（不要返回 null）
//                        var games = json["games"] as JArray;
//                        return games ?? new JArray();
//                    }

//                    string errorContent = await response.Content.ReadAsStringAsync();
//                    //Debug.WriteLine($"请求失败: {response.StatusCode} - {errorContent}");

//                    // 401/403 再刷新 token
//                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
//                    {
//                        if (!await LoadEntitlementsTokenAsync())
//                        {
//                            Debug.WriteLine("刷新 Token 失败");
//                            return null;
//                        }
//                    }
//                    // 5xx / 503 等服务器错误：继续重试，最后仍失败则返回 null
//                }
//                catch (TaskCanceledException tce)
//                {
//                    Debug.WriteLine($"SGP 查询超时（尝试 {attempt}/3）: {tce.Message}");
//                }
//                catch (HttpRequestException hre)
//                {
//                    Debug.WriteLine($"HTTP 请求异常: {hre.Message} (Inner: {hre.InnerException?.Message})");
//                }
//                catch (Exception ex)
//                {
//                    Debug.WriteLine($"其他异常: {ex.Message}");
//                }
//                finally
//                {
//                    _sgpLimiter.Release();
//                }

//                if (attempt < 3)
//                {
//                    await LoadEntitlementsTokenAsync();
//                    await Task.Delay(400 * attempt);
//                }
//            }

//            Debug.WriteLine("SGP 多次重试后仍失败（服务器异常或网络不可用）");
//            return null; // ★ 明确表示：请求失败，不是「没有战绩」
//        }
//    }
//}

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

        private string _sgpServerId;
        public string SgpServerId => _sgpServerId;
        public bool IsReady => !string.IsNullOrEmpty(_sgpUrl) && !string.IsNullOrEmpty(_accessToken);

        // 全局复用一个 SGP HttpClient（线程安全）
        private static readonly HttpClient _sharedSgpClient = CreateSharedSgpClient();

        // 国服 SGP 对并发敏感：最多同时 2 个请求
        private static readonly SemaphoreSlim _sgpLimiter = new(2, 2);

        // 单次请求超时（秒）—— 比原来的 30 更合理，失败快速重试
        private const int RequestTimeoutSeconds = 12;

        private static bool IsTencentHost(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.EndsWith(".lol.qq.com", StringComparison.OrdinalIgnoreCase);

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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="lcu"></param>
        /// <param name="sgpServerId">来自 LcuManager，可为 null（会尝试 LCU 补全或动态解析）</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public async Task<bool> InitSgpAsync(HttpClient lcu, string sgpServerId = null)
        {
            _lcuClient = lcu ?? throw new ArgumentNullException(nameof(lcu));
            _sgpServerId = sgpServerId;

            if (!await LoadSessionAsync()) return false;
            if (!await LoadEntitlementsTokenAsync()) return false;
            if (string.IsNullOrEmpty(_puuid) || string.IsNullOrEmpty(_accessToken)) return false;

            // 无效 / 空 → 先从 LCU 补
            if (string.IsNullOrEmpty(_sgpServerId) ||
                string.Equals(_sgpServerId, "TENCENT_TENCENT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_sgpServerId, "TENCENT", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = await TryResolveSgpServerIdFromLcuAsync();
                if (!string.IsNullOrEmpty(resolved))
                    _sgpServerId = resolved;
                else if (IsInvalidSgpServerId(_sgpServerId))
                    _sgpServerId = null;
            }

            // 1) 配置表
            if (!string.IsNullOrEmpty(_sgpServerId) &&
                SgpServerConfig.TryGetMatchHistory(_sgpServerId, out var tableUrl))
            {
                _sgpUrl = tableUrl;
                Debug.WriteLine($"[SGP] 配置表 {_sgpServerId} -> {_sgpUrl}");
                return true;
            }

            // 表 miss：再试一次 LCU 解析（外服关键）
            if (!string.IsNullOrEmpty(_sgpServerId))
            {
                var resolved = await TryResolveSgpServerIdFromLcuAsync();
                if (!string.IsNullOrEmpty(resolved) &&
                    !string.Equals(resolved, _sgpServerId, StringComparison.OrdinalIgnoreCase) &&
                    SgpServerConfig.TryGetMatchHistory(resolved, out tableUrl))
                {
                    _sgpServerId = resolved;
                    _sgpUrl = tableUrl;
                    Debug.WriteLine($"[SGP] LCU补全后配置表 {_sgpServerId} -> {_sgpUrl}");
                    return true;
                }
            }
            else
            {
                var resolved = await TryResolveSgpServerIdFromLcuAsync();
                if (!string.IsNullOrEmpty(resolved) &&
                    SgpServerConfig.TryGetMatchHistory(resolved, out tableUrl))
                {
                    _sgpServerId = resolved;
                    _sgpUrl = tableUrl;
                    Debug.WriteLine($"[SGP] LCU补全后配置表 {_sgpServerId} -> {_sgpUrl}");
                    return true;
                }
            }

            // 2) 动态解析兜底（国服很稳；外服视客户端配置而定）
            _sgpUrl = await LoadSgpEndpointAsync();
            if (!string.IsNullOrEmpty(_sgpUrl))
            {
                Debug.WriteLine($"[SGP] 动态解析 -> {_sgpUrl}");
                return true;
            }

            Debug.WriteLine("[SGP] 初始化失败：无 Endpoint");
            return false;
        }

        private static bool IsInvalidSgpServerId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return true;
            if (string.Equals(id, "TENCENT", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(id, "TENCENT_TENCENT", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// 命令行无 region 时，从 LCU 接口补全区服 ID
        /// </summary>
        private async Task<string> TryResolveSgpServerIdFromLcuAsync()
        {
            try
            {
                // ① // 优先 region-locale（外服常见）
                var resp = await _lcuClient.GetAsync("/riotclient/region-locale");
                if (resp.IsSuccessStatusCode)
                {
                    string raw = await resp.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[SGP] region-locale raw (region-locale相关): {raw}");

                    var obj = JObject.Parse(raw);
                    string region = obj["region"]?.ToString()
                                 ?? obj["webRegion"]?.ToString();
                    string rso = obj["rsoPlatformId"]?.ToString()
                              ?? obj["platformId"]?.ToString()
                              ?? obj["region"]?.ToString();

                    var id = SgpServerConfig.BuildSgpServerId(region, rso);
                    if (!string.IsNullOrEmpty(id))
                    {
                        Debug.WriteLine($"[SGP] LCU region-locale -> {id}");
                        return id;
                    }
                }

                // ② 再试session
                resp = await _lcuClient.GetAsync("lol-login/v1/session");
                if (resp.IsSuccessStatusCode)
                {
                    string raw = await resp.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[SGP] session raw (region相关): {raw}");

                    var obj = JObject.Parse(raw);
                    string region = obj["region"]?.ToString();
                    string rso = obj["platformId"]?.ToString()
                              ?? obj["rsoPlatformId"]?.ToString();

                    var id = SgpServerConfig.BuildSgpServerId(region, rso);
                    if (!string.IsNullOrEmpty(id))
                    {
                        Debug.WriteLine($"[SGP] LCU session -> {id}");
                        return id;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SGP] 补全区服失败: {ex.Message}");
            }
            return null;
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

        
        /// <summary>
        /// 国服：PlayerPreferences.ServiceEndpoint 即战绩 SGP。
        /// Riot 服：该项常是偏好服务，查战绩会 403；改用 client_settings.player_platform_edge.url。
        /// </summary>
        public async Task<string> LoadSgpEndpointAsync()
        {
            string configured = await LoadConfiguredEndpointAsync();

            // 国服：直接用配置里的 qq.com 地址
            if (IsTencentHost(configured))
            {
                //Debug.WriteLine($"[SGP] 国服 Endpoint: {configured}");
                return configured;
            }

            // 外服：优先 player_platform_edge（日服已验证）
            string edge = await LoadPlatformEdgeAsync();
            string chosen = edge ?? configured;
            Debug.WriteLine($"[SGP] 战绩服务地址: {chosen} (edge={edge}, configured={configured})");
            return chosen;
        }

        /// <summary>
        /// lol.client_settings → player_platform_edge.url
        /// </summary>
        private async Task<string> LoadPlatformEdgeAsync()
        {
            try
            {
                using HttpResponseMessage resp = await _lcuClient.GetAsync(
                    "lol-client-config/v3/client-config/lol.client_settings");
                if (!resp.IsSuccessStatusCode) return null;

                string raw = await resp.Content.ReadAsStringAsync();
                // 调试时可打开；JSON 可能较长
                // Debug.WriteLine($"[SGP] client_settings raw: {raw}");

                JObject settings = JObject.Parse(raw);
                string platformEdge = settings["player_platform_edge"]?["url"]?.ToString();
                if (string.IsNullOrEmpty(platformEdge)) return null;

                platformEdge = platformEdge.TrimEnd('/');
                Debug.WriteLine($"[SGP] player_platform_edge: {platformEdge}");
                return platformEdge;
            }
            catch (Exception ex) when (ex is HttpRequestException or Newtonsoft.Json.JsonException)
            {
                Debug.WriteLine($"[SGP] 读取 player_platform_edge 失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 原逻辑：namespaces 里的 ServiceEndpoint / CapOrdersUrl
        /// </summary>
        private async Task<string> LoadConfiguredEndpointAsync()
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
                    //Debug.WriteLine($"[SGP] 配置 Endpoint: {sgpUrl}");
                    return sgpUrl.TrimEnd('/');
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SGP] 获取配置 Endpoint 异常: {ex.Message}");
            }
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
                return null; // 配置/初始化失败，视为不可用
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

                        // ★ 成功：有 games 就返回，没有就返回空数组（不要返回 null）
                        var games = json["games"] as JArray;
                        return games ?? new JArray();
                    }

                    string errorContent = await response.Content.ReadAsStringAsync();
                    //Debug.WriteLine($"请求失败: {response.StatusCode} - {errorContent}");

                    // 401/403 再刷新 token
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        if (!await LoadEntitlementsTokenAsync())
                        {
                            Debug.WriteLine("刷新 Token 失败");
                            return null;
                        }
                    }
                    // 5xx / 503 等服务器错误：继续重试，最后仍失败则返回 null
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

                if (attempt < 3)
                {
                    await LoadEntitlementsTokenAsync();
                    await Task.Delay(400 * attempt);
                }
            }

            Debug.WriteLine("SGP 多次重试后仍失败（服务器异常或网络不可用）");
            return null; // ★ 明确表示：请求失败，不是「没有战绩」
        }
    }
}