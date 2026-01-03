using System.Diagnostics;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using static League.FormMain;
using League.uitls;
using League.Models;
using League.Controls;

namespace League.Parsers
{
    public class MatchParser
    {
        public event Action<string> PlayerIconClicked;

        // 控制并行度的信号量（限制最大并发数）
        private static readonly SemaphoreSlim _parallelSemaphore = new SemaphoreSlim(initialCount: 10, maxCount: 10);

        /// <summary>
        /// 解析历史战绩数据，并构造显示列表（并行优化版）
        /// </summary>
        public async Task<Panel> ParseGameToPanelAsync(JObject game, string gameName, string tagLine)
        {
            try
            {
                // 基础数据校验
                if (game == null || game["participants"] == null || game["participantIdentities"] == null)
                {
                    return new Panel();
                }

                var allParticipants = game["participants"].Cast<JObject>().ToList();
                var identities = game["participantIdentities"].Cast<JObject>().ToList();

                // 查找当前玩家身份
                var participantIdentity = identities.FirstOrDefault(p =>
                    p["player"]?["gameName"]?.ToString().Equals(gameName, StringComparison.OrdinalIgnoreCase) == true &&
                    p["player"]?["tagLine"]?.ToString().Equals(tagLine, StringComparison.OrdinalIgnoreCase) == true);

                if (participantIdentity == null) return new Panel();

                // 获取当前玩家数据
                int participantId = participantIdentity["participantId"].Value<int>();
                var selfParticipant = allParticipants.FirstOrDefault(p => p["participantId"].Value<int>() == participantId);
                if (selfParticipant == null) return new Panel();

                // 提取核心数据
                var stats = selfParticipant["stats"];
                bool isWin = stats["win"].Value<bool>();
                int teamId = selfParticipant["teamId"].Value<int>();
                int duration = game["gameDuration"].Value<int>();
                DateTime createTime = ParseGameCreationTime(game["gameCreationDate"]);

                // 当前玩家基础数值
                int goldEarned = stats["goldEarned"].Value<int>();
                int damageToChampions = stats["totalDamageDealtToChampions"].Value<int>();
                int kills = stats["kills"].Value<int>();
                int deaths = stats["deaths"].Value<int>();
                int assists = stats["assists"].Value<int>();

                // 参团率计算
                int teamTotalKills = CalculateTeamTotalKills(allParticipants, teamId);
                string kpText = "0%";
                if (teamTotalKills > 0)
                {
                    double kp = (kills + assists) / (double)teamTotalKills * 100;
                    kpText = $"{kp:0.#}%";
                }

                // 排名计算
                var allPlayerStats = allParticipants.Select(p =>
                {
                    var pStats = p["stats"];
                    int pTeamId = p["teamId"].Value<int>();
                    int pTeamTotalKills = CalculateTeamTotalKills(allParticipants, pTeamId);

                    return new PlayerStats
                    {
                        Damage = pStats["totalDamageDealtToChampions"].Value<int>(),
                        Gold = pStats["goldEarned"].Value<int>(),
                        KP = pTeamTotalKills > 0
                            ? (pStats["kills"].Value<int>() + pStats["assists"].Value<int>()) / (double)pTeamTotalKills * 100
                            : 0
                    };
                }).ToList();

                int currentPlayerIndex = allParticipants.IndexOf(selfParticipant);
                int damageRank = GetRank(allPlayerStats.Select(s => s.Damage).ToList(), damageToChampions);
                int goldRank = GetRank(allPlayerStats.Select(s => s.Gold).ToList(), goldEarned);
                int kpRank = GetRank(allPlayerStats.Select(s => s.KP).ToList(), allPlayerStats[currentPlayerIndex].KP);

                string damageText = $"{damageToChampions / 1000f:0.#}K |{damageRank}";
                string goldText = $"{goldEarned / 1000f:0.#}K |{goldRank}";
                string kpTextWithRank = $"{kpText} |{kpRank}";


                // 并行加载所有资源
                var championTask = LoadWithSemaphore(() =>
                    Globals.resLoading.GetChampionInfoAsync(selfParticipant["championId"].Value<int>()));

                var spell1Task = LoadWithSemaphore(() =>
                    Globals.resLoading.GetSpellInfoAsync(selfParticipant["spell1Id"].Value<int>()));

                var spell2Task = LoadWithSemaphore(() =>
                    Globals.resLoading.GetSpellInfoAsync(selfParticipant["spell2Id"].Value<int>()));

                var runesTask = LoadWithSemaphore(() => LoadRunesAsync(stats));
                var itemsTask = LoadWithSemaphore(() => LoadItemsAsync(stats));

                await Task.WhenAll(championTask, spell1Task, spell2Task, runesTask, itemsTask);

                // 获取结果
                var (champIcon, champName, champDesc) = await championTask;
                var (spellImg1, spellName1, spellDesc1) = await spell1Task;
                var (spellImg2, spellName2, spellDesc2) = await spell2Task;
                var (primary, secondary) = await runesTask;
                var items = await itemsTask;

                // 提取当前玩家 puuid
                string selfPuuid = selfParticipant["puuid"]?.Value<string>() ?? "";

                // 构建比赛信息对象
                var match = new MatchInfo
                {
                    ResultText = isWin ? "胜利" : "失败",
                    ResultColor = isWin ? Color.Green : Color.Red,
                    DurationText = $"{duration / 60}分{duration % 60}秒",
                    Mode = GameMod.GetModeName(
                        game["queueId"]?.Value<int>() ?? -1,
                        game["gameMode"]?.ToString()
                    ),
                    GameTime = createTime.ToString("yyyy-MM-dd HH:mm"),
                    HeroIcon = champIcon,
                    ChampionName = champName,
                    ChampionDescription = champDesc,
                    Kills = kills,
                    Deaths = deaths,
                    Assists = assists,
                    SummonerSpells = new[] { spellImg1, spellImg2 },
                    SpellNames = new[] { spellName1, spellName2 },
                    SpellDescriptions = new[] { spellDesc1, spellDesc2 },
                    Items = items,
                    DamageText = damageText,
                    GoldText = goldText,
                    KPPercentage = kpTextWithRank,
                    PrimaryRunes = primary,
                    SecondaryRunes = secondary,
                    BlueTeam = new List<PlayerInfo>(),
                    RedTeam = new List<PlayerInfo>(),
                    SelfPlayer = new PlayerInfo
                    {
                        FullName = $"{gameName}#{tagLine}",
                        Avatar = champIcon,
                        TeamId = teamId,
                        IsSelf = true
                    }
                };

                FillMatchToolTips(match);

                // 并行加载队伍成员
                await ParallelLoadTeamMembers(match, allParticipants, identities, participantId);

                var panel = new MatchListPanel(match);
                panel.PlayerIconClicked += name => PlayerIconClicked?.Invoke(name);
                return panel;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"解析异常: {ex}");
                return new Panel();
            }
        }

        #region 并行加载辅助方法

        private async Task<T> LoadWithSemaphore<T>(Func<Task<T>> taskFactory)
        {
            await _parallelSemaphore.WaitAsync();
            try
            {
                return await taskFactory();
            }
            finally
            {
                _parallelSemaphore.Release();
            }
        }

        private async Task ParallelLoadTeamMembers(MatchInfo match, List<JObject> participants, List<JObject> identities, int selfParticipantId)
        {
            var blueTeam = new ConcurrentBag<PlayerInfo>();
            var redTeam = new ConcurrentBag<PlayerInfo>();

            var tasks = participants.Select(async p =>
            {
                await _parallelSemaphore.WaitAsync();
                try
                {
                    int pid = p["participantId"].Value<int>();
                    var identity = identities.FirstOrDefault(i =>
                        i["participantId"].Value<int>() == pid)?["player"];
                    if (identity == null) return;

                    int champId = p["championId"].Value<int>();
                    var (champIcon, champName, champDesc) = await Globals.resLoading.GetChampionInfoAsync(champId);

                    var player = new PlayerInfo
                    {
                        FullName = $"{identity["gameName"]}#{identity["tagLine"]}",
                        Avatar = champIcon,
                        TeamId = p["teamId"].Value<int>(),
                        IsSelf = pid == selfParticipantId
                    };

                    if (player.TeamId == 100)
                        blueTeam.Add(player);
                    else if (player.TeamId == 200)
                        redTeam.Add(player);
                }
                finally
                {
                    _parallelSemaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            match.BlueTeam = blueTeam.OrderBy(p => p.FullName).ToList();
            match.RedTeam = redTeam.OrderBy(p => p.FullName).ToList();
        }

        #endregion

        #region 资源加载方法（并行优化版）

        private async Task<(RuneInfo[] Primary, RuneInfo[] Secondary)> LoadRunesAsync(JToken stats)
        {
            var primaryTasks = Enumerable.Range(0, 4).Select(async i =>
            {
                await _parallelSemaphore.WaitAsync();
                try
                {
                    var runeId = stats[$"perk{i}"]?.Value<int>() ?? 0;
                    var item = Globals.resLoading.GetRuneById(runeId);
                    if (item == null) return null;

                    var icon = await Globals.resLoading.GetRuneInfoAsync(runeId);
                    return new RuneInfo
                    {
                        RuneId = runeId,
                        Icon = icon.image,
                        Name = icon.name,
                        Description = icon.description
                    };
                }
                finally
                {
                    _parallelSemaphore.Release();
                }
            });

            var secondaryTasks = Enumerable.Range(4, 2).Select(async i =>
            {
                await _parallelSemaphore.WaitAsync();
                try
                {
                    var runeId = stats[$"perk{i}"]?.Value<int>() ?? 0;
                    var item = Globals.resLoading.GetRuneById(runeId);
                    if (item == null) return null;

                    var icon = await Globals.resLoading.GetRuneInfoAsync(runeId);
                    return new RuneInfo
                    {
                        RuneId = runeId,
                        Icon = icon.image,
                        Name = icon.name,
                        Description = icon.description
                    };
                }
                finally
                {
                    _parallelSemaphore.Release();
                }
            });

            var primaryRunes = await Task.WhenAll(primaryTasks);
            var secondaryRunes = await Task.WhenAll(secondaryTasks);

            return (primaryRunes, secondaryRunes);
        }

        public async Task<Item[]> LoadItemsAsync(JToken stats)
        {
            var itemTasks = Enumerable.Range(0, 6).Select(async i =>
            {
                await _parallelSemaphore.WaitAsync();
                try
                {
                    int itemId = stats[$"item{i}"]?.Value<int>() ?? 0;
                    if (itemId <= 0) return null;

                    var (image, name, desc) = await Globals.resLoading.GetItemInfoAsync(itemId);
                    return new Item
                    {
                        Id = itemId,
                        Icon = image,
                        Name = name,
                        Description = desc
                    };
                }
                finally
                {
                    _parallelSemaphore.Release();
                }
            });

            return await Task.WhenAll(itemTasks);
        }

        #endregion

        #region Helper Methods

        private void FillMatchToolTips(MatchInfo match)
        {
            // === 装备 ===
            match.ItemNames = new string[6];
            match.ItemDescriptions = new string[6];
            for (int i = 0; i < 6; i++)
            {
                var item = match.Items?[i];
                if (item != null)
                {
                    match.ItemNames[i] = item.Name;
                    match.ItemDescriptions[i] = item.Description;
                }
                else
                {
                    match.ItemNames[i] = null;
                    match.ItemDescriptions[i] = null;
                }
            }

            // === 主系符文 ===
            match.PrimaryRuneNames = new string[4];
            match.PrimaryRuneDescriptions = new string[4];
            for (int i = 0; i < 4; i++)
            {
                var rune = match.PrimaryRunes?[i];
                if (rune != null)
                {
                    match.PrimaryRuneNames[i] = rune.Name;
                    match.PrimaryRuneDescriptions[i] = rune.Description;
                }
                else
                {
                    match.PrimaryRuneNames[i] = null;
                    match.PrimaryRuneDescriptions[i] = null;
                }
            }

            // === 副系符文 ===
            match.SecondaryRuneNames = new string[2];
            match.SecondaryRuneDescriptions = new string[2];
            for (int i = 0; i < 2; i++)
            {
                var rune = match.SecondaryRunes?[i];
                if (rune != null)
                {
                    match.SecondaryRuneNames[i] = rune.Name;
                    match.SecondaryRuneDescriptions[i] = rune.Description;
                }
                else
                {
                    match.SecondaryRuneNames[i] = null;
                    match.SecondaryRuneDescriptions[i] = null;
                }
            }
        }

        private DateTime ParseGameCreationTime(JToken creationDateToken)
        {
            if (creationDateToken.Type == JTokenType.Date)
                return creationDateToken.Value<DateTime>().ToLocalTime();

            if (creationDateToken.Type == JTokenType.Integer)
            {
                long timestamp = creationDateToken.Value<long>();
                return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToLocalTime().DateTime;
            }

            throw new ArgumentException("无效的 gameCreationDate 格式");
        }

        private int CalculateTeamTotalKills(List<JObject> participants, int teamId)
        {
            return participants
                .Where(p => p["teamId"].Value<int>() == teamId)
                .Sum(p => p["stats"]?["kills"]?.Value<int>() ?? 0);
        }

        private int GetRank<T>(List<T> allValues, T currentValue) where T : IComparable
        {
            // 降序排序后查找当前值的第一个出现位置
            var sorted = allValues.OrderByDescending(v => v).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (currentValue.CompareTo(sorted[i]) == 0)
                    return i + 1; // 返回从1开始的排名
            }
            return sorted.Count;
        }

        #endregion

        #region Helper Classes

        private class PlayerStats
        {
            public int Damage { get; set; }
            public int Gold { get; set; }
            public double KP { get; set; }
        }

        #endregion
    }
}