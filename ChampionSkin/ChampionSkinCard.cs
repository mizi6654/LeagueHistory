using League.Caching;
using League.Models;

namespace League.ChampionSkin
{
    /// <summary>
    /// 单个英雄卡片：上方预览 250x360，下方皮肤小格 50x50（可左右滑动）。
    /// 图片走 SkinBrowserImageCache；皮肤文件优先 .jpg，兼容旧 .png。
    /// </summary>
    public partial class ChampionSkinCard : UserControl
    {
        #region 尺寸常量

        private const int PreviewW = 250;
        private const int PreviewH = 360;
        private const int ThumbSize = 50;

        #endregion

        #region 字段

        private int _championId;
        private int _selectedSkinId;
        private int _defaultSkinId;
        private Panel? _selectedFrame;
        private SkinBrowserImageCache? _cache;

        public event Action<int, int>? PreferredSkinChanged;

        private static readonly Color SkinFrameNormal = Color.FromArgb(50, 50, 50);
        private static readonly Color SkinFrameSelected = Color.FromArgb(255, 40, 40);

        public void RefreshPreviewFromDisk() => ApplyHeroPreview();

        #endregion

        #region 构造 / 注入

        public ChampionSkinCard()
        {
            InitializeComponent();

            // 反射再兜一层（部分容器有效）
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(this, true);

            // 与设计器一致，防止被其它地方覆盖
            Width = 262;
            Height = 438;
            BorderStyle = BorderStyle.FixedSingle;
            Padding = new Padding(6);
            Margin = new Padding(8);

            InitSkinBar();
        }

        private void InitSkinBar()
        {
            if (panelSkinViewport == null || flpSkins == null) return;

            panelSkinViewport.AutoScroll = false;

            flpSkins.WrapContents = false;
            flpSkins.FlowDirection = FlowDirection.LeftToRight;
            flpSkins.AutoSize = true;
            flpSkins.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flpSkins.AutoScroll = false;
            flpSkins.Height = ThumbSize + 8;
            flpSkins.Top = 4;
            flpSkins.Left = 0;
            flpSkins.Padding = Padding.Empty;
            flpSkins.Margin = Padding.Empty;

            btnSkinLeft.Click -= BtnSkinLeft_Click;
            btnSkinRight.Click -= BtnSkinRight_Click;
            btnSkinLeft.Click += BtnSkinLeft_Click;
            btnSkinRight.Click += BtnSkinRight_Click;

            panelSkinViewport.MouseWheel -= PanelSkinViewport_MouseWheel;
            panelSkinViewport.MouseWheel += PanelSkinViewport_MouseWheel;

            UpdateSkinArrows();
        }

        private void BtnSkinLeft_Click(object? sender, EventArgs e) => ScrollSkins(-1);
        private void BtnSkinRight_Click(object? sender, EventArgs e) => ScrollSkins(+1);

        private void PanelSkinViewport_MouseWheel(object? sender, MouseEventArgs e)
        {
            ScrollSkins(e.Delta > 0 ? -1 : +1);
        }

        public void SetImageCache(SkinBrowserImageCache cache) => _cache = cache;

        public bool HasHeroImage => picHero != null && picHero.Image != null;
        public int ChampionId => _championId;

        /// <summary>优先 jpg，兼容 png。</summary>
        private static string? FindSkinFile(int skinId)
        {
            string dir = Path.Combine(Application.StartupPath, "Assets", "skins");
            string jpg = Path.Combine(dir, $"{skinId}.jpg");
            try
            {
                if (File.Exists(jpg) && new FileInfo(jpg).Length >= 256)
                    return jpg;
            }
            catch { }

            string png = Path.Combine(dir, $"{skinId}.png");
            try
            {
                if (File.Exists(png) && new FileInfo(png).Length >= 256)
                    return png;
            }
            catch { }

            return null;
        }

        #endregion

        #region 绑定

        public void Bind(ChampionSkinCardModel model)
        {
            if (model == null || picHero == null) return;

            _championId = model.ChampionId;
            _defaultSkinId = model.DefaultSkinId;

            bool prefOk = model.PreferredSkinId > 0
                && model.PreferredSkinId % 1000 != 0
                && model.Skins != null
                && model.Skins.Any(s => s.SkinId == model.PreferredSkinId);
            _selectedSkinId = prefOk ? model.PreferredSkinId : model.DefaultSkinId;

            picHero.Image = null;
            picHero.BackColor = Color.FromArgb(20, 20, 20);

            // 只建格子结构，不读盘
            BuildSkinThumbsShell(model.Skins);
            ApplySelectionHighlight();
            Tag = model.ChampionId;
        }

        /// <summary>只创建 50x50 空壳，不解码图片。</summary>
        private void BuildSkinThumbsShell(List<SkinThumbItem>? skins)
        {
            ClearSkinFrames();
            if (skins == null || skins.Count == 0)
            {
                UpdateSkinArrows(0);
                return;
            }

            var tip = new ToolTip { InitialDelay = 50, ReshowDelay = 50, AutoPopDelay = 5000 };

            foreach (var s in skins)
            {
                bool isBase = s.IsBase || (s.SkinId % 1000 == 0);
                var frame = new Panel
                {
                    Width = ThumbSize,
                    Height = ThumbSize,
                    Margin = new Padding(2),
                    Padding = new Padding(2),
                    BackColor = SkinFrameNormal,
                    Tag = s
                };
                var box = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BorderStyle = BorderStyle.None,
                    Cursor = Cursors.Hand,
                    BackColor = Color.FromArgb(30, 30, 30),
                    Tag = s
                };
                tip.SetToolTip(box, string.IsNullOrEmpty(s.Name)
                    ? (isBase ? "默认" : s.SkinId.ToString())
                    : s.Name);

                box.Click += SkinBox_Click;
                frame.Click += (_, _) => SkinBox_Click(box, EventArgs.Empty);
                frame.Controls.Add(box);
                flpSkins.Controls.Add(frame);
            }
            ResetSkinScroll();
        }

        public void RefreshPreference(int preferredSkinId)
        {
            bool ok = preferredSkinId > 0 && preferredSkinId % 1000 != 0;
            _selectedSkinId = ok ? preferredSkinId : _defaultSkinId;
            ApplySelectionHighlight();
            ApplyHeroPreview();
        }

        #endregion

        #region 英雄大图（统一 PreviewW x PreviewH）

        public void SetHeroImage(Image? image)
        {
            if (IsDisposed || image == null || picHero == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(() => SetHeroImage(image));
                return;
            }

            // 已有皮肤预览就别用头像盖掉
            if (picHero.Image != null) return;
            if (FindSkinFile(_selectedSkinId > 0 ? _selectedSkinId : _defaultSkinId) != null)
                return;

            // 可选：完全不把头像设进主预览，直接 return;
            // return;

            // 若仍想极弱占位，可保留缩放头像；更建议直接 return 保持深色底
        }

        private void ApplyHeroPreview()
        {
            if (picHero == null || _cache == null) return;

            int previewSkinId = (_selectedSkinId > 0 && _selectedSkinId % 1000 != 0)
                ? _selectedSkinId
                : _defaultSkinId;

            // 内存有图：只设引用，不解码 → 不卡拖动
            var hit = _cache.Get($"skin:{previewSkinId}:{PreviewW}x{PreviewH}");
            if (hit != null)
            {
                picHero.SizeMode = PictureBoxSizeMode.Zoom;
                picHero.Image = hit;
                return;
            }

            string? path = FindSkinFile(previewSkinId);
            if (path == null)
            {
                picHero.Image = null;
                return;
            }

            // 未命中：后台解码，避免 UI 卡顿
            string pathCopy = path;
            int id = previewSkinId;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Image? img = null;
                try
                {
                    img = SkinBrowserImageCache.LoadCroppedFromFile(pathCopy, PreviewW, PreviewH)
                       ?? SkinBrowserImageCache.LoadScaledFromFile(pathCopy, PreviewW, PreviewH);
                }
                catch { return; }
                if (img == null) return;

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed || picHero == null) return;
                        int cur = (_selectedSkinId > 0 && _selectedSkinId % 1000 != 0)
                            ? _selectedSkinId : _defaultSkinId;
                        if (cur != id) return;

                        var toUse = _cache?.GetOrLoad(
                            $"skin:{id}:{PreviewW}x{PreviewH}", () => img) ?? img;
                        picHero.SizeMode = PictureBoxSizeMode.Zoom;
                        picHero.Image = toUse;
                    }));
                }
                catch { }
            });
        }

        #endregion

        #region 皮肤小格

        /// <summary>
        /// direction: -1 内容向右移（看左边），+1 内容向左移（看右边）
        /// </summary>
        private void ScrollSkins(int direction)
        {
            if (flpSkins == null || panelSkinViewport == null) return;

            const int step = 110;
            int contentW = Math.Max(flpSkins.Width, flpSkins.PreferredSize.Width);
            int viewW = panelSkinViewport.Width;
            int max = Math.Max(0, contentW - viewW);

            int next = Math.Clamp(flpSkins.Left - direction * step, -max, 0);
            flpSkins.Left = next;
            UpdateSkinArrows(max);
        }

        private void UpdateSkinArrows(int maxScroll = -1)
        {
            if (btnSkinLeft == null || btnSkinRight == null || flpSkins == null || panelSkinViewport == null)
                return;

            int contentW = Math.Max(flpSkins.Width, flpSkins.PreferredSize.Width);
            if (maxScroll < 0)
                maxScroll = Math.Max(0, contentW - panelSkinViewport.Width);

            bool need = maxScroll > 0;
            btnSkinLeft.Enabled = need && flpSkins.Left < 0;
            btnSkinRight.Enabled = need && flpSkins.Left > -maxScroll;
        }

        private void ResetSkinScroll()
        {
            if (flpSkins == null) return;

            flpSkins.SuspendLayout();
            try
            {
                flpSkins.Left = 0;
                flpSkins.PerformLayout();

                int w = flpSkins.PreferredSize.Width;
                if (w > 0)
                    flpSkins.Width = w;
                flpSkins.Height = ThumbSize + 8;
            }
            finally
            {
                flpSkins.ResumeLayout(true);
            }

            UpdateSkinArrows();
        }

        private void ApplySelectionHighlight()
        {
            Panel? target = null;
            Panel? baseFrame = null;

            foreach (Control c in flpSkins.Controls)
            {
                if (c is not Panel frame || frame.Tag is not SkinThumbItem item) continue;
                frame.BackColor = SkinFrameNormal;
                frame.Padding = new Padding(2);
                if (item.SkinId == _selectedSkinId) target = frame;
                if (item.IsBase || item.SkinId % 1000 == 0) baseFrame = frame;
            }

            target ??= baseFrame;
            if (target == null) return;

            _selectedFrame = target;
            target.Padding = new Padding(3);
            target.BackColor = SkinFrameSelected;
            if (target.Tag is SkinThumbItem it)
                _selectedSkinId = it.SkinId;
        }

        private void SkinBox_Click(object? sender, EventArgs e)
        {
            if (sender is not PictureBox box) return;
            if (box.Tag is not SkinThumbItem item) return;

            _selectedSkinId = item.SkinId;

            if (box.Parent is Panel frame)
            {
                foreach (Control c in flpSkins.Controls)
                {
                    if (c is Panel p)
                    {
                        p.BackColor = SkinFrameNormal;
                        p.Padding = new Padding(2);
                    }
                }
                frame.BackColor = SkinFrameSelected;
                frame.Padding = new Padding(3);
            }

            bool isBase = item.IsBase || item.SkinId % 1000 == 0;

            if (_cache != null)
            {
                string? path = FindSkinFile(item.SkinId);
                if (path != null)
                {
                    picHero.Image = _cache.GetOrLoad(
                        $"skin:{item.SkinId}:{PreviewW}x{PreviewH}",
                        () => SkinBrowserImageCache.LoadCroppedFromFile(path, PreviewW, PreviewH)
                           ?? SkinBrowserImageCache.LoadScaledFromFile(path, PreviewW, PreviewH));
                }
                else
                {
                    picHero.Image = null;
                }
            }

            PreferredSkinChanged?.Invoke(_championId, isBase ? 0 : item.SkinId);
        }

        public void SetSkinImage(int skinId, Image? image)
        {
            if (IsDisposed || image == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(() => SetSkinImage(skinId, image));
                return;
            }

            var thumb = _cache != null
                ? _cache.GetOrLoad($"skin:{skinId}:{ThumbSize}",
                    () => SkinBrowserImageCache.CropScale(image, ThumbSize, ThumbSize)
                       ?? SkinBrowserImageCache.Scale(image, ThumbSize, ThumbSize))
                : image;

            foreach (Control c in flpSkins.Controls)
            {
                if (c is not Panel frame || frame.Tag is not SkinThumbItem item) continue;
                if (item.SkinId != skinId) continue;
                if (frame.Controls.Count > 0 && frame.Controls[0] is PictureBox pb)
                    pb.Image = thumb;
                break;
            }

            if (skinId != _selectedSkinId || _cache == null) return;

            // 大图已在内存：同步显示；否则后台 ApplyHeroPreview，不在这里解码
            var preview = _cache.Get($"skin:{skinId}:{PreviewW}x{PreviewH}");
            if (preview != null)
            {
                picHero.SizeMode = PictureBoxSizeMode.Zoom;
                picHero.Image = preview;
            }
            else
            {
                ApplyHeroPreview(); // 内部走 ThreadPool
            }
        }

        #endregion

        #region 清理

        private void ClearSkinFrames()
        {
            foreach (Control c in flpSkins.Controls.Cast<Control>().ToList())
                DisposeSkinFrame(c);
            flpSkins.Controls.Clear();
            _selectedFrame = null;

            ResetSkinScroll();
        }

        private void DisposeSkinFrame(Control c)
        {
            if (c is Panel frame)
            {
                foreach (Control child in frame.Controls)
                {
                    if (child is PictureBox pb)
                        pb.Image = null;
                    child.Dispose();
                }
            }
            c.Dispose();
        }

        public void ReleaseResources()
        {
            if (picHero != null)
                picHero.Image = null;
            ClearSkinFrames();
        }

        #endregion
    }
}