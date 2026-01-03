using System.ComponentModel;

namespace League.Controls
{
    public class StyledSplitContainer : SplitContainer
    {
        private Color _splitterColor = Color.FromArgb(200, 200, 200); // 你想要的灰白色

        [Category("自定义样式")]
        [Description("设置分隔线颜色")]
        public Color SplitterColor
        {
            get { return _splitterColor; }
            set { _splitterColor = value; Invalidate(); }
        }

        public StyledSplitContainer()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Rectangle rect;
            if (Orientation == Orientation.Horizontal)
                rect = new Rectangle(0, SplitterDistance, Width, SplitterWidth);
            else
                rect = new Rectangle(SplitterDistance, 0, SplitterWidth, Height);

            using (SolidBrush brush = new SolidBrush(SplitterColor))
            {
                e.Graphics.FillRectangle(brush, rect);
            }
        }
    }
}