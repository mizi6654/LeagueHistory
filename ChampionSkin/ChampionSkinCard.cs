using League.Caching;
using League.Models;
using System.Diagnostics;

namespace League.ChampionSkin
{
    /// <summary>
    /// 单个英雄卡片：上方大图，下方皮肤小格（金黄框为当前选中）。
    /// 图片走 SkinBrowserImageCache；皮肤文件优先 .jpg，兼容旧 .png。
    /// </summary>
    public partial class ChampionSkinCard : UserControl
    {
        #region 字段

        private int _championId;
        private int _selectedSkinId;
        private int _defaultSkinId;
        private Panel? _selectedFrame;
        private SkinBrowserImageCache? _cache;

        public event Action<int, int>? PreferredSkinChanged;

        private static readonly Color SkinFrameNormal = Color.FromArgb(50, 50, 50);
        private static readonly Color SkinFrameSelected = Color.FromArgb(255, 40, 40); // 亮红

        #endregion

        #region 构造 / 注入

        public ChampionSkinCard()
        {
            InitializeComponent();
            Width = 158;
            Height = 215;
            BorderStyle = BorderStyle.FixedSingle;
            Padding = new Padding(4);
            Margin = new Padding(6);

            InitSkinBar();
        }

        private void InitSkinBar()
        {
            // 视口不滚动
            panelSkinViewport.AutoScroll = false;

            // 皮肤条：单行、随内容变宽
            flpSkins.WrapContents = false;
            flpSkins.FlowDirection = FlowDirection.LeftToRight;
            flpSkins.AutoSize = true;
            flpSkins.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flpSkins.AutoScroll = false;
            flpSkins.Height = 40;
            flpSkins.Top = 4;
            flpSkins.Left = 0;
            flpSkins.Padding = Padding.Empty;
            flpSkins.Margin = Padding.Empty;

            btnSkinLeft.Click -= BtnSkinLeft_Click;
            btnSkinRight.Click -= BtnSkinRight_Click;
            btnSkinLeft.Click += BtnSkinLeft_Click;
            btnSkinRight.Click += BtnSkinRight_Click;

            // 可选：滚轮左右滑（鼠标在视口上时）
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
            if (model == null) return;
            if (picHero == null)
            {
                Debug.WriteLine("[ChampionSkinCard] picHero 为 null，检查 Designer");
                return;
            }

            _championId = model.ChampionId;
            _defaultSkinId = model.DefaultSkinId;

            bool prefOk = model.PreferredSkinId > 0
                && model.PreferredSkinId % 1000 != 0
                && model.Skins != null
                && model.Skins.Any(s => s.SkinId == model.PreferredSkinId);
            _selectedSkinId = prefOk ? model.PreferredSkinId : model.DefaultSkinId;

            picHero.Image = null;
            LoadHeroIcon(model);
            BuildSkinThumbs(model.Skins);
            ApplySelectionHighlight();
            ApplyHeroPreview();
            Tag = model.ChampionId;
        }

        public void RefreshPreference(int preferredSkinId)
        {
            bool ok = preferredSkinId > 0 && preferredSkinId % 1000 != 0;
            _selectedSkinId = ok ? preferredSkinId : _defaultSkinId;
            ApplySelectionHighlight();
            ApplyHeroPreview();
        }

        #endregion

        #region 英雄大图

        private void LoadHeroIcon(ChampionSkinCardModel model)
        {
            if (picHero == null || _cache == null) return;

            var img = _cache.GetOrLoad($"hero:{model.ChampionId}", () =>
            {
                string dir = Path.Combine(Application.StartupPath, "Assets", "champion");
                foreach (var p in new[]
                {
                    Path.Combine(dir, $"{model.ChampionId}.png"),
                    Path.Combine(dir, $"{(model.Name ?? "").Replace(" ", "").Replace("'", "")}.png"),
                    Path.Combine(dir, $"{model.Alias}.png"),
                })
                {
                    var scaled = SkinBrowserImageCache.LoadScaledFromFile(p, 150, 150);
                    if (scaled != null) return scaled;
                }
                return null;
            });

            if (img == null) return;
            picHero.SizeMode = PictureBoxSizeMode.Zoom;
            picHero.Image = img;
        }

        public void SetHeroImage(Image? image)
        {
            if (IsDisposed || image == null || picHero == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(() => SetHeroImage(image));
                return;
            }
            if (picHero.Image != null) return;

            if (_cache != null)
            {
                var img = _cache.GetOrLoad($"hero:{_championId}",
                    () => SkinBrowserImageCache.Scale(image, 150, 150));
                picHero.SizeMode = PictureBoxSizeMode.Zoom;
                picHero.Image = img;
            }
            else
            {
                picHero.SizeMode = PictureBoxSizeMode.Zoom;
                picHero.Image = (Image)image.Clone();
            }

            if (_selectedSkinId % 1000 == 0)
                ApplyHeroPreview();
        }

        private void ApplyHeroPreview()
        {
            if (picHero == null) return;

            bool useSkin = _selectedSkinId > 0 && _selectedSkinId % 1000 != 0;
            if (useSkin && _cache != null)
            {
                string? path = FindSkinFile(_selectedSkinId);
                if (path != null)
                {
                    var skinImg = _cache.GetOrLoad($"skin:{_selectedSkinId}:150", () =>
                        SkinBrowserImageCache.LoadScaledFromFile(path, 150, 150));
                    if (skinImg != null)
                    {
                        picHero.SizeMode = PictureBoxSizeMode.Zoom;
                        picHero.Image = skinImg;
                        return;
                    }
                }
            }

            picHero.Image = _cache?.Get($"hero:{_championId}");
        }

        #endregion

        #region 皮肤小格

        private void BuildSkinThumbs(List<SkinThumbItem>? skins)
        {
            ClearSkinFrames();
            if (skins == null || skins.Count == 0)
            {
                UpdateSkinArrows(0);
                return;
            }

            string champDir = Path.Combine(Application.StartupPath, "Assets", "champion");
            var tip = new ToolTip { InitialDelay = 50, ReshowDelay = 50, AutoPopDelay = 5000 };

            foreach (var s in skins)
            {
                bool isBase = s.IsBase || (s.SkinId % 1000 == 0);
                var frame = new Panel
                {
                    Width = 28,
                    Height = 28,
                    Margin = new Padding(1),
                    Padding = new Padding(2),
                    BackColor = Color.FromArgb(50, 50, 50),
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

                if (isBase && _cache != null)
                {
                    box.Image = _cache.GetOrLoad($"hero:{_championId}:31", () =>
                    {
                        var full = _cache.Get($"hero:{_championId}");
                        if (full != null)
                            return SkinBrowserImageCache.Scale(full, 31, 31);
                        return SkinBrowserImageCache.LoadScaledFromFile(
                            Path.Combine(champDir, $"{_championId}.png"), 31, 31);
                    });
                }
                else if (_cache != null)
                {
                    box.Image = _cache.GetOrLoad($"skin:{s.SkinId}:31", () =>
                    {
                        string? p = FindSkinFile(s.SkinId);
                        return p == null
                            ? null
                            : SkinBrowserImageCache.LoadScaledFromFile(p, 31, 31);
                    });
                }

                box.Click += SkinBox_Click;
                frame.Click += (_, _) => SkinBox_Click(box, EventArgs.Empty);
                frame.Controls.Add(box);
                flpSkins.Controls.Add(frame);
            }
            // 加完后复位滚动并更新箭头
            ResetSkinScroll();
        }

        /// <summary>
        /// direction: -1 内容向右移（看左边），+1 内容向左移（看右边）
        /// </summary>
        private void ScrollSkins(int direction)
        {
            if (flpSkins == null || panelSkinViewport == null) return;

            const int step = 64; // 大约两格
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

        /// <summary>格子加完后复位并刷新箭头</summary>
        private void ResetSkinScroll()
        {
            if (flpSkins == null) return;

            flpSkins.SuspendLayout();
            try
            {
                flpSkins.Left = 0;
                flpSkins.PerformLayout();

                // AutoSize 有时 Width 仍为 0，用 PreferredSize 兜底
                int w = flpSkins.PreferredSize.Width;
                if (w > 0)
                    flpSkins.Width = w;
                flpSkins.Height = 40;
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
                frame.Padding = new Padding(1);
                if (item.SkinId == _selectedSkinId) target = frame;
                if (item.IsBase || item.SkinId % 1000 == 0) baseFrame = frame;
            }

            target ??= baseFrame;
            if (target == null) return;

            _selectedFrame = target;
            target.Padding = new Padding(3);          // 加粗
            target.BackColor = SkinFrameSelected;     // 亮红
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
                        p.Padding = new Padding(1);
                    }
                }
                frame.BackColor = SkinFrameSelected;
                frame.Padding = new Padding(3);
            }

            bool isBase = item.IsBase || item.SkinId % 1000 == 0;
            if (isBase)
            {
                picHero.Image = _cache?.Get($"hero:{_championId}");
                PreferredSkinChanged?.Invoke(_championId, 0);
            }
            else
            {
                if (_cache != null)
                {
                    string? path = FindSkinFile(item.SkinId);
                    picHero.Image = path == null
                        ? null
                        : _cache.GetOrLoad($"skin:{item.SkinId}:150", () =>
                            SkinBrowserImageCache.LoadScaledFromFile(path, 150, 150));
                }
                PreferredSkinChanged?.Invoke(_championId, item.SkinId);
            }
        }

        public void SetSkinImage(int skinId, Image? image)
        {
            if (IsDisposed || image == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(() => SetSkinImage(skinId, image));
                return;
            }

            Image? thumb = _cache != null
                ? _cache.GetOrLoad($"skin:{skinId}:31",
                    () => SkinBrowserImageCache.Scale(image, 31, 31))
                : SkinBrowserImageCache.Scale(image, 31, 31);

            foreach (Control c in flpSkins.Controls)
            {
                if (c is not Panel frame || frame.Tag is not SkinThumbItem item) continue;
                if (item.SkinId != skinId) continue;

                if (frame.Controls.Count > 0 && frame.Controls[0] is PictureBox pb)
                    pb.Image = thumb;

                if (_selectedSkinId == skinId && _cache != null)
                {
                    string? path = FindSkinFile(skinId);
                    picHero.Image = _cache.GetOrLoad($"skin:{skinId}:150", () =>
                    {
                        if (path != null)
                        {
                            var fromFile = SkinBrowserImageCache.LoadScaledFromFile(path, 150, 150);
                            if (fromFile != null) return fromFile;
                        }
                        return SkinBrowserImageCache.Scale(image, 150, 150);
                    });
                }
                break;
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