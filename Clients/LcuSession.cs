using League.PrimaryElection;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

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