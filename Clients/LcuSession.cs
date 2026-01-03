using League.PrimaryElection;
using League.States;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using static League.FormMain;

namespace League.Clients
{
    public class LcuSession
    {
        private HttpClient _lcuClient;

        private string _lcuPort;

        private string _lcuToken;

        private readonly Queue<long> _responseTimeHistory = new Queue<long>();

        private const int MaxHistorySize = 5;

        private readonly SemaphoreSlim _concurrencySemaphore = new SemaphoreSlim(6);

        private static readonly ConcurrentDictionary<string, JObject> _summonerCache = new ConcurrentDictionary<string, JObject>();

        public HttpClient Client => _lcuClient;

        public async Task<bool> InitializeAsync()
        {
            string port = null;
            string token = null;
            for (int i = 0; i < 10; i++)
            {
                Process proc = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
                if (proc != null)
                {
                    string cmdLine = GetCommandLine(proc);
                    port = ExtractArgument(cmdLine, "--app-port=");
                    token = ExtractArgument(cmdLine, "--remoting-auth-token=");
                    if (!string.IsNullOrEmpty(port))
                    {
                        port = port.Trim().Trim('"', '\'', ' ', '\r', '\n');
                    }
                    if (!string.IsNullOrEmpty(token))
                    {
                        token = token.Trim().Trim('"', '\'', ' ', '\r', '\n');
                    }
                    if (!int.TryParse(port, out var portNumber) || portNumber < 1 || portNumber > 65535)
                    {
                        Debug.WriteLine("[LCU] 提取到的端口不合法: " + port);
                        return false;
                    }
                    if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token))
                    {
                        //Debug.WriteLine("[LCU] 找到参数 → Port: " + port + ", Token: " + token);
                        break;
                    }
                    //Debug.WriteLine("[LCU] LeagueClientUx 启动了，但参数暂时为空，等待重试...");
                }
                else
                {
                    Debug.WriteLine("[LCU] LeagueClientUx 尚未启动，等待...");
                }
                await Task.Delay(1000);
            }
            if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            {
                Debug.WriteLine("[LCU] 多次重试后仍未获取到 LCU 参数");
                return false;
            }
            _lcuPort = port;
            _lcuToken = token;
            //Debug.WriteLine("Authorization: Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + token)));
            SocketsHttpHandler handler = new SocketsHttpHandler
            {
                UseProxy = false,
                PooledConnectionLifetime = TimeSpan.Zero,
                PooledConnectionIdleTimeout = TimeSpan.Zero,
                MaxConnectionsPerServer = int.MaxValue,
                EnableMultipleHttp2Connections = true,
                ConnectTimeout = TimeSpan.FromSeconds(3.0),
                UseCookies = false,
                AllowAutoRedirect = false,
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true
                }
            };
            _lcuClient?.Dispose();
            _lcuClient = new HttpClient(handler)
            {
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            _lcuClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + token)));
            _lcuClient.BaseAddress = new Uri("https://127.0.0.1:" + port + "/");
            try
            {
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10.0));
                HttpResponseMessage response = await _lcuClient.GetAsync("lol-summoner/v1/current-summoner", HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("[LCU] 初始化成功，已连接 LCU API");
                    return true;
                }
                Debug.WriteLine($"[LCU] LCU 返回异常状态码: {response.StatusCode}");
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine("[LCU] 请求超时: " + ex.Message);
            }
            catch (Exception value)
            {
                Debug.WriteLine($"[LCU] 其他异常: {value}");
            }
            return false;
        }

        private string GetCommandLine(Process process)
        {
            using ManagementObjectSearcher searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
            using (ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = searcher.Get().GetEnumerator())
            {
                if (managementObjectEnumerator.MoveNext())
                {
                    ManagementObject obj = (ManagementObject)managementObjectEnumerator.Current;
                    return obj["CommandLine"]?.ToString() ?? "";
                }
            }
            return "";
        }

        private string ExtractArgument(string cmdLine, string key)
        {
            Match match = Regex.Match(cmdLine, key + "([^ ]+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        public async Task<JObject> GetSummonerByNameAsync(string summonerName)
        {
            try
            {
                HttpResponseMessage response = await _lcuClient.GetAsync("/lol-summoner/v1/summoners?name=" + Uri.EscapeDataString(summonerName));
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                return JObject.Parse(await response.Content.ReadAsStringAsync());
            }
            catch (TaskCanceledException ex)
            {
                TaskCanceledException ex2 = ex;
                Debug.WriteLine("[LCU] 请求超时: " + ex2.Message);
                return null;
            }
            catch (Exception ex3)
            {
                Exception ex4 = ex3;
                Debug.WriteLine($"查询异常：{ex4}");
                return null;
            }
        }

        public async Task<JObject> GetGameNameBySummonerId(string summonerId)
        {
            if (string.IsNullOrEmpty(summonerId))
            {
                return null;
            }
            if (_summonerCache.TryGetValue(summonerId, out JObject cached))
            {
                return cached;
            }
            try
            {
                HttpResponseMessage response = await _lcuClient.GetAsync("/lol-summoner/v1/summoners/" + summonerId);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                JObject obj = JObject.Parse(await response.Content.ReadAsStringAsync());
                _summonerCache[summonerId] = obj;
                return obj;
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine("[LCU] 请求超时: " + ex.Message);
                return null;
            }
            catch (Exception value)
            {
                Debug.WriteLine($"ID查询异常：{value}");
                return null;
            }
        }

        public async Task<JObject> GetCurrentSummoner()
        {
            try
            {
                HttpResponseMessage response = await _lcuClient.GetAsync("/lol-summoner/v1/current-summoner");
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                return JObject.Parse(await response.Content.ReadAsStringAsync());
            }
            catch (TaskCanceledException ex)
            {
                TaskCanceledException ex2 = ex;
                Debug.WriteLine("[LCU] 请求超时: " + ex2.Message);
                return new JObject();
            }
            catch (Exception ex3)
            {
                Exception ex4 = ex3;
                Debug.WriteLine($"获取段位失败: {ex4}");
                return new JObject();
            }
        }

        public async Task<JObject> GetSummonerByPuuidAsync(string puuid)
        {
            try
            {
                HttpResponseMessage response = await _lcuClient.GetAsync($"/lol-summoner/v1/summoners-by-puuid/{puuid}");
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[LCU] GetSummonerByPuuid 失败: {response.StatusCode}");
                    return null;
                }
                return JObject.Parse(await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LCU] GetSummonerByPuuid 异常: {ex.Message}");
                return null;
            }
        }

        public async Task<JArray> FetchMatchesWithRetry(string puuid, int begIndex, int endIndex, bool isPreheat = false)
        {
            int maxRetries = isPreheat ? 1 : 3;
            int attempt = 0;
            while (attempt < maxRetries)
            {
                attempt++;
                CancellationTokenSource cts = null;
                Debug.WriteLine($"[{puuid}] 第{attempt}次尝试，等待信号量...");
                await _concurrencySemaphore.WaitAsync();
                Debug.WriteLine("[" + puuid + "] 获取到信号量，开始请求");
                try
                {
                    int timeoutSeconds = attempt == 1 ? 2 : 4;
                    cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                    string path = $"lol-match-history/v1/products/lol/{puuid}/matches?begIndex={begIndex}&endIndex={endIndex}";
                    Debug.WriteLine("[正常请求] " + path);
                    Stopwatch watch = Stopwatch.StartNew();
                    HttpResponseMessage response = await _lcuClient.GetAsync(path, cts.Token);
                    watch.Stop();
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        if (!isPreheat)
                        {
                            UpdateResponseTimeHistory(watch.ElapsedMilliseconds);
                            Debug.WriteLine($"[正常请求] （默认返回{endIndex}场）耗时: {watch.ElapsedMilliseconds}ms");
                        }
                        JObject json = JObject.Parse(content);
                        return json["games"]?["games"] as JArray;
                    }
                    Debug.WriteLine($"[响应失败] {response.StatusCode}，已使用缓存接口（忽略分页）");
                }
                catch (TaskCanceledException)
                {
                    Debug.WriteLine($"第{attempt}次请求超时（使用缓存接口）");
                    if (isPreheat)
                    {
                        break;
                    }
                }
                catch (Exception ex2)
                {
                    Exception ex3 = ex2;
                    Debug.WriteLine("请求异常（使用缓存接口）: " + ex3.Message);
                }
                finally
                {
                    _concurrencySemaphore.Release();
                    cts?.Dispose();
                }
            }
            return null;
        }

        public async Task<JArray> FetchLatestMatches(string puuid, bool isPreheat = false)
        {
            int[] retryDelays = new int[2] { 10, 15 };
            for (int i = 0; i < retryDelays.Length; i++)
            {
                TimeSpan timeout = TimeSpan.FromSeconds(retryDelays[i]);
                using (CancellationTokenSource cts = new CancellationTokenSource(timeout))
                {
                    try
                    {
                        using HttpResponseMessage response = await _lcuClient.GetAsync("lol-match-history/v1/products/lol/" + puuid + "/matches", HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(continueOnCapturedContext: false);
                        response.EnsureSuccessStatusCode();
                        JObject json = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false));
                        return json["games"]?["games"] as JArray;
                    }
                    catch (TaskCanceledException)
                    {
                        Debug.WriteLine($"[Fetch超时] {puuid} 超过 {retryDelays[i]} 秒未响应");
                    }
                    catch (Exception ex2)
                    {
                        Exception ex3 = ex2;
                        Debug.WriteLine($"[第{i + 1}次尝试失败] 异常: {ex3.Message}");
                    }
                }
                if (i < retryDelays.Length - 1)
                {
                    Debug.WriteLine($"[等待重试] 即将进行第{i + 2}次尝试");
                    await Task.Delay(1000);
                }
            }
            return null;
        }

        public async Task<JObject> GetFullMatchByGameIdAsync(long gameId)
        {
            try
            {
                HttpResponseMessage response = await _lcuClient.GetAsync($"/lol-match-history/v1/games/{gameId}");
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                return JObject.Parse(await response.Content.ReadAsStringAsync());
            }
            catch (TaskCanceledException ex)
            {
                TaskCanceledException ex2 = ex;
                Debug.WriteLine("[LCU] 请求超时: " + ex2.Message);
                return null;
            }
            catch (Exception ex3)
            {
                Exception ex4 = ex3;
                Debug.WriteLine("获取完整对战信息失败：" + ex4.Message);
                return null;
            }
        }

        public async Task<JObject> GetCurrentRankedStatsAsync(string puuid)
        {
            try
            {
                return JObject.Parse(await (await _lcuClient.GetAsync("/lol-ranked/v1/ranked-stats/" + puuid)).Content.ReadAsStringAsync());
            }
            catch (TaskCanceledException ex)
            {
                TaskCanceledException ex2 = ex;
                Debug.WriteLine("[LCU] 请求超时: " + ex2.Message);
                return new JObject();
            }
            catch (Exception ex3)
            {
                Exception ex4 = ex3;
                Debug.WriteLine($"获取段位失败: {ex4}");
                return new JObject();
            }
        }

        public async Task<string> GetGameflowPhase()
        {
            try
            {
                HttpResponseMessage response = await _lcuClient.GetAsync("/lol-gameflow/v1/gameflow-phase");
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                return (await response.Content.ReadAsStringAsync()).Trim().Trim('"');
            }
            catch (TaskCanceledException ex)
            {
                TaskCanceledException ex2 = ex;
                Debug.WriteLine("[LCU] 请求超时: " + ex2.Message);
                return null;
            }
            catch (Exception ex3)
            {
                Exception ex4 = ex3;
                Debug.WriteLine($"获取游戏阶段失败: {ex4}");
                return null;
            }
        }

        public async Task<JObject> GetChampSelectSession()
        {
            try
            {
                HttpResponseMessage response = await _lcuClient.GetAsync("/lol-champ-select/v1/session");
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                return JObject.Parse(await response.Content.ReadAsStringAsync());
            }
            catch (TaskCanceledException ex)
            {
                TaskCanceledException ex2 = ex;
                Debug.WriteLine("[LCU] 请求超时: " + ex2.Message);
                return new JObject();
            }
            catch (Exception ex3)
            {
                Exception ex4 = ex3;
                Debug.WriteLine($"获取选人阶段数据失败: {ex4}");
                return new JObject();
            }
        }

        public async Task<bool> SkipHonorAsync()
        {
            try
            {
                HttpResponseMessage response =
                    await _lcuClient.PostAsync("/lol-end-of-game/v1/skip-honor", null);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("[LCU] skip-honor 调用失败: " + response.StatusCode);
                    return false;
                }

                Debug.WriteLine("[LCU] 已成功跳过点赞结算");
                return true;
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine("[LCU] skip-honor 请求超时: " + ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[LCU] skip-honor 异常: " + ex);
                return false;
            }
        }

        /// <summary>
        /// 返回完整的游戏信息，包括 queueId！
        /// </summary>
        /// <returns></returns>
        public async Task<JObject> GetGameSession()
        {
            try
            {
                HttpResponseMessage response = await _lcuClient.GetAsync("/lol-gameflow/v1/session");
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                return JObject.Parse(await response.Content.ReadAsStringAsync());
            }
            catch (TaskCanceledException ex)
            {
                TaskCanceledException ex2 = ex;
                Debug.WriteLine("[LCU] 请求超时: " + ex2.Message);
                return new JObject();
            }
            catch (Exception ex3)
            {
                Exception ex4 = ex3;
                Debug.WriteLine($"获取游戏会话失败: {ex4}");
                return new JObject();
            }
        }
        
        private void UpdateResponseTimeHistory(long elapsedMs)
        {
            _responseTimeHistory.Enqueue(elapsedMs);
            if (_responseTimeHistory.Count > 5)
            {
                _responseTimeHistory.Dequeue();
            }
        }

        public async Task<bool> DownloadReplayAsync(long gameId, string contextData = "match-history")
        {
            try
            {
                Debug.WriteLine($"[回放] 开始下载回放，GameId: {gameId}");
                (bool exists, bool isValid) existingCheck = await CheckExistingReplay(gameId);
                if (existingCheck.exists && existingCheck.isValid)
                {
                    Debug.WriteLine("[回放] 回放已存在且有效，跳过下载");
                    return true;
                }
                JObject jsonObj = new JObject { ["contextData"] = contextData };
                StringContent postContent = new StringContent(jsonObj.ToString(), Encoding.UTF8, "application/json");
                HttpResponseMessage dlResp = await _lcuClient.PostAsync($"/lol-replays/v1/rofls/{gameId}/download", postContent);
                Debug.WriteLine($"[回放] 下载请求响应: {dlResp.StatusCode}");
                if (!dlResp.IsSuccessStatusCode && dlResp.StatusCode != HttpStatusCode.NoContent && dlResp.StatusCode != HttpStatusCode.Accepted)
                {
                    Debug.WriteLine("[回放] 下载请求失败");
                    return false;
                }
                return await WaitForDownloadComplete(gameId);
            }
            catch (Exception ex)
            {
                Exception ex2 = ex;
                Debug.WriteLine($"[回放] 下载异常: {ex2}");
                return false;
            }
        }

        public async Task<bool> PlayReplayAsync(long gameId, string contextData = "match-history")
        {
            try
            {
                Debug.WriteLine($"[回放] 尝试播放回放，GameId: {gameId}");
                JObject jsonObj = new JObject { ["contextData"] = contextData };
                StringContent postContent = new StringContent(jsonObj.ToString(), Encoding.UTF8, "application/json");
                HttpResponseMessage watchResp = await _lcuClient.PostAsync($"/lol-replays/v1/rofls/{gameId}/watch", postContent);
                if (watchResp.IsSuccessStatusCode)
                {
                    Debug.WriteLine("[回放] 播放请求成功");
                    return true;
                }
                string respText = await watchResp.Content.ReadAsStringAsync();
                Debug.WriteLine($"[回放] 播放失败: {watchResp.StatusCode} {respText}");
                return false;
            }
            catch (Exception ex)
            {
                Exception ex2 = ex;
                Debug.WriteLine($"[回放] 播放异常: {ex2}");
                return false;
            }
        }

        private async Task<(bool exists, bool isValid)> CheckExistingReplay(long gameId)
        {
            try
            {
                HttpResponseMessage metaResp = await _lcuClient.GetAsync($"/lol-replays/v1/metadata/{gameId}");
                if (metaResp.IsSuccessStatusCode)
                {
                    JObject metaJson = JObject.Parse(await metaResp.Content.ReadAsStringAsync());
                    string state = metaJson["state"]?.ToString()?.ToLowerInvariant() ?? "";
                    int progress = metaJson["downloadProgress"]?.Value<int>() ?? 0;
                    Debug.WriteLine($"[回放] 现有metadata状态: {state}, 进度: {progress}%");
                    return (exists: true, isValid: state == "watch" && progress >= 100);
                }
                return (exists: false, isValid: false);
            }
            catch (Exception ex)
            {
                Exception ex2 = ex;
                Debug.WriteLine("[回放] 检查现有回放异常: " + ex2.Message);
                return (exists: false, isValid: false);
            }
        }

        private async Task<bool> WaitForDownloadComplete(long gameId)
        {
            Stopwatch sw = Stopwatch.StartNew();
            int timeoutMs = 90000;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(2000);
                try
                {
                    HttpResponseMessage metaResp = await _lcuClient.GetAsync($"/lol-replays/v1/metadata/{gameId}");
                    if (metaResp.IsSuccessStatusCode)
                    {
                        JObject metaJson = JObject.Parse(await metaResp.Content.ReadAsStringAsync());
                        string state = metaJson["state"]?.ToString()?.ToLowerInvariant() ?? "";
                        int progress = metaJson["downloadProgress"]?.Value<int>() ?? 0;
                        Debug.WriteLine($"[回放] 下载状态: {state}, 进度: {progress}%");
                        if (state == "watch" && progress >= 100)
                        {
                            Debug.WriteLine("[回放] 下载完成");
                            return true;
                        }
                        if (state == "failed" || state == "error")
                        {
                            Debug.WriteLine("[回放] 下载失败");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[回放] 状态查询异常: " + ex.Message);
                }
            }
            Debug.WriteLine("[回放] 下载超时");
            return false;
        }

        #region 匹配，排位英雄自动预选
        /// <summary>
        /// 自动预选英雄（根据优先级列表）
        /// </summary>
        /// <param name="preSelectedHeroes">按优先级排序的预选英雄列表（Priority 越小越优先）</param>
        /// <returns>是否成功预选了一个英雄</returns>
        public async Task<bool> AutoDeclareIntentAsync(List<PreliminaryHero> preSelectedHeroes)
        {
            Debug.WriteLine($"[LCU 自动预选] 开始执行，列表 {preSelectedHeroes.Count} 个英雄");

            if (preSelectedHeroes == null || !preSelectedHeroes.Any())
                return false;

            try
            {
                var session = await GetChampSelectSession();
                if (session == null || session["actions"] == null)
                    return false;

                // 获取自己的 pick action（未完成的）
                var actions = session["actions"] as JArray;
                var myPickAction = actions
                    .SelectMany(a => a as JArray)
                    .FirstOrDefault(a =>
                        a["actorCellId"]?.Value<long>() == session["localPlayerCellId"]?.Value<long>() &&
                        a["type"]?.ToString() == "pick" &&
                        a["completed"]?.Value<bool>() != true);

                if (myPickAction == null)
                    return false;

                int actionId = myPickAction["id"]?.Value<int>() ?? 0;
                if (actionId == 0)
                    return false;

                // 获取已 ban 和已选英雄
                var bannedChampIds = new HashSet<int>(
                    (session["bans"]?["myTeamBans"] as JArray ?? new JArray())
                        .Select(t => t.Value<int>())
                        .Concat(
                            (session["bans"]?["theirTeamBans"] as JArray ?? new JArray())
                                .Select(t => t.Value<int>())
                        )
                );

                // 我方已选英雄
                var myTeam = session["myTeam"] as JArray ?? new JArray();
                foreach (var player in myTeam)
                {
                    int champId = player["championId"]?.Value<int>() ?? 0;
                    if (champId > 0) bannedChampIds.Add(champId);
                }

                // 找第一个可用英雄
                var availableHero = preSelectedHeroes
                    .FirstOrDefault(h => !bannedChampIds.Contains(h.ChampionId));

                if (availableHero == null)
                {
                    Debug.WriteLine("[自动预选] 所有预选英雄均已被禁或已选");
                    return false;
                }

                // 检查是否已经 hover 了这个英雄
                int currentHoverId = myPickAction["championId"]?.Value<int>() ?? 0;
                if (currentHoverId == availableHero.ChampionId)
                    return false; // 已经预选，无需重复

                // 执行预选
                var patchBody = new JObject { ["championId"] = availableHero.ChampionId };
                var response = await PatchAsync($"/lol-champ-select/v1/session/actions/{actionId}", patchBody);

                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[自动预选] 成功预选: {availableHero.ChampionName} (ID: {availableHero.ChampionId})");
                    return true;
                }
                else
                {
                    Debug.WriteLine($"[自动预选] 请求失败: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[自动预选] 执行异常: {ex.Message}");
                return false;
            }
        }

        public async Task<HttpResponseMessage> PatchAsync(string requestUri, JObject content)
        {
            try
            {
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), requestUri)
                {
                    Content = new StringContent(content.ToString(), Encoding.UTF8, "application/json")
                };
                return await _lcuClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LCU Patch] 异常: {ex.Message}");
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
        }
        #endregion

        #region ARAM 大乱斗自动抢英雄（色子交换）
        /// <summary>
        /// ARAM 模式下自动抢取 bench 中更高优先级的预选英雄
        /// 使用状态对象传递用户意图，避免 ref 限制
        /// </summary>
        /// <param name="preSelectedHeroes">预选英雄列表</param>
        /// <param name="state">ARAM 自动换状态（包含用户偏好和停止标志）</param>
        /// <returns>是否成功执行了一次交换</returns>
        public async Task<bool> AutoSwapInAramAsync(List<PreliminaryHero> preSelectedHeroes, AramAutoSwapState state)
        {
            if (preSelectedHeroes == null || !preSelectedHeroes.Any() || state.StopAutoSwap)
                return false;

            try
            {
                var session = await GetChampSelectSession();
                if (session == null) return false;

                int queueId = session["queueId"]?.Value<int>() ?? 0;
                if (queueId != 450) return false;

                int myCellId = session["localPlayerCellId"]?.Value<int>() ?? -1;
                if (myCellId == -1) return false;

                var myTeam = session["myTeam"] as JArray ?? new JArray();
                var myPlayer = myTeam.FirstOrDefault(p => p["cellId"]?.Value<int>() == myCellId);
                int currentChampId = myPlayer?["championId"]?.Value<int>() ?? 0;
                if (currentChampId == 0) return false;

                var currentHero = preSelectedHeroes.FirstOrDefault(h => h.ChampionId == currentChampId);

                // 用户意图检测
                if (currentHero != null)
                {
                    if (state.UserPreferredChampId == 0)
                    {
                        state.UserPreferredChampId = currentChampId;
                        Debug.WriteLine($"[ARAM Auto] 用户持有预选英雄 {currentHero.ChampionName}，记录为当前偏好");
                    }
                    else if (currentChampId != state.UserPreferredChampId)
                    {
                        var oldHero = preSelectedHeroes.First(h => h.ChampionId == state.UserPreferredChampId);
                        if (currentHero.Priority > oldHero.Priority)
                        {
                            Debug.WriteLine($"[ARAM Auto] 用户主动换到较低优先级 {currentHero.ChampionName}，本局停止自动换");
                            state.StopAutoSwap = true;
                            return false;
                        }
                        state.UserPreferredChampId = currentChampId;
                        Debug.WriteLine($"[ARAM Auto] 用户换到更高优先级 {currentHero.ChampionName}，继续优化");
                    }
                }
                else
                {
                    Debug.WriteLine("[ARAM Auto] 用户选择了非预选英雄，本局停止自动换");
                    state.StopAutoSwap = true;
                    return false;
                }

                if (state.StopAutoSwap) return false;

                // bench 逻辑
                var benchChampions = session["benchChampions"] as JArray ?? new JArray();
                var availableChampIds = new HashSet<int>();
                foreach (var item in benchChampions)
                {
                    int id = item["championId"]?.Value<int>() ?? 0;
                    if (id > 0) availableChampIds.Add(id);
                }

                int currentPriority = currentHero?.Priority ?? int.MaxValue;

                var bestHero = preSelectedHeroes
                    .Where(h => availableChampIds.Contains(h.ChampionId))
                    .OrderBy(h => h.Priority)
                    .FirstOrDefault();

                if (bestHero == null) return false;

                if (bestHero.Priority < currentPriority)
                {
                    var response = await PostAsync($"/lol-champ-select/v1/session/bench/swap/{bestHero.ChampionId}");
                    if (response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine($"[ARAM Auto 成功] 自动换到更高优先级 {bestHero.ChampionName} (Priority: {bestHero.Priority})");
                        await Task.Delay(50);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ARAM Auto] 异常: {ex.Message}");
                return false;
            }
        }
        #endregion

        /// <summary>
        /// 通用的 POST 请求封装（已在前一个回复中添加，这里确保完整）
        /// </summary>
        public async Task<HttpResponseMessage> PostAsync(string requestUri, JObject content = null)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
                if (content != null)
                {
                    request.Content = new StringContent(content.ToString(), Encoding.UTF8, "application/json");
                }
                return await _lcuClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LCU Post] 异常: {ex.Message}");
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
        }
    }
}
