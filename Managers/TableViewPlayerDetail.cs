namespace League.Managers
{
    /// <summary>
    /// 表格视图玩家详情
    /// </summary>
    public class TableViewPlayerDetail
    {
        public string Team { get; set; }
        public Image ChampionIcon { get; set; }
        public string PlayerName { get; set; }
        public string KDA { get; set; }
        public string Damage { get; set; }
        public string Gold { get; set; }
        public int CS { get; set; }
        public string KP { get; set; }
        public int Vision { get; set; }
        public Image SpellsImage { get; set; }
        public Image ItemsImage { get; set; }
        public Image RunesImage { get; set; }
        public bool IsSelf { get; set; }
        public string ChampionTooltip { get; set; }
        public string SpellsTooltip { get; set; }
        public string ItemsTooltip { get; set; }
        public string RunesTooltip { get; set; }
    }
}
