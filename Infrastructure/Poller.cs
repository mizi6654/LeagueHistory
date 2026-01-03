using System.Diagnostics;

namespace League.Infrastructure
{
    public class Poller
    {
        private CancellationTokenSource _cts;
        private int _interval;

        public void Start(Func<Task> action, int interval)
        {
            Stop();

            _interval = interval;
            _cts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        await action();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Poller异常] {ex}");
                    }

                    await Task.Delay(_interval, _cts.Token);
                }
            });
        }

        public void Stop()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
        }
    }

}
