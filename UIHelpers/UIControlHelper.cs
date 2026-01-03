namespace League.UIHelpers
{
    /// <summary>
    /// UI控件创建辅助类
    /// </summary>
    public static class UIControlHelper
    {
        /// <summary>
        /// 创建基础标签
        /// </summary>
        public static Label CreateLabel(string text, Point location, Size size,
            Font font = null, Color? foreColor = null,
            ContentAlignment textAlign = ContentAlignment.MiddleLeft,
            bool bold = false)
        {
            var label = new Label
            {
                Text = text,
                Location = location,
                Size = size,
                TextAlign = textAlign
            };

            if (font != null)
                label.Font = new Font(font.FontFamily, font.Size, bold ? FontStyle.Bold : FontStyle.Regular);

            if (foreColor.HasValue)
                label.ForeColor = foreColor.Value;

            return label;
        }
    }
}
