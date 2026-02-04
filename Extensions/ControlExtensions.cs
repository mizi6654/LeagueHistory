using System.Diagnostics;

namespace League.Extensions 
{
    // 在 League.model 命名空间下，替换或新增这个扩展方法
    public static class ControlExtensions
    {
        // 修复的同步版本
        public static void InvokeIfRequired(this Control control, Action action)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));

            // 重要：检查句柄是否已创建
            if (!control.IsHandleCreated || control.IsDisposed || control.Disposing)
                return;

            if (control.InvokeRequired)
            {
                try
                {
                    control.Invoke(action);
                }
                catch (InvalidOperationException ex)
                {
                    // 句柄在检查后可能又被销毁，记录日志但不抛出
                    Debug.WriteLine($"[InvokeIfRequired] 句柄异常: {ex.Message}");
                }
            }
            else
            {
                action();
            }
        }

        // 修复的异步版本
        public static async Task InvokeIfRequiredAsync(this Control control, Func<Task> asyncAction)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));

            // 重要：检查句柄是否已创建
            if (!control.IsHandleCreated || control.IsDisposed || control.Disposing)
                return;

            if (control.InvokeRequired)
            {
                try
                {
                    await control.Invoke(asyncAction);
                }
                catch (InvalidOperationException ex)
                {
                    // 句柄在检查后可能又被销毁，记录日志但不抛出
                    Debug.WriteLine($"[InvokeIfRequiredAsync] 句柄异常: {ex.Message}");
                    // 可以选择等待一小段时间再重试，或者直接返回
                }
            }
            else
            {
                await asyncAction();
            }
        }
    }
}