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

        //private async Task UpdateMatchListInternal(JArray matches, CancellationToken token)
        //{
        //    var panels = await Task.Run(async () =>
        //    {
        //        var panelList = new List<Panel>();
        //        Debug.WriteLine("进入解析环节！");
        //        int num = 1;
        //        var tasks = matches.Cast<JObject>()
        //            .Select(match => ParsePanelRequested?.Invoke(match, _puuid, num++))
        //            .ToList();

        //        foreach (var t in tasks)
        //        {
        //            if (token.IsCancellationRequested) break;
        //            var panel = await t.ConfigureAwait(false);
        //            if (panel != null) panelList.Add(panel);
        //        }

        //        return panelList;
        //    });

        //    SafeInvoke(flowLayoutPanelRight, () =>
        //    {
        //        flowLayoutPanelRight.SuspendLayout();
        //        try
        //        {
        //            foreach (Control control in flowLayoutPanelRight.Controls)
        //            {
        //                if (control is IDisposable disposable) disposable.Dispose();
        //            }
        //            flowLayoutPanelRight.Controls.Clear();
        //            flowLayoutPanelRight.Controls.AddRange(panels.ToArray());
        //        }
        //        finally
        //        {
        //            flowLayoutPanelRight.ResumeLayout(true);
        //        }
        //    });
        //}
        // 2. 修改 UpdateMatchListInternal：在 foreach await 时捕获取消异常
        private async Task UpdateMatchListInternal(JArray matches, CancellationToken token)
        {
            var panels = await Task.Run(async () =>
            {
                var panelList = new List<Panel>();
                //Debug.WriteLine("进入解析环节！");
                int num = 1;
                var tasks = matches.Cast<JObject>()
                    .Select(match => ParsePanelRequested?.Invoke(match, _puuid, num++))
                    .ToList();

                // 保存当前解析任务（用于 Cleanup 时等待）
                _currentParsingTask = Task.WhenAll(tasks);  // 新增这行

                foreach (var t in tasks)
                {
                    if (token.IsCancellationRequested) break;

                    try
                    {
                        var panel = await t.ConfigureAwait(false);
                        if (panel != null) panelList.Add(panel);
                    }
                    catch (OperationCanceledException)  // 或 catch (TaskCanceledException)
                    {
                        // 被取消了，正常情况，直接跳出
                        break;
                    }
                }

                _currentParsingTask = null;  // 新增：任务结束清空引用
                return panelList;
            }, token);  // 建议加上 token，让 Task.Run 也能响应取消

            SafeInvoke(flowLayoutPanelRight, () =>
            {
                flowLayoutPanelRight.SuspendLayout();
                try
                {
                    foreach (Control control in flowLayoutPanelRight.Controls)
                    {
                        if (control is IDisposable disposable) disposable.Dispose();
                    }
                    flowLayoutPanelRight.Controls.Clear();
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
            linkGameName.Text = fullName;
            lblLevel.Text = $"等级：【{summonerLevel}】";
            lblPrivacy.Text = $"隐私：【{privacy}】";
            picChampionId.Image = await Profileicon.GetProfileIconAsync(int.Parse(profileIconId));

            // 检查数据是否存在
            if (rankedStats == null) return;

            // 访问单双排数据（安全访问）
            if (rankedStats.TryGetValue("单双排", out var soloStats))
            {
                lblSoloTier.Text = soloStats.FormattedTier;      // 段位
                lblSoloGames.Text = $"{soloStats.TotalGames} 场";    //场次
                lblSoloWins.Text = $"{soloStats.Wins}场";  //胜场
                lblSoloLosses.Text = $"{soloStats.Losses}场";  //负场
                lblSoloWinRate.Text = $"{soloStats.WinRateDisplay}%";  //胜率
                //lblSoloWinRate.Text = $"{soloStats.WinRate}%";  //胜率
                lblSoloLeaguePoints.Text = $"{soloStats.LeaguePoints}点";  //胜点
            }

            // 访问灵活组排数据
            if (rankedStats.TryGetValue("灵活组排", out var flexStats))
            {
                lblFlexTier.Text = flexStats.FormattedTier;      // 段位
                lblFlexGames.Text = $"{flexStats.TotalGames} 场";    //场次
                lblFlexWins.Text = $"{flexStats.Wins}场";  //胜场
                lblFlexLosses.Text = $"{flexStats.Losses}场";  //负场
                lblFlexWinRate.Text = $"{flexStats.WinRateDisplay}%";  //胜率胜率
                //lblFlexWinRate.Text = $"{flexStats.WinRate}%";  //胜率胜率
                lblFlexLeaguePoints.Text = $"{flexStats.LeaguePoints}点";  //胜点
            }
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

        //public void Cleanup()
        //{
        //    Debug.WriteLine($"[清理] MatchTabPageContent清理开始");

        //    // 1. 取消所有异步操作
        //    _updateCts?.Cancel();
        //    _updateCts?.Dispose();
        //    _updateCts = null;

        //    // 2. 清理所有Panel及其资源
        //    SafeInvoke(() =>
        //    {
        //        foreach (Control control in flowLayoutPanelRight.Controls)
        //        {
        //            if (control is MatchListPanel matchPanel)
        //            {
        //                // 清理MatchInfo中的资源
        //                if (matchPanel.MatchInfo != null)
        //                {
        //                    // 释放原始JSON数据
        //                    matchPanel.MatchInfo.RawGameData?.RemoveAll();
        //                    matchPanel.MatchInfo.RawGameData = null;
        //                    matchPanel.MatchInfo.AllParticipants?.Clear();
        //                    matchPanel.MatchInfo.AllParticipants = null;
        //                }
        //            }
        //            control.Dispose();
        //        }
        //        flowLayoutPanelRight.Controls.Clear();
        //    });

        //    // 3. 清理事件
        //    LoadDataRequested = null;
        //    ParsePanelRequested = null;

        //    // 5. 强制GC
        //    GC.Collect();
        //    GC.WaitForPendingFinalizers();

        //    Debug.WriteLine($"[清理] MatchTabPageContent清理完成");
        //}

        // 3. 修改 Cleanup()：等待正在解析的任务结束（关键！彻底消除异常）
        public void Cleanup()
        {
            //Debug.WriteLine($"[清理] MatchTabPageContent清理开始");

            // 取消正在进行的操作
            _updateCts?.Cancel();

            // 关键：等待当前正在解析的面板任务结束（如果有）
            if (_currentParsingTask != null && !_currentParsingTask.IsCompleted)
            {
                try
                {
                    // 同步等待（在 Dispose 中可以接受，不会死锁）
                    _currentParsingTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    // 预期中的取消，忽略
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[Cleanup 等待任务异常]" + ex);
                }
            }

            _updateCts?.Dispose();
            _updateCts = null;

            // 清理 UI
            SafeInvoke(() =>
            {
                foreach (Control control in flowLayoutPanelRight.Controls)
                {
                    if (control is MatchListPanel matchPanel && matchPanel.MatchInfo != null)
                    {
                        matchPanel.MatchInfo.RawGameData?.RemoveAll();
                        matchPanel.MatchInfo.RawGameData = null;
                        matchPanel.MatchInfo.AllParticipants?.Clear();
                        matchPanel.MatchInfo.AllParticipants = null;
                    }
                    control.Dispose();
                }
                flowLayoutPanelRight.Controls.Clear();
            });

            LoadDataRequested = null;
            ParsePanelRequested = null;

            // GC 强制收集可保留也可去掉（建议去掉，没必要）
            // GC.Collect();
            // GC.WaitForPendingFinalizers();

            //Debug.WriteLine($"[清理] MatchTabPageContent清理完成");
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