//using System.Diagnostics;

//namespace League.Infrastructure
//{
//    public class Poller
//    {
//        private CancellationTokenSource _cts;
//        private int _interval;

//        public void Start(Func<Task> action, int interval)
//        {
//            Stop();

//            _interval = interval;
//            _cts = new CancellationTokenSource();

//            Task.Run(async () =>
//            {
//                while (!_cts.IsCancellationRequested)
//                {
//                    try
//                    {
//                        await action();
//                    }
//                    catch (Exception ex)
//                    {
//                        Debug.WriteLine($"[Poller异常] {ex}");
//                    }

//                    await Task.Delay(_interval, _cts.Token);
//                }
//            });
//        }

//        public void Stop()
//        {
//            if (_cts != null && !_cts.IsCancellationRequested)
//            {
//                _cts.Cancel();
//            }
//        }
//    }

//}

using System.Diagnostics;

namespace League.Infrastructure
{
    public class Poller : IDisposable
    {
        private PeriodicTimer? _timer;
        private Task? _pollingTask;
        private CancellationTokenSource? _cts;

        /// <summary>
        /// 启动轮询
        /// </summary>
        public void Start(Func<Task> action, int intervalMs)
        {
            Stop(); // 先停止之前的

            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));

            _pollingTask = Task.Run(async () =>
            {
                try
                {
                    while (await _timer.WaitForNextTickAsync(_cts.Token))
                    {
                        try
                        {
                            await action();
                        }
                        catch (Exception ex) when (ex is not TaskCanceledException)
                        {
                            Debug.WriteLine($"[Poller 异常] {ex.Message}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，不打印异常
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Poller 严重异常] {ex}");
                }
            }, _cts.Token);
        }

        /// <summary>
        /// 异步停止（推荐使用）
        /// </summary>
        public async Task StopAsync()
        {
            if (_timer == null) return;

            _timer.Dispose();
            _cts?.Cancel();

            if (_pollingTask != null)
            {
                try
                {
                    await _pollingTask;
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Poller Stop 异常] {ex}");
                }
            }

            _timer = null;
            _cts?.Dispose();
            _cts = null;
            _pollingTask = null;
        }

        /// <summary>
        /// 同步停止（兼容旧代码）
        /// </summary>
        public void Stop()
        {
            _ = StopAsync(); // fire-and-forget
        }

        public void Dispose()
        {
            Stop();
        }
    }
}