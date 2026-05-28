//using System.Diagnostics;
//using static League.FormMain;

//namespace League.Services
//{
//    /// <summary>
//    /// 负责纯游戏流程阶段监听和阶段变化通知
//    /// </summary>
//    public class GameFlowPhaseMonitor
//    {
//        public event Func<string, string?, Task>? PhaseChanged;

//        private CancellationTokenSource? _monitorCts;

//        /// <summary>
//        /// 启动全局游戏流程监听
//        /// </summary>
//        public async Task StartMonitoringAsync(CancellationToken token)
//        {
//            _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(token);

//            try
//            {
//                await Task.Run(async () =>
//                {
//                    string? lastPhase = null;

//                    while (!_monitorCts.Token.IsCancellationRequested)
//                    {
//                        try
//                        {
//                            string? phase = await Globals.lcuClient.GetGameflowPhase();

//                            if (string.IsNullOrEmpty(phase))
//                            {
//                                // LCU断开
//                                await OnLcuDisconnected();
//                                return;
//                            }

//                            if (phase != lastPhase)
//                            {
//                                if (PhaseChanged != null)
//                                    await PhaseChanged(phase, lastPhase);

//                                lastPhase = phase;
//                            }

//                            await Task.Delay(1000, _monitorCts.Token);
//                        }
//                        catch (TaskCanceledException)
//                        {
//                            return;
//                        }
//                        catch (Exception ex)
//                        {
//                            Debug.WriteLine($"[GameFlowPhaseMonitor] 轮询异常：{ex}");
//                            await Task.Delay(2000, _monitorCts.Token);
//                        }
//                    }
//                }, _monitorCts.Token);
//            }
//            catch (TaskCanceledException) { }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[GameFlowPhaseMonitor] 严重异常：{ex}");
//            }
//        }

//        private async Task OnLcuDisconnected()
//        {
//            // 可在此处通知外部或直接处理
//            Debug.WriteLine("[GameFlowPhaseMonitor] LCU 已断开连接");
//        }

//        public void Stop()
//        {
//            _monitorCts?.Cancel();
//            _monitorCts?.Dispose();
//            _monitorCts = null;
//        }
//    }
//}

using System.Diagnostics;
using static League.FormMain;

public class GameFlowPhaseMonitor
{
    public event Func<string, string?, Task>? PhaseChanged;
    public event Action? OnLcuDisconnected;   // ← 新增事件，通知外部断线

    private CancellationTokenSource? _monitorCts;

    public async Task StartMonitoringAsync(CancellationToken token)
    {
        _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(token);

        try
        {
            await Task.Run(async () =>
            {
                string? lastPhase = null;

                while (!_monitorCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        string? phase = await Globals.lcuClient.GetGameflowPhase();

                        if (string.IsNullOrEmpty(phase))
                        {
                            OnLcuDisconnected?.Invoke();   // 触发断线
                            return;
                        }

                        if (phase != lastPhase)
                        {
                            if (PhaseChanged != null)
                                await PhaseChanged(phase, lastPhase);
                            lastPhase = phase;
                        }

                        await Task.Delay(1000, _monitorCts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)   // ← 关键修改
                    {
                        Debug.WriteLine($"[GameFlowPhaseMonitor] 异常（可能客户端关闭）: {ex.Message}");
                        OnLcuDisconnected?.Invoke();   // 异常也视为断线
                        return;   // 退出循环
                    }
                }
            }, _monitorCts.Token);
        }
        catch { }
    }

    public void Stop()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = null;
    }
}