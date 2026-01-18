using League.Models;
using Newtonsoft.Json.Linq;
using System.Text;

namespace League.Services
{
    // 负责把队伍数据 → 格式化字符串（上等马、中等马那种）
    public class ChatMessageBuilder
    {
        private readonly Func<Dictionary<long, PlayerMatchInfo>> _getCachedPlayerInfos;

        public ChatMessageBuilder(Func<Dictionary<long, PlayerMatchInfo>> getCachedPlayerInfos)
        {
            _getCachedPlayerInfos = getCachedPlayerInfos ?? throw new ArgumentNullException(nameof(getCachedPlayerInfos));
        }

        public string BuildMyTeamSummary(JArray? team, int maxGames = 20)
        {
            if (team == null || team.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            var infos = _getCachedPlayerInfos();

            foreach (var p in team)
            {
                long sid = p["summonerId"]?.Value<long>() ?? 0;
                if (sid == 0) continue;

                if (!infos.TryGetValue(sid, out var info)) continue;

                string name = info.Player?.GameName ?? "未知";
                string line = BuildAkariStyleLine(info, name, maxGames);
                sb.AppendLine(line);
            }

            return sb.ToString().Trim();
        }

        public string BuildFullTeamSummary(JArray? myTeam, JArray? enemyTeam, int maxGames = 20)
        {
            var sb = new StringBuilder();

            string my = BuildMyTeamSummary(myTeam, maxGames);
            if (!string.IsNullOrWhiteSpace(my))
            {
                sb.AppendLine(my);
            }

            string enemy = BuildMyTeamSummary(enemyTeam, maxGames);
            if (!string.IsNullOrWhiteSpace(enemy))
            {
                if (sb.Length > 0) sb.AppendLine("");
                sb.AppendLine(enemy);
            }

            return sb.ToString().Trim();
        }

        private string BuildAkariStyleLine(PlayerMatchInfo info, string playerName, int maxGames)
        {
            double winRate = CalculateWinRate(info, maxGames);
            string avgKDA = GetAverageKDA(info, maxGames);
            double score = Math.Round(winRate * 2 + (info.RecentMatches?.Count ?? 0) * 0.5, 1);

            string eval = winRate switch
            {
                >= 60 => "上等马",
                >= 50 => "中等马",
                >= 40 => "下等马",
                _ => "摆烂马"
            };

            return $"{eval} {playerName} 评分: {score}，近{maxGames}场KDA {avgKDA} 胜率 {winRate:F0}%";
        }

        private double CalculateWinRate(PlayerMatchInfo info, int maxGames = 20)
        {
            if (info.WinHistory == null || info.WinHistory.Count == 0) return 0.0;

            var recentWins = info.WinHistory.Take(maxGames).ToList();
            int wins = recentWins.Count(w => w);
            int total = recentWins.Count;

            return total > 0 ? (wins * 100.0 / total) : 0.0;
        }

        // 计算近 N 场平均 KDA（返回 "平均值" 字符串，如 "3.3"）
        private string GetAverageKDA(PlayerMatchInfo info, int maxGames = 20)
        {
            if (info.RecentMatches == null || info.RecentMatches.Count == 0) return "0.0";

            var recent = info.RecentMatches.Take(maxGames).ToList();
            if (recent.Count == 0) return "0.0";

            double totalKills = recent.Sum(m => m.Kills);
            double totalDeaths = recent.Sum(m => m.Deaths);
            double totalAssists = recent.Sum(m => m.Assists);

            double avgKDA = totalDeaths > 0 ? (totalKills + totalAssists) / totalDeaths : (totalKills + totalAssists);

            return avgKDA.ToString("F1"); // 保留一位小数，如 3.3
        }
    }
}