using League.PrimaryElection;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Text;

namespace League.Clients
{
    /// <summary>
    /// 英雄选择服务
    /// </summary>
    public class ChampionSelectService
    {
        private readonly LcuClient _client;
        private readonly GameflowService _gameflowService;

        public ChampionSelectService(LcuClient client, GameflowService gameflowService)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _gameflowService = gameflowService ?? throw new ArgumentNullException(nameof(gameflowService));
        }

        /// <summary>
        /// 自动预选英雄（根据优先级列表）
        /// </summary>
        public async Task<bool> AutoDeclareIntentAsync(List<PreliminaryHero> preSelectedHeroes)
        {
            Debug.WriteLine($"[LCU 自动预选] 开始执行，列表 {preSelectedHeroes.Count} 个英雄");

            if (preSelectedHeroes == null || !preSelectedHeroes.Any())
                return false;

            try
            {
                var session = await _gameflowService.GetChampSelectSessionAsync();
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

                var actionId = myPickAction["id"]?.Value<int>() ?? 0;
                if (actionId == 0)
                    return false;

                // 获取已禁和已选英雄
                var bannedChampIds = GetBannedAndSelectedChampions(session);

                // 找第一个可用英雄
                var availableHero = preSelectedHeroes
                    .FirstOrDefault(h => !bannedChampIds.Contains(h.ChampionId));

                if (availableHero == null)
                {
                    Debug.WriteLine("[自动预选] 所有预选英雄均已被禁或已选");
                    return false;
                }

                // 检查是否已经 hover 了这个英雄
                var currentHoverId = myPickAction["championId"]?.Value<int>() ?? 0;
                if (currentHoverId == availableHero.ChampionId)
                    return false; // 已经预选，无需重复

                // 执行预选
                var patchBody = new JObject { ["championId"] = availableHero.ChampionId };
                var jsonContent = new StringContent(patchBody.ToString(), Encoding.UTF8, "application/json");

                var response = await _client.PatchAsync($"/lol-champ-select/v1/session/actions/{actionId}", jsonContent);

                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[自动预选] 成功预选: {availableHero.ChampionName} (ID: {availableHero.ChampionId})");
                    return true;
                }

                Debug.WriteLine($"[自动预选] 请求失败: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[自动预选] 执行异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ARAM 大乱斗自动抢英雄
        /// </summary>
        public async Task AutoSwapToHighestPriorityAsync(List<PreliminaryHero> preSelectedHeroes)
        {
            if (preSelectedHeroes == null || !preSelectedHeroes.Any())
                return;

            try
            {
                var session = await _gameflowService.GetChampSelectSessionAsync();
                if (session == null) return;

                var queueId = session["queueId"]?.Value<int>() ?? 0;
                if (queueId != 450 && queueId != 2400) return; // 只有非ARAM模式才返回

                var myCellId = session["localPlayerCellId"]?.Value<int>() ?? -1;
                if (myCellId == -1) return;

                var myTeam = session["myTeam"] as JArray ?? new JArray();
                var myPlayer = myTeam.FirstOrDefault(p => p["cellId"]?.Value<int>() == myCellId);
                var currentChampId = myPlayer?["championId"]?.Value<int>() ?? 0;

                if (currentChampId == 0) return;

                var currentPriority = preSelectedHeroes
                    .FirstOrDefault(h => h.ChampionId == currentChampId)
                    ?.Priority ?? int.MaxValue;

                // bench 中所有可用英雄
                var benchChampions = session["benchChampions"] as JArray ?? new JArray();
                var availableChampIds = new HashSet<int>();

                foreach (var item in benchChampions)
                {
                    var id = item["championId"]?.Value<int>() ?? 0;
                    if (id > 0) availableChampIds.Add(id);
                }

                // 找出 bench 中优先级最高的预选英雄
                var bestAvailable = preSelectedHeroes
                    .Where(h => availableChampIds.Contains(h.ChampionId))
                    .OrderBy(h => h.Priority)
                    .FirstOrDefault();

                if (bestAvailable == null) return;

                // 只有当 bench 中的最高优先级严格高于当前持有，才抢
                if (bestAvailable.Priority < currentPriority)
                {
                    var response = await _client.PostAsync($"/lol-champ-select/v1/session/bench/swap/{bestAvailable.ChampionId}");

                    if (response.IsSuccessStatusCode)
                    {
                        await Task.Delay(100); // 稍微延时，避免API过于频繁
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ARAM 贪婪抢] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取已禁和已选英雄列表
        /// </summary>
        private HashSet<int> GetBannedAndSelectedChampions(JObject session)
        {
            var bannedChampIds = new HashSet<int>();

            // 获取禁用英雄
            var myTeamBans = session["bans"]?["myTeamBans"] as JArray ?? new JArray();
            var theirTeamBans = session["bans"]?["theirTeamBans"] as JArray ?? new JArray();

            foreach (var ban in myTeamBans.Concat(theirTeamBans))
            {
                var champId = ban.Value<int>();
                if (champId > 0) bannedChampIds.Add(champId);
            }

            // 获取我方已选英雄
            var myTeam = session["myTeam"] as JArray ?? new JArray();
            foreach (var player in myTeam)
            {
                var champId = player["championId"]?.Value<int>() ?? 0;
                if (champId > 0) bannedChampIds.Add(champId);
            }

            return bannedChampIds;
        }
    }
}