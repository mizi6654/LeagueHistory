using League.Models;
using Newtonsoft.Json.Linq;
using System.Text;

namespace League.Services
{
    public class ChatMessageBuilder
    {
        private readonly Func<Dictionary<long, PlayerMatchInfo>> _getCachedPlayerInfos;

        public ChatMessageBuilder(Func<Dictionary<long, PlayerMatchInfo>> getCachedPlayerInfos)
        {
            _getCachedPlayerInfos = getCachedPlayerInfos ?? throw new ArgumentNullException(nameof(getCachedPlayerInfos));
        }

        /// <summary>
        /// 我方队伍（极简版）
        /// </summary>
        public string BuildMyTeamSummary(JArray? team, int maxGames = 20)
        {
            if (team == null || team.Count == 0)
                return "暂无数据";

            var sb = new StringBuilder();
            var infos = _getCachedPlayerInfos();

            foreach (var p in team)
            {
                long sid = p["summonerId"]?.Value<long>() ?? 0;
                if (sid == 0) continue;

                if (!infos.TryGetValue(sid, out var info) || info?.Player == null)
                    continue;

                string name = info.Player.GameName ?? "未知";
                string line = BuildSimpleLine(info, name, maxGames);
                sb.AppendLine(line);
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 全队（我方 + 敌方）
        /// </summary>
        public string BuildFullTeamSummary(JArray? myTeam, JArray? enemyTeam, int maxGames = 20)
        {
            var sb = new StringBuilder();

            string my = BuildMyTeamSummary(myTeam, maxGames);
            if (!string.IsNullOrWhiteSpace(my))
            {
                sb.AppendLine(my);
                sb.AppendLine();
            }

            if (enemyTeam?.Count > 0 == true)
            {
                var infos = _getCachedPlayerInfos();

                foreach (var p in enemyTeam)
                {
                    long sid = p["summonerId"]?.Value<long>() ?? 0;
                    if (sid == 0) continue;

                    if (!infos.TryGetValue(sid, out var info) || info?.Player == null)
                        continue;

                    string name = info.Player.GameName ?? "未知";
                    string line = BuildSimpleLine(info, name, maxGames);
                    sb.AppendLine(line);
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 最简洁格式（当前推荐，降低封禁风险）
        /// </summary>
        private string BuildSimpleLine(PlayerMatchInfo info, string playerName, int maxGames)
        {
            double winRate = CalculateWinRate(info, maxGames);
            string avgKDA = GetAverageKDA(info, maxGames);

            // 极简格式：名字 + KDA + 胜率
            return $"{playerName}  近20场KDA:{avgKDA}  胜率:{winRate:F0}%";
        }

        private double CalculateWinRate(PlayerMatchInfo info, int maxGames = 20)
        {
            if (info.WinHistory == null || info.WinHistory.Count == 0)
                return 0.0;

            var recent = info.WinHistory.Take(maxGames).ToList();
            int wins = recent.Count(w => w);
            return recent.Count > 0 ? (wins * 100.0 / recent.Count) : 0.0;
        }

        private string GetAverageKDA(PlayerMatchInfo info, int maxGames = 20)
        {
            if (info.RecentMatches == null || info.RecentMatches.Count == 0)
                return "0.0";

            var recent = info.RecentMatches.Take(maxGames).ToList();
            if (recent.Count == 0) return "0.0";

            double totalK = recent.Sum(m => m.Kills);
            double totalD = recent.Sum(m => m.Deaths);
            double totalA = recent.Sum(m => m.Assists);

            double avg = totalD > 0 ? (totalK + totalA) / totalD : (totalK + totalA);
            return avg.ToString("F1");
        }
    }
}