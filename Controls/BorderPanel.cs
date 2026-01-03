namespace League.Controls
{
    public class BorderPanel : Panel
    {
        public Color BorderColor { get; set; } = Color.Black;
        public int BorderWidth { get; set; } = 2;

        public BorderPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(BorderColor, BorderWidth))
            {
                pen.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset; // 关键！
                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
                e.Graphics.DrawRectangle(pen, rect);
            }
        }
    }
}
