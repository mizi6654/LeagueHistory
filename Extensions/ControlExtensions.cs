namespace League.Extensions 
{
    // 在 League.model 命名空间下，替换或新增这个扩展方法
    public static class ControlExtensions
    {
        // 原有同步版本保留（兼容旧代码）
        public static void InvokeIfRequired(this Control control, Action action)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            if (control.IsDisposed || control.Disposing) return;

            if (control.InvokeRequired)
                control.Invoke(action);
            else
                action();
        }

        // 新增：支持 async 委托的安全版本
        public static async Task InvokeIfRequiredAsync(this Control control, Func<Task> asyncAction)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            if (control.IsDisposed || control.Disposing) return;

            if (control.InvokeRequired)
            {
                await control.Invoke(asyncAction);  // Invoke 会等待 Task 完成
            }
            else
            {
                await asyncAction();
            }
        }
    }
}