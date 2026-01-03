namespace League.Models
{
    public class PlayerInfo
    {
        public string FullName { get; set; } // 如 "总有狗喜欢投降#65905"

        public Image Avatar { get; set; }
        public string Tooltip { get; set; }
        public int TeamId { get; set; }
        public bool IsSelf { get; set; }//如果是自己，则图片高亮显示


        public string Puuid { get; set; }
        public long SummonerId { get; set; }
        public string GameName { get; set; }     // 玩家名称
        public int ChampionId { get; set; }      // 英雄ID
        public string ChampionName { get; set; } // 英雄英文名

        public string SoloRank { get; set; } // 单双排段位
        public string FlexRank { get; set; } // 灵活组排段位

        public string IsPublic { get; set; }    //是否公开

        public bool IsInParty { get; set; } // 是否近期经常组队

        // 新增两个属性
        public string GroupKey { get; set; }
        public Color NameColor { get; set; } = Color.Black;
    }

}
