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
    }
}
