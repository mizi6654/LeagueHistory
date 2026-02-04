using System.ComponentModel;
using System.Diagnostics;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.uitls
{
    public partial class MatchTabPageContent : UserControl, IDisposable
    {
        // 分页状态
        private int _currentPage = 1;
        private int _pageSize = 8;
        private string _selectedQueueId = "";
        private string _puuid;

        // 数据加载事件
        public event Func<string, int, int, string, Task<JArray>> LoadDataRequested;
        public event Func<JObject, string, int, Task<Panel>> ParsePanelRequested;

        private SemaphoreSlim _loadSemaphore = new SemaphoreSlim(1, 1);
        private SemaphoreSlim _updateLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _updateCts;

        // ✅ 新增：保存当前JArray引用，用于释放
        private JArray _currentMatches;

        // 1. 添加一个字段，保存当前正在解析的任务（可选但推荐）
        private Task _currentParsingTask;  // 新增这行

        // 新增字段：跟踪已加载的图片资源
        private List<Image> _loadedImages = new List<Image>();

        // 新增字段：用于深度清理
        private List<WeakReference> _allParsedPanels = new List<WeakReference>();
        private Dictionary<string, object> _parsedDataCache = new Dictionary<string, object>();

        public MatchTabPageContent()
        {
            InitializeComponent();

            //this.Dock = DockStyle.Fill; // 这将使控件填充父容器
            //this.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            //this.AutoScroll = true;

            // 确保在构造函数中设置正确的属性
            this.AutoScaleMode = AutoScaleMode.None;
            this.Dock = DockStyle.Fill;

            // 对 flowLayoutPanelRight 的设置
            flowLayoutPanelRight.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            flowLayoutPanelRight.AutoScroll = true;
            flowLayoutPanelRight.AutoSize = false; // 重要：关闭自动大小

            //初始化数据绑定
            InitComboBoxes();
            WireEvents();

            
        }

        // 🔥 新增 ForceCleanup 方法
        public void ForceCleanup()
        {
            // 停止所有异步操作
            _updateCts?.Cancel();

            // 等待任务完成（最多等待 1 秒）
            if (_currentParsingTask != null && !_currentParsingTask.IsCompleted)
            {
                try
                {
                    _currentParsingTask.Wait(TimeSpan.FromSeconds(1));
                }
                catch { }
            }

            // 清理图片资源
            DisposeAllImages();

            // 清理 JSON 数据
            if (_currentMatches != null)
            {
                try
                {
                    _currentMatches.RemoveAll();
                }
                catch { }
                _currentMatches = null;
            }

            // 调用原有的 Cleanup
            Cleanup();
        }

        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Cleanup();
            _loadSemaphore?.Dispose();
            _updateLock?.Dispose();
            _updateCts?.Dispose();
            foreach (Control control in flowLayoutPanelRight.Controls)
            {
                if (control is IDisposable disposable) disposable.Dispose();
            }
            flowLayoutPanelRight.Dispose();
            GC.SuppressFinalize(this);
        }

        // 初始化筛选器
        private void InitComboBoxes()
        {
            comboPage.Items.AddRange(new object[] { 7, 8, 20, 30, 50 });
            comboPage.SelectedItem = 7; // 默认值
            _pageSize = 7;

            comboFilter.DisplayMember = "Text";
            comboFilter.ValueMember = "Value";
            comboFilter.Items.AddRange(new[]
            {
                new QueueFilterItem { Text = "全部", Value = "" },
                new QueueFilterItem { Text = "单双排位", Value = "q_420" },
                new QueueFilterItem { Text = "灵活排位", Value = "q_440" },
                new QueueFilterItem { Text = "匹配模式", Value = "q_430" },
                new QueueFilterItem { Text = "大乱斗", Value = "q_450" }
            });
            comboFilter.SelectedIndex = 0;
        }

        //下拉框事件绑定
        private void WireEvents()
        {
            btnPrev.Click += async (s, e) => await OnPrevPage();
            btnNext.Click += async (s, e) => await OnNextPage();

            // 将事件处理程序保存到成员变量
            asyncComboPageChangedHandler = async (s, e) => await OnPageSizeChanged();
            comboPage.SelectedIndexChanged += asyncComboPageChangedHandler;

            comboFilter.SelectedIndexChanged += async (s, e) => await OnFilterChanged();
        }

        public void Initialize(string puuid)
        {
            if (string.IsNullOrEmpty(puuid))
                throw new ArgumentException("PUUID不能为空");

            _puuid = puuid;
            _ = LoadDataAsync(); // 异步初始化加载
        }

        private async Task LoadDataAsync(bool resetPage = true)
        {
            if (resetPage) _currentPage = 1;
            await UpdateMatchesDisplay();
        }

        private async Task UpdateMatchesDisplay()
        {
            await _loadSemaphore.WaitAsync();
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 开始加载数据 - PUUID: {_puuid}");
            if (this.LoadDataRequested == null || this.ParsePanelRequested == null)
            {
                Debug.WriteLine("[严重错误] 未绑定数据加载事件");
                ShowErrorMessage("系统错误：功能未初始化");
                return;
            }
            try
            {
                SetLoadingState(isLoading: true);
                int startIndex = (_currentPage - 1) * _pageSize;
                int count = _pageSize;
                if (startIndex + count > 200)
                {
                    count = 200 - startIndex;
                    if (count <= 0)
                    {
                        ShowEmptyMessage();
                        return;
                    }
                }
                TotalPages = (int)Math.Ceiling(200.0 / (double)_pageSize);
                JArray matches = await this.LoadDataRequested(_puuid, startIndex, count, _selectedQueueId);
                Debug.WriteLine($"收到 {matches?.Count ?? 0} 条数据，请求参数: startIndex={startIndex}, count={count}");
                if (matches == null || matches.Count == 0)
                {
                    ShowEmptyMessage();
                    return;
                }
                //Debug.WriteLine("开始解析数据...");
                await UpdateMatchList(matches);
                SafeSetEnabled(btnPrev, _currentPage > 1);
                SafeSetEnabled(btnNext, _currentPage < TotalPages);
            }
            catch (Exception ex)
            {
                Exception ex2 = ex;
                Debug.WriteLine("[异常] " + ex2.GetType().Name + ": " + ex2.Message);
                Debug.WriteLine(ex2.StackTrace);
                ShowErrorMessage("加载失败: " + ex2.Message);
            }
            finally
            {
                SetLoadingState(isLoading: false);
                SafeInvoke(delegate
                {
                    //Debug.WriteLine("UI 安全回调检查通过。");
                });
                //Debug.WriteLine("界面更新结束");
                _loadSemaphore.Release();
            }
        }


        private async Task UpdateMatchList(JArray matches)
        {
            await _updateLock.WaitAsync();
            try
            {
                _updateCts?.Cancel();
                _updateCts = new CancellationTokenSource();
                CancellationToken token = _updateCts.Token;
                if (flowLayoutPanelRight.InvokeRequired)
                {
                    await flowLayoutPanelRight.Invoke((Func<Task>)async delegate
                    {
                        if (!token.IsCancellationRequested)
                        {
                            await UpdateMatchListInternal(matches, token);
                        }
                    });
                }
                else if (!token.IsCancellationRequested)
                {
                    await UpdateMatchListInternal(matches, token);
                }
            }
            finally
            {
                _updateLock.Release();
            }
        }

        
        // 修改 UpdateMatchListInternal 方法，跟踪解析的面板
        private async Task UpdateMatchListInternal(JArray matches, CancellationToken token)
        {
            // 先清理旧的
            CleanupAllParsedPanels();

            var panels = await Task.Run(async () =>
            {
                var panelList = new List<Panel>();
                int num = 1;
                var tasks = matches.Cast<JObject>()
                    .Select(match => ParsePanelRequested?.Invoke(match, _puuid, num++))
                    .ToList();

                _currentParsingTask = Task.WhenAll(tasks);

                foreach (var t in tasks)
                {
                    if (token.IsCancellationRequested) break;

                    try
                    {
                        var panel = await t.ConfigureAwait(false);
                        if (panel != null)
                        {
                            panelList.Add(panel);
                            _allParsedPanels.Add(new WeakReference(panel)); // 🔥 跟踪
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                _currentParsingTask = null;
                return panelList;
            }, token);

            SafeInvoke(flowLayoutPanelRight, () =>
            {
                flowLayoutPanelRight.SuspendLayout();
                try
                {
                    // 先清理旧的
                    foreach (Control control in flowLayoutPanelRight.Controls)
                    {
                        if (control is IDisposable disposable) disposable.Dispose();
                    }
                    flowLayoutPanelRight.Controls.Clear();

                    // 添加新的
                    flowLayoutPanelRight.Controls.AddRange(panels.ToArray());
                }
                finally
                {
                    flowLayoutPanelRight.ResumeLayout(true);
                }
            });
        }


        public async Task SafeInitializeAsync(string puuid)
        {
            if (string.IsNullOrEmpty(puuid)) return;

            _puuid = puuid;

            await Task.Delay(200); // 可选延迟，确保控件加载完

            await LoadDataAsync();
        }


        public async Task InitiaRank(string fullName, string profileIconId, string summonerLevel, string privacy, Dictionary<string, RankedStats> rankedStats)
        {
            // 检查是否在UI线程
            if (this.InvokeRequired)
            {
                // 使用异步方式，避免死锁
                await Task.Run(async () =>
                    await InitiaRank(fullName, profileIconId, summonerLevel, privacy, rankedStats));
                return;
            }

            try
            {
                linkGameName.Text = fullName;
                lblLevel.Text = $"等级：【{summonerLevel}】";
                lblPrivacy.Text = $"隐私：【{privacy}】";

                // 只在头像ID不同时才更新图片
                if (int.TryParse(profileIconId, out int iconId))
                {
                    // 获取当前头像ID（如果有的话）
                    int currentIconId = GetCurrentProfileIconId();

                    // 只有当头像ID不同时才更新
                    if (currentIconId != iconId)
                    {
                        await UpdateProfileIcon(iconId);
                    }
                }

                // 检查数据是否存在
                if (rankedStats == null) return;

                // 访问单双排数据（安全访问）
                if (rankedStats.TryGetValue("单双排", out var soloStats))
                {
                    lblSoloTier.Text = soloStats.FormattedTier;
                    lblSoloGames.Text = $"{soloStats.TotalGames} 场";
                    lblSoloWins.Text = $"{soloStats.Wins}场";
                    lblSoloLosses.Text = $"{soloStats.Losses}场";
                    lblSoloWinRate.Text = $"{soloStats.WinRateDisplay}%";
                    lblSoloLeaguePoints.Text = $"{soloStats.LeaguePoints}点";
                }

                // 访问灵活组排数据
                if (rankedStats.TryGetValue("灵活组排", out var flexStats))
                {
                    lblFlexTier.Text = flexStats.FormattedTier;
                    lblFlexGames.Text = $"{flexStats.TotalGames} 场";
                    lblFlexWins.Text = $"{flexStats.Wins}场";
                    lblFlexLosses.Text = $"{flexStats.Losses}场";
                    lblFlexWinRate.Text = $"{flexStats.WinRateDisplay}%";
                    lblFlexLeaguePoints.Text = $"{flexStats.LeaguePoints}点";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InitiaRank异常] {ex.Message}");
            }
        }

        // 辅助方法：获取当前头像ID
        private int GetCurrentProfileIconId()
        {
            // 这里可以从Tag或其他地方存储当前头像ID
            // 暂时返回-1表示没有当前头像
            if (picChampionId.Tag != null && int.TryParse(picChampionId.Tag.ToString(), out int currentId))
            {
                return currentId;
            }
            return -1;
        }

        // 辅助方法：更新头像
        private async Task UpdateProfileIcon(int iconId)
        {
            try
            {
                // 清理旧图片
                if (picChampionId.Image != null)
                {
                    var oldImage = picChampionId.Image;
                    picChampionId.Image = null;

                    // 延迟释放，避免立即释放导致的问题
                    _ = Task.Delay(100).ContinueWith(_ =>
                    {
                        try
                        {
                            oldImage?.Dispose();
                        }
                        catch { }
                    });
                }

                // 加载新图片
                var image = await Profileicon.GetProfileIconAsync(iconId);
                if (image != null)
                {
                    picChampionId.Image = image;
                    picChampionId.Tag = iconId.ToString(); // 保存当前头像ID
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[更新头像异常] {ex.Message}");
            }
        }

        // 🔥 清理所有图片资源的方法
        private void DisposeAllImages()
        {
            SafeInvoke(() =>
            {
                // 清理 profile icon
                if (picChampionId.Image != null)
                {
                    try
                    {
                        picChampionId.Image.Dispose();
                    }
                    catch { }
                    picChampionId.Image = null;
                }

                // 清理已跟踪的图片
                foreach (var image in _loadedImages)
                {
                    try
                    {
                        image?.Dispose();
                    }
                    catch { }
                }
                _loadedImages.Clear();

                // 递归清理 flowLayoutPanelRight 中的所有图片
                foreach (Control control in flowLayoutPanelRight.Controls)
                {
                    if (control is MatchListPanel matchPanel)
                    {
                        try
                        {
                            matchPanel.DisposeImages();
                        }
                        catch { }
                    }
                }
            });
        }

        private async Task OnPrevPage()
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                await UpdateMatchesDisplay();
            }
        }

        private async Task OnNextPage()
        {
            if (_currentPage < TotalPages)
            {
                _currentPage++;
                await UpdateMatchesDisplay();
            }
        }

        private async Task OnPageSizeChanged()
        {
            if (int.TryParse(comboPage.SelectedItem?.ToString(), out int newPageSize))
            {
                if (_pageSize != newPageSize)
                {
                    _pageSize = newPageSize;
                    _currentPage = 1;
                    await UpdateMatchesDisplay();
                }
            }
        }

        private async Task OnFilterChanged()
        {
            var item = comboFilter.SelectedItem as QueueFilterItem;
            _selectedQueueId = item?.Value;

            int desiredPageSize = string.IsNullOrEmpty(_selectedQueueId) ? 7 : 7;

            if (_pageSize != desiredPageSize)
            {
                SafeSetSelectedItem(comboPage, desiredPageSize);
                _pageSize = desiredPageSize;
            }

            _currentPage = 1;
            await UpdateMatchesDisplay();
        }


        // 你需要把这个事件委托保存为成员
        private EventHandler asyncComboPageChangedHandler;


        private void SetLoadingState(bool isLoading)
        {
            SafeSetEnabled(btnPrev, !isLoading);
            SafeSetEnabled(btnNext, !isLoading);
            SafeSetEnabled(comboPage, !isLoading);
            SafeSetEnabled(comboFilter, !isLoading);
        }

        private void ShowErrorMessage(string message)
        {
            Invoke((Action)(() =>
            {
                flowLayoutPanelRight.Controls.Clear();
                flowLayoutPanelRight.Controls.Add(new Label
                {
                    Text = message,
                    ForeColor = Color.Red,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter
                });
            }));
        }

        private void ShowEmptyMessage()
        {
            if (flowLayoutPanelRight.InvokeRequired)
            {
                flowLayoutPanelRight.Invoke((Action)ShowEmptyMessage);
                return;
            }

            flowLayoutPanelRight.SuspendLayout();
            try
            {
                flowLayoutPanelRight.Controls.Clear();

                // 创建标签并显式设置属性
                var label = new Label
                {
                    Text = "查询太快超时，或没有匹配的数据！",
                    TextAlign = ContentAlignment.MiddleCenter,
                    AutoSize = false,
                    Size = new Size(flowLayoutPanelRight.ClientSize.Width, 50),
                    BackColor = Color.White,
                    ForeColor = Color.Red,
                    Dock = DockStyle.Top // 确保标签占据顶部
                };

                flowLayoutPanelRight.Controls.Add(label);
                label.BringToFront(); // 防止被其他控件覆盖

                // 调试输出
                Debug.WriteLine($"Label尺寸: {label.Width}x{label.Height}, 父容器尺寸: {flowLayoutPanelRight.Size}");
            }
            finally
            {
                flowLayoutPanelRight.ResumeLayout(true);
                flowLayoutPanelRight.PerformLayout();
                flowLayoutPanelRight.Update();
                flowLayoutPanelRight.Parent?.Refresh();
            }
        }

        private void linkGameName_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            // 获取LinkLabel的文本
            string textToCopy = linkGameName.Text;

            // 复制文本到剪贴板
            Clipboard.SetText(textToCopy);

            // 将屏幕坐标转成控件坐标
            Point clientPos = linkGameName.PointToClient(Cursor.Position);

            // 在鼠标附近显示 ToolTip
            new ToolTip().Show($"已复制: {textToCopy}", linkGameName, clientPos.X + 10, clientPos.Y + 10, 1500);
        }

        /// <summary>
        /// 通用线程安全调用（带控件）
        /// 适合跨线程访问 WinForms 控件
        /// </summary>
        private void SafeInvoke(Control ctrl, Action action)
        {
            if (ctrl == null || ctrl.IsDisposed || ctrl.Disposing) return;

            try
            {
                if (ctrl.InvokeRequired)
                    ctrl.Invoke(action);
                else
                    action();
            }
            catch (ObjectDisposedException)
            {
                // 控件已释放时静默忽略
            }
        }

        /// <summary>
        /// 当前控件自身安全调用（无参数）
        /// 用于从控件内部调用UI更新
        /// </summary>
        private void SafeInvoke(Action action)
        {
            if (IsDisposed || Disposing) return;

            try
            {
                if (InvokeRequired)
                    BeginInvoke(action);
                else
                    action();
            }
            catch (ObjectDisposedException)
            {
                // 控件已释放时静默忽略
            }
        }


        private void SafeSetEnabled(Control ctrl, bool enabled)
        {
            if (ctrl == null) return;
            SafeInvoke(() =>
            {
                if (!ctrl.IsDisposed)
                    ctrl.Enabled = enabled;
            });
        }

        private void SafeSetSelectedItem(ComboBox combo, object item)
        {
            SafeInvoke(combo, () =>
            {
                combo.SelectedIndexChanged -= asyncComboPageChangedHandler;
                combo.SelectedItem = item;
                combo.SelectedIndexChanged += asyncComboPageChangedHandler;
            });
        }

        // 修改 Cleanup 方法，增加深度清理
        public void Cleanup()
        {
            Debug.WriteLine($"[深度清理] MatchTabPageContent 开始清理，PUUID: {_puuid}");

            // 1. 取消所有异步操作
            _updateCts?.Cancel();

            // 2. 等待当前解析任务完成（最多500ms）
            if (_currentParsingTask != null && !_currentParsingTask.IsCompleted)
            {
                try
                {
                    bool completed = _currentParsingTask.Wait(500);
                    if (!completed)
                    {
                        Debug.WriteLine($"[清理] 解析任务未在500ms内完成，强制继续清理");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[清理等待异常] {ex.Message}");
                }
            }

            // 3. 清理所有图片资源
            DisposeAllImages();

            // 4. 清理 JSON 数据
            if (_currentMatches != null)
            {
                try
                {
                    // 深度清理JSON对象
                    DeepCleanJArray(_currentMatches);
                    _currentMatches = null;
                }
                catch { }
            }

            // 5. 清理所有已解析的面板
            CleanupAllParsedPanels();

            // 6. 清理数据缓存
            _parsedDataCache.Clear();

            // 7. 清理UI控件
            SafeInvoke(() =>
            {
                try
                {
                    // 获取副本，避免修改集合时遍历
                    var controls = flowLayoutPanelRight.Controls.OfType<Control>().ToList();
                    foreach (var control in controls)
                    {
                        if (control is MatchListPanel matchPanel)
                        {
                            // 深度清理MatchListPanel
                            matchPanel.DeepClean();
                        }
                        control.Dispose();
                    }
                    flowLayoutPanelRight.Controls.Clear();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UI清理异常] {ex.Message}");
                }
            });

            // 8. 清理事件引用
            LoadDataRequested = null;
            ParsePanelRequested = null;

            // 9. 清理下拉框事件
            if (asyncComboPageChangedHandler != null)
            {
                comboPage.SelectedIndexChanged -= asyncComboPageChangedHandler;
                asyncComboPageChangedHandler = null;
            }

            // 10. 清理委托缓存
            _currentParsingTask = null;
            _puuid = null;

            // 11. 清理弱引用列表
            _allParsedPanels.Clear();

            // 12. 通知GC
            GC.Collect(0, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();

            Debug.WriteLine($"[深度清理] MatchTabPageContent 清理完成");
        }

        // 🔥 新增：深度清理JArray
        private void DeepCleanJArray(JArray array)
        {
            if (array == null) return;

            try
            {
                // 遍历所有JObject
                foreach (var item in array)
                {
                    if (item is JObject obj)
                    {
                        DeepCleanJObject(obj);
                    }
                }
                array.RemoveAll();
            }
            catch { }
        }

        // 🔥 新增：深度清理JObject
        private void DeepCleanJObject(JObject obj)
        {
            if (obj == null) return;

            try
            {
                // 移除所有属性
                var properties = obj.Properties().ToList();
                foreach (var prop in properties)
                {
                    prop.Remove();
                }
            }
            catch { }
        }

        // 🔥 新增：清理所有已解析的面板
        private void CleanupAllParsedPanels()
        {
            foreach (var weakRef in _allParsedPanels)
            {
                if (weakRef.IsAlive && weakRef.Target is Panel panel)
                {
                    try
                    {
                        if (panel is MatchListPanel matchPanel)
                        {
                            matchPanel.DeepClean();
                        }
                        panel.Dispose();
                    }
                    catch { }
                }
            }
            _allParsedPanels.Clear();
        }

        #region 公共属性
        [Browsable(false)]
        public int CurrentPage => _currentPage;

        [Browsable(false)]
        public int TotalPages { get; private set; }

        [Browsable(false)]
        public string CurrentFilter => _selectedQueueId;
        #endregion
    }
    public class QueueFilterItem
    {
        public string Text { get; set; }
        public string Value { get; set; }
    }
}