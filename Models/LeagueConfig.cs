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
    }
}
