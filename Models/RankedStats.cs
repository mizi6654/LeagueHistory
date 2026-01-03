
using Newtonsoft.Json.Linq;

namespace League.Models
{
    public class RankedStats
    {
        private const string SoloQueueKey = "RANKED_SOLO_5x5";  // 单双排
        private const string FlexQueueKey = "RANKED_FLEX_SR";   // 灵活排位

        public string QueueType { get; private set; }   // 队列类型
        public string Tier { get; private set; }    // 段位（如：黄金）
        public string Division { get; private set; }    // 小段（如：IV）
        public int Wins { get; private set; }   // 胜场
        public int Losses { get; private set; } // 负场
        public int LeaguePoints { get; private set; }   // 胜点

        /// <summary>
        /// 如果 losses 为 0，视为未知
        /// </summary>
        public bool IsUnknown => Losses == 0;

        /// <summary>
        /// 总场次
        /// </summary>
        public int TotalGames => !IsUnknown ? Wins + Losses : 0;

        /// <summary>
        /// 胜率
        /// </summary>
        public double WinRate => !IsUnknown && TotalGames > 0
            ? Math.Round(Wins * 100.0 / TotalGames, 2)
            : 0;

        /// <summary>
        /// 格式化显示的段位
        /// </summary>
        public string FormattedTier => FormatTierDisplay();

        /// <summary>
        /// 胜率显示文本
        /// </summary>
        public string WinRateDisplay => !IsUnknown ? $"{WinRate}%" : "未知";

        /// <summary>
        /// 总场次显示文本
        /// </summary>
        public string TotalGamesDisplay => !IsUnknown ? $"{TotalGames} 场" : "未知";

        /// <summary>
        /// 负场显示文本
        /// </summary>
        public string LossesDisplay => !IsUnknown ? $"{Losses} 场" : "未知";

        /// <summary>
        /// 胜场显示文本（始终显示）
        /// </summary>
        public string WinsDisplay => $"{Wins} 场";

        /// <summary>
        /// 从 JSON 创建单双排和灵活排位数据
        /// </summary>
        public static Dictionary<string, RankedStats> FromJson(JObject rankedJson)
        {
            var result = new Dictionary<string, RankedStats>
            {
                { "单双排", CreateFromQueueData(rankedJson?.SelectToken($"$.queueMap.{SoloQueueKey}")) },
                { "灵活组排", CreateFromQueueData(rankedJson?.SelectToken($"$.queueMap.{FlexQueueKey}")) }
            };

            // 确保至少返回空数据
            result["单双排"] ??= new RankedStats { Tier = "未定级" };
            result["灵活组排"] ??= new RankedStats { Tier = "未定级" };

            return result;
        }

        /// <summary>
        /// 从单个队列 JSON 创建实例
        /// </summary>
        private static RankedStats CreateFromQueueData(JToken queueData)
        {
            if (queueData == null || !queueData.HasValues)
                return new RankedStats { Tier = "未定级" };

            return new RankedStats
            {
                QueueType = queueData["queueType"]?.ToString() ?? string.Empty,
                Tier = NormalizeTier(queueData["tier"]?.ToString()),
                Division = NormalizeDivision(queueData["division"]?.ToString()),
                Wins = queueData["wins"]?.ToObject<int>() ?? 0,
                Losses = queueData["losses"]?.ToObject<int>() ?? 0,
                LeaguePoints = queueData["leaguePoints"]?.ToObject<int>() ?? 0
            };
        }

        /// <summary>
        /// 格式化段位中文显示
        /// </summary>
        private string FormatTierDisplay()
        {
            if (string.IsNullOrEmpty(Tier) || Tier == "NONE")
                return "未定级";

            var tierMap = new Dictionary<string, string>
            {
                {"IRON", "黑铁"},
                {"BRONZE", "青铜"},
                {"SILVER", "白银"},
                {"GOLD", "黄金"},
                {"PLATINUM", "铂金"},
                {"EMERALD", "翡翠"},
                {"DIAMOND", "钻石"},
                {"MASTER", "大师"},
                {"GRANDMASTER", "宗师"},
                {"CHALLENGER", "最强王者"}
            };

            if (!tierMap.TryGetValue(Tier, out var chineseTier))
                chineseTier = "未知";

            // 大师以上无小段
            var isHighTier = Tier is "MASTER" or "GRANDMASTER" or "CHALLENGER";

            return isHighTier ? chineseTier : $"{chineseTier} {ToChineseDivision(Division)}";
        }

        /// <summary>
        /// 小段位转换
        /// </summary>
        private static string ToChineseDivision(string division)
        {
            return division switch
            {
                "I" => "I",
                "II" => "II",
                "III" => "III",
                "IV" => "IV",
                _ => ""
            };
        }

        /// <summary>
        /// 标准化段位
        /// </summary>
        private static string NormalizeTier(string rawTier)
        {
            return string.IsNullOrWhiteSpace(rawTier) || rawTier == "NONE"
                ? "未定级"
                : rawTier;
        }

        /// <summary>
        /// 标准化小段位
        /// </summary>
        private static string NormalizeDivision(string rawDivision)
        {
            return rawDivision == "NA" || string.IsNullOrEmpty(rawDivision)
                ? string.Empty
                : rawDivision;
        }
    }
}

