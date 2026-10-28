namespace League.UIState
{
    public partial class ProgressForm : Form
    {
        private Label lblStatus;
        private ProgressBar pbProgress;
        private string _sourceName = "Gitee / GitHub";

        public ProgressForm(string sourceName = null)
        {
            if (!string.IsNullOrEmpty(sourceName))
                _sourceName = sourceName;

            InitializeComponent();

            // 初始提示（动态显示来源）
            lblStatus.Text = $"正在从 {_sourceName} 下载更新包...\r\n" +
                             "(网络环境不同，下载速度可能较慢，请耐心等待)\r\n" +
                             "请勿关闭此窗口";
            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(520, 120);
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Font = new Font("Microsoft YaHei", 11F, FontStyle.Regular);
        }

        private void InitializeComponent()
        {
            this.Text = "更新中...";
            this.Size = new Size(580, 260);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.White;

            lblStatus = new Label
            {
                Location = new Point(30, 30),
                ForeColor = Color.DarkBlue
            };
            this.Controls.Add(lblStatus);

            pbProgress = new ProgressBar
            {
                Location = new Point(30, 160),
                Size = new Size(520, 40),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 40
            };
            this.Controls.Add(pbProgress);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CenterToOwner();
        }

        private void CenterToOwner()
        {
            if (Owner == null) return;

            int x = Owner.Location.X + (Owner.Width - this.Width) / 2;
            int y = Owner.Location.Y + (Owner.Height - this.Height) / 2;

            var screen = Screen.FromControl(Owner);
            x = Math.Max(screen.WorkingArea.Left, Math.Min(x, screen.WorkingArea.Right - this.Width));
            y = Math.Max(screen.WorkingArea.Top, Math.Min(y, screen.WorkingArea.Bottom - this.Height));

            this.Location = new Point(x, y);
        }

        public void UpdateStatus(string message, int? percent = null)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateStatus(message, percent)));
                return;
            }

            lblStatus.Text = message;

            if (percent.HasValue)
            {
                pbProgress.Style = ProgressBarStyle.Blocks;
                pbProgress.Value = Math.Clamp(percent.Value, 0, 100);
            }
            else
            {
                pbProgress.Style = ProgressBarStyle.Marquee;
            }

            this.Refresh();
        }

        public void Complete(string message = "更新完成，正在重启...")
        {
            UpdateStatus(message, 100);

            Task.Delay(1500).ContinueWith(_ =>
            {
                if (!this.IsDisposed)
                {
                    if (this.InvokeRequired)
                        this.Invoke(new Action(() => this.Close()));
                    else
                        this.Close();
                }
            });
        }
    }
}