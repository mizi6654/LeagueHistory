using League.Models;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.Parsers
{
    /// <summary>
    /// 负责战绩数据的解析和转换
    /// </summary>
    public class MatchDataParser
    {
        public PlayerMatchInfo ParsePlayerMatchInfo(string puuid, JArray matches)
        {
            var result = new PlayerMatchInfo();
            if (matches == null || matches.Count == 0)
            {
                Debug.WriteLine("matches 数据为空");
                return result;
            }

            try
            {
                foreach (JObject match in matches.Cast<JObject>())
                {
                    JObject gameJson = match["json"] as JObject ?? match;
                    if (gameJson == null || gameJson["gameId"]?.Value<long>() == 0)
                        continue;

                    var participant = FindParticipant(gameJson, puuid);
                    if (participant == null) continue;

                    ProcessMatchData(result, gameJson, participant);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"解析比赛数据异常: {ex.Message}");
            }

            return result;
        }

        private void ProcessMatchData(PlayerMatchInfo result, JObject gameJson, JObject participant)
        {
            long gameId = gameJson["gameId"]?.Value<long>() ?? 0;
            int championId = participant["championId"]?.Value<int>() ?? 0;
            string championName = participant["championName"]?.ToString() ?? "";
            int kills = participant["kills"]?.Value<int>() ?? 0;
            int deaths = participant["deaths"]?.Value<int>() ?? 0;
            int assists = participant["assists"]?.Value<int>() ?? 0;
            bool isWin = participant["win"]?.Value<bool>() ?? false;

            string gameMode = ExtractGameMode(gameJson);   // ← 关键调用

            long gameStart = gameJson["gameStartTimestamp"]?.Value<long>() ??
                            gameJson["gameCreation"]?.Value<long>() ?? 0;
            string gameDate = gameStart > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(gameStart).ToString("yyyy-MM-dd")
                : "未知";

            result.RecentMatches.Add(new MatchStat { Kills = kills, Deaths = deaths, Assists = assists });
            result.WinHistory.Add(isWin);

            var item = new ListViewItem
            {
                ImageKey = championName.Replace(" ", "").Replace("'", ""),
                ForeColor = isWin ? Color.Green : Color.Red,
                Tag = new MatchMetadata { GameId = gameId, TeamId = participant["teamId"]?.Value<int>() ?? -1 }
            };

            item.SubItems.AddRange(new[] { gameMode, $"{kills}/{deaths}/{assists}", gameDate });
            result.MatchItems.Add(item);
            result.MatchKeys.Add($"{gameId}_{participant["teamId"]?.Value<int>() ?? -1}");

            CacheChampionIcon(championName, championId, result.HeroIcons);
        }

        private JObject FindParticipant(JObject gameJson, string puuid)
        {
            var participants = gameJson["participants"] as JArray;
            return participants?.Cast<JObject>()
                .FirstOrDefault(p => p["puuid"]?.ToString() == puuid);
        }

        private string ExtractGameMode(JObject gameJson)
        {
            if (gameJson == null) return "未知模式";

            // 1. 优先尝试 metadata.tags（原始逻辑）
            var tags = gameJson["metadata"]?["tags"] as JArray;
            if (tags != null && tags.Count > 0)
            {
                string mode = MapGameTags(tags);
                if (!mode.StartsWith("未知("))
                    return mode;
            }

            // 2. 尝试直接从顶层 queueId 获取（SUMMARY 接口常用）
            int queueId = gameJson["queueId"]?.Value<int>() ?? -1;
            if (queueId > 0)
            {
                string modeFromQueue = GetModeFromQueueId(queueId);
                if (!string.IsNullOrEmpty(modeFromQueue))
                    return modeFromQueue;
            }

            // 3. 尝试 gameMode 字段
            string gameModeStr = gameJson["gameMode"]?.ToString();
            if (!string.IsNullOrEmpty(gameModeStr))
            {
                return GameMod.GetModeName(queueId, gameModeStr);
            }

            // 4. 兜底
            return $"未知(queueId={queueId})";
        }

        private string GetModeFromQueueId(int queueId)
        {
            return queueId switch
            {
                420 => "单双排",
                440 => "灵活组排",
                400 or 430 => "匹配",
                450 => "大乱斗",
                480 or 890 => "快速模式（AI）",
                900 => "无限火力",
                1020 => "克隆大作战",
                1300 => "极限闪击",
                1400 => "终极魔典",
                1700 => "斗魂竞技场",
                2400 => "海克斯乱斗",           // ← 海克斯乱斗
                3270 => "自定义 · 海克斯乱斗",
                950 or 960 => "末日人机",
                1900 => "快速模式",
                2000 or 2010 or 2020 => "神木之门",
                700 or 720 or 740 or 750 => "云顶之弈",
                1090 => "云顶之弈(快速)",
                1100 => "云顶之弈(排位)",
                3100 => "未知(模式3100)",     // 你日志中出现的模式
                _ => ""
            };
        }

        private string MapGameTags(JArray tags)
        {
            if (tags == null || tags.Count == 0) return "未知模式";

            var tagList = tags.Select(t => t.ToString()).ToList();

            if (tagList.Contains("q_420")) return "单双排";
            if (tagList.Contains("q_440")) return "灵活组排";
            if (tagList.Contains("q_400") || tagList.Contains("q_430")) return "匹配";
            if (tagList.Contains("q_450")) return "大乱斗";
            if (tagList.Contains("q_480") || tagList.Contains("q_890")) return "快速模式（AI）";
            if (tagList.Contains("q_900")) return "无限火力";
            if (tagList.Contains("q_1020")) return "克隆大作战";
            if (tagList.Contains("q_1300")) return "极限闪击";
            if (tagList.Contains("q_1400")) return "终极魔典";
            if (tagList.Contains("q_1700")) return "斗魂竞技场";
            if (tagList.Contains("q_2400")) return "海克斯乱斗";           // 确保有
            if (tagList.Contains("q_3270")) return "自定义 · 海克斯乱斗";
            if (tagList.Contains("q_3100")) return "自定义 · 召唤师峡谷";
            if (tagList.Contains("q_950") || tagList.Contains("q_960")) return "末日人机";
            if (tagList.Contains("q_1900")) return "快速模式";
            if (tagList.Contains("q_2000") || tagList.Contains("q_2010") || tagList.Contains("q_2020")) return "神木之门";
            if (tagList.Contains("q_700") || tagList.Contains("q_720") || tagList.Contains("q_740") || tagList.Contains("q_750")) return "云顶之弈";
            if (tagList.Contains("q_1090")) return "云顶之弈(快速)";
            if (tagList.Contains("q_1100")) return "云顶之弈(排位)";

            // 自定义模式
            if (tagList.Contains("mode_practicetool")) return "训练模式";
            if (tagList.Contains("mode_classic")) return "自定义 · 召唤师峡谷";
            if (tagList.Contains("mode_aram")) return "自定义 · 极地大乱斗";
            if (tagList.Contains("mode_cherry") || tagList.Contains("mode_kiwi")) return "自定义 · 海克斯乱斗";
            if (tagList.Contains("type_CUSTOM_GAME")) return "自定义模式";

            return $"未知({string.Join(",", tagList)})";
        }

        private void CacheChampionIcon(string championName, int championId, ImageList heroIcons)
        {
            string cleanName = championName.Replace(" ", "").Replace("'", "");
            if (!MatchQueryProcessor._imageCache.TryGetValue(cleanName, out var image))
            {
                image = Globals.resLoading.GetChampionIconAsync(championId).GetAwaiter().GetResult();
                MatchQueryProcessor._imageCache.TryAdd(cleanName, image);
            }

            if (image != null && !heroIcons.Images.ContainsKey(cleanName))
            {
                heroIcons.Images.Add(cleanName, image);
            }
        }
    }
}