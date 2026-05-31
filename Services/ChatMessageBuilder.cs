using League.Models;
using Newtonsoft.Json.Linq;
using System.Text;

namespace League.Services
{
    public class ChatMessageBuilder
    {
        private readonly Func<Dictionary<long, PlayerMatchInfo>> _getCachedPlayerInfos;
        private string _myPuuid = string.Empty;

        public ChatMessageBuilder(Func<Dictionary<long, PlayerMatchInfo>> getCachedPlayerInfos)
        {
            _getCachedPlayerInfos = getCachedPlayerInfos ?? throw new ArgumentNullException(nameof(getCachedPlayerInfos));
        }

        public void SetMyPuuid(string puuid)
        {
            _myPuuid = puuid?.Trim() ?? "";
        }

        private bool IsMySelf(string? puuid)
        {
            if (string.IsNullOrEmpty(puuid) || string.IsNullOrEmpty(_myPuuid))
                return false;
            return string.Equals(puuid, _myPuuid, StringComparison.OrdinalIgnoreCase);
        }

        // ==================== 简略版（F7 和 F9 使用） ====================
        public string BuildMyTeamSummary(JArray? team, int maxGames = 20)
        {
            return BuildTeamSummary(team, maxGames, detailed: false);
        }

        public string BuildFullTeamSummary(JArray? myTeam, JArray? enemyTeam, int maxGames = 20)
        {
            var sb = new StringBuilder();
            string my = BuildTeamSummary(myTeam, maxGames, detailed: false);
            if (!string.IsNullOrWhiteSpace(my))
            {
                sb.AppendLine(my);
                sb.AppendLine();
            }

            if (enemyTeam?.Count > 0 == true)
            {
                string enemy = BuildTeamSummary(enemyTeam, maxGames, detailed: true); // 全队用详细版
                if (!string.IsNullOrWhiteSpace(enemy))
                    sb.AppendLine(enemy);
            }
            return sb.ToString().TrimEnd();
        }

        // 内部统一方法
        private string BuildTeamSummary(JArray? team, int maxGames, bool detailed)
        {
            if (team == null || team.Count == 0)
                return "暂无数据";

            var sb = new StringBuilder();
            var infos = _getCachedPlayerInfos();

            foreach (var p in team)
            {
                string? puuid = p["puuid"]?.ToString();
                if (string.IsNullOrEmpty(puuid) || IsMySelf(puuid))
                    continue;

                long sid = p["summonerId"]?.Value<long>() ?? 0;
                if (sid == 0) continue;

                if (!infos.TryGetValue(sid, out var info) || info?.Player == null)
                    continue;

                string name = info.Player.GameName ?? "未知";
                string line = detailed
                    ? BuildDetailedLine(info, name, maxGames)
                    : BuildSimpleLine(info, name, maxGames);

                sb.AppendLine(line);
            }
            return sb.ToString().TrimEnd();
        }

        // ==================== 简略版（F7 和 F9 使用） ====================
        private string BuildSimpleLine(PlayerMatchInfo info, string playerName, int maxGames = 20)
        {
            double winRate = CalculateWinRate(info, maxGames);
            string behavior = info.Behavior?.BehaviorSummary ?? "";

            if (string.IsNullOrEmpty(behavior))
            {
                return $"{playerName} 近20场 胜率 [{winRate:F0}%]";
            }
            else
            {
                return $"{playerName} 近20场 胜率 [{winRate:F0}%] {behavior}";
            }
        }

        // ==================== 详细版（F11 使用） ====================
        private string BuildDetailedLine(PlayerMatchInfo info, string playerName, int maxGames = 20)
        {
            double winRate = CalculateWinRate(info, maxGames);
            string behavior = info.Behavior?.BehaviorSummary ?? "";
            var b = info.Behavior;

            if (b == null)
                return $"{playerName} 近20场 胜率 [{winRate:F0}%]";

            // 核心逻辑：只有存在特征标签时，才显示信号详情
            if (string.IsNullOrEmpty(behavior))
            {
                // 无特征标签：只显示胜率
                return $"{playerName} 近20场 胜率 [{winRate:F0}%]";
            }
            else
            {
                // 有特征标签：显示胜率 + 信号详情 + 特征标签
                var details = new List<string>();

                // 直接显示平均值（不在这里做阈值判断，因为已经在 GenerateBehaviorSummary 中判断过了）
                if (b.AvgMissingPings > 0)
                    details.Add($"发问号 {b.AvgMissingPings:F1}");
                if (b.AvgVisionPings > 0)
                    details.Add($"发提醒 {b.AvgVisionPings:F1}");
                if (b.AvgGetBackPings > 0)
                    details.Add($"发撤退 {b.AvgGetBackPings:F1}");
                if (b.AvgDangerPings > 0)
                    details.Add($"发危险 {b.AvgDangerPings:F1}");

                string detailStr = details.Count > 0 ? $" | {string.Join(" | ", details)}" : "";

                return $"{playerName} 近20场 胜率 [{winRate:F0}%]{detailStr} 特征 {behavior}";
            }
        }

        private double CalculateWinRate(PlayerMatchInfo info, int maxGames = 20)
        {
            if (info.WinHistory == null || info.WinHistory.Count == 0)
                return 0.0;

            var recent = info.WinHistory.Take(maxGames).ToList();
            int wins = recent.Count(w => w);
            return recent.Count > 0 ? (wins * 100.0 / recent.Count) : 0.0;
        }
    }
}