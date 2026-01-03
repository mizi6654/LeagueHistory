using League.UIHelpers;
using System.Diagnostics;

namespace League.Infrastructure
{
    /// <summary>
    /// 数据加载辅助类
    /// </summary>
    public static class DataLoader
    {
        /// <summary>
        /// 安全加载图片资源
        /// </summary>
        public static async Task<Image> SafeLoadImageAsync(Func<Task<(Image, string, string)>> loader,
            int width, int height, Color? bgColor = null)
        {
            try
            {
                var task = loader();
                if (await Task.WhenAny(task, Task.Delay(500)) == task)
                {
                    var result = await task;
                    if (result.Item1 != null)
                        return ImageProcessor.ResizeImage(result.Item1, width, height, bgColor);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"图片加载失败: {ex.Message}");
            }
            return ImageProcessor.CreateDefaultImage(width, height, bgColor ?? Color.LightGray);
        }
    }
}
