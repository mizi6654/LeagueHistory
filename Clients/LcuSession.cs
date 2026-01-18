using League.PrimaryElection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;

namespace League.Clients
{
    /// <summary>
    /// LCU会话管理器 - 提供向后兼容的接口
    /// </summary>
    public class LcuSession
    {
        private LcuClient _lcuClient;
        private LcuConnector _connector;

        // 服务实例
        private SummonerService _summonerService;
        private MatchService _matchService;
        private RankedService _rankedService;
        private GameflowService _gameflowService;
        private ReplayService _replayService;
        private ChampionSelectService _championSelectService;
        private ChatService _chatService;
        public ChatService ChatService => _chatService;

        /// <summary>
        /// 获取HTTP客户端（保持向后兼容）
        /// </summary>
        public HttpClient Client => _lcuClient?.HttpClient;

        public LcuSession()
        {
            _connector = new LcuConnector();
        }

        /// <summary>
        /// 初始化LCU连接
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            var (success, port, token) = await _connector.InitializeAsync();

            if (!success)
            {
                Debug.WriteLine("[LCU] 连接初始化失败");
                return false;
            }

            try
            {
                // 创建LCU客户端
                _lcuClient = new LcuClient(port, token);

                // 初始化各个服务
                InitializeServices();

                // 测试连接
                return await TestConnectionAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LCU] 初始化异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 初始化所有服务
        /// </summary>
        private void InitializeServices()
        {
            _gameflowService = new GameflowService(_lcuClient);
            _summonerService = new SummonerService(_lcuClient);
            _matchService = new MatchService(_lcuClient);
            _rankedService = new RankedService(_lcuClient);
            _replayService = new ReplayService(_lcuClient);
            _championSelectService = new ChampionSelectService(_lcuClient, _gameflowService);
            _chatService = new ChatService(_lcuClient);
        }

        /// <summary>
        /// 测试LCU连接
        /// </summary>
        private async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _lcuClient.GetAsync("/lol-summoner/v1/current-summoner");

                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("[LCU] 初始化成功，已连接 LCU API");
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
            catch (Exception ex)
            {
                Debug.WriteLine($"[LCU] 其他异常: {ex}");
                return false;
            }
        }

        #region 发送战绩 - 增强版
        /// <summary>
        /// 选人阶段发送（弱化），已成功发送
        /// </summary>
        public async Task<bool> SendChampSelectMessageAsync(string text)
        {
            Debug.WriteLine($"[SendChampSelect] 尝试发送: {text}");

            var chatId = await _chatService.FindConversationIdAsync("championSelect");
            if (!string.IsNullOrEmpty(chatId))
            {
                bool result = await _chatService.SendMessageAsync(chatId, text);
                Debug.WriteLine($"[SendChampSelect] 结果: {(result ? "成功" : "失败")}");
                return result;
            }

            Debug.WriteLine("[SendChampSelect] 未找到 championSelect 会话，发送失败");
            return false;
        }

        
        /// <summary>
        /// Akari风格：直接使用游戏内聊天API（不依赖会话查找）
        /// </summary>
        public async Task<bool> SendGameChatDirectly(string message)
        {
            var payload = new
            {
                body = message,
                conversationType = "game"
            };

            var resp = await _lcuClient.PostAsync(
                "/lol-game-client-chat/v1/instant-messages",
                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
            );

            return resp.IsSuccessStatusCode;
        }
        #endregion

        #region 游戏内聊天消息发送，尝试使用websocket
        /// <summary>
        /// 综合发送方法：尝试所有可能的方案
        /// </summary>
        public async Task<bool> SendMessageComprehensive(string text, string target = "all")
        {
            try
            {
                Debug.WriteLine($"[SendMessageComprehensive] 开始发送，长度={text.Length}");

                // 1. 先尝试简单的API方法
                Debug.WriteLine("[SendMessageComprehensive] 尝试方法1: 简单API");
                var result1 = await SendInGameMessageSimple(text);
                if (result1)
                {
                    Debug.WriteLine("[SendMessageComprehensive] 方法1成功");
                    return true;
                }

                // 2. 尝试WebSocket方法
                Debug.WriteLine("[SendMessageComprehensive] 尝试方法2: WebSocket");
                var result2 = await SendViaWebSocket(text, target);
                if (result2)
                {
                    Debug.WriteLine("[SendMessageComprehensive] 方法2成功");
                    return true;
                }

                Debug.WriteLine("[SendMessageComprehensive] 所有方法都失败");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SendMessageComprehensive] 异常: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 简化版：直接尝试发送，不检查状态
        /// </summary>
        public async Task<bool> SendInGameMessageSimple(string text)
        {
            try
            {
                Debug.WriteLine($"[SendSimple] 尝试发送消息，长度={text.Length}");

                // 尝试不同的参数组合
                var payloads = new List<object>
                {
                    new { message = text, summonerName = "all" },
                    new { body = text, summonerName = "all" },
                    new { message = text, to = "all" },
                    new { text = text, target = "all" }
                };

                foreach (var payload in payloads)
                {
                    try
                    {
                        var content = new StringContent(
                            JsonConvert.SerializeObject(payload),
                            Encoding.UTF8,
                            "application/json"
                        );

                        var resp = await _lcuClient.PostAsync(
                            "/lol-game-client-chat/v1/instant-messages",
                            content
                        );

                        if (resp.IsSuccessStatusCode)
                        {
                            Debug.WriteLine($"[SendSimple] 发送成功，使用: {payload.GetType().Name}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SendSimple] 尝试异常: {ex.Message}");
                    }

                    await Task.Delay(50);
                }

                Debug.WriteLine("[SendSimple] 所有尝试都失败");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SendSimple] 异常: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 尝试通过WebSocket发送消息
        /// </summary>
        public async Task<bool> SendViaWebSocket(string text, string target = "all")
        {
            try
            {
                Debug.WriteLine($"[WebSocket] 准备发送消息，长度={text.Length}, target={target}");

                // 获取WebSocket连接信息
                var wsInfo = await GetWebSocketConnectionInfo();
                if (wsInfo == null)
                {
                    Debug.WriteLine("[WebSocket] 无法获取连接信息");
                    return false;
                }

                Debug.WriteLine($"[WebSocket] 连接信息: 端口={wsInfo.Port}, Token={wsInfo.Token}");

                // 建立WebSocket连接
                using (var ws = new ClientWebSocket())
                {
                    // 创建URI
                    var uri = new Uri($"wss://127.0.0.1:{wsInfo.Port}/");

                    // 设置选项
                    ws.Options.RemoteCertificateValidationCallback =
                        (sender, certificate, chain, sslPolicyErrors) => true;

                    // 添加认证头
                    string auth = Convert.ToBase64String(
                        System.Text.Encoding.ASCII.GetBytes($"riot:{wsInfo.Token}")
                    );
                    ws.Options.SetRequestHeader("Authorization", $"Basic {auth}");

                    Debug.WriteLine($"[WebSocket] 连接中: {uri}");

                    // 连接到WebSocket服务器
                    await ws.ConnectAsync(uri, CancellationToken.None);

                    Debug.WriteLine("[WebSocket] 连接成功");

                    // 构建消息
                    var message = new
                    {
                        type = "chat",
                        data = new
                        {
                            message = text,
                            summonerName = target,
                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        }
                    };

                    string json = JsonConvert.SerializeObject(message);
                    Debug.WriteLine($"[WebSocket] 发送JSON: {json}");

                    var buffer = System.Text.Encoding.UTF8.GetBytes(json);

                    // 发送消息
                    await ws.SendAsync(
                        new ArraySegment<byte>(buffer),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None
                    );

                    Debug.WriteLine("[WebSocket] 消息已发送");

                    // 等待确认（可选）
                    await Task.Delay(500);

                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] 异常: {ex.GetType().Name} - {ex.Message}");
                if (ex.InnerException != null)
                {
                    Debug.WriteLine($"[WebSocket] 内部异常: {ex.InnerException.Message}");
                }
                return false;
            }
        }

        /// <summary>
        /// 获取WebSocket连接信息
        /// </summary>
        private async Task<WebSocketInfo> GetWebSocketConnectionInfo()
        {
            try
            {
                // 方法1：从当前LcuClient获取连接信息
                if (_lcuClient != null)
                {
                    // 通常LcuClient会存储这些信息
                    // 你需要查看LcuClient类的实现
                }

                // 方法2：通过HTTP API获取连接信息
                var resp = await _lcuClient.GetAsync("/riotclient/command-line-args");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    var args = JsonConvert.DeserializeObject<string[]>(json);

                    string port = null;
                    string token = null;

                    foreach (var arg in args)
                    {
                        if (arg.StartsWith("--app-port="))
                            port = arg.Substring(11);
                        else if (arg.StartsWith("--remoting-auth-token="))
                            token = arg.Substring(22);
                    }

                    if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token))
                    {
                        return new WebSocketInfo
                        {
                            Port = port.Trim('"'),
                            Token = token.Trim('"')
                        };
                    }
                }

                // 方法3：尝试直接读取进程命令行（备用方法）
                var process = System.Diagnostics.Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
                if (process != null)
                {
                    var cmdLine = GetCommandLine(process);
                    var port = ExtractArgument(cmdLine, "--app-port=");
                    var token = ExtractArgument(cmdLine, "--remoting-auth-token=");

                    if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token))
                    {
                        return new WebSocketInfo
                        {
                            Port = port.Trim('"'),
                            Token = token.Trim('"')
                        };
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GetWebSocketConnectionInfo] 异常: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 获取进程命令行（从你的LcuConnector中提取）
        /// </summary>
        private string GetCommandLine(System.Diagnostics.Process process)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"
                );
                using var collection = searcher.Get();

                foreach (System.Management.ManagementObject obj in collection)
                {
                    return obj["CommandLine"]?.ToString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GetCommandLine] 失败: {ex.Message}");
            }
            return "";
        }

        /// <summary>
        /// 提取命令行参数（从你的LcuConnector中提取）
        /// </summary>
        private string ExtractArgument(string cmdLine, string key)
        {
            var match = System.Text.RegularExpressions.Regex.Match(cmdLine, key + "([^ ]+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// WebSocket连接信息
        /// </summary>
        private class WebSocketInfo
        {
            public string Port { get; set; }
            public string Token { get; set; }
        }
        #endregion

        #region 向后兼容的方法包装器

        /// <summary>
        /// 根据玩家的名称查询返回个人信息
        /// </summary>
        public async Task<JObject> GetSummonerByNameAsync(string summonerName)
        {
            return await _summonerService.GetSummonerByNameAsync(summonerName);
        }

        /// <summary>
        /// 根据summonerId查询玩家的名称等信息
        /// </summary>
        public async Task<JObject> GetGameNameBySummonerId(string summonerId)
        {
            return await _summonerService.GetSummonerByIdAsync(summonerId);
        }

        /// <summary>
        /// 获取当前登录玩家的等级隐私信息
        /// </summary>
        public async Task<JObject> GetCurrentSummoner()
        {
            return await _summonerService.GetCurrentSummonerAsync();
        }

        /// <summary>
        /// 获取当前排位统计数据
        /// </summary>
        public async Task<JObject> GetCurrentRankedStatsAsync(string puuid)
        {
            return await _rankedService.GetCurrentRankedStatsAsync(puuid);
        }

        // ============ 游戏流程相关方法 ============

        /// <summary>
        /// 获取游戏流程阶段
        /// </summary>
        public async Task<string> GetGameflowPhase()
        {
            return await _gameflowService.GetGameflowPhaseAsync();
        }

        /// <summary>
        /// 获取英雄选择会话信息
        /// </summary>
        public async Task<JObject> GetChampSelectSession()
        {
            return await _gameflowService.GetChampSelectSessionAsync();
        }

        /// <summary>
        /// 返回完整的游戏信息，包括 queueId！
        /// </summary>
        public async Task<JObject> GetGameSession()
        {
            return await _gameflowService.GetGameSessionAsync();
        }

        /// <summary>
        /// 下载游戏回放
        /// </summary>
        public async Task<bool> DownloadReplayAsync(long gameId, string contextData = "match-history")
        {
            return await _replayService.DownloadReplayAsync(gameId, contextData);
        }

        /// <summary>
        /// 播放游戏回放
        /// </summary>
        public async Task<bool> PlayReplayAsync(long gameId, string contextData = "match-history")
        {
            return await _replayService.PlayReplayAsync(gameId, contextData);
        }

        /// <summary>
        /// 自动预选英雄（根据优先级列表）
        /// </summary>
        public async Task<bool> AutoDeclareIntentAsync(List<PreliminaryHero> preSelectedHeroes)
        {
            return await _championSelectService.AutoDeclareIntentAsync(preSelectedHeroes);
        }

        /// <summary>
        /// ARAM 大乱斗自动抢英雄（极致贪婪版）
        /// </summary>
        public async Task AutoSwapToHighestPriorityAsync(List<PreliminaryHero> preSelectedHeroes)
        {
            await _championSelectService.AutoSwapToHighestPriorityAsync(preSelectedHeroes);
        }

        /// <summary>
        /// 查询历史战绩，带分页
        /// </summary>
        public async Task<JArray> FetchMatchesWithRetry(string puuid, int begIndex, int endIndex, bool isPreheat = false)
        {
            return await _matchService.FetchMatchesWithRetryAsync(puuid, begIndex, endIndex, isPreheat);
        }

        /// <summary>
        /// 查询历史战绩，不分页，默认返回20局记录
        /// </summary>
        public async Task<JArray> FetchLatestMatches(string puuid, bool isPreheat = false)
        {
            return await _matchService.FetchLatestMatchesAsync(puuid, isPreheat);
        }

        /// <summary>
        /// 获取历史战绩详情信息
        /// </summary>
        public async Task<JObject> GetFullMatchByGameIdAsync(long gameId)
        {
            return await _matchService.GetFullMatchByGameIdAsync(gameId);
        }
        #endregion

        #region 新方法 - 获取服务实例

        /// <summary>
        /// 获取召唤师服务实例
        /// </summary>
        public SummonerService GetSummonerService()
        {
            return _summonerService;
        }

        /// <summary>
        /// 获取对战记录服务实例
        /// </summary>
        public MatchService GetMatchService()
        {
            return _matchService;
        }

        /// <summary>
        /// 获取排位服务实例
        /// </summary>
        public RankedService GetRankedService()
        {
            return _rankedService;
        }

        /// <summary>
        /// 获取游戏流程服务实例
        /// </summary>
        public GameflowService GetGameflowService()
        {
            return _gameflowService;
        }

        /// <summary>
        /// 获取回放服务实例
        /// </summary>
        public ReplayService GetReplayService()
        {
            return _replayService;
        }

        /// <summary>
        /// 获取英雄选择服务实例
        /// </summary>
        public ChampionSelectService GetChampionSelectService()
        {
            return _championSelectService;
        }

        #endregion

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _lcuClient?.HttpClient?.Dispose();
        }
    }
}