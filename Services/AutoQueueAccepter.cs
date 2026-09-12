using System.Diagnostics;
using static League.FormMain;

namespace League.Services
{
    public class AutoQueueAccepter
    {
        private readonly FormMain _form;
        private bool _hasAcceptedReadyCheck = false;

        public AutoQueueAccepter(FormMain form) => _form = form;

        public async Task TryAcceptAsync(int delaySeconds = 0)
        {
            if (_hasAcceptedReadyCheck) return;

            try
            {
                if (delaySeconds > 0)
                {
                    Debug.WriteLine($"[自动接受] 等待 {delaySeconds} 秒后接受...");
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                }

                // 延迟后再尝试接受（最多重试3次）
                for (int i = 0; i < 3; i++)
                {
                    bool success = await Globals.lcuClient.AcceptReadyCheckAsync();
                    if (success)
                    {
                        _hasAcceptedReadyCheck = true;
                        Debug.WriteLine($"[自动接受] ✅ 第 {i + 1} 次尝试成功（延迟 {delaySeconds}s）");
                        return;
                    }
                    await Task.Delay(300);
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