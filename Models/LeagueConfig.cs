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

        // 新增：预选相关配置
        public PreliminaryConfig Preliminary { get; set; } = new PreliminaryConfig();

        // ==================== 新增：各模式自动预选开关 ====================
        // 默认全开，确保升级后旧用户行为不变
        public bool EnablePreliminaryInNormal { get; set; } = true;    // 匹配（盲选/征召）
        public bool EnablePreliminaryInRanked { get; set; } = true;    // 排位（单双/灵活）
        public bool EnablePreliminaryInAram { get; set; } = true;      // 大乱斗
        public bool EnablePreliminaryInNexusBlitz { get; set; } = true; // 海克斯大乱斗（2400）

        // ==================== 新增：战绩/自定义文本发送配置 ====================

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
    }
}
