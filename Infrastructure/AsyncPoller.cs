//using System.Diagnostics;

//namespace League.Infrastructure
//{
//    public class AsyncPoller
//    {
//        private CancellationTokenSource _cts;

//        public void Start(Func<Task> action, int intervalMs = 1000)
//        {
//            Stop(); // 确保只有一个实例在跑
//            _cts = new CancellationTokenSource();
//            var token = _cts.Token;

//            Task.Run(async () =>
//            {
//                while (!token.IsCancellationRequested)
//                {
//                    try
//                    {
//                        await action();
//                    }
//                    catch (Exception ex)
//                    {
//                        Debug.WriteLine($"轮询异常: {ex.Message}");
//                    }

//                    await Task.Delay(intervalMs, token);
//                }
//            }, token);
//        }

//        public void Stop()
//        {
//            _cts?.Cancel();
//        }
//    }

//}

using System.Diagnostics;

namespace League.Infrastructure
{
    public class AsyncPoller : IDisposable
    {
        private CancellationTokenSource? _cts;
        private Task? _runningTask;

        public void Start(Func<Task> action, int intervalMs = 1000)
        {
            Stop(); // 先彻底停止旧任务

            _cts = new CancellationTokenSource();

            _runningTask = Task.Run(async () =>
            {
                var token = _cts.Token;

                while (!token.IsCancellationRequested)
                {
                    // 执行动作
                    try
                    {
                        await action();
                    }
                    catch (Exception ex) when (ex is not TaskCanceledException)
                    {
                        Debug.WriteLine($"[AsyncPoller] 动作异常: {ex.Message}");
                    }

                    // ==================== 关键延迟部分 ====================
                    if (token.IsCancellationRequested)
                        break;

                    try
                    {
                        await Task.Delay(intervalMs, token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;   // 正常取消
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AsyncPoller] Delay异常: {ex.Message}");
                        break;
                    }
                }
            });
        }

        public async Task StopAsync()
        {
            var cts = _cts;
            var task = _runningTask;

            _cts = null;
            _runningTask = null;

            if (cts == null) return;

            try
            {
                cts.Cancel();

                if (task != null && !task.IsCompleted)
                {
                    // 最多等待 800ms
                    await task.WaitAsync(TimeSpan.FromMilliseconds(800));
                }
            }
            catch (TaskCanceledException) { }
            catch (TimeoutException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AsyncPoller Stop] {ex.Message}");
            }
            finally
            {
                cts.Dispose();
            }
        }

        public void Stop()
        {
            _ = StopAsync();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}