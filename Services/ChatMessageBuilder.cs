using League.Models;
using League.PrimaryElection;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Text;

namespace League.Services
{
    public class ChatMessageBuilder
    {
        private readonly Func<Dictionary<long, PlayerMatchInfo>> _getCachedPlayerInfos;
        private readonly ChampionManager _championManager;
        private readonly bool _hideSelf;   // 是否隐藏自己

        private string _myPuuid = string.Empty;

        public ChatMessageBuilder(Func<Dictionary<long, PlayerMatchInfo>> getCachedPlayerInfos,
                                  ChampionManager championManager,
                                  bool hideSelf = true)
        {
            _getCachedPlayerInfos = getCachedPlayerInfos ?? throw new ArgumentNullException(nameof(getCachedPlayerInfos));
            _championManager = championManager ?? throw new ArgumentNullException(nameof(championManager));
            _hideSelf = hideSelf;
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

        private bool ShouldSkipPlayer(string? puuid)
        {
            return _hideSelf && IsMySelf(puuid);
        }

        private string GetDisplayName(PlayerMatchInfo info, JToken? rawPlayerData = null, bool useChampionName = false)
        {
            if (!useChampionName)
                return (info?.Player?.GameName ?? "未知").Trim();

            int champId = rawPlayerData?["championId"]?.Value<int>() ?? info?.Player?.ChampionId ?? 0;

            if (champId <= 0)
            {
                Debug.WriteLine($"[英雄映射] 失败 - ChampionId=0，回退玩家名: {info?.Player?.GameName}");
                return (info?.Player?.GameName ?? "未知").Trim();
            }

            var champ = _championManager.GetChampionById(champId);
            if (champ != null && !string.IsNullOrWhiteSpace(champ.Name))
                return champ.Name.Trim();

            return (info?.Player?.GameName ?? "未知").Trim();
        }

        public string BuildMyTeamSummary(JArray? team, bool useChampionName = false)
        {
            return BuildTeamSummary(team, false, useChampionName);
        }

        public string BuildFullTeamSummary(JArray? myTeam, JArray? enemyTeam, bool useChampionName = false)
        {
            var sb = new StringBuilder();

            string my = BuildTeamSummary(myTeam, false, useChampionName);
            if (!string.IsNullOrWhiteSpace(my))
            {
                sb.AppendLine(my);
                sb.AppendLine();
            }

            if (enemyTeam?.Count > 0 == true)
            {
                string enemy = BuildTeamSummary(enemyTeam, true, useChampionName);
                if (!string.IsNullOrWhiteSpace(enemy))
                    sb.AppendLine(enemy);
            }

            return sb.ToString().TrimEnd();
        }

        private string BuildTeamSummary(JArray? team, bool detailed, bool useChampionName = false)
        {
            if (team == null || team.Count == 0)
                return "暂无数据";

            var sb = new StringBuilder();
            var infos = _getCachedPlayerInfos();

            foreach (var p in team)
            {
                string? puuid = p["puuid"]?.ToString();
                if (string.IsNullOrEmpty(puuid) || ShouldSkipPlayer(puuid))
                    continue;

                long sid = p["summonerId"]?.Value<long>() ?? 0;
                if (sid == 0) continue;

                if (!infos.TryGetValue(sid, out var info) || info?.Player == null)
                    continue;

                string line = detailed
                    ? BuildDetailedLine(info, useChampionName, p)
                    : BuildSimpleLine(info, useChampionName, p);

                sb.AppendLine(line);
            }

            return sb.ToString().TrimEnd();
        }

        private string BuildSimpleLine(PlayerMatchInfo info, bool useChampionName, JToken? rawPlayerData)
        {
            string displayName = GetDisplayName(info, rawPlayerData, useChampionName);
            int gameCount = info.WinHistory?.Count ?? 0;
            if (gameCount == 0)
                return $"{displayName} 暂无战绩";

            double winRate = CalculateWinRate(info, gameCount);
            string behavior = info.Behavior?.BehaviorSummary ?? "";
            string gameText = $"近{gameCount}场";
            string baseStr = $"{displayName} {gameText} 胜率 [{winRate:F0}%]";

            if (string.IsNullOrEmpty(behavior))
                return baseStr;

            var details = new List<string>();
            var b = info.Behavior;
            if (b?.AvgMissingPings > 0) details.Add($"发问号 {b.AvgMissingPings:F1}");
            if (b?.AvgVisionPings > 0) details.Add($"发提醒 {b.AvgVisionPings:F1}");
            if (b?.AvgGetBackPings > 0) details.Add($"发撤退 {b.AvgGetBackPings:F1}");
            if (b?.AvgDangerPings > 0) details.Add($"发危险 {b.AvgDangerPings:F1}");

            string detailStr = details.Count > 0 ? $" | {string.Join(" | ", details)}" : "";
            return baseStr + detailStr + $" 特征 [{behavior}]";
        }

        private string BuildDetailedLine(PlayerMatchInfo info, bool useChampionName, JToken? rawPlayerData)
        {
            string displayName = GetDisplayName(info, rawPlayerData, useChampionName);
            int gameCount = info.WinHistory?.Count ?? 0;
            if (gameCount == 0)
                return $"{displayName} 暂无战绩";

            double winRate = CalculateWinRate(info, gameCount);
            string behavior = info.Behavior?.BehaviorSummary ?? "";
            string gameText = $"近{gameCount}场";
            string baseStr = $"{displayName} {gameText} 胜率 [{winRate:F0}%]";

            if (string.IsNullOrEmpty(behavior))
                return baseStr;

            var details = new List<string>();
            var b = info.Behavior;
            if (b?.AvgMissingPings > 0) details.Add($"发问号 {b.AvgMissingPings:F1}");
            if (b?.AvgVisionPings > 0) details.Add($"发提醒 {b.AvgVisionPings:F1}");
            if (b?.AvgGetBackPings > 0) details.Add($"发撤退 {b.AvgGetBackPings:F1}");
            if (b?.AvgDangerPings > 0) details.Add($"发危险 {b.AvgDangerPings:F1}");

            string detailStr = details.Count > 0 ? $" | {string.Join(" | ", details)}" : "";
            return baseStr + detailStr + $" 特征 [{behavior}]";
        }

        private double CalculateWinRate(PlayerMatchInfo info, int gamesToUse)
        {
            if (info.WinHistory == null || info.WinHistory.Count == 0)
                return 0.0;

            var recent = info.WinHistory.Take(gamesToUse).ToList();
            int wins = recent.Count(w => w);
            return recent.Count > 0 ? (wins * 100.0 / recent.Count) : 0.0;
        }
    }
}