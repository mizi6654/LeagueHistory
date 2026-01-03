using System.Diagnostics;
using System.Text.RegularExpressions;
using League.Models;
using League.UIHelpers;
using Newtonsoft.Json.Linq;

namespace League.UIState
{
    public class DetailedViewLayoutManager : IDisposable
    {
        private static readonly Dictionary<int, (Image, string, string)> _championCache = new Dictionary<int, (Image, string, string)>();

        private static readonly Dictionary<int, (Image, string, string)> _spellCache = new Dictionary<int, (Image, string, string)>();

        private static readonly Dictionary<int, (Image, string, string)> _itemCache = new Dictionary<int, (Image, string, string)>();

        private static readonly Dictionary<int, (Image, string, string)> _runeCache = new Dictionary<int, (Image, string, string)>();

        private const int PANEL_W = 1200;

        private const int ITEM_H = 25;

        private const int ROW2_H = 30;

        private ToolTip _toolTip;

        public DetailedViewLayoutManager(ToolTip toolTip)
        {
            _toolTip = toolTip;
        }

        public void Dispose()
        {
            _toolTip?.Dispose();
        }

        public async Task<Panel> CreateDetailedPlayerPanel(JObject participant, MatchInfo matchInfo, int x, int y, int teamId)
        {
            (string name, bool isSelf) tuple = await GetPlayerInfoAsync(participant, matchInfo);
            string playerName = tuple.name;
            bool isSelf = tuple.isSelf;
            Color backColor = GetPanelBackColor(teamId, isSelf);
            Panel panel = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(1200, 80),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = backColor
            };
            await PreloadAllAsync(participant, matchInfo); // 修改：传入matchInfo
            (int damage, int damageRank, int gold, int goldRank, double kp, int kpRank, int damageTaken, int damageTakenRank) ranks = CalculateRanks(participant, matchInfo);
            panel.SuspendLayout();
            CreateAllUI(panel, participant, playerName, isSelf, ranks, matchInfo); // 修改：传入matchInfo
            panel.ResumeLayout(performLayout: false);
            return panel;
        }

        private async Task<(string name, bool isSelf)> GetPlayerInfoAsync(JObject participant, MatchInfo matchInfo)
        {
            string summonerId = participant["summonerId"]?.ToString();
            string riotName = participant["riotIdGameName"]?.ToString();
            string ritoTag = participant["riotIdTagline"]?.ToString();
            if (!string.IsNullOrEmpty(riotName) && !string.IsNullOrEmpty(ritoTag))
            {
                return new ValueTuple<string, bool>(item2: riotName == matchInfo.SelfPlayer?.FullName, item1: riotName + "#" + ritoTag);
            }
            if (!string.IsNullOrEmpty(summonerId))
            {
                JObject summonerData = await FormMain.Globals.lcuClient.GetGameNameBySummonerId(summonerId);
                if (summonerData != null)
                {
                    return new ValueTuple<string, bool>(item2: summonerData["gameName"].ToString() == matchInfo.SelfPlayer?.FullName, item1: $"{summonerData["gameName"]}#{summonerData["tagLine"]}");
                }
            }
            return (name: "未知玩家", isSelf: false);
        }

        private Color GetPanelBackColor(int teamId, bool isSelf)
        {
            return isSelf ? Color.LightYellow : teamId switch
            {
                200 => Color.FromArgb(245, 240, 245),
                100 => Color.FromArgb(200, 240, 245),
                _ => Color.FloralWhite,
            };
        }

        private void CreateAllUI(Panel panel, JObject participant, string playerName, bool isSelf, (int damage, int damageRank, int gold, int goldRank, double kp, int kpRank, int damageTaken, int damageTakenRank) ranks, MatchInfo matchInfo)
        {
            CreateHeroImage(panel, participant);
            CreateFirstRowStats(panel, participant, playerName, isSelf, ranks);
            CreateSecondRowItems(panel, participant, matchInfo); // 修改：传入matchInfo
        }

        private void CreateFirstRowStats(Panel panel, JObject participant, string playerName, bool isSelf, (int damage, int damageRank, int gold, int goldRank, double kp, int kpRank, int damageTaken, int damageTakenRank) ranks)
        {
            // 调整：整体上移，给顶部留一点空间
            int baseY = 8;

            Label nameLabel = CreateLabel(playerName, 80, baseY, 200, isSelf ? FontStyle.Bold : FontStyle.Regular, isSelf ? Color.Blue : Color.Black, 10f);
            panel.Controls.Add(nameLabel);
            panel.Controls.Add(CreateSeparator(290, baseY));
            Label kdaLabel = CreateLabel($"数据: {participant["kills"]}/{participant["deaths"]}/{participant["assists"]}", 305, baseY, 130, FontStyle.Bold, Color.DeepSkyBlue, 10f);
            panel.Controls.Add(kdaLabel);
            panel.Controls.Add(CreateSeparator(445, baseY));
            Label damageLabel = CreateLabel($"伤害: {ranks.damage} (#{ranks.damageRank})", 460, baseY, 140, FontStyle.Regular, Color.OrangeRed, 10f);
            panel.Controls.Add(damageLabel);
            panel.Controls.Add(CreateSeparator(590, baseY));
            Label goldLabel = CreateLabel($"经济: {ranks.gold} (#{ranks.goldRank})", 615, baseY, 130, FontStyle.Regular, Color.Goldenrod, 10f);
            panel.Controls.Add(goldLabel);
            panel.Controls.Add(CreateSeparator(735, baseY));
            Label csLabel = CreateLabel($"补刀: {participant["totalMinionsKilled"]}", 760, baseY, 90, FontStyle.Regular, Color.DarkGreen, 10f);
            panel.Controls.Add(csLabel);
            panel.Controls.Add(CreateSeparator(850, baseY));
            Label kpLabel = CreateLabel($"参团: {ranks.kp * 100.0:F1}% (#{ranks.kpRank})", 875, baseY, 130, FontStyle.Regular, Color.DodgerBlue, 10f);
            panel.Controls.Add(kpLabel);
            // 注意：保留你原来的字段用法（我没有改逻辑）
            Label damageTakenLabel = CreateLabel($"承伤: {ranks.damageTaken} (#{ranks.damageTakenRank})", 1010, baseY, 130, FontStyle.Regular, Color.MediumPurple, 10f);
            panel.Controls.Add(damageTakenLabel);
        }

        private void CreateHeroImage(Panel panel, JObject participant)
        {
            int championId = participant["championId"]?.Value<int>() ?? 0;
            if (championId > 0)
            {
                if (_championCache.TryGetValue(championId, out (Image, string, string) info) && info.Item1 != null)
                {
                    PictureBox heroPic = new PictureBox
                    {
                        // 修改：将头像垂直居中（在高度80时，y=15使50px头像位于垂直中央）
                        Location = new Point(15, 15),
                        Size = new Size(50, 50),
                        Image = ImageProcessor.ResizeImage(info.Item1, 50, 50),
                        SizeMode = PictureBoxSizeMode.StretchImage,
                        Tag = "英雄: " + info.Item2 + "\n\n" + StripHtmlTags(info.Item3)
                    };
                    _toolTip.SetToolTip(heroPic, heroPic.Tag?.ToString());
                    panel.Controls.Add(heroPic);
                }
                else
                {
                    CreatePlaceholderImage(panel, 15, 15, 50, 50, "英雄");
                }
            }
        }

        private void CreateSecondRowItems(Panel panel, JObject participant, MatchInfo matchInfo)
        {
            // 修改：将第二行整体上移 4px（45 -> 41），并统一图片基线
            int baseY = 41;
            Label spellsLabel = CreateLabel("召唤师技能:", 80, baseY, 80, FontStyle.Bold, Color.Black, 9f);
            Label itemsLabel = CreateLabel("装备:", 250, baseY, 40, FontStyle.Bold, Color.Black, 9f);
            Label runesLabel = CreateLabel("符文:", 580, baseY, 40, FontStyle.Bold, Color.Black, 9f);
            panel.Controls.AddRange(new Control[3] { spellsLabel, itemsLabel, runesLabel });
            panel.Controls.Add(CreateSeparator(235, baseY));
            panel.Controls.Add(CreateSeparator(505, baseY));
            CreateSpellImages(panel, participant, 165, baseY);
            CreateItemImages(panel, participant, 295, baseY);
            //CreateRuneImages(panel, participant, matchInfo); // 修改：传入matchInfo
            CreateRuneImages(panel, participant, 630, baseY, matchInfo); // 修改：传入matchInfo
        }

        private void CreateSpellImages(Panel panel, JObject participant, int startX, int y)
        {
            for (int i = 1; i <= 2; i++)
            {
                int spellId = participant[$"spell{i}Id"]?.Value<int>() ?? 0;
                int x = startX + (i - 1) * 35;
                if (spellId > 0 && _spellCache.TryGetValue(spellId, out (Image, string, string) info) && info.Item1 != null)
                {
                    PictureBox pic = CreatePictureBox(info, x, y, 30, 30, "召唤师技能: " + info.Item2);
                    panel.Controls.Add(pic);
                }
                else
                {
                    CreateEmptySlot(panel, x, y, 30, 30);
                }
            }
        }

        private void CreateItemImages(Panel panel, JObject participant, int startX, int y)
        {
            for (int i = 0; i < 7; i++)
            {
                int itemId = participant[$"item{i}"]?.Value<int>() ?? 0;
                int x = startX + i * 35;
                if (itemId > 0 && _itemCache.TryGetValue(itemId, out (Image, string, string) info) && info.Item1 != null)
                {
                    string itemType = i == 6 ? "饰品" : "装备";
                    PictureBox pic = CreatePictureBox(info, x, y, 30, 30, itemType + ": " + info.Item2);
                    panel.Controls.Add(pic);
                }
                else
                {
                    CreateEmptySlot(panel, x, y, 30, 30);
                }
            }
        }

        private void CreateRuneImages(Panel panel, JObject participant, int startX, int y, MatchInfo matchInfo)
        {
            // 判断是否为海克斯大乱斗模式
            bool isHextechArena = IsHextechArenaMode(participant);

            if (isHextechArena)
            {
                // 显示海克斯符文 - 直接从MatchInfo中获取
                CreateAugmentImages(panel, participant, matchInfo);
            }
            else
            {
                // 显示普通符文（原有逻辑）
                CreateNormalRuneImages(panel, participant, startX, y);
            }
        }

        // 修改：更准确的模式判断
        private bool IsHextechArenaMode(JObject participant)
        {
            try
            {
                // 方法1：通过队列ID判断（从matchInfo获取更准确）
                // 这里需要从matchInfo获取队列信息，或者从participant的父级获取

                // 方法2：通过游戏模式字段判断
                string gameMode = participant["gameMode"]?.ToString() ?? "";
                if (gameMode.Contains("cherry", StringComparison.OrdinalIgnoreCase) ||
                    gameMode.Contains("hextech", StringComparison.OrdinalIgnoreCase))
                    return true;

                // 方法3：如果有海克斯符文数据，也认为是海克斯大乱斗
                int[] augments = ExtractAugments(participant);
                return augments.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        // 修改：显示海克斯符文，直接从MatchInfo获取
        private void CreateAugmentImages(Panel panel, JObject participant, MatchInfo matchInfo)
        {
            int startX = 630; // 符文起始位置
            int y = 41; // 符文Y坐标
            int x = startX;

            // 直接从MatchInfo中获取当前玩家的海克斯符文
            var selfAugments = GetSelfAugments(participant, matchInfo);

            foreach (var augment in selfAugments)
            {
                if (augment?.Icon != null)
                {
                    // 创建一个带黑色背景的PictureBox
                    PictureBox pic = new PictureBox
                    {
                        Location = new Point(x, y),
                        Size = new Size(30, 30),
                        BackColor = Color.Black, // 设置黑色背景
                        SizeMode = PictureBoxSizeMode.StretchImage,
                        Tag = (augment.name ?? "未知符文") + "\n\n" + StripHtmlTags(augment.longDesc ?? "")
                    };

                    // 设置图片（透明PNG在黑色背景上会显示得更清晰）
                    pic.Image = ImageProcessor.ResizeImage(augment.Icon, 30, 30);

                    _toolTip.SetToolTip(pic, pic.Tag?.ToString());
                    panel.Controls.Add(pic);
                }
                else
                {
                    // 占位符也设置黑色背景保持一致性
                    CreateAugmentEmptySlot(panel, x, y, 30, 30);
                }
                x += 37;
            }
        }

        // 新增：创建带黑色背景的占位符
        private void CreateAugmentEmptySlot(Panel panel, int x, int y, int width, int height)
        {
            Panel placeholder = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = Color.Black, // 黑色背景
                BorderStyle = BorderStyle.FixedSingle,
            };
            panel.Controls.Add(placeholder);
        }

        // 修改：获取当前玩家的海克斯符文
        private RuneInfo[] GetSelfAugments(JObject participant, MatchInfo matchInfo)
        {
            string participantPuuid = participant["puuid"]?.ToString();
            if (!string.IsNullOrEmpty(participantPuuid) && matchInfo.AllPlayersAugments != null)
            {
                if (matchInfo.AllPlayersAugments.TryGetValue(participantPuuid, out var augments))
                {
                    return augments ?? Array.Empty<RuneInfo>();
                }
            }

            // 备用方案：如果AllPlayersAugments中没有，尝试从participant数据中提取
            int[] augmentIds = ExtractAugments(participant);
            return augmentIds.Select(id => matchInfo.Augments?.FirstOrDefault(a => a.id == id))
                            .Where(a => a != null)
                            .ToArray();
        }

        // 修改后的普通符文显示（重命名原有方法）
        private void CreateNormalRuneImages(Panel panel, JObject participant, int startX, int y)
        {
            (RuneInfo[], RuneInfo[], RuneInfo[]) runesData = LoadRunesForParticipant(participant);
            int x = startX;

            if (runesData.Item1 != null)
            {
                foreach (RuneInfo rune in runesData.Item1.Where((r) => r != null))
                {
                    if (_runeCache.TryGetValue(rune.RuneId, out (Image, string, string) info) && info.Item1 != null)
                    {
                        PictureBox pic = CreatePictureBox(info, x, y, 30, 30, "符文: " + info.Item2);
                        panel.Controls.Add(pic);
                    }
                    x += 37;
                }
            }

            x += 10;
            if (runesData.Item2 != null)
            {
                foreach (RuneInfo rune2 in runesData.Item2.Where((r) => r != null))
                {
                    if (_runeCache.TryGetValue(rune2.RuneId, out (Image, string, string) info2) && info2.Item1 != null)
                    {
                        PictureBox pic2 = CreatePictureBox(info2, x, y, 30, 30, "符文: " + info2.Item2);
                        panel.Controls.Add(pic2);
                    }
                    x += 37;
                }
            }

            x += 5;
            if (runesData.Item3 == null)
            {
                return;
            }

            string[] shardTypes = new string[3] { "攻击", "灵活", "防御" };
            for (int i = 0; i < Math.Min(3, runesData.Item3.Length); i++)
            {
                RuneInfo shard = runesData.Item3[i];
                if (shard != null && _rune_cache_tryget(shard.RuneId, out var info3) && info3.Item1 != null)
                {
                    PictureBox pic3 = CreatePictureBox(info3, x, y, 30, 30, shardTypes[i] + "碎片: " + info3.Item2);
                    panel.Controls.Add(pic3);
                }
                x += 37;
            }
        }

        // 提取海克斯符文ID
        private int[] ExtractAugments(JObject participant)
        {
            if (participant == null)
                return Array.Empty<int>();

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

        // 为了保持你原始代码风格，我把对 _runeCache.TryGetValue 的调用用一个内联小方法处理以避免重复写法错误
        private bool _rune_cache_tryget(int runeId, out (Image, string, string) info)
        {
            return _runeCache.TryGetValue(runeId, out info);
        }

        private PictureBox CreatePictureBox((Image, string, string) info, int x, int y, int width, int height, string tooltipPrefix)
        {
            PictureBox pic = new PictureBox
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                Image = ImageProcessor.ResizeImage(info.Item1, width, height),
                SizeMode = PictureBoxSizeMode.StretchImage,
                Tag = tooltipPrefix + "\n\n" + StripHtmlTags(info.Item3)
            };
            _toolTip.SetToolTip(pic, pic.Tag?.ToString());
            return pic;
        }

        private Label CreateLabel(string text, int x, int y, int width, FontStyle style, Color color, float fontSize)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 25),
                Font = new Font("微软雅黑", fontSize, style),
                ForeColor = color,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private Panel CreateSeparator(int x, int y)
        {
            return new Panel
            {
                Location = new Point(x, y),
                Size = new Size(1, 21),
                BackColor = Color.LightGray
            };
        }

        private void CreateEmptySlot(Panel panel, int x, int y, int width, int height)
        {
            Panel placeholder = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = Color.Transparent,
                BorderStyle = BorderStyle.FixedSingle
            };
            panel.Controls.Add(placeholder);
        }

        private void CreatePlaceholderImage(Panel panel, int x, int y, int width, int height, string text)
        {
            Panel placeholder = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = Color.Yellow,
                BorderStyle = BorderStyle.FixedSingle
            };
            Label label = new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("微软雅黑", 7f, FontStyle.Bold),
                ForeColor = Color.Red
            };
            placeholder.Controls.Add(label);
            panel.Controls.Add(placeholder);
        }


        // 修改：简化预加载，海克斯符文不需要重新加载
        private async Task PreloadAllAsync(JObject participant, MatchInfo matchInfo)
        {
            List<Task> tasks = new List<Task>();
            int championId = participant["championId"]?.Value<int>() ?? 0;
            if (championId > 0 && !_championCache.ContainsKey(championId))
            {
                tasks.Add(PreloadResourceAsync(championId, _championCache, FormMain.Globals.resLoading.GetChampionInfoAsync));
            }

            for (int i = 1; i <= 2; i++)
            {
                int spellId = participant[$"spell{i}Id"]?.Value<int>() ?? 0;
                if (spellId > 0 && !_spellCache.ContainsKey(spellId))
                {
                    tasks.Add(PreloadResourceAsync(spellId, _spellCache, FormMain.Globals.resLoading.GetSpellInfoAsync));
                }
            }

            for (int j = 0; j < 7; j++)
            {
                int itemId = participant[$"item{j}"]?.Value<int>() ?? 0;
                if (itemId > 0 && !_itemCache.ContainsKey(itemId))
                {
                    tasks.Add(PreloadResourceAsync(itemId, _itemCache, FormMain.Globals.resLoading.GetItemInfoAsync));
                }
            }

            // 判断是否为海克斯大乱斗模式
            bool isHextechArena = IsHextechArenaMode(participant);

            if (!isHextechArena)
            {
                // 只有普通模式才需要预加载符文
                (RuneInfo[] Primary, RuneInfo[] Secondary, RuneInfo[] Shards) runesData = LoadRunesForParticipant(participant);
                PreloadRuneArray(runesData.Primary, tasks);
                PreloadRuneArray(runesData.Secondary, tasks);
                PreloadRuneArray(runesData.Shards, tasks);
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }

        private void PreloadRuneArray(RuneInfo[] runes, List<Task> tasks)
        {
            if (runes == null)
            {
                return;
            }
            foreach (RuneInfo rune in runes.Where((r) => r != null && !_runeCache.ContainsKey(r.RuneId)))
            {
                tasks.Add(PreloadResourceAsync(rune.RuneId, _runeCache, FormMain.Globals.resLoading.GetRuneInfoAsync));
            }
        }

        private async Task PreloadResourceAsync(int resourceId, Dictionary<int, (Image, string, string)> cache, Func<int, Task<(Image, string, string)>> loader)
        {
            try
            {
                (Image, string, string) resource = await loader(resourceId);
                if (resource.Item1 != null)
                {
                    lock (cache)
                    {
                        cache[resourceId] = resource;
                    }
                }
            }
            catch (Exception ex)
            {
                Exception ex2 = ex;
                Debug.WriteLine($"预加载资源 {resourceId} 失败: {ex2.Message}");
            }
        }

        private (int damage, int damageRank, int gold, int goldRank, double kp, int kpRank, int damageTaken, int damageTakenRank) CalculateRanks(JObject participant, MatchInfo matchInfo)
        {
            List<JObject> allParticipants = matchInfo.AllParticipants;
            int currentDamage = participant["totalDamageDealtToChampions"]?.Value<int>() ?? 0;
            int currentGold = participant["goldEarned"]?.Value<int>() ?? 0;
            double currentKP = (participant["challenges"]?["killParticipation"]?.Value<double>()).GetValueOrDefault();
            int currentDamageTaken = participant["totalDamageTaken"]?.Value<int>() ?? 0;
            return (damage: currentDamage, damageRank: GetRank(allParticipants.Select((p) => p["totalDamageDealtToChampions"]?.Value<int>() ?? 0).ToList(), currentDamage), gold: currentGold, goldRank: GetRank(allParticipants.Select((p) => p["goldEarned"]?.Value<int>() ?? 0).ToList(), currentGold), kp: currentKP, kpRank: GetRank(allParticipants.Select((p) => (p["challenges"]?["killParticipation"]?.Value<double>()).GetValueOrDefault()).ToList(), currentKP), damageTaken: currentDamageTaken, damageTakenRank: GetRank(allParticipants.Select((p) => p["totalDamageTaken"]?.Value<int>() ?? 0).ToList(), currentDamageTaken));
        }

        private int GetRank<T>(List<T> allValues, T currentValue) where T : IComparable
        {
            List<T> sorted = allValues.OrderByDescending((v) => v).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (currentValue.CompareTo(sorted[i]) == 0)
                {
                    return i + 1;
                }
            }
            return sorted.Count;
        }

        private (RuneInfo[] Primary, RuneInfo[] Secondary, RuneInfo[] Shards) LoadRunesForParticipant(JObject participant)
        {
            JToken perks = participant["perks"];
            if (!(perks?["styles"] is JArray styles))
            {
                return (Primary: null, Secondary: null, Shards: null);
            }
            RuneInfo[] primaryRunes = (from r in styles.FirstOrDefault((s) => s["description"]?.ToString() == "primaryStyle")?["selections"]?.Cast<JObject>().Take(4).Select(delegate (JObject selection)
            {
                int num = selection["perk"]?.Value<int>() ?? 0;
                return num > 0 ? new RuneInfo
                {
                    RuneId = num
                } : null;
            })
                                       where r != null
                                       select r).ToArray();
            RuneInfo[] secondaryRunes = (from r in styles.FirstOrDefault((s) => s["description"]?.ToString() == "subStyle")?["selections"]?.Cast<JObject>().Take(2).Select(delegate (JObject selection)
            {
                int num = selection["perk"]?.Value<int>() ?? 0;
                return num > 0 ? new RuneInfo
                {
                    RuneId = num
                } : null;
            })
                                         where r != null
                                         select r).ToArray();
            JToken statPerks = perks["statPerks"];
            RuneInfo[] shards = null;
            if (statPerks != null)
            {
                shards = new RuneInfo[3]
                {
                    CreateShard(statPerks["offense"]?.Value<int>() ?? 0),
                    CreateShard(statPerks["flex"]?.Value<int>() ?? 0),
                    CreateShard(statPerks["defense"]?.Value<int>() ?? 0)
                }.Where((r) => r != null).ToArray();
            }
            return (Primary: primaryRunes, Secondary: secondaryRunes, Shards: shards);
        }

        private RuneInfo CreateShard(int shardId)
        {
            return shardId > 0 ? new RuneInfo
            {
                RuneId = shardId
            } : null;
        }

        private string StripHtmlTags(string input)
        {
            return string.IsNullOrEmpty(input) ? string.Empty : Regex.Replace(input.Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n"), "<.*?>", "").Trim();
        }
    }
}
