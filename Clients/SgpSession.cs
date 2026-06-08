using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace League.Clients
{
    public class SgpSession
    {
        private HttpClient _lcuClient;

        private HttpClient _sgpClient;

        private string _accessToken;

        private string _entitlementsToken;

        private string _puuid;

        private string _summonerId;

        private string _sgpUrl;

        private string _leagueSessionToken;

        public async Task<bool> InitSgpAsync(HttpClient lcu)
        {
            _lcuClient = lcu;
            if (!await LoadSessionAsync())
            {
                return false;
            }
            if (!await LoadEntitlementsTokenAsync())
            {
                return false;
            }
            if (string.IsNullOrEmpty(_puuid) || string.IsNullOrEmpty(_accessToken))
            {
                return false;
            }
            _sgpUrl = await LoadSgpEndpointAsync();
            if (string.IsNullOrEmpty(_sgpUrl))
            {
                return false;
            }
            //Debug.WriteLine("[puuId]：" + _puuid);
            //Debug.WriteLine("[accessToken]：" + _accessToken);
            return true;
        }

        public async Task<bool> LoadSessionAsync()
        {
            HttpResponseMessage resp = await _lcuClient.GetAsync("lol-login/v1/session");
            if (!resp.IsSuccessStatusCode)
            {
                return false;
            }
            JObject obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
            _puuid = obj["puuid"]?.ToString();
            _summonerId = obj["summonerId"]?.ToString();
            _leagueSessionToken = obj["idToken"]?.ToString();

            return !string.IsNullOrEmpty(_puuid) && !string.IsNullOrEmpty(_leagueSessionToken);
        }

        public async Task<bool> LoadEntitlementsTokenAsync()
        {
            HttpResponseMessage resp = await _lcuClient.GetAsync("entitlements/v1/token");
            if (!resp.IsSuccessStatusCode)
            {
                return false;
            }
            JObject obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
            _accessToken = obj["accessToken"]?.ToString();
            _entitlementsToken = obj["token"]?.ToString();
            obj["issuer"]?.ToString();
            //Debug.WriteLine("[Entitlements] accessToken=" + _accessToken);
            return !string.IsNullOrEmpty(_accessToken);
        }

        public async Task<string> LoadSgpEndpointAsync()
        {
            try
            {
                HttpResponseMessage resp = await _lcuClient.GetAsync("lol-platform-config/v1/namespaces");
                if (!resp.IsSuccessStatusCode)
                    return null;

                string json = await resp.Content.ReadAsStringAsync();
                JObject obj = JObject.Parse(json);

                // 优先从 PlayerPreferences 取（最直接）
                string sgpUrl = obj["PlayerPreferences"]?["ServiceEndpoint"]?.ToString();

                // 如果上面没有，尝试从 LcuPurchaseWidget 提取
                if (string.IsNullOrEmpty(sgpUrl))
                {
                    string capUrl = obj["LcuPurchaseWidget"]?["CapOrdersUrl"]?.ToString();
                    if (!string.IsNullOrEmpty(capUrl))
                    {
                        // 提取基础地址
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
       
        public async Task<JArray> SgpFetchLatestMatches(string puuid, int startIndex = 0, int count = 19, string tag = null)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    try
                    {
                        using var handler = new HttpClientHandler
                        {
                            ServerCertificateCustomValidationCallback = (msg, cert, chain, err) => true
                        };

                        using HttpClient client = new HttpClient(handler);
                        client.Timeout = TimeSpan.FromSeconds(30);   // 双重保险

                        client.DefaultRequestHeaders.Clear();
                        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "LeagueOfLegendsClient/14.13.596.7996 (rcp-be-lol-match-history)");
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Riot-ClientPlatform", "Windows");
                        // client.DefaultRequestHeaders.TryAddWithoutValidation("X-Riot-ClientVersion", await GetClientVersionAsync());

                        if (string.IsNullOrWhiteSpace(_sgpUrl))
                        {
                            Debug.WriteLine("❌ _sgpUrl 为空");
                            return null;
                        }

                        string path = $"/match-history-query/v1/products/lol/player/{puuid}/SUMMARY";
                        var queries = new List<string> { $"startIndex={startIndex}", $"count={count}" };
                        if (!string.IsNullOrEmpty(tag)) queries.Add($"tag={tag}");

                        string fullUrl = _sgpUrl.TrimEnd('/') + path + "?" + string.Join("&", queries);
                        Debug.WriteLine($"SGP 请求地址: {fullUrl} (尝试 {attempt}/3)");

                        Stopwatch watch = Stopwatch.StartNew();
                        HttpResponseMessage response = await client.GetAsync(fullUrl, cts.Token);
                        watch.Stop();

                        Debug.WriteLine($"SGP 查询状态: {response.StatusCode} 耗时: {watch.ElapsedMilliseconds}ms");

                        if (response.IsSuccessStatusCode)
                        {
                            string content = await response.Content.ReadAsStringAsync();
                            JObject json = JObject.Parse(content);
                            return json["games"] as JArray;
                        }
                        else
                        {
                            string errorContent = await response.Content.ReadAsStringAsync();
                            Debug.WriteLine($"请求失败: {response.StatusCode} - {errorContent}");
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
                }

                if (!await LoadEntitlementsTokenAsync())
                {
                    Debug.WriteLine("刷新 Token 失败");
                    return null;
                }
                await Task.Delay(1500);
            }

            Debug.WriteLine("SGP 多次重试后仍失败");
            return null;
        }
    }
}
