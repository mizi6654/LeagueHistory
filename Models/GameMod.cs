namespace League.Models
{
    internal class GameMod
    {
        // 根据 queueId 和 gameMode 映射模式名称
        public static string GetModeName(int? queueId, string gameMode = null)
        {
            // 优先用 queueId 映射（转换为字符串）
            if (queueId.HasValue)
            {
                string queueKey = queueId.Value.ToString();
                if (_modeTranslations.TryGetValue(queueKey, out string name))
                    return name;
            }

            // 其次尝试用 gameMode 映射（通常为 CLASSIC、ARAM 等）
            if (!string.IsNullOrEmpty(gameMode))
            {
                string modeKey = gameMode.ToUpperInvariant();
                if (_modeTranslations.TryGetValue(modeKey, out string name))
                    return name;
            }

            //return $"未知模式{queueId}|{gameMode}";
            // 明确显示 null 值和格式
            //return $"未知模式（QueueID={queueId?.ToString() ?? "null"} | GameMode={gameMode ?? "null"}）";
            return $"未知({queueId?.ToString()})";
        }

        // 模式映射表（键为 queueId 或 gameMode 的字符串形式）
        private static readonly Dictionary<string, string> _modeTranslations = new()
        {
            // 常规匹配和排位
            ["400"] = "盲选匹配",
            ["420"] = "单双排",
            ["430"] = "征召匹配",
            ["440"] = "灵活组排",

            // 轮换模式
            ["450"] = "极地大乱斗",
            ["480"] = "快速模式",          // 根据 queueId=480 映射
            ["900"] = "无限火力",
            ["1020"] = "克隆大作战",
            ["1300"] = "极限闪击",
            ["1700"] = "斗魂竞技场",


            // 云顶之弈
            ["1090"] = "云顶之弈：排位",
            ["1130"] = "云顶之弈：双人",

            // 人机模式
            ["830"] = "人机：入门",
            ["840"] = "人机：初级",
            ["850"] = "人机：中级",

            // 自定义
            ["-1"] = "自定义模式",

            // gameMode 字段兼容映射
            ["CLASSIC"] = "召唤师峡谷",
            ["ARAM"] = "极地大乱斗",
            ["SWIFTPLAY"] = "快速模式",     // 根据 gameMode=SWIFTPLAY 映射
            ["URF"] = "无限火力",
            ["ONEFORALL"] = "克隆大作战",
            ["NEXUSBLITZ"] = "极限闪击",
            ["CHERRY"] = "斗魂竞技场",
            ["TFT"] = "云顶之弈"
        };
    }
}
