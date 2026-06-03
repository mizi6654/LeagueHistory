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