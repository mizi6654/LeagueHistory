namespace League.Models
{
    public class PlayerMatchInfo
    {
        public PlayerInfo Player { get; set; }
        public List<ListViewItem> MatchItems { get; set; } = new List<ListViewItem>();  
        public ImageList HeroIcons { get; set; } = new ImageList { ImageSize = new Size(20, 20) };

        //用于表示该对象是否来自缓存
        public bool IsFromCache { get; set; }

        // 新增：用于存储比赛的唯一标识（gameId + teamId）
        public List<string> MatchKeys { get; set; } = new List<string>();

        // 读取 MatchKeys 作为 Matches
        public IEnumerable<string> Matches => MatchKeys;

        public string PartyId { get; set; } // 加上这个字段用于判断组队身份

        public List<bool> WinHistory { get; set; } = new(); // 新增: 每场 true=胜利, false=失败

        public List<MatchStat> RecentMatches { get; set; } = new List<MatchStat>();
    }
    public class MatchStat
    {
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }
    }
}
