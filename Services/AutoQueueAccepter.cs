using System.Diagnostics;
using static League.FormMain;

namespace League.Services
{
    public class AutoQueueAccepter
    {
        private readonly FormMain _form;
        private bool _hasAcceptedReadyCheck = false;

        public AutoQueueAccepter(FormMain form) => _form = form;

        public async Task TryAcceptAsync()
        {
            if (_hasAcceptedReadyCheck) return;

            try
            {
                // 减少延迟，或者做 2~3 次快速尝试
                for (int i = 0; i < 3; i++)
                {
                    bool success = await Globals.lcuClient.AcceptReadyCheckAsync();
                    if (success)
                    {
                        _hasAcceptedReadyCheck = true;
                        Debug.WriteLine($"[自动接受] ✅ 第 {i + 1} 次尝试成功");
                        return;
                    }
                    await Task.Delay(300); // 每次失败后等 300ms
                }
                Debug.WriteLine("[自动接受] 多次尝试后仍失败");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[自动接受] 异常: {ex.Message}");
            }
        }

        public void Reset() => _hasAcceptedReadyCheck = false;
    }
}
