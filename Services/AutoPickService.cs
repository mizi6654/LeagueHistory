using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.Services
{
    /// <summary>
    /// 自动预选 / 抢英雄服务
    /// </summary>
    public class AutoPickService
    {
        private readonly FormMain _form;
        private CancellationTokenSource? _champSelectCts;

        private bool _hasAutoPreliminated = false;
        private bool _hasSwappedInAram = false;
        private TeamCardDisplayService? _cardDisplayService;

        public AutoPickService(FormMain form)
        {
            _form = form;
        }

        /// <summary>
        /// 启动选人阶段监控（同时负责我方卡片更新）
        /// </summary>
        public async Task StartChampSelectMonitoring(TeamCardDisplayService cardDisplayService)
        {
            _cardDisplayService = cardDisplayService;
            _champSelectCts?.Cancel();
            _champSelectCts = new CancellationTokenSource();
            var token = _champSelectCts.Token;

            _hasAutoPreliminated = false;
            _hasSwappedInAram = false;

            await Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var currentPhase = await Globals.lcuClient.GetGameflowPhase();
                        if (currentPhase != "ChampSelect") break;

                        // 我方队伍卡片显示
                        await _cardDisplayService.ShowMyTeamCards();

                        await TryAutoPreliminaryAsync();

                        var session = await Globals.lcuClient.GetChampSelectSession();
                        int queueId = session?["queueId"]?.Value<int>() ?? 0;

                        // 乱斗类轮询更快，其它模式 1 秒
                        int delay = (queueId == 450 || queueId == 2400 || queueId == 900) ? 500 : 1000;
                        await Task.Delay(delay, token);
                    }
                    catch (TaskCanceledException) { break; }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AutoPickService] 异常: {ex.Message}");
                    }
                }
            }, token);
        }

        public void Stop()
        {
            _champSelectCts?.Cancel();
            _champSelectCts?.Dispose();
            _champSelectCts = null;
        }

        public void Reset()
        {
            _hasAutoPreliminated = false;
            _hasSwappedInAram = false;
        }

        /// <summary>
        /// 自动预选功能核心方法，新增根据勾选的模式进行过滤
        /// </summary>
        private async Task TryAutoPreliminaryAsync()
        {
            var preConfig = _form.GetAppConfig()?.Preliminary;
            if (preConfig == null || !preConfig.EnableAutoPreliminary)
                return;

            var preList = await _form.GetPreSelectedHeroesAsync();
            if (!preList.Any())
                return;

            var session = await Globals.lcuClient.GetChampSelectSession();
            if (session == null)
                return;

            int queueId = session["queueId"]?.Value<int>() ?? 0;

            // 根据 queueId 和用户配置决定是否执行
            bool shouldExecute = queueId switch
            {
                400 or 430 => _form.GetAppConfig().EnablePreliminaryInNormal,      // 匹配
                420 or 440 => _form.GetAppConfig().EnablePreliminaryInRanked,      // 排位
                450 => _form.GetAppConfig().EnablePreliminaryInAram,        // 大乱斗
                2400 => _form.GetAppConfig().EnablePreliminaryInNexusBlitz,  // 海克斯
                900 => _form.GetAppConfig().EnablePreliminaryInUrf,         // 无限乱斗（随机）
                1900 => _form.GetAppConfig().EnablePreliminaryInPickUrf,     // 无限火力（自选）
                _ => false
            };

            if (!shouldExecute)
                return;

            // 有板凳席的乱斗类模式：大乱斗、海克斯、无限乱斗 → 抢英雄
            if (queueId == 450 || queueId == 2400 || queueId == 900)
            {
                await Globals.lcuClient.AutoSwapToHighestPriorityAsync(preList);
                return;
            }

            // 普通自选模式：匹配、排位、无限火力 → 只预选一次
            if (_hasAutoPreliminated) return;

            bool success = await Globals.lcuClient.AutoDeclareIntentAsync(preList);
            if (success)
            {
                _hasAutoPreliminated = true;
                Debug.WriteLine("[自动预选] 普通模式意图声明成功");
            }
        }
    }
}