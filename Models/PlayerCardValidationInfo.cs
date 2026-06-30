using League.Controls;

namespace League.Models
{
    /// <summary>
    /// 卡片校验信息
    /// </summary>
    public class PlayerCardValidationInfo
    {
        public long SummonerId { get; set; }
        public int ChampionId { get; set; }
        public string Puuid { get; set; } = "";
        public int Row { get; set; }
        public int Column { get; set; }
        public PlayerCardControl Card { get; set; }
        public string CurrentName { get; set; }
        public string CurrentSoloRank { get; set; }
        public string CurrentFlexRank { get; set; }
        public bool HasAvatar { get; set; }

        public int RetryCount { get; set; } = 0;   // 新增：防止无限重试
    }
}