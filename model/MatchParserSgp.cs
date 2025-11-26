using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace League.model
{
    public class MatchParserSgp
    {
        private class ResourceInfo
        {
            public Image Image { get; set; }

            public string Name { get; set; }

            public string Description { get; set; }
        }

        private const int MAX_CONCURRENT_PARSING = 10;  // 增加到10

        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(MAX_CONCURRENT_PARSING, MAX_CONCURRENT_PARSING);

        private static int _currentParsingCount;

        private static readonly object _lockObject;

        private static readonly ConcurrentDictionary<int, WeakReference<ResourceInfo>> _championCache;

        private static readonly ConcurrentDictionary<int, WeakReference<ResourceInfo>> _spellCache;

        private static readonly ConcurrentDictionary<int, WeakReference<ResourceInfo>> _itemCache;

        private static readonly ConcurrentDictionary<int, WeakReference<ResourceInfo>> _runeCache;

        private static Image _defaultChampionIcon;

        private static Image _defaultSpellIcon;

        private static Image _defaultItemIcon;

        private static Image _defaultRuneIcon;

        public event Action<string> PlayerIconClicked;

        static MatchParserSgp()
        {
            _currentParsingCount = 0;
            _lockObject = new object();
            _championCache = new ConcurrentDictionary<int, WeakReference<ResourceInfo>>();
            _spellCache = new ConcurrentDictionary<int, WeakReference<ResourceInfo>>();
            _itemCache = new ConcurrentDictionary<int, WeakReference<ResourceInfo>>();
            _runeCache = new ConcurrentDictionary<int, WeakReference<ResourceInfo>>();
            _defaultChampionIcon = CreateDefaultImage(Color.Gray);
            _defaultSpellIcon = CreateDefaultImage(Color.LightGray);
            _defaultItemIcon = CreateDefaultImage(Color.DarkGray);
            _defaultRuneIcon = CreateDefaultImage(Color.Gold);
        }

        private static Image CreateDefaultImage(Color color)
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(color);
            }
            return bmp;
        }

        // 修改解析方法，使用 SemaphoreSlim 替代 lock
        public async Task<Panel> ParseGameToPanelFromSgpAsync(JObject game, string gameName, string tagLine, int index)
        {
            // 使用 SemaphoreSlim 控制并发，而不是直接跳过
            await _semaphore.WaitAsync();
            try
            {
                if (game == null)
                {
                    return CreateEmptyPanel();
                }

                MatchInfo matchInfo = await ParseGameDataLightweight(game, gameName, tagLine, index);
                if (matchInfo == null)
                {
                    return CreateEmptyPanel();
                }

                MatchListPanel panel = new MatchListPanel(matchInfo);
                panel.PlayerIconClicked += delegate (string summonerId)
                {
                    this.PlayerIconClicked?.Invoke(summonerId);
                };
                return panel;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"解析异常: {ex}");
                return CreateEmptyPanel();
            }
            finally
            {
                game?.RemoveAll();
                game = null;
                _semaphore.Release();
            }
        }

        private async Task<MatchInfo> ParseGameDataLightweight(JObject game, string gameName, string tagLine, int index)
        {
            var (gameJson, participants) = ParseJsonStructure(game);
            if (participants == null || participants.Count == 0)
            {
                return null;
            }
            List<JObject> allParticipants = participants.Cast<JObject>().ToList();
            JObject selfParticipant = FindSelfParticipant(allParticipants, gameName, tagLine);
            if (selfParticipant == null)
            {
                return null;
            }
            long gameId = ((long?)gameJson["gameId"]).GetValueOrDefault();
            (string currPuuid, string currSummonerId, bool isWin, int teamId, int duration, DateTime createTime, int kills, int deaths, int assists, int goldEarned, int damageToChampions, double killParticipation, string mode, string queueId)? basicData = ExtractBasicMatchData(gameJson, selfParticipant, participants);
            if (!basicData.HasValue)
            {
                return null;
            }
            (string, string, bool, int, int, DateTime, int, int, int, int, int, double, string, string) value = basicData.Value;
            string currPuuid = value.Item1;
            string currSummonerId = value.Item2;
            bool isWin = value.Item3;
            int teamId = value.Item4;
            int duration = value.Item5;
            DateTime createTime = value.Item6;
            int kills = value.Item7;
            int deaths = value.Rest.Item1;
            int assists = value.Rest.Item2;
            int goldEarned = value.Rest.Item3;
            int damageToChampions = value.Rest.Item4;
            double killParticipation = value.Rest.Item5;
            string mode = value.Rest.Item6;
            string queueId = value.Rest.Item7;
            (int, int, int) tuple2 = CalculateRanks(participants, selfParticipant, damageToChampions, goldEarned, killParticipation);
            int damageRank = tuple2.Item1;
            int goldRank = tuple2.Item2;
            int kpRank = tuple2.Item3;
            (int, string) tuple3 = ExtractDamageTakenData(selfParticipant, participants);
            _ = tuple3.Item1;
            string damageTakenText = tuple3.Item2;
            (Image champIcon, string champName, Image spellImg1, Image spellImg2, string spellName1, string spellDesc1, string spellName2, string spellDesc2, RuneInfo[] primaryRunes, RuneInfo[] secondaryRunes, RuneInfo[] statRunes, Item[] items) resources = await LoadEssentialResourcesSimple(selfParticipant);

            // 修改：加载所有玩家的海克斯符文
            var allPlayersAugments = await LoadAllPlayersAugmentsAsync(allParticipants);
            var selfAugments = allPlayersAugments.ContainsKey(currPuuid) ? allPlayersAugments[currPuuid] : Array.Empty<RuneInfo>();

            MatchInfo matchInfo = CreateMatchInfo(index, gameId, currPuuid, currSummonerId, isWin, teamId, duration, createTime, kills, deaths, assists, $"{(float)damageToChampions / 1000f:0.#}K |{damageRank}", $"{(float)goldEarned / 1000f:0.#}K |{goldRank}", $"{killParticipation:P0} |{kpRank}", damageTakenText, mode, queueId, resources.champIcon, resources.champName, resources.spellImg1, resources.spellImg2, resources.primaryRunes, resources.secondaryRunes, resources.statRunes, resources.items, gameName.Contains("#") ? gameName.Substring(0, gameName.IndexOf('#')) : gameName, game, allParticipants, selfAugments, allPlayersAugments);
            matchInfo.SpellNames[0] = resources.spellName1;
            matchInfo.SpellDescriptions[0] = resources.spellDesc1;
            matchInfo.SpellNames[1] = resources.spellName2;
            matchInfo.SpellDescriptions[1] = resources.spellDesc2;
            await LoadTeamMembers(matchInfo, allParticipants, gameName, tagLine);
            return matchInfo;
        }

        //添加一个方法来加载所有玩家的海克斯符文
        private async Task<Dictionary<string, RuneInfo[]>> LoadAllPlayersAugmentsAsync(List<JObject> allParticipants)
        {
            var allAugments = new Dictionary<string, RuneInfo[]>();

            // 使用信号量限制并发数
            var semaphore = new SemaphoreSlim(5);
            var tasks = allParticipants.Select(async participant =>
            {
                await semaphore.WaitAsync();
                try
                {
                    string puuid = participant["puuid"]?.ToString();
                    if (!string.IsNullOrEmpty(puuid))
                    {
                        int[] augmentIds = ExtractAugments(participant);
                        var augments = await LoadAugmentsAsync(augmentIds);
                        allAugments[puuid] = augments;
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            return allAugments;
        }

        // 修改海克斯符文加载方法，确保正确处理空值
        private async Task<RuneInfo[]> LoadAugmentsAsync(int[] augmentIds)
        {
            if (augmentIds == null || augmentIds.Length == 0)
            {
                return Array.Empty<RuneInfo>();
            }

            // 使用并发加载，但限制并发数避免过多网络请求
            var semaphore = new SemaphoreSlim(3); // 限制同时下载3个图标
            var tasks = augmentIds.Where(id => id > 0).Select(async id =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var (img, name, desc) = await FormMain.Globals.resLoading.GetAugmentInfoAsync(id);
                    if (img != null)
                    {
                        return new RuneInfo
                        {
                            id = id,
                            Icon = img,
                            name = name,
                            longDesc = desc
                        };
                    }
                    return null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"加载海克斯符文 {id} 失败: {ex.Message}");
                    return null;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            return results.Where(r => r != null).ToArray();
        }
        

        // 修改 ExtractAugments 方法，确保正确处理空值
        private int[] ExtractAugments(JObject participant)
        {
            if (participant == null)
            {
                return Array.Empty<int>();
            }

            int[] augIds = new int[]
            {
                participant["playerAugment1"]?.Value<int>() ?? 0,
                participant["playerAugment2"]?.Value<int>() ?? 0,
                participant["playerAugment3"]?.Value<int>() ?? 0,
                participant["playerAugment4"]?.Value<int>() ?? 0,
                participant["playerAugment5"]?.Value<int>() ?? 0,
                participant["playerAugment6"]?.Value<int>() ?? 0
            };

            return augIds.Where(x => x > 0).ToArray();
        }


        private (JObject gameJson, JArray participants) ParseJsonStructure(JObject game)
        {
            JObject gameJson = null;
            JArray participants = null;
            if (game["json"] != null)
            {
                gameJson = game["json"] as JObject;
                participants = (gameJson?["participants"] as JArray) ?? (gameJson?["info"]?["participants"] as JArray);
            }
            else
            {
                gameJson = game;
                participants = (game["participants"] as JArray) ?? (game["info"]?["participants"] as JArray);
            }
            return (gameJson: gameJson, participants: participants);
        }

        private JObject FindSelfParticipant(List<JObject> participants, string gameName, string tagLine)
        {
            if (participants == null || participants.Count == 0)
            {
                return null;
            }
            JObject self = null;
            self = participants.FirstOrDefault((JObject p) => string.Equals(p["riotIdGameName"]?.ToString(), gameName, StringComparison.OrdinalIgnoreCase) && string.Equals(p["riotIdTagline"]?.ToString(), tagLine, StringComparison.OrdinalIgnoreCase));
            if (self != null)
            {
                return self;
            }
            self = participants.FirstOrDefault((JObject p) => string.Equals(p["riotIdGameName"]?.ToString(), gameName, StringComparison.OrdinalIgnoreCase));
            if (self != null)
            {
                return self;
            }
            self = participants.FirstOrDefault((JObject p) => string.Equals(p["summonerName"]?.ToString(), gameName, StringComparison.OrdinalIgnoreCase));
            if (self != null)
            {
                return self;
            }
            self = participants.FirstOrDefault((JObject p) => (p["riotIdGameName"]?.ToString() ?? "").IndexOf(gameName, StringComparison.OrdinalIgnoreCase) >= 0 || (p["summonerName"]?.ToString() ?? "").IndexOf(gameName, StringComparison.OrdinalIgnoreCase) >= 0);
            if (self != null)
            {
                return self;
            }
            Debug.WriteLine("⚠ 未找到匹配玩家，使用第一个玩家代替以确保解析不中断。");
            return participants.FirstOrDefault();
        }

        private (string currPuuid, string currSummonerId, bool isWin, int teamId, int duration, DateTime createTime, int kills, int deaths, int assists, int goldEarned, int damageToChampions, double killParticipation, string mode, string queueId)? ExtractBasicMatchData(JObject gameJson, JObject selfParticipant, JArray participants)
        {
            try
            {
                JToken info = gameJson["info"] ?? gameJson;
                string currPuuid = (string?)selfParticipant["puuid"];
                string currSummonerId = (string?)selfParticipant["summonerId"];
                bool isWin = selfParticipant["win"]?.Value<bool>() ?? false;
                int teamId = selfParticipant["teamId"]?.Value<int>() ?? 100;
                int duration = info["gameDuration"]?.Value<int>() ?? 0;
                long gameStart = info["gameStartTimestamp"]?.Value<long>() ?? info["gameCreation"]?.Value<long>() ?? 0;
                DateTime createTime = ((gameStart > 0) ? DateTimeOffset.FromUnixTimeMilliseconds(gameStart).LocalDateTime : DateTime.MinValue);
                int kills = selfParticipant["kills"]?.Value<int>() ?? 0;
                int deaths = selfParticipant["deaths"]?.Value<int>() ?? 0;
                int assists = selfParticipant["assists"]?.Value<int>() ?? 0;
                int goldEarned = selfParticipant["goldEarned"]?.Value<int>() ?? 0;
                int damageToChampions = selfParticipant["totalDamageDealtToChampions"]?.Value<int>() ?? 0;
                double killParticipation = (selfParticipant["challenges"]?["killParticipation"]?.Value<double>()).GetValueOrDefault();
                var (mode, queueId) = ExtractGameMode(info as JObject);
                return (currPuuid, currSummonerId, isWin, teamId, duration, createTime, kills, deaths, assists, goldEarned, damageToChampions, killParticipation, mode, queueId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("提取基础数据异常: " + ex.Message);
                return null;
            }
        }

        private (string mode, string queueId) ExtractGameMode(JObject gameJson)
        {
            string mode = "未知模式";
            string queueId = "";
            int queue = gameJson["queueId"]?.Value<int>() ?? (gameJson["info"]?["queueId"]?.Value<int>()).GetValueOrDefault();
            if (queue > 0)
            {
                queueId = "q_" + queue;
            }
            string gameMode = gameJson["gameMode"]?.ToString() ?? gameJson["info"]?["gameMode"]?.ToString();
            switch (queue)
            {
                case 420:
                    mode = "单双排";
                    break;
                case 440:
                    mode = "灵活组排";
                    break;
                case 400:
                case 430:
                    mode = "匹配";
                    break;
                case 450:
                    mode = "大乱斗";
                    break;
                case 1700:
                    mode = "斗魂竞技场";
                    break;
                case 900:
                    mode = "无限火力";
                    break;
                case 1020:
                    mode = "克隆大作战";
                    break;
                case 1300:
                    mode = "极限闪击";
                    break;
                case 1400:
                    mode = "终极魔典";
                    break;
                case 1900:
                    mode = "快速模式";
                    break;
                case 2000:
                case 2010:
                case 2020:
                    mode = "神木之门";
                    break;
                case 830:
                case 840:
                case 850:
                    mode = "人机对战";
                    break;
                case 950:
                case 960:
                    mode = "末日人机";
                    break;
                case 700:
                case 720:
                case 740:
                case 750:
                    mode = "云顶之弈";
                    break;
                case 1090:
                    mode = "云顶(快速)";
                    break;
                case 1100:
                    mode = "云顶(排位)";
                    break;
                case 610:
                    mode = "联盟战棋";
                    break;
                case 100:
                    mode = "魄罗大乱斗";
                    break;
                case 910:
                    mode = "互选征召";
                    break;
                case 1200:
                    mode = "闪击模式";
                    break;
                case 2400:
                    mode = "海克斯乱斗";
                    break;
                case 0:
                    if (!string.IsNullOrEmpty(gameMode))
                    {
                        string text3 = gameMode.ToLower();
                        if (1 == 0)
                        {
                        }
                        string text2 = text3 switch
                        {
                            "classic" => "经典模式",
                            "aram" => "大乱斗",
                            "urf" => "无限火力",
                            "onoforall" => "克隆大作战",
                            "nexusblitz" => "极限闪击",
                            "cherry" => "斗魂竞技场",
                            "swiftplay" => "快速模式",
                            "tft" => "云顶之弈",
                            "tutorial" => "教程",
                            "practice" => "练习模式",
                            "intro" => "新手教程",
                            _ => gameMode,
                        };
                        if (1 == 0)
                        {
                        }
                        mode = text2;
                    }
                    break;
                default:
                    if (!string.IsNullOrEmpty(gameMode))
                    {
                        string text = gameMode.ToLower();
                        if (1 == 0)
                        {
                        }
                        string text2 = text switch
                        {
                            "classic" => "经典模式",
                            "aram" => "大乱斗",
                            "urf" => "无限火力",
                            "onoforall" => "克隆大作战",
                            "nexusblitz" => "极限闪击",
                            "cherry" => "斗魂竞技场",
                            "swiftplay" => "快速模式",
                            "tft" => "云顶之弈",
                            "tutorial" => "教程",
                            "practice" => "练习模式",
                            "intro" => "新手教程",
                            _ => $"未知模式({queue})",
                        };
                        if (1 == 0)
                        {
                        }
                        mode = text2;
                    }
                    else
                    {
                        mode = $"未知模式({queue})";
                    }
                    break;
            }
            return (mode: mode, queueId: queueId);
        }

        private (int damageRank, int goldRank, int kpRank) CalculateRanks(JArray participants, JObject selfParticipant, int selfDamage, int selfGold, double selfKP)
        {
            if (participants == null)
            {
                return (damageRank: 1, goldRank: 1, kpRank: 1);
            }
            var allStats = participants.Cast<JObject>().Select(delegate (JObject p)
            {
                int damage = p["totalDamageDealtToChampions"]?.Value<int>() ?? 0;
                int gold = p["goldEarned"]?.Value<int>() ?? 0;
                double num = 0.0;
                JToken jToken = p["challenges"];
                if (jToken != null)
                {
                    num = jToken["killParticipation"]?.Value<double>() ?? 0.0;
                }
                if (num == 0.0)
                {
                    num = p["killParticipation"]?.Value<double>() ?? 0.0;
                }
                return new
                {
                    Damage = damage,
                    Gold = gold,
                    KP = num
                };
            }).ToList();
            int damageRank = GetRankInt(allStats.Select(s => s.Damage).ToList(), selfDamage);
            int goldRank = GetRankInt(allStats.Select(s => s.Gold).ToList(), selfGold);
            int kpRank = GetRankDouble(allStats.Select(s => s.KP).ToList(), selfKP);
            return (damageRank: damageRank, goldRank: goldRank, kpRank: kpRank);
        }

        private int GetRankInt(List<int> allValues, int currentValue)
        {
            List<int> sorted = allValues.OrderByDescending((int v) => v).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (currentValue == sorted[i])
                {
                    return i + 1;
                }
            }
            return sorted.Count;
        }

        private int GetRankDouble(List<double> allValues, double currentValue, double eps = 1E-06)
        {
            List<double> sorted = allValues.OrderByDescending((double v) => v).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (Math.Abs(currentValue - sorted[i]) <= eps)
                {
                    return i + 1;
                }
            }
            for (int i2 = 0; i2 < sorted.Count; i2++)
            {
                if (currentValue > sorted[i2])
                {
                    return i2 + 1;
                }
            }
            return sorted.Count;
        }

        private int GetRank<T>(List<T> allValues, T currentValue) where T : IComparable
        {
            List<T> sorted = allValues.OrderByDescending((T v) => v).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (currentValue.CompareTo(sorted[i]) == 0)
                {
                    return i + 1;
                }
            }
            return sorted.Count;
        }

        private (int damageTaken, string damageTakenText) ExtractDamageTakenData(JObject selfParticipant, JArray participants)
        {
            try
            {
                int damageTaken = selfParticipant["totalDamageTaken"]?.Value<int>() ?? 0;
                int damageTakenRank = CalculateDamageTakenRank(participants, damageTaken);
                string damageTakenText = $"{(float)damageTaken / 1000f:0.#}K |{damageTakenRank}";
                return (damageTaken: damageTaken, damageTakenText: damageTakenText);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("提取承伤数据异常: " + ex.Message);
                return (damageTaken: 0, damageTakenText: "0K |1");
            }
        }

        private int CalculateDamageTakenRank(JArray participants, int selfDamageTaken)
        {
            if (participants == null)
            {
                return 1;
            }
            List<int> allDamageTaken = (from JObject p in participants
                                        select p["totalDamageTaken"]?.Value<int>() ?? 0).ToList();
            return GetRank(allDamageTaken, selfDamageTaken);
        }

        private async Task<RuneInfo[]> LoadStatRunesAsync(JObject participant)
        {
            JToken perks = participant["perks"];
            if (perks == null)
            {
                return new RuneInfo[3];
            }
            JToken statPerks = perks["statPerks"];
            if (statPerks == null)
            {
                return new RuneInfo[3];
            }
            int[] statRuneIds = new int[3]
            {
                statPerks["offense"]?.Value<int>() ?? 0,
                statPerks["flex"]?.Value<int>() ?? 0,
                statPerks["defense"]?.Value<int>() ?? 0
            };
            IEnumerable<Task<RuneInfo>> statRuneTasks = statRuneIds.Select(async delegate (int runeId, int index)
            {
                if (runeId <= 0)
                {
                    return new RuneInfo();
                }
                var (image, name, description) = await FormMain.Globals.resLoading.GetRuneInfoAsync(runeId);
                return new RuneInfo
                {
                    RuneId = runeId,
                    Icon = image,
                    Name = name,
                    Description = description
                };
            });
            return await Task.WhenAll(statRuneTasks);
        }

        private async Task<(Image champIcon, string champName, Image spellImg1, Image spellImg2, string spellName1, string spellDesc1, string spellName2, string spellDesc2, RuneInfo[] primaryRunes, RuneInfo[] secondaryRunes, RuneInfo[] statRunes, Item[] items)> LoadEssentialResourcesSimple(JObject participant)
        {
            int championId = participant["championId"]?.Value<int>() ?? 0;
            int spell1Id = participant["spell1Id"]?.Value<int>() ?? 0;
            int spell2Id = participant["spell2Id"]?.Value<int>() ?? 0;
            Task<(Image image, string name, string description)> championTask = FormMain.Globals.resLoading.GetChampionInfoAsync(championId);
            Task<(Image image, string name, string description)> spell1Task = FormMain.Globals.resLoading.GetSpellInfoAsync(spell1Id);
            Task<(Image image, string name, string description)> spell2Task = FormMain.Globals.resLoading.GetSpellInfoAsync(spell2Id);
            Task<(RuneInfo[] Primary, RuneInfo[] Secondary)> runesTask = LoadRunesSimpleAsync(participant);
            Task<RuneInfo[]> statRunesTask = LoadStatRunesAsync(participant);
            Task<Item[]> itemsTask = LoadItemsSimpleAsync(participant);
            await Task.WhenAll(championTask, spell1Task, spell2Task, runesTask, statRunesTask, itemsTask);
            var (champIcon, champName, _) = await championTask;
            var (spellImg1, spellName1, spellDesc1) = await spell1Task;
            var (spellImg2, spellName2, spellDesc2) = await spell2Task;
            RuneInfo[] primaryRunes;
            RuneInfo[] secondaryRunes;
            (primaryRunes, secondaryRunes) = await runesTask;
            return (champIcon: champIcon, champName: champName, spellImg1: spellImg1, spellImg2: spellImg2, spellName1: spellName1, spellDesc1: spellDesc1, spellName2: spellName2, spellDesc2: spellDesc2, primaryRunes: primaryRunes, secondaryRunes: secondaryRunes, statRunes: await statRunesTask, items: await itemsTask);
        }

        private async Task<(RuneInfo[] Primary, RuneInfo[] Secondary)> LoadRunesSimpleAsync(JObject participant)
        {
            JToken perks = participant["perks"];
            if (perks == null)
            {
                return (Primary: null, Secondary: null);
            }
            if (!(perks["styles"] is JArray styles))
            {
                return (Primary: null, Secondary: null);
            }
            JToken primaryStyle = styles.FirstOrDefault((JToken s) => s["description"]?.ToString() == "primaryStyle");
            return new ValueTuple<RuneInfo[], RuneInfo[]>(item2: await LoadRuneStyleAsync(styles.FirstOrDefault((JToken s) => s["description"]?.ToString() == "subStyle"), 2), item1: await LoadRuneStyleAsync(primaryStyle, 4));
        }

        private async Task<RuneInfo[]> LoadRuneStyleAsync(JToken style, int maxCount)
        {
            if (style == null)
            {
                return null;
            }
            if (!(style["selections"] is JArray selections))
            {
                return null;
            }
            IEnumerable<Task<RuneInfo>> runeTasks = selections.Cast<JObject>().Take(maxCount).Select(async delegate (JObject selection)
            {
                int runeId = selection["perk"]?.Value<int>() ?? 0;
                if (runeId <= 0)
                {
                    return (RuneInfo)null;
                }
                var (image, name, description) = await FormMain.Globals.resLoading.GetRuneInfoAsync(runeId);
                return new RuneInfo
                {
                    RuneId = runeId,
                    Icon = image,
                    Name = name,
                    Description = description
                };
            });
            return (await Task.WhenAll(runeTasks)).Where((RuneInfo r) => r != null).ToArray();
        }

        private async Task<Item[]> LoadItemsSimpleAsync(JObject participant)
        {
            IEnumerable<Task<Item>> itemTasks = Enumerable.Range(0, 6).Select(async delegate (int i)
            {
                int itemId = participant[$"item{i}"]?.Value<int>() ?? 0;
                if (itemId <= 0)
                {
                    return (Item)null;
                }
                var (image, name, desc) = await FormMain.Globals.resLoading.GetItemInfoAsync(itemId);
                return new Item
                {
                    Id = itemId,
                    Icon = image,
                    Name = name,
                    Description = desc
                };
            });
            return (await Task.WhenAll(itemTasks)).Where((Item item) => item != null).ToArray();
        }

        private async Task LoadTeamMembers(MatchInfo matchInfo, List<JObject> participants, string selfGameName, string selfTagLine)
        {
            List<PlayerInfo> blueTeam = new List<PlayerInfo>();
            List<PlayerInfo> redTeam = new List<PlayerInfo>();
            foreach (JObject participant in participants)
            {
                PlayerInfo player = await CreatePlayerInfo(participant, selfGameName, selfTagLine);
                if (player != null)
                {
                    if (player.TeamId == 100)
                    {
                        blueTeam.Add(player);
                    }
                    else if (player.TeamId == 200)
                    {
                        redTeam.Add(player);
                    }
                }
            }
            matchInfo.BlueTeam = blueTeam.OrderBy((PlayerInfo p) => p.FullName).ToList();
            matchInfo.RedTeam = redTeam.OrderBy((PlayerInfo p) => p.FullName).ToList();
        }

        private async Task<PlayerInfo> CreatePlayerInfo(JObject participant, string selfGameName, string selfTagLine)
        {
            try
            {
                string fullName = GetDisplayNameFromParticipant(participant);
                int champId = participant["championId"]?.Value<int>() ?? 0;
                int teamId = participant["teamId"]?.Value<int>() ?? 100;
                long summonerId = participant["summonerId"]?.Value<long>() ?? 0;
                (Image image, string name, string description) tuple = await FormMain.Globals.resLoading.GetChampionInfoAsync(champId);
                var (champIcon, _, _) = tuple;
                _ = tuple.name;
                bool isSelf = CheckIsSelf(participant, selfGameName, selfTagLine);
                return new PlayerInfo
                {
                    FullName = fullName,
                    Avatar = champIcon,
                    SummonerId = summonerId,
                    TeamId = teamId,
                    IsSelf = isSelf
                };
            }
            catch
            {
                return null;
            }
        }

        private string GetDisplayNameFromParticipant(JObject p)
        {
            if (p == null)
            {
                return "Unknown";
            }
            string riotName = p["riotIdGameName"]?.ToString();
            string summonerName = p["summonerName"]?.ToString();
            if (!string.IsNullOrWhiteSpace(riotName))
            {
                return riotName;
            }
            if (!string.IsNullOrWhiteSpace(summonerName))
            {
                return summonerName;
            }
            return "Unknown";
        }

        private bool CheckIsSelf(JObject p, string selfGameName, string selfTagLine)
        {
            string playerRiotName = p["riotIdGameName"]?.ToString();
            string playerSummoner = p["summonerName"]?.ToString();
            if (!string.IsNullOrEmpty(playerRiotName))
            {
                return string.Equals(playerRiotName, selfGameName, StringComparison.OrdinalIgnoreCase);
            }
            if (!string.IsNullOrEmpty(playerSummoner))
            {
                return string.Equals(playerSummoner, selfGameName, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private MatchInfo CreateMatchInfo(int index, long gameId, string currPuuid, string currSummonerId, bool isWin, int teamId, int duration, DateTime createTime, int kills, int deaths, int assists, string damageText, string goldText, string kpText, string damageTakenText, string mode, string queueId, Image champIcon, string champName, Image spellImg1, Image spellImg2, RuneInfo[] primaryRunes, RuneInfo[] secondaryRunes, RuneInfo[] statRunes, Item[] items, string selfName, JObject rawGameData, List<JObject> allParticipants, RuneInfo[] augments, Dictionary<string, RuneInfo[]> allPlayersAugments)
        {
            MatchInfo matchInfo = new MatchInfo();
            matchInfo.Index = index;
            matchInfo.GameId = gameId;
            matchInfo.CurrPuuid = currPuuid;
            matchInfo.CurrSummonerId = currSummonerId;
            matchInfo.ResultText = (isWin ? "胜利" : "失败");
            matchInfo.DurationText = FormatDuration(duration);
            matchInfo.Mode = mode;
            matchInfo.QueueId = queueId;
            matchInfo.GameTime = ((createTime > DateTime.MinValue) ? createTime.ToString("yyyy-MM-dd HH:mm") : "未知时间");
            matchInfo.HeroIcon = champIcon;
            matchInfo.ChampionName = champName;
            matchInfo.Kills = kills;
            matchInfo.Deaths = deaths;
            matchInfo.Assists = assists;
            matchInfo.DamageText = damageText;
            matchInfo.GoldText = goldText;
            matchInfo.KPPercentage = kpText;
            matchInfo.DamageTakenText = damageTakenText;
            matchInfo.SummonerSpells = new Image[2] { spellImg1, spellImg2 };
            matchInfo.Items = items ?? new Item[0];
            matchInfo.PrimaryRunes = primaryRunes ?? new RuneInfo[0];
            matchInfo.SecondaryRunes = secondaryRunes ?? new RuneInfo[0];
            matchInfo.Augments = augments ?? Array.Empty<RuneInfo>();  // 当前玩家的海克斯符文
            matchInfo.AllPlayersAugments = allPlayersAugments ?? new Dictionary<string, RuneInfo[]>(); // 所有玩家的海克斯符文
            matchInfo.StatRunes = statRunes ?? new RuneInfo[3];
            matchInfo.BlueTeam = new List<PlayerInfo>();
            matchInfo.RedTeam = new List<PlayerInfo>();
            matchInfo.SelfPlayer = new PlayerInfo
            {
                FullName = selfName,
                Avatar = champIcon,
                TeamId = teamId,
                IsSelf = true
            };
            matchInfo.AllParticipants = allParticipants;
            return matchInfo;
        }

        private string FormatDuration(int duration)
        {
            if (duration <= 0)
            {
                return "0分0秒";
            }
            int minutes = duration / 60;
            int seconds = duration % 60;
            return $"{minutes}分{seconds}秒";
        }

        private Panel CreateEmptyPanel()
        {
            return new Panel
            {
                Size = new Size(0, 0)
            };
        }
    }
}
