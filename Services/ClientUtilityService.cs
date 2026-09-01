using System.Diagnostics;
using static League.FormMain;

namespace League.Services
{
    /// <summary>
    /// 客户端工具：选人软退（回大厅）+ 取消匹配层 + 监视并结束游戏进程（保底）
    /// 说明：这不是官方意义的「无惩罚真秒退」，而是尽量不进游戏、保留大厅。
    /// </summary>
    public class ClientUtilityService
    {
        
        #region 重启 UX / 关大厅进程

        public async Task<(bool Success, string Message)> RestartUxAsync()
        {
            try
            {
                var client = Globals.lcuClient?.Client;
                if (client == null) return (false, "LCU 未连接");

                string? phase = await Globals.lcuClient.GetGameflowPhase();
                if (phase == "InProgress")
                    return (false, "游戏进行中无法重启大厅 UX。");

                var response = await client.PostAsync("/riotclient/kill-and-restart-ux", null);
                return response.IsSuccessStatusCode
                    ? (true, "已请求重启大厅界面")
                    : (false, $"失败: {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>结束大厅相关进程，不需要 LCU 已连接</summary>
        public (bool AnyKilled, string Message) CloseAllClients()
        {
            int killed = 0;
            foreach (var name in new[] { "LeagueClient", "LeagueClientUx", "LeagueClientUxRender" })
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            p.Kill();
                            p.WaitForExit(3000);
                            killed++;
                            Debug.WriteLine($"[关进程] {name} PID={p.Id}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[关进程] {name} 失败: {ex.Message}");
                        }
                    }
                }
                catch { }
            }
            return killed > 0
                ? (true, $"已结束 {killed} 个进程")
                : (false, "未找到运行中的客户端进程");
        }

        #endregion
    }
}