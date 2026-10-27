using League.Caching;
using League.Models;
using League.PrimaryElection;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Drawing.Imaging;
using static League.FormMain;

namespace League.ChampionSkin
{
    /// <summary>
    /// 英雄皮肤浏览器：分路 / 搜索 / 已拥有 / 有皮肤过滤。
    /// 卡片池只建一次；仅缓存当前账号已拥有的非默认皮。
    /// 写盘：最长边 128 + JPEG，减小 Assets/skins 体积；UI 线程再解码。
    /// </summary>
    public partial class ChampionSkinBrowser : Form
    {
        #region 字段

        private readonly LeagueConfig _config;
        private ChampionManager _championManager = null!;
        private List<ChampionSkinCardModel> _allModels = new();
        private ChampionPosition _currentPosition = ChampionPosition.All;
        private bool _dataReady;
        private bool _isRendering;

        private SkinBrowserImageCache _imgCache = new();
        private readonly Dictionary<int, ChampionSkinCard> _cardPool = new();
        private System.Windows.Forms.Timer? _searchDebounce;
        private CancellationTokenSource? _loadCts;

        private readonly SemaphoreSlim _skinDownloadLock = new(8, 8);

        /// <summary>250×360 裁切预览用 560～600 一般够；再低可能略糊。千皮肤约几十 MB。</summary>
        private const int SkinCacheMaxEdge = 560;

        /// <summary>JPEG 质量 70～78 更省空间。</summary>
        private const long JpegQuality = 85L;

        /// <summary>同一 skinId 同时只允许一条下载，避免 Move 冲突。</summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> _skinIdLocks = new();

        private readonly object _pendingUiLock = new();
        private readonly List<Action> _pendingUi = new();
        private System.Windows.Forms.Timer? _uiFlushTimer;

        #endregion

        #region 构造

        public ChampionSkinBrowser(LeagueConfig config)
        {
            InitializeComponent();

            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(flpCards, true);

            // 窗体可选
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();

            _uiFlushTimer = new System.Windows.Forms.Timer { Interval = 120 };
            _uiFlushTimer.Tick += (_, _) => FlushPendingUi();
            _uiFlushTimer.Start();

            _config = config ?? throw new ArgumentNullException(nameof(config));

            if (txtSearch != null)
                txtSearch.PlaceholderText = "搜索英雄（中文/称号/英文）";

            chkOwnedOnly.Checked = _config.SkinBrowserOwnedOnly;
            if (chkHasSkinOnly != null)
                chkHasSkinOnly.Checked = _config.SkinBrowserHasSkinOnly;

            BindUiEvents();
            Load += async (_, _) => await InitializeAsync();
            FormClosing += ChampionSkinBrowser_FormClosing;
        }

        #endregion

        #region UI 事件

        private void BindUiEvents()
        {
            btnAll.Click += (_, _) => OnPositionClick(ChampionPosition.All, btnAll);
            btnTop.Click += (_, _) => OnPositionClick(ChampionPosition.Top, btnTop);
            btnJungle.Click += (_, _) => OnPositionClick(ChampionPosition.Jungle, btnJungle);
            btnMid.Click += (_, _) => OnPositionClick(ChampionPosition.Mid, btnMid);
            btnAdc.Click += (_, _) => OnPositionClick(ChampionPosition.ADC, btnAdc);
            btnSupport.Click += (_, _) => OnPositionClick(ChampionPosition.Support, btnSupport);
            BtnClearPrefs.Click += BtnClearPrefs_Click;

            _searchDebounce = new System.Windows.Forms.Timer { Interval = 280 };
            _searchDebounce.Tick += (_, _) =>
            {
                _searchDebounce!.Stop();
                ApplyFilterAndRender();
            };
            txtSearch.TextChanged += (_, _) =>
            {
                _searchDebounce!.Stop();
                _searchDebounce.Start();
            };

            chkOwnedOnly.CheckedChanged += (_, _) =>
            {
                _config.SkinBrowserOwnedOnly = chkOwnedOnly.Checked;
                ApplyFilterAndRender();
            };
            if (chkHasSkinOnly != null)
            {
                chkHasSkinOnly.CheckedChanged += (_, _) =>
                {
                    _config.SkinBrowserHasSkinOnly = chkHasSkinOnly.Checked;
                    ApplyFilterAndRender();
                };
            }
        }

        private void OnPositionClick(ChampionPosition pos, Button btn)
        {
            _currentPosition = pos;
            HighlightPositionButton(btn);
            ApplyFilterAndRender();
        }

        private void HighlightPositionButton(Button active)
        {
            foreach (var b in new[] { btnAll, btnTop, btnJungle, btnMid, btnAdc, btnSupport })
            {
                b.BackColor = SystemColors.Control;
                b.ForeColor = SystemColors.ControlText;
                b.Font = new Font(b.Font, FontStyle.Regular);
            }
            active.BackColor = Color.FromArgb(0, 120, 215);
            active.ForeColor = Color.White;
            active.Font = new Font(active.Font, FontStyle.Bold);
        }

        private void SetBusy(bool busy)
        {
            UseWaitCursor = busy;
            btnAll.Enabled = btnTop.Enabled = btnJungle.Enabled =
                btnMid.Enabled = btnAdc.Enabled = btnSupport.Enabled = !busy;
            txtSearch.Enabled = !busy;
            chkOwnedOnly.Enabled = !busy;
            if (chkHasSkinOnly != null) chkHasSkinOnly.Enabled = !busy;
            BtnClearPrefs.Enabled = !busy;
        }

        #endregion

        #region 初始化 / 库存

        private async Task InitializeAsync()
        {
            // 开头，加载完成前先藏起 flpCards
            flpCards.Visible = false;
            flpCards.SuspendLayout();

            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            try
            {
                SetBusy(true);
                lblStats.Text = "正在加载英雄数据...";

                _championManager = Globals.resLoading.ChampionManager;
                await _championManager.InitializeAsync(forceRefresh: false);

                if (_championManager.AllChampions.Count == 0)
                {
                    lblStats.Text = "未加载到英雄数据";
                    return;
                }

                var ownedSkinsByChamp = await LoadOwnedSkinsAsync();
                _config.PreferredChampionSkins ??= new Dictionary<int, int>();
                _allModels.Clear();

                foreach (var champ in _championManager.AllChampions.OrderBy(c => c.Name))
                {
                    int defaultSkinId = champ.Id * 1000;
                    ownedSkinsByChamp.TryGetValue(champ.Id, out var skinList);
                    skinList ??= new List<SkinThumbItem>();

                    if (!skinList.Any(s => s.IsBase))
                    {
                        skinList.Insert(0, new SkinThumbItem
                        {
                            SkinId = defaultSkinId,
                            Name = "默认",
                            IsBase = true
                        });
                    }

                    bool isOwned = ownedSkinsByChamp.ContainsKey(champ.Id);
                    int preferred = 0;
                    if (_config.PreferredChampionSkins.TryGetValue(champ.Id, out int pref)
                        && pref > 0 && pref % 1000 != 0)
                        preferred = pref;

                    _allModels.Add(new ChampionSkinCardModel
                    {
                        ChampionId = champ.Id,
                        Name = champ.Name,
                        Title = champ.Title,
                        Alias = champ.Alias,
                        IsOwned = isOwned,
                        DefaultSkinId = defaultSkinId,
                        PreferredSkinId = preferred,
                        Skins = skinList.OrderBy(s => s.IsBase ? 0 : 1).ThenBy(s => s.SkinId).ToList()
                    });
                }

                _dataReady = true;
                HighlightPositionButton(btnAll);
                EnsureCardPool();          // 只建空壳
                ApplyFilterAndRender();    // 显示可见卡，发起本地上图（不 await 下载）

                flpCards.ResumeLayout(true);
                flpCards.Visible = true;
                SetBusy(false);            // 立刻结束转圈 —— 必须在 Preload 之前

                _ = LoadSkinThumbsAsync();
                // 纯后台，不要 await
                _ = PreloadAllOwnedSkinImagesAsync(ownedSkinsByChamp, token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChampionSkinBrowser] 初始化失败: {ex}");
                MessageBox.Show($"加载失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task<Dictionary<int, List<SkinThumbItem>>> LoadOwnedSkinsAsync()
        {
            var result = new Dictionary<int, List<SkinThumbItem>>();
            try
            {
                var summoner = await Globals.lcuClient.GetCurrentSummoner();
                string? sid = summoner?["summonerId"]?.ToString();
                if (string.IsNullOrEmpty(sid))
                {
                    Debug.WriteLine("[ChampionSkinBrowser] 无 summonerId");
                    return result;
                }

                var http = Globals.lcuClient.Client
                    ?? throw new InvalidOperationException("LCU Client 未就绪");

                var resp = await http.GetAsync($"lol-champions/v1/inventories/{sid}/skins-minimal");
                string body = await resp.Content.ReadAsStringAsync();
                Debug.WriteLine(
                    $"[ChampionSkinBrowser] skins-minimal status={resp.StatusCode} len={body?.Length}");
                if (!resp.IsSuccessStatusCode) return result;

                foreach (var t in JArray.Parse(body))
                {
                    if (t is not JObject o) continue;
                    if (!(o["ownership"]?["owned"]?.Value<bool>() ?? false)) continue;

                    int skinId = o["id"]?.Value<int>() ?? 0;
                    int champId = o["championId"]?.Value<int>() ?? 0;
                    if (champId <= 0 && skinId > 0) champId = skinId / 1000;
                    if (champId <= 0) continue;

                    bool isBase = o["isBase"]?.Value<bool>() ?? (skinId % 1000 == 0);
                    string name = o["name"]?.ToString() ?? (isBase ? "默认" : skinId.ToString());

                    // 优先 splash / loadScreen，避免先拿到方图 tile
                    string? assetPath =
                        o["splashPath"]?.ToString()
                        ?? o["uncenteredSplashPath"]?.ToString()
                        ?? o["loadScreenPath"]?.ToString()
                        ?? o["tilePath"]?.ToString()
                        ?? o["tileUrl"]?.ToString();

                    if (!result.TryGetValue(champId, out var list))
                    {
                        list = new List<SkinThumbItem>();
                        result[champId] = list;
                    }
                    if (list.Any(x => x.SkinId == skinId)) continue;

                    list.Add(new SkinThumbItem
                    {
                        SkinId = skinId,
                        Name = name,
                        IsBase = isBase,
                        TilePath = string.IsNullOrWhiteSpace(assetPath) ? null : assetPath.Trim()
                    });
                }

                int nonBase = result.Values.Sum(l => l.Count(s => !s.IsBase && s.SkinId % 1000 != 0));
                Debug.WriteLine($"[ChampionSkinBrowser] 英雄数={result.Count}, 非默认皮肤={nonBase}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChampionSkinBrowser] LoadOwnedSkinsAsync: {ex.Message}");
            }
            return result;
        }

        #endregion

        #region 卡片池 / 过滤

        private void EnsureCardPool()
        {
            if (_cardPool.Count > 0) return;

            flpCards.SuspendLayout();
            try
            {
                foreach (var model in _allModels)
                {
                    var card = new ChampionSkinCard();
                    card.SetImageCache(_imgCache);
                    card.Bind(model);
                    card.PreferredSkinChanged += OnPreferredSkinChanged;
                    card.Visible = false;
                    _cardPool[model.ChampionId] = card;
                    //flpCards.Controls.Add(card);  // 确认不要预先全加进 flpCards
                }
            }
            finally
            {
                flpCards.ResumeLayout(true);
            }
        }

        private void ApplyFilterAndRender()
        {
            if (!_dataReady || _isRendering) return;
            _isRendering = true;
            try
            {
                EnsureCardPool();

                // 筛选一变，丢掉还没刷完的旧 UI 任务，避免和拖动抢消息
                lock (_pendingUiLock)
                    _pendingUi.Clear();

                string keyword = txtSearch.Text?.Trim() ?? "";
                bool ownedOnly = chkOwnedOnly.Checked;
                bool hasSkinOnly = chkHasSkinOnly?.Checked ?? false;

                IEnumerable<ChampionSkinCardModel> q = _allModels;

                if (_currentPosition != ChampionPosition.All)
                {
                    q = q.Where(m =>
                    {
                        var c = _championManager.GetChampionById(m.ChampionId);
                        return c != null && c.GetPositions().Contains(_currentPosition);
                    });
                }

                if (ownedOnly)
                    q = q.Where(m => m.IsOwned);

                if (hasSkinOnly)
                    q = q.Where(m => m.Skins.Any(s => !s.IsBase && s.SkinId % 1000 != 0));

                if (!string.IsNullOrEmpty(keyword))
                {
                    q = q.Where(m =>
                        m.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        m.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        m.Alias.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                }

                var list = q.OrderBy(m => m.Name).ToList();

                flpCards.SuspendLayout();
                try
                {
                    // 关键：只把「当前列表」放进容器，其它卡留在池子里但不参与布局
                    flpCards.Controls.Clear();

                    foreach (var model in list)
                    {
                        if (!_cardPool.TryGetValue(model.ChampionId, out var card)) continue;
                        card.Visible = true;
                        flpCards.Controls.Add(card);
                    }
                }
                finally
                {
                    flpCards.ResumeLayout(true);
                }

                int withSkin = list.Count(m => m.Skins.Any(s => !s.IsBase && s.SkinId % 1000 != 0));
                lblStats.Text = $"共 {list.Count} 英雄，其中 {withSkin} 个有皮肤";

                _ = LoadHeroIconsAsync();
                _ = LoadSkinThumbsAsync();
            }
            finally
            {
                _isRendering = false;
            }
        }

        #endregion

        #region 英雄头像补图

        private async Task LoadHeroIconsAsync()
        {
            foreach (Control c in flpCards.Controls)
            {
                if (c is not ChampionSkinCard card || !card.Visible) continue;
                if (card.Tag is not int champId) continue;
                if (card.HasHeroImage) continue;
                try
                {
                    var img = await Globals.resLoading.GetChampionIconAsync(champId);
                    if (img != null && !card.IsDisposed)
                        card.SetHeroImage(img);
                }
                catch { }
            }
        }

        #endregion

        #region 皮肤路径 / 清理

        private static string SkinCacheDir =>
            Path.Combine(Application.StartupPath, "Assets", "skins");

        private static string SkinJpgPath(string cacheDir, int skinId) =>
            Path.Combine(cacheDir, $"{skinId}.jpg");

        private static bool IsValidSkinFile(string path)
        {
            try { return File.Exists(path) && new FileInfo(path).Length >= 256; }
            catch { return false; }
        }

        /// <summary>优先 .jpg，兼容旧 .png。</summary>
        private static string? ResolveSkinFile(string cacheDir, int skinId)
        {
            string jpg = SkinJpgPath(cacheDir, skinId);
            if (IsValidSkinFile(jpg)) return jpg;
            string png = Path.Combine(cacheDir, $"{skinId}.png");
            if (IsValidSkinFile(png)) return png;
            return null;
        }

        private static void CleanupCorruptSkinCache(string cacheDir)
        {
            if (!Directory.Exists(cacheDir)) return;
            foreach (var f in Directory.GetFiles(cacheDir, "*.*"))
            {
                try
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext is not (".png" or ".jpg" or ".jpeg" or ".tmp")) continue;
                    if (new FileInfo(f).Length < 256)
                        File.Delete(f);
                }
                catch { }
            }
        }

        #endregion

        #region 可见皮肤补图 + 预缓存

        private async Task LoadSkinThumbsAsync()
        {
            string cacheDir = SkinCacheDir;
            if (!Directory.Exists(cacheDir))
                Directory.CreateDirectory(cacheDir);

            // UI 线程收集可见卡，避免跨线程碰 Controls
            var jobs = new List<(ChampionSkinCard card, int skinId, string path, bool isPreview)>();

            foreach (Control c in flpCards.Controls)
            {
                if (c is not ChampionSkinCard card || !card.Visible) continue;
                if (card.Tag is not int champId) continue;

                var model = _allModels.FirstOrDefault(m => m.ChampionId == champId);
                if (model == null) continue;

                int previewId = model.PreferredSkinId > 0 && model.PreferredSkinId % 1000 != 0
                    ? model.PreferredSkinId
                    : model.DefaultSkinId;

                foreach (var skin in model.Skins)
                {
                    string? path = ResolveSkinFile(cacheDir, skin.SkinId);
                    if (path == null) continue;
                    jobs.Add((card, skin.SkinId, path, skin.SkinId == previewId));
                }
            }

            if (jobs.Count == 0)
            {
                //Debug.WriteLine("[皮肤图] LoadSkinThumbsAsync: 可见卡无本地皮肤文件");
                return;
            }

            //Debug.WriteLine($"[皮肤图] LoadSkinThumbsAsync: 本地待上图 {jobs.Count} 个");

            await Task.Run(() =>
            {
                // 按 card 分组，每张卡只进 UI 一次
                foreach (var group in jobs.GroupBy(j => j.card))
                {
                    var card = group.Key;
                    if (card.IsDisposed) continue;

                    var thumbPairs = new List<(int skinId, Image img)>();
                    bool needPreview = false;
                    int previewSkinId = 0;

                    foreach (var job in group)
                    {
                        var thumb = _imgCache.GetOrLoad($"skin:{job.skinId}:50", () =>
                            SkinBrowserImageCache.LoadCroppedFromFile(job.path, 50, 50)
                            ?? SkinBrowserImageCache.LoadScaledFromFile(job.path, 50, 50));
                        if (thumb != null)
                            thumbPairs.Add((job.skinId, thumb));

                        if (job.isPreview)
                        {
                            needPreview = true;
                            previewSkinId = job.skinId;
                            // 后台先解大图进缓存
                            _imgCache.GetOrLoad($"skin:{job.skinId}:250x360", () =>
                                SkinBrowserImageCache.LoadCroppedFromFile(job.path, 250, 360)
                                ?? SkinBrowserImageCache.LoadScaledFromFile(job.path, 250, 360));
                        }
                    }

                    var pairs = thumbPairs;
                    var cardRef = card;
                    var prev = needPreview;
                    EnqueueUi(() =>
                    {
                        if (cardRef.IsDisposed) return;
                        foreach (var (skinId, img) in pairs)
                            cardRef.SetSkinImage(skinId, img);
                        if (prev)
                            cardRef.RefreshPreviewFromDisk(); // 此时几乎必命中缓存
                    });
                }
            }).ConfigureAwait(true);
        }

        // ApplySkinFileToCard 改为异步解码示例（可选优化）
        private void ApplySkinFileToCard(ChampionSkinCard card, int skinId, string path)
        {
            if (card.IsDisposed || !IsValidSkinFile(path)) return;

            // 已在内存则直接用
            var cached = _imgCache.Get($"skin:{skinId}:50");
            if (cached != null)
            {
                card.SetSkinImage(skinId, cached);
                return;
            }

            string pathCopy = path;
            int id = skinId;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Image? img = null;
                try
                {
                    img = SkinBrowserImageCache.LoadCroppedFromFile(pathCopy, 50, 50)
                       ?? SkinBrowserImageCache.LoadScaledFromFile(pathCopy, 50, 50);
                }
                catch { return; }

                if (img == null || card.IsDisposed) return;
                try
                {
                    EnqueueUi(() =>
                    {
                        if (card.IsDisposed) return;
                        var toUse = _imgCache.GetOrLoad($"skin:{id}:50", () => img);
                        card.SetSkinImage(id, toUse ?? img);
                    });
                }
                catch { }
            });
        }

        private async Task PreloadAllOwnedSkinImagesAsync(
            Dictionary<int, List<SkinThumbItem>> owned,
            CancellationToken token)
        {
            if (!IsDisposed && IsHandleCreated)
            {
                try { BeginInvoke(new Action(() => _ = LoadSkinThumbsAsync())); }
                catch { }
            }

            var http = Globals.lcuClient.Client;
            if (http == null) return;

            string cacheDir = SkinCacheDir;
            Directory.CreateDirectory(cacheDir);
            CleanupCorruptSkinCache(cacheDir);

            var jobs = new List<(int champId, int skinId, string? tilePath)>();
            foreach (var model in _allModels)
            {
                if (!model.IsOwned) continue;
                foreach (var s in model.Skins)
                {
                    if (ResolveSkinFile(cacheDir, s.SkinId) != null) continue;
                    jobs.Add((model.ChampionId, s.SkinId, s.TilePath));
                }
            }

            int done = 0, fail = 0;
            await Task.WhenAll(jobs.Select(async job =>
            {
                if (token.IsCancellationRequested) return;
                try
                {
                    bool ok = await EnsureSkinFileCachedAsync(
                        http, cacheDir, job.champId, job.skinId, job.tilePath)
                        .ConfigureAwait(false);
                    if (ok)
                    {
                        Interlocked.Increment(ref done);
                        // 每成功一张，尝试刷新可见 UI（不整表重扫也可）
                        NotifySkinFileReady(job.champId, job.skinId);
                    }
                    else Interlocked.Increment(ref fail);
                }
                catch { Interlocked.Increment(ref fail); }
            })).ConfigureAwait(false);

            Debug.WriteLine($"[皮肤图] 预缓存结束 ok≈{done} fail={fail}");
            EnqueueUi(() => _ = LoadSkinThumbsAsync());
        }

        private void NotifySkinFileReady(int championId, int skinId)
        {
            if (IsDisposed || !IsHandleCreated) return;
            EnqueueUi(() =>
            {
                if (!_cardPool.TryGetValue(championId, out var card)) return;
                if (card.IsDisposed || !card.Visible) return;

                string? path = ResolveSkinFile(SkinCacheDir, skinId);
                if (path == null) return;

                ApplySkinFileToCard(card, skinId, path);
                // 仅更新小格；大图等本批 Flush 后按需，或下面按选中判断
                // 若要刷大图，至少避免每张 skin 都 Refresh：
                // card.RefreshPreviewFromDisk();
            });
        }

        #endregion

        #region 下载写盘（JPEG）

        private async Task<bool> EnsureSkinFileCachedAsync(
        HttpClient lcuHttp,
        string cacheDir,
        int championId,
        int skinId,
        string? tilePathFromApi = null,
        string? loadingScreenPath = null)
        {
            if (ResolveSkinFile(cacheDir, skinId) != null)
                return true;

            var idLock = _skinIdLocks.GetOrAdd(skinId, _ => new SemaphoreSlim(1, 1));
            await idLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ResolveSkinFile(cacheDir, skinId) != null)
                    return true;

                await _skinDownloadLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (ResolveSkinFile(cacheDir, skinId) != null)
                        return true;

                    string cachePath = SkinJpgPath(cacheDir, skinId);
                    int baseSkinId = championId * 1000;
                    var lcuPaths = new List<string>();

                    // API 给的优先（且不是 tile）
                    if (!string.IsNullOrWhiteSpace(tilePathFromApi))
                    {
                        var p = tilePathFromApi.Trim();
                        if (!p.StartsWith("/")) p = "/" + p;
                        if (p.IndexOf("champion-tiles", StringComparison.OrdinalIgnoreCase) < 0)
                            lcuPaths.Add(p);
                    }

                    // 标准 splash（原皮 skinId = champId*1000 同样适用）
                    lcuPaths.Add($"/lol-game-data/assets/v1/champion-splashes/{championId}/{skinId}.jpg");
                    lcuPaths.Add($"/lol-game-data/assets/v1/champion-splashes/uncentered/{championId}/{skinId}.jpg");

                    // 仅一张 tile 兜底
                    lcuPaths.Add($"/lol-game-data/assets/v1/champion-tiles/{championId}/{skinId}.jpg");

                    foreach (var path in lcuPaths.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        var bytes = await TryGetBytesFromLcuAsync(lcuHttp, path).ConfigureAwait(false);
                        if (bytes == null) continue;
                        if (SaveBytesAsCachedJpeg(bytes, cachePath))
                            return true;
                        if (ResolveSkinFile(cacheDir, skinId) != null)
                            return true;
                    }

                    // 兜底：英雄头（仅占位，避免黑格）
                    string champIcon = Path.Combine(
                        Application.StartupPath, "Assets", "champion", $"{championId}.png");
                    if (File.Exists(champIcon))
                    {
                        try
                        {
                            var iconBytes = await File.ReadAllBytesAsync(champIcon).ConfigureAwait(false);
                            if (SaveBytesAsCachedJpeg(iconBytes, cachePath))
                            {
                                Debug.WriteLine($"[皮肤图] skin={skinId} 使用英雄头占位");
                                return true;
                            }
                        }
                        catch { }
                    }

                    if (ResolveSkinFile(cacheDir, skinId) != null)
                        return true;

                    Debug.WriteLine($"[皮肤图] 全部失败 skin={skinId} champ={championId}");
                    return false;
                }
                finally
                {
                    _skinDownloadLock.Release();
                }
            }
            finally
            {
                idLock.Release();
            }
        }

        private static async Task<byte[]?> TryGetBytesFromLcuAsync(HttpClient http, string path)
        {
            try
            {
                using var resp = await http.GetAsync(path).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                return bytes.Length >= 256 ? bytes : null;
            }
            catch { return null; }
        }

        private static Bitmap ScaleToMaxEdge(Image src, int maxEdge)
        {
            int w = src.Width, h = src.Height;
            if (w <= maxEdge && h <= maxEdge)
                return new Bitmap(src);

            double r = Math.Min((double)maxEdge / w, (double)maxEdge / h);
            int nw = Math.Max(1, (int)(w * r));
            int nh = Math.Max(1, (int)(h * r));
            var bmp = new Bitmap(nw, nh);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, nw, nh);
            return bmp;
        }

        /// <summary>
        /// 解码 → 缩边 → 转 24bpp → JPEG 原子写盘；删除同 id 旧 png。
        /// </summary>
        private static bool SaveBytesAsCachedJpeg(byte[] bytes, string cachePath)
        {
            if (bytes == null || bytes.Length < 256) return false;

            string? dir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string tmp = Path.Combine(
                dir ?? Path.GetTempPath(),
                $"{Path.GetFileNameWithoutExtension(cachePath)}_{Guid.NewGuid():N}.tmp");

            try
            {
                using var ms = new MemoryStream(bytes);
                using var src = Image.FromStream(ms);
                using var scaled = ScaleToMaxEdge(src, SkinCacheMaxEdge);
                using var rgb = ToJpegCompatibleBitmap(scaled);

                var jpegCodec = ImageCodecInfo.GetImageEncoders()
                    .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
                if (jpegCodec == null) return false;

                using (var ep = new EncoderParameters(1))
                {
                    ep.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
                    rgb.Save(tmp, jpegCodec, ep);
                }

                var fi = new FileInfo(tmp);
                if (!fi.Exists || fi.Length < 256)
                {
                    try { File.Delete(tmp); } catch { }
                    return false;
                }

                // 目标已存在时先删，避免「无法创建该文件」
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        if (File.Exists(cachePath))
                            File.Delete(cachePath);
                        File.Move(tmp, cachePath);
                        break;
                    }
                    catch (IOException) when (i < 4)
                    {
                        Thread.Sleep(30);
                        // 若目标已是有效文件，视为成功
                        if (IsValidSkinFile(cachePath))
                        {
                            try { File.Delete(tmp); } catch { }
                            return true;
                        }
                    }
                }

                if (!IsValidSkinFile(cachePath))
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    return false;
                }

                string legacyPng = Path.ChangeExtension(cachePath, ".png");
                if (!string.IsNullOrEmpty(legacyPng) && File.Exists(legacyPng))
                {
                    try { File.Delete(legacyPng); } catch { }
                }

                return true;
            }
            catch (Exception ex)
            {
                // 竞态下目标已写好 → 成功
                if (IsValidSkinFile(cachePath))
                    return true;
                Debug.WriteLine($"[皮肤图] JPEG 写盘失败: {ex.Message}");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return false;
            }
        }

        /// <summary>
        /// 转成 JPEG 可写的 24bppRgb，避免 GDI+ generic error。
        /// </summary>
        private static Bitmap ToJpegCompatibleBitmap(Image src)
        {
            var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Black);
            g.DrawImage(src, 0, 0, src.Width, src.Height);
            return bmp;
        }

        #endregion

        #region 偏好 / 清除 / 关窗

        private void OnPreferredSkinChanged(int championId, int skinId)
        {
            _config.PreferredChampionSkins ??= new Dictionary<int, int>();
            if (skinId <= 0 || skinId % 1000 == 0)
                _config.PreferredChampionSkins.Remove(championId);
            else
                _config.PreferredChampionSkins[championId] = skinId;

            var m = _allModels.FirstOrDefault(x => x.ChampionId == championId);
            if (m != null)
                m.PreferredSkinId = (skinId <= 0 || skinId % 1000 == 0) ? 0 : skinId;

            if (_cardPool.TryGetValue(championId, out var card))
                card.RefreshPreference(m?.PreferredSkinId ?? 0);

            if (Owner is FormMain main)
                main.SaveAppConfig();

            Debug.WriteLine($"[ChampionSkinBrowser] 偏好 champ={championId} skin={skinId}");
        }

        private void BtnClearPrefs_Click(object? sender, EventArgs e)
        {
            if (MessageBox.Show(
                    "确定清除全部皮肤预选并恢复为默认皮？",
                    "清除皮肤预选恢复默认",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _config.PreferredChampionSkins ??= new Dictionary<int, int>();
            _config.PreferredChampionSkins.Clear();
            foreach (var m in _allModels)
                m.PreferredSkinId = 0;
            foreach (var card in _cardPool.Values)
                card.RefreshPreference(0);

            if (Owner is FormMain main)
                main.SaveAppConfig();
            ApplyFilterAndRender();
        }

        private void ChampionSkinBrowser_FormClosing(object? sender, FormClosingEventArgs e)
        {
            try
            {
                _searchDebounce?.Stop();
                _searchDebounce?.Dispose();
                _searchDebounce = null;
                _loadCts?.Cancel();

                _uiFlushTimer?.Stop();
                _uiFlushTimer?.Dispose();
                _uiFlushTimer = null;

                // 解绑即可，不要在这里 Controls.Clear / 逐张 Dispose
                foreach (var card in _cardPool.Values)
                {
                    try { card.PreferredSkinChanged -= OnPreferredSkinChanged; }
                    catch { }
                }
                _cardPool.Clear();

                // 图片缓存放到线程池释放（不要碰 Control）
                var cache = _imgCache;
                _imgCache = new SkinBrowserImageCache();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { cache?.Dispose(); } catch { }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChampionSkinBrowser] FormClosing: {ex.Message}");
            }

            // 不要：Opacity/Visible 花活 + 后台 Clear 控件
            // 窗体关时框架会自己 Dispose 子控件
        }

        #endregion

        private void EnqueueUi(Action action)
        {
            if (IsDisposed || action == null) return;
            lock (_pendingUiLock)
                _pendingUi.Add(action);
        }

        private void FlushPendingUi()
        {
            if (IsDisposed) return;

            const int maxPerTick = 12; // 每 120ms 最多处理 12 个上图动作
            List<Action> batch;
            lock (_pendingUiLock)
            {
                if (_pendingUi.Count == 0) return;
                int n = Math.Min(maxPerTick, _pendingUi.Count);
                batch = _pendingUi.GetRange(0, n);
                _pendingUi.RemoveRange(0, n);
            }

            foreach (var a in batch)
            {
                try { a(); } catch { }
            }
        }
    }
}