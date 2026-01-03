using System.Drawing.Drawing2D;

namespace League.Controls
{
    public class ClosableTabControl : TabControl
    {
        private const int CloseButtonSize = 12;
        private const int CloseButtonMargin = 5;

        private Dictionary<int, Rectangle> _closeButtonBounds = new Dictionary<int, Rectangle>();
        public ClosableTabControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Normal;
            Appearance = TabAppearance.Normal;

            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Normal;
            Padding = new Point(20, 4); // 标签标题内边距，避免文字与叉重叠

            // 立即触发重绘
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(SystemColors.Control);
            _closeButtonBounds.Clear();

            for (int i = 0; i < TabCount; i++)
            {
                Rectangle tabRect = GetTabRect(i);
                bool isSelected = i == SelectedIndex;

                // 在OnPaint方法中修改选中标签页样式
                if (isSelected)
                {
                    using (var brush = new LinearGradientBrush(tabRect, Color.White, Color.LightBlue, 45f))
                        e.Graphics.FillRectangle(brush, tabRect);
                }
                else
                {
                    using (var brush = new SolidBrush(Color.FromArgb(240, 240, 240)))
                        e.Graphics.FillRectangle(brush, tabRect);
                }

                Color backColor = isSelected ? Color.White : Color.LightGray;
                Color borderColor = Color.Gray;

                using (Brush b = new SolidBrush(backColor))
                    e.Graphics.FillRectangle(b, tabRect);

                using (Pen p = new Pen(borderColor))
                    e.Graphics.DrawRectangle(p, tabRect);

                // 绘制文字
                string tabText = TabPages[i].Text;
                Rectangle textRect = new Rectangle(tabRect.X + 5, tabRect.Y + 4, tabRect.Width - CloseButtonSize - 10, tabRect.Height - 8);
                TextRenderer.DrawText(e.Graphics, tabText, Font, textRect, Color.Black, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

                // 绘制关闭按钮
                Rectangle closeRect = new Rectangle(tabRect.Right - CloseButtonSize - 4, tabRect.Top + (tabRect.Height - CloseButtonSize) / 2, CloseButtonSize, CloseButtonSize);
                _closeButtonBounds[i] = closeRect;

                using (Pen pen = new Pen(Color.DarkGray, 2))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.DrawLine(pen, closeRect.Left + 3, closeRect.Top + 3, closeRect.Right - 3, closeRect.Bottom - 3);
                    e.Graphics.DrawLine(pen, closeRect.Right - 3, closeRect.Top + 3, closeRect.Left + 3, closeRect.Bottom - 3);
                }
            }

            // 绘制标签页内容区域边框
            // 在 OnPaint 方法中添加选中项检查
            if (SelectedIndex >= 0 && SelectedIndex < TabPages.Count)
            {
                // 绘制标签页内容区域边框
                Rectangle pageBounds = TabPages[SelectedIndex].Bounds;
                using (Pen borderPen = new Pen(Color.Gray))
                {
                    e.Graphics.DrawRectangle(borderPen,
                        pageBounds.X - 2,
                        pageBounds.Y - 2,
                        pageBounds.Width + 3,
                        pageBounds.Height + 3);
                }
                using (Pen borderPen = new Pen(Color.Gray))
                {
                    e.Graphics.DrawRectangle(borderPen, pageBounds.X - 2, pageBounds.Y - 2, pageBounds.Width + 3, pageBounds.Height + 3);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            for (int i = 0; i < TabCount; i++)
            {
                if (_closeButtonBounds.TryGetValue(i, out var rect) && rect.Contains(e.Location))
                {
                    if (i == 0)
                    {
                        MessageBox.Show("此选项卡为自己数据，不能关闭", "提示", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                    }
                    else
                    {
                        TabPages.RemoveAt(i);
                    }
                    break;
                }
            }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            base.OnDrawItem(e);

            var tabRect = GetTabRect(e.Index);
            var tabPage = TabPages[e.Index];

            // 绘制标签文字
            TextRenderer.DrawText(
                e.Graphics,
                tabPage.Text,
                Font,
                tabRect,
                tabPage.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // 绘制关闭按钮
            Rectangle closeRect = GetCloseButtonRect(tabRect);
            e.Graphics.DrawRectangle(Pens.DarkGray, closeRect);
            e.Graphics.DrawLine(Pens.Black, closeRect.Left + 3, closeRect.Top + 3, closeRect.Right - 3, closeRect.Bottom - 3);
            e.Graphics.DrawLine(Pens.Black, closeRect.Right - 3, closeRect.Top + 3, closeRect.Left + 3, closeRect.Bottom - 3);
        }

        private Rectangle GetCloseButtonRect(Rectangle tabRect)
        {
            return new Rectangle(
                tabRect.Right - CloseButtonSize - CloseButtonMargin,
                tabRect.Top + (tabRect.Height - CloseButtonSize) / 2,
                CloseButtonSize,
                CloseButtonSize);
        }
    }
}
