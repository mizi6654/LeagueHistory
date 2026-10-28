using League.PrimaryElection;

namespace League.Models
{
    public class LeagueConfig
    {
        /// <summary>
        /// 游戏结束后自动退出并返回客户端
        /// </summary>
        public bool AutoExitGameAfterEnd { get; set; } = false;

        /// <summary>
        /// 是否过滤游戏模式战绩
        /// </summary>
        public bool FilterByGameMode { get; set; } = false; // 默认查询所有

        /// <summary>
        /// 选人卡片查询的近期战绩场数（仅允许 10/20/30/50）
        /// </summary>
        public int CardMatchHistoryCount { get; set; } = 20;

        /// <summary>
        /// 预选相关配置
        /// </summary>
        public PreliminaryConfig Preliminary { get; set; } = new PreliminaryConfig();

        // 默认全开，确保升级后旧用户行为不变
        public bool EnablePreliminaryInNormal { get; set; } = false;    // 匹配（盲选/征召）
        public bool EnablePreliminaryInRanked { get; set; } = false;    // 排位（单双/灵活）
        public bool EnablePreliminaryInAram { get; set; } = false;      // 大乱斗
        public bool EnablePreliminaryInNexusBlitz { get; set; } = false; // 海克斯大乱斗（2400）
        public bool EnablePreliminaryInUrf { get; set; } = false;          // 无限乱斗（900，随机）
        public bool EnablePreliminaryInPickUrf { get; set; } = false;      // 无限火力（1900，自选）
        public bool EnablePreliminaryInClassic { get; set; } = false;      // 经典模式 / 怀旧峡谷（4310）
        public bool EnablePreliminaryInBot { get; set; } = false;       // 普通人机（830/840/850/870/880/890）

        /// <summary>
        /// 发送信息模式：1 = 自动发送战绩，2 = 发送自定义文本
        /// </summary>
        public int SendMode { get; set; } = 1;

        /// <summary>
        /// 存储自定义发送的文本内容（支持多行诗词/歌词）
        /// </summary>
        public string CustomSendContent { get; set; } = string.Empty;

        /// <summary>
        /// 是否启用自动接受对局（Ready Check）
        /// </summary>
        public bool EnableAutoAcceptQueue { get; set; } = false;

        /// <summary>
        /// 自动接受对局的延迟秒数（0=立即，5，10）
        /// </summary>
        public int AutoAcceptDelaySeconds { get; set; } = 0;

        /// <summary>
        /// 发送战绩时是否隐藏自己
        /// </summary>
        public bool HideSelfWhenSending { get; set; } = false;   // 默认隐藏自己

        /// <summary>
        /// 发送战绩时是否使用英雄名称代替玩家名称
        /// </summary>
        public bool UseChampionNameWhenSending { get; set; } = false;  // 默认使用英雄名


        /// <summary>
        /// 自动跳过点赞界面
        /// </summary>
        public bool EnableSkipHonor { get; set; } = false;

        /// <summary>
        /// 自动跳过结算统计界面
        /// </summary>
        public bool EnableSkipEndOfGameStats { get; set; } = false;

        /// <summary>
        /// 英雄偏好皮肤：key=championId, value=skinId
        /// </summary>
        public Dictionary<int, int> PreferredChampionSkins { get; set; } = new();

        /// <summary>
        /// 是否仅显示账号已有英雄
        /// </summary>
        public bool SkinBrowserOwnedOnly { get; set; } = true;

        /// <summary>
        /// 是否仅显示已有皮肤的英雄
        /// </summary>
        public bool SkinBrowserHasSkinOnly { get; set; } = true;

        /// <summary>
        /// 选人阶段是否按 PreferredChampionSkins 自动切皮肤
        /// </summary>
        public bool EnableAutoSkinSelect { get; set; } = false;

        /// <summary>
        /// 快捷键选人阶段发送，默认 Ctrl+F7
        /// </summary>
        public string HotkeyChampSelect { get; set; } = "Ctrl+F7";

        /// <summary>
        /// 快捷键游戏内发送我方，默认 F9
        /// </summary>
        public string HotkeyMyTeam { get; set; } = "F9";

        /// <summary>
        /// 快捷键游戏内发送全队，默认 F11
        /// </summary>
        public string HotkeyFullTeam { get; set; } = "F11";

        /// <summary>
        /// 日志保留天数（日常 .log 和 hidden_*.json 都会清理）
        /// 小于等于 0 表示不自动清理
        /// </summary>
        public int LogRetentionDays { get; set; } = 7;
    }
}
