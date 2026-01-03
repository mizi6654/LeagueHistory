namespace League.UIHelpers
{
    /// <summary>
    /// 图片处理辅助类
    /// </summary>
    public static class ImageProcessor
    {
        /// <summary>
        /// 调整图片大小（添加黑色透明背景）
        /// </summary>
        public static Image ResizeImage(Image originalImage, int width, int height, Color? bgColor = null)
        {
            if (originalImage == null)
                return CreateDefaultImage(width, height, bgColor ?? Color.Transparent);

            try
            {
                var resizedImage = new Bitmap(width, height);
                using (var graphics = Graphics.FromImage(resizedImage))
                {
                    // 填充黑色半透明背景（针对表格视图的小图片）
                    if (width <= 30 && height <= 30) // 小图片添加黑色背景
                    {
                        using (var bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                        {
                            graphics.FillRectangle(bgBrush, 0, 0, width, height);
                        }
                    }
                    else if (bgColor.HasValue) // 大图片使用指定背景色
                    {
                        graphics.Clear(bgColor.Value);
                    }

                    // 高质量绘制
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                    // 计算居中位置
                    int x = 0;
                    int y = 0;

                    // 对于小图片，稍微缩小一点让黑色边框更明显
                    if (width <= 30 && height <= 30)
                    {
                        x = 1;
                        y = 1;
                        width -= 2;
                        height -= 2;
                    }

                    graphics.DrawImage(originalImage, x, y, width, height);
                }
                return resizedImage;
            }
            catch
            {
                return CreateDefaultImage(width, height, bgColor ?? Color.Transparent);
            }
        }

        /// <summary>
        /// 专门为表格视图调整图片大小（只给图片加黑色背景）
        /// </summary>
        public static Image ResizeImageForTableView(Image originalImage, int width, int height)
        {
            if (originalImage == null)
                return CreateDefaultImage(width, height, Color.FromArgb(180, 0, 0, 0));

            try
            {
                var resizedImage = new Bitmap(width, height);
                using (var graphics = Graphics.FromImage(resizedImage))
                {
                    // 填充黑色半透明背景（只给单个图片加背景）
                    using (var bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                    {
                        graphics.FillRectangle(bgBrush, 0, 0, width, height);
                    }

                    // 高质量绘制
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                    // 图片稍微缩小，露出黑色边框
                    int padding = 2;
                    graphics.DrawImage(originalImage, padding, padding, width - padding * 2, height - padding * 2);
                }
                return resizedImage;
            }
            catch
            {
                return CreateDefaultImage(width, height, Color.FromArgb(180, 0, 0, 0));
            }
        }

        // 其他方法保持不变...
        public static Image CreateDefaultImage(int width, int height, Color color)
        {
            var bitmap = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bitmap))
                g.Clear(color);
            return bitmap;
        }

        public static Image CreateEmptySlotImage(int width, int height)
        {
            var bitmap = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(30, 128, 128, 128));
                using (var pen = new Pen(Color.LightGray, 1))
                    g.DrawRectangle(pen, 0, 0, width - 1, height - 1);
            }
            return bitmap;
        }
    }
}
