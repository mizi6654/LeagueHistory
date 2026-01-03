using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
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

        private async Task<bool> TrySgpRequestWithAccessToken(string version)
        {
            try
            {
                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "LeagueOfLegendsClient/14.13.596.7996 (rcp-be-lol-match-history)");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                string fullUrl = string.Concat(str1: "/match-history-query/v1/products/lol/player/" + _puuid + "/SUMMARY", str3: string.Join("&", new List<string> { "startIndex=0", "count=1" }), str0: _sgpUrl, str2: "?");
                HttpResponseMessage response = await client.GetAsync(fullUrl);
                string content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"SGP 连接初始化状态: {response.StatusCode}");
                if (response.IsSuccessStatusCode)
                {
                    _sgpClient = client;
                    return true;
                }
                Debug.WriteLine($"SGP 初始化失败: {response.StatusCode}, 响应: {content}");
                return false;
            }
            catch (Exception ex)
            {
                Exception ex2 = ex;
                Debug.WriteLine("SGP 初始化异常: " + ex2.Message);
                return false;
            }
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
                HttpResponseMessage resp = await _lcuClient.GetAsync("lol-platform-config/v1/namespaces/LoginDataPacket");
                if (resp.IsSuccessStatusCode)
                {
                    JObject obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
                    string platformId = obj["platformId"]?.ToString().ToLower();
                    //Debug.WriteLine("[SGP] LoginDataPacket root -> platformId=" + platformId);
                    if (!string.IsNullOrEmpty(platformId))
                    {
                        string sgpUrl = MapPlatformIdToSgp(platformId);
                        //Debug.WriteLine("[SGP] mapped sgpUrl=" + sgpUrl);
                        return sgpUrl;
                    }
                }
            }
            catch (Exception ex)
            {
                Exception ex2 = ex;
                Debug.WriteLine("[SGP] 读取 LoginDataPacket 异常: " + ex2.Message);
            }
            Debug.WriteLine("[SGP] 未能获取到 platformId 来映射 SGP 地址");
            return null;
        }

        public string MapPlatformIdToSgp(string platformId)
        {
            if (string.IsNullOrWhiteSpace(platformId))
            {
                return null;
            }
            Dictionary<string, string> known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "hn1", "https://hn1-k8s-sgp.lol.qq.com:21019" },
                { "hn10", "https://hn10-k8s-sgp.lol.qq.com:21019" },
                { "tj100", "https://tj100-sgp.lol.qq.com:21019" },
                { "tj101", "https://tj101-sgp.lol.qq.com:21019" },
                { "nj100", "https://nj100-sgp.lol.qq.com:21019" },
                { "gz100", "https://gz100-sgp.lol.qq.com:21019" },
                { "cq100", "https://cq100-sgp.lol.qq.com:21019" },
                { "bgp2", "https://bgp2-k8s-sgp.lol.qq.com:21019" }
            };
            if (known.TryGetValue(platformId.Trim(), out var url))
            {
                return url;
            }
            return "https://" + platformId + "-k8s-sgp.lol.qq.com:21019";
        }

        public async Task<string> GetClientVersionAsync()
        {
            try
            {
                HttpResponseMessage resp = await _lcuClient.GetAsync("/lol-patch/v1/game-version");
                if (!resp.IsSuccessStatusCode)
                {
                    return "15.19.7148453";
                }
                string version = (await resp.Content.ReadAsStringAsync()).Trim('"', ' ', '\r', '\n', ',');
                int idx = version.IndexOf('+');
                if (idx > 0)
                {
                    version = version.Substring(0, idx);
                }
                version = version.Trim().TrimEnd(',', ' ', '\r', '\n');
                Debug.WriteLine("[LCU] ClientVersion='" + version + "'");
                return string.IsNullOrEmpty(version) ? "15.19.7148453" : version;
            }
            catch (Exception ex)
            {
                Exception ex2 = ex;
                Debug.WriteLine("获取版本异常: " + ex2.Message);
                return "15.19.7148453";
            }
        }

        public async Task<JArray> SgpFetchLatestMatches(string puuid, int startIndex = 0, int count = 19, string tag = null)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10.0)))
                {
                    try
                    {
                        using HttpClient client = new HttpClient();
                        client.DefaultRequestHeaders.Clear();
                        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "LeagueOfLegendsClient/14.13.596.7996 (rcp-be-lol-match-history)");
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        string fullUrl = string.Concat(str1: "/match-history-query/v1/products/lol/player/" + puuid + "/SUMMARY", str3: string.Join("&", new List<string>
                        {
                            $"startIndex={startIndex}",
                            $"count={count}",
                            "tag=" + tag
                        }), str0: _sgpUrl, str2: "?");
                        //Debug.WriteLine($"SGP {puuid} 查询链接: {fullUrl}  尝试次数: {attempt}/{3}");
                        //Debug.WriteLine($"SGP查询战绩 {puuid} 尝试次数: {attempt}/{3}");
                        Stopwatch watch = Stopwatch.StartNew();
                        HttpResponseMessage response = await client.GetAsync(fullUrl, cts.Token);
                        watch.Stop();
                        Debug.WriteLine($"SGP {puuid} 查询状态: {response.StatusCode}  耗时: {watch.ElapsedMilliseconds}ms");
                        if (response.IsSuccessStatusCode)
                        {
                            JObject json = JObject.Parse(await response.Content.ReadAsStringAsync());
                            return json["games"] as JArray;
                        }
                        Debug.WriteLine($"请求失败（状态码: {response.StatusCode}），准备刷新 Token 并重试...");
                    }
                    catch (TaskCanceledException)
                    {
                        Debug.WriteLine($"SGP 查询超时，准备刷新 Token 并重试...（尝试 {attempt}/{3}）");
                    }
                    catch (Exception ex2)
                    {
                        Exception ex3 = ex2;
                        Debug.WriteLine($"获取战绩异常: {ex3.Message}（尝试 {attempt}/{3}）");
                    }
                }
                if (!await LoadEntitlementsTokenAsync())
                {
                    Debug.WriteLine("刷新 Token 失败，终止重试。");
                    return null;
                }
                await Task.Delay(1000);
            }
            Debug.WriteLine("SGP 查询在多次重试后仍未成功。");
            return null;
        }

        public async Task<string> SgpFetchRankedStats(string puuid)
        {
            try
            {
                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "LeagueOfLegendsClient/14.13.596.7996 (rcp-be-lol-match-history)");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                GetPlatformIdFromSgpUrl();
                string fullUrl = string.Concat(str1: "/leagues-ledge/v2/rankedStats/puuid/" + puuid, str0: _sgpUrl);
                Debug.WriteLine("SGP 排位查询链接: " + fullUrl);
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(3.0));
                HttpResponseMessage response = await client.GetAsync(fullUrl, cts.Token);
                Debug.WriteLine($"SGP 排位查询状态: {response.StatusCode}");
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine("sgp 排位：" + content);
                    return content;
                }
                return null;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("排位查询超时");
                return null;
            }
            catch (Exception ex2)
            {
                Exception ex3 = ex2;
                Debug.WriteLine("获取排位数据异常: " + ex3.Message);
                return null;
            }
        }

        public async Task<string> SgpFetchSummonerInfo(string puuid)
        {
            try
            {
                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "LeagueOfLegendsClient/14.13.596.7996 (rcp-be-lol-match-history)");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                string platformId = GetPlatformIdFromSgpUrl();
                string apiPath = "/summoner-ledge/v1/regions/" + platformId + "/summoners/puuids";
                string[] puuids = new string[1] { puuid };
                string jsonContent = JsonConvert.SerializeObject(puuids);
                StringContent contentData = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                string fullUrl = _sgpUrl + apiPath;
                Debug.WriteLine("SGP 召唤师查询链接: " + fullUrl);
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(3.0));
                HttpResponseMessage response = await client.PostAsync(fullUrl, contentData, cts.Token);
                Debug.WriteLine($"SGP 召唤师查询状态: {response.StatusCode}");
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine("sgp 玩家：" + content);
                    return content;
                }
                return null;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("召唤师查询超时");
                return null;
            }
            catch (Exception ex2)
            {
                Exception ex3 = ex2;
                Debug.WriteLine("获取召唤师信息异常: " + ex3.Message);
                return null;
            }
        }

        private string GetPlatformIdFromSgpUrl()
        {
            Match match = Regex.Match(_sgpUrl, "https://([^-]+)-");
            return match.Success ? match.Groups[1].Value.ToLower() : "unknown";
        }
    }
}
