namespace League.Models
{
    public class PlayerBehavior
    {
        public double SurrenderRate { get; set; } = 0;           // 投降率（%）

        public double AvgMissingPings { get; set; } = 0;            // 平均 Missing Ping
        public double AvgVisionPings { get; set; } = 0;             // 平均视野 Ping
        public double AvgGetBackPings { get; set; } = 0;            // 平均撤退信号
        public double AvgDangerPings { get; set; } = 0;             // 平均危险信号

        public int ShortGameCount { get; set; } = 0;             // 短局数量
        public string BehaviorSummary { get; set; } = "";        // 用于发送的消息文字
    }
}