namespace League.Extensions
{
    public static class TaskExtensions
    {
        public static async Task<T> WithTimeout<T>(this Task<T> task, int millisecondsTimeout)
        {
            using (var cts = new CancellationTokenSource())
            {
                var timeoutTask = Task.Delay(millisecondsTimeout, cts.Token);
                var completedTask = await Task.WhenAny(task, timeoutTask);
                if (completedTask == task)
                {
                    cts.Cancel(); // 取消超时等待
                    return await task; // 返回原始结果
                }
                throw new TimeoutException("操作超时");
            }
        }
    }

}
