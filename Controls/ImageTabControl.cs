using System.Drawing.Drawing2D;

namespace League.Controls
{
    public class ImageTabControl : TabControl
    {
        private int _tabPadding = 1;
        private Size _imageSize = new Size(40, 40);
        private Color _separatorColor = Color.Gray;

        public ImageTabControl()
        {
            // 控件支持设计器
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

            // 设置 TabControl 样式
            Alignment = TabAlignment.Left;
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Fixed;

            ItemSize = new Size(_imageSize.Height + 10, _imageSize.Width + 10);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);

            // 绘制每个选项卡上的图片
            for (int i = 0; i < TabPages.Count; i++)
            {
                DrawTabImage(e.Graphics, i);
            }

            // 绘制分隔线
            DrawSeparatorLine(e.Graphics);

        }


        private void DrawTabImage(Graphics g, int index)
        {
            Rectangle tabRect = GetTabRect(index);
            bool isSelected = SelectedIndex == index;
            tabRect.Inflate(-_tabPadding, -_tabPadding);

            using (var brush = new SolidBrush(isSelected ? Color.White : Color.FromArgb(230, 230, 230)))
            {
                g.FillRectangle(brush, tabRect);
            }

            // BorderStyle.FixedSingle → 黑色、1像素边框
            //using (var pen = new Pen(Color.Lime, 1))
            //{
            //    g.DrawRectangle(pen, tabRect);
            //}

            Image img = GetTabImage(TabPages[index]);
            if (img != null)
            {
                float ratioX = (float)tabRect.Width / img.Width;
                float ratioY = (float)tabRect.Height / img.Height;

                float ratio = Math.Min(ratioX, ratioY);

                int newWidth = (int)(img.Width * ratio);
                int newHeight = (int)(img.Height * ratio);

                int offsetX = tabRect.X + (tabRect.Width - newWidth) / 2;
                int offsetY = tabRect.Y + (tabRect.Height - newHeight) / 2;

                Rectangle imgRect = new Rectangle(offsetX, offsetY, newWidth, newHeight);

                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                g.DrawImage(img, imgRect);
            }

            // 加灰色蒙层
            if (!isSelected)
            {
                using (var overlay = new SolidBrush(Color.FromArgb(120, Color.Gray)))
                {
                    g.FillRectangle(overlay, tabRect);
                }
            }

            // 选中 tab 可以画高亮边框
            if (isSelected)
            {
                using (var pen = new Pen(Color.DodgerBlue, 1))
                {
                    g.DrawRectangle(pen, tabRect);
                }
            }
            else
            {
                using (var pen = new Pen(Color.DarkGray, 1))
                {
                    g.DrawRectangle(pen, tabRect);
                }
            }
        }


        private Image GetTabImage(TabPage page)
        {
            return page.Tag as Image;
        }

        private void DrawSeparatorLine(Graphics g)
        {
            int x = GetTabRect(0).Right + _tabPadding;
            using (var pen = new Pen(_separatorColor, 1))
            {
                g.DrawLine(pen, x, 0, x, Height);
            }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            // 忽略默认绘制
        }
    }
}
