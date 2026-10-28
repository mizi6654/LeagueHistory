using League.Controls;
using League.Infrastructure;
using League.UIHelpers;
using System.Diagnostics;

namespace League.UIState
{
    /// <summary>
    /// 管理主窗体的UI状态，包括LCU连接状态、加载提示、Tab切换等
    /// </summary>
    public class FormUiStateManager
    {
        private readonly FormMain _form;
        private Panel? _loadingOverlay;
        private Panel? _waitingPanel;
        private ToolTip _toolTip = new ToolTip();
        private int _lastIndex = -1;

        // 加载动画相关
        private System.Windows.Forms.Timer? _currentLoadingTimer;
        private bool _isShowingLoading = false;

        public bool IsGame { get; set; }
        public bool LcuReady { get; set; }

        public FormUiStateManager(FormMain form)
        {
            _form = form;
            InitializeToolTip();
        }

        private void InitializeToolTip()
        {
            _toolTip.AutoPopDelay = 5000;
            _toolTip.InitialDelay = 100;
            _toolTip.ReshowDelay = 100;
            _toolTip.ShowAlways = true;
        }

        #region 加载提示管理（核心优化）

        /// <summary>
        /// 显示轻柔半透明加载提示
        /// </summary>
        public void ShowLoadingIndicator()
        {
            SafeInvoke(_form, () =>
            {
                // 已经在显示中则跳过
                if (_isShowingLoading && _loadingOverlay != null && !_loadingOverlay.IsDisposed)
                    return;

                // 清理旧的（防止竞态）
                CleanupCurrentLoadingOverlay();

                _isShowingLoading = true;

                // 创建加载遮罩
                _loadingOverlay = new Panel
                {
                    Size = new Size((int)(_form.Width * 0.6), (int)(_form.Height * 0.4)),
                    BackColor = Color.FromArgb(0, 255, 255, 255),
                    BorderStyle = BorderStyle.FixedSingle,
                    Visible = false
                };

                // 居中
                _loadingOverlay.Location = new Point(
                    (_form.ClientSize.Width - _loadingOverlay.Width) / 2,
                    (_form.ClientSize.Height - _loadingOverlay.Height) / 2
                );

                // 提示文本
                var lbl = new Label
                {
                    Text = "正在加载，请稍候...",
                    Font = new Font("微软雅黑", 12, FontStyle.Regular),
                    ForeColor = Color.DimGray,
                    AutoSize = true,
                    BackColor = Color.Transparent
                };

                lbl.Location = new Point(
                    (_loadingOverlay.Width - lbl.Width) / 2,
                    (_loadingOverlay.Height - lbl.Height) / 2
                );

                _loadingOverlay.Controls.Add(lbl);
                _form.Controls.Add(_loadingOverlay);
                _loadingOverlay.BringToFront();
                _loadingOverlay.Visible = true;

                StartFadeInAnimation();
            });
        }

        private void StartFadeInAnimation()
        {
            _currentLoadingTimer?.Stop();
            _currentLoadingTimer?.Dispose();

            var timer = new System.Windows.Forms.Timer { Interval = 20 };
            _currentLoadingTimer = timer;

            int alpha = 0;

            timer.Tick += (s, e) =>
            {
                if (_loadingOverlay == null || _loadingOverlay.IsDisposed)
                {
                    timer.Stop();
                    timer.Dispose();
                    _currentLoadingTimer = null;
                    return;
                }

                alpha += 20;
                if (alpha >= 160)
                {
                    alpha = 160;
                    timer.Stop();
                    timer.Dispose();
                    _currentLoadingTimer = null;
                }

                try
                {
                    _loadingOverlay.BackColor = Color.FromArgb(alpha, 255, 255, 255);
                }
                catch { }
            };

            timer.Start();
        }

        /// <summary>
        /// 隐藏加载提示
        /// </summary>
        public void HideLoadingIndicator()
        {
            SafeInvoke(_form, () =>
            {
                if (!_isShowingLoading || _loadingOverlay == null)
                    return;

                _isShowingLoading = false;

                var timer = new System.Windows.Forms.Timer { Interval = 20 };
                int alpha = 160;

                timer.Tick += (s, e) =>
                {
                    if (_loadingOverlay == null || _loadingOverlay.IsDisposed)
                    {
                        timer.Stop();
                        timer.Dispose();
                        return;
                    }

                    alpha -= 20;
                    if (alpha <= 0)
                    {
                        timer.Stop();
                        timer.Dispose();
                        CleanupCurrentLoadingOverlay();
                    }
                    else
                    {
                        try
                        {
                            _loadingOverlay.BackColor = Color.FromArgb(alpha, 255, 255, 255);
                        }
                        catch { }
                    }
                };

                timer.Start();
            });
        }

        /// <summary>
        /// 彻底清理当前加载遮罩
        /// </summary>
        private void CleanupCurrentLoadingOverlay()
        {
            _currentLoadingTimer?.Stop();
            _currentLoadingTimer?.Dispose();
            _currentLoadingTimer = null;

            if (_loadingOverlay != null)
            {
                try
                {
                    if (_form.Controls.Contains(_loadingOverlay))
                        _form.Controls.Remove(_loadingOverlay);

                    _loadingOverlay.Dispose();
                }
                catch { }
                finally
                {
                    _loadingOverlay = null;
                }
            }

            _isShowingLoading = false;
        }

        #endregion

        #region LCU连接状态管理

        public void SetLcuUiState(bool connected, bool inGame)
        {
            ClearAllStatusMessages(_form.panelMatchList);
            ClearAllStatusMessages(_form.penalGameMatchData);

            if (!connected)
            {
                SafeInvoke(_form.panelMatchList, () => ShowLcuNotConnectedMessage(_form.panelMatchList));
                SafeInvoke(_form.penalGameMatchData, () => ShowLcuNotConnectedMessage(_form.penalGameMatchData));
            }
            else if (!inGame)
            {
                SafeInvoke(_form.penalGameMatchData, () => ShowWaitingForGameMessage(_form.penalGameMatchData));
            }
        }

        private void ClearAllStatusMessages(Control parentControl)
        {
            SafeInvoke(parentControl, () =>
            {
                var statusPanels = parentControl.Controls.OfType<Panel>()
                    .Where(p => p != _form.tableLayoutPanel1 && p != _loadingOverlay)
                    .ToList();

                foreach (var panel in statusPanels)
                {
                    if (IsStatusPanel(panel))
                    {
                        parentControl.Controls.Remove(panel);
                        panel.Dispose();
                    }
                }
            });
        }

        private bool IsStatusPanel(Panel panel)
        {
            var labels = panel.Controls.OfType<Label>();
            if (labels.Any(l => l.Text.Contains("正在检测客户端连接") ||
                   l.Text.Contains("正在重新连接") ||
                   l.Text.Contains("正在等待加入游戏")))
            {
                return true;
            }

            if (panel.Controls.OfType<ProgressBar>().Any())
                return true;

            return false;
        }

        private Panel CreateStatusPanel(string message, bool showLolLauncher = false)
        {
            Panel containerPanel = new Panel
            {
                Width = 580,
                Height = 280,
                BackColor = Color.Transparent
            };

            Label label = new Label
            {
                Text = message,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 50,
                Font = new Font("微软雅黑", 12f, FontStyle.Bold)
            };

            ProgressBar progress = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                Width = 220,
                Height = 30,
                MarqueeAnimationSpeed = 30
            };
            progress.Left = (containerPanel.Width - progress.Width) / 2;
            progress.Top = label.Bottom + 10;

            containerPanel.Controls.Add(label);
            containerPanel.Controls.Add(progress);

            if (showLolLauncher)
            {
                LOLHelper helper = new LOLHelper();

                string officialPath = helper.GetOfficialLauncherPath();
                string wegamePath = helper.GetWeGameLauncherPath();
                string intlPath = helper.GetInternationalLauncherPath();

                bool hasOfficial = !string.IsNullOrEmpty(officialPath);
                bool hasWeGame = !string.IsNullOrEmpty(wegamePath);
                bool hasIntl = !string.IsNullOrEmpty(intlPath);
                bool hasChina = hasOfficial || hasWeGame;

                // ===== 控件 =====
                LinkLabel linkIntl = new LinkLabel
                {
                    AutoSize = true,
                    Font = new Font("微软雅黑", 9.5f),
                    Text = hasIntl ? intlPath : "未找到外服客户端",
                    LinkColor = Color.DodgerBlue
                };
                Button btnIntl = new Button
                {
                    Text = "启动外服版",
                    Width = 170,
                    Height = 36,
                    Enabled = hasIntl
                };

                LinkLabel linkOfficial = new LinkLabel
                {
                    AutoSize = true,
                    Font = new Font("微软雅黑", 9.5f),
                    Text = hasOfficial ? officialPath : "未找到国服官方客户端",
                    LinkColor = Color.DodgerBlue
                };
                Button btnOfficial = new Button
                {
                    Text = "启动国服官方版",
                    Width = 170,
                    Height = 36,
                    Enabled = hasOfficial
                };

                LinkLabel linkWeGame = new LinkLabel
                {
                    AutoSize = true,
                    Font = new Font("微软雅黑", 9.5f),
                    Text = hasWeGame ? wegamePath : "未找到 WeGame 版",
                    LinkColor = Color.DodgerBlue
                };
                Button btnWeGame = new Button
                {
                    Text = "启动 WeGame 版",
                    Width = 170,
                    Height = 36,
                    Enabled = hasWeGame
                };

                // 事件
                linkIntl.LinkClicked += (s, e) => OpenFolder(intlPath);
                linkOfficial.LinkClicked += (s, e) => OpenFolder(officialPath);
                linkWeGame.LinkClicked += (s, e) => OpenFolder(wegamePath);

                btnIntl.Click += (s, e) =>
                {
                    helper.StartInternationalClient();
                    if (hasIntl) helper.SaveInternationalPath(intlPath);
                };
                btnOfficial.Click += (s, e) =>
                {
                    helper.StartOfficialClient();
                    if (hasOfficial) helper.SaveCustomPath(officialPath);
                };
                btnWeGame.Click += (s, e) =>
                {
                    helper.StartWeGameClient();
                    if (hasWeGame) helper.SaveCustomPath(null, wegamePath);
                };

                int top = progress.Bottom + 18;
                int centerX = containerPanel.Width / 2;
                const int btnGap = 40;   // 两个按钮之间的间距

                // ========== 1. 外服（垂直：路径 → 按钮） ==========
                if (hasIntl)
                {
                    linkIntl.Left = Math.Max(10, (containerPanel.Width - linkIntl.PreferredWidth) / 2);
                    linkIntl.Top = top;
                    containerPanel.Controls.Add(linkIntl);

                    btnIntl.Left = centerX - btnIntl.Width / 2;
                    btnIntl.Top = linkIntl.Bottom + 6;
                    containerPanel.Controls.Add(btnIntl);

                    top = btnIntl.Bottom + 20;   // 和外服下面内容隔开一点
                }

                // ========== 2. 国服（路径上下排列，按钮左右排列） ==========
                if (hasChina)
                {
                    // 路径1（官方）
                    if (hasOfficial)
                    {
                        linkOfficial.Left = Math.Max(10, (containerPanel.Width - linkOfficial.PreferredWidth) / 2);
                        linkOfficial.Top = top;
                        containerPanel.Controls.Add(linkOfficial);
                        top = linkOfficial.Bottom + 4;
                    }

                    // 路径2（WeGame）
                    if (hasWeGame)
                    {
                        linkWeGame.Left = Math.Max(10, (containerPanel.Width - linkWeGame.PreferredWidth) / 2);
                        linkWeGame.Top = top;
                        containerPanel.Controls.Add(linkWeGame);
                        top = linkWeGame.Bottom + 8;
                    }

                    // 按钮行（左右隔开）
                    if (hasOfficial && hasWeGame)
                    {
                        // 两个都有 → 左右排列
                        int totalBtnWidth = btnOfficial.Width + btnGap + btnWeGame.Width;
                        int startX = centerX - totalBtnWidth / 2;

                        btnOfficial.Left = startX;
                        btnOfficial.Top = top;
                        containerPanel.Controls.Add(btnOfficial);

                        btnWeGame.Left = startX + btnOfficial.Width + btnGap;
                        btnWeGame.Top = top;
                        containerPanel.Controls.Add(btnWeGame);
                    }
                    else if (hasOfficial)
                    {
                        // 只有官方
                        btnOfficial.Left = centerX - btnOfficial.Width / 2;
                        btnOfficial.Top = top;
                        containerPanel.Controls.Add(btnOfficial);
                    }
                    else if (hasWeGame)
                    {
                        // 只有 WeGame
                        btnWeGame.Left = centerX - btnWeGame.Width / 2;
                        btnWeGame.Top = top;
                        containerPanel.Controls.Add(btnWeGame);
                    }
                }

                // ========== 3. 都没有 ==========
                if (!hasIntl && !hasChina)
                {
                    Label noClient = new Label
                    {
                        Text = "未检测到任何客户端，请手动启动游戏后再试",
                        AutoSize = true,
                        Font = new Font("微软雅黑", 9.5f),
                        ForeColor = Color.Gray
                    };
                    noClient.Left = (containerPanel.Width - noClient.PreferredWidth) / 2;
                    noClient.Top = progress.Bottom + 20;
                    containerPanel.Controls.Add(noClient);
                }

                // 动态高度
                int maxBottom = 0;
                foreach (Control c in containerPanel.Controls)
                    maxBottom = Math.Max(maxBottom, c.Bottom);

                containerPanel.Height = Math.Max(280, maxBottom + 20);
            }

            return containerPanel;
        }

        private void OpenFolder(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return;
            try
            {
                string? dir = Path.GetDirectoryName(exePath);
                if (Directory.Exists(dir))
                    Process.Start("explorer.exe", dir);
            }
            catch { }
        }

        private void ShowLcuNotConnectedMessage(Control parentControl)
        {
            parentControl.Controls.Clear();
            var panel = CreateStatusPanel("正在检测/重新连接客户端，请确保已登录游戏...", true);
            panel.Left = (parentControl.Width - panel.Width) / 2;
            panel.Top = (parentControl.Height - panel.Height) / 2;
            panel.Anchor = AnchorStyles.None;
            parentControl.Controls.Add(panel);
        }

        private void ShowWaitingForGameMessage(Control parentControl)
        {
            if (_waitingPanel != null)
            {
                if (parentControl.Controls.Contains(_waitingPanel))
                    parentControl.Controls.Remove(_waitingPanel);
                _waitingPanel.Dispose();
                _waitingPanel = null;
            }

            _form.tableLayoutPanel1.Visible = false;

            _waitingPanel = CreateStatusPanel("正在等待加入游戏，请稍后...", false);
            _waitingPanel.Left = (parentControl.Width - _waitingPanel.Width) / 2;
            _waitingPanel.Top = (parentControl.Height - _waitingPanel.Height) / 2;
            _waitingPanel.Anchor = AnchorStyles.None;

            parentControl.Controls.Add(_waitingPanel);
        }

        #endregion

        #region Tab控制相关

        public void HandleImageTabControlMouseMove(object? sender, MouseEventArgs e)
        {
            var tabControl = sender as ImageTabControl;
            if (tabControl == null) return;

            for (int i = 0; i < tabControl.TabPages.Count; i++)
            {
                Rectangle r = tabControl.GetTabRect(i);
                if (r.Contains(e.Location))
                {
                    if (_lastIndex != i)
                    {
                        _lastIndex = i;
                        Point clientPos = tabControl.PointToClient(Cursor.Position);
                        _toolTip.Show(tabControl.TabPages[i].Text, tabControl,
                            clientPos.X + 10, clientPos.Y + 10, 1500);
                    }
                    return;
                }
            }

            _toolTip.SetToolTip(tabControl, null);
            _lastIndex = -1;
        }

        public void HandleTabSelectionChanged(int selectedIndex, Poller tab1Poller)
        {
            switch (selectedIndex)
            {
                case 0:
                    tab1Poller.Stop();
                    break;
                case 1:
                    StartTab1Polling(tab1Poller);
                    break;
                case 2:
                    tab1Poller.Stop();
                    break;
            }
        }

        private void StartTab1Polling(Poller poller)
        {
            poller.Start(async () =>
            {
                try
                {
                    SetLcuUiState(LcuReady, IsGame);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Tab1Poller轮询异常: {ex}");
                }
            }, 3000);
        }

        #endregion

        #region 工具方法

        public static void SafeInvoke(Control? control, Action action)
        {
            if (control == null || control.IsDisposed || action == null)
                return;

            if (control.IsHandleCreated)
            {
                if (control.InvokeRequired)
                {
                    try
                    {
                        control.BeginInvoke(action);
                    }
                    catch { }
                }
                else
                {
                    try { action(); }
                    catch (Exception ex) { Debug.WriteLine($"[SafeInvoke] {ex.Message}"); }
                }
            }
            else
            {
                try { action(); }
                catch (Exception ex) { Debug.WriteLine($"[SafeInvoke] {ex.Message}"); }
            }
        }

        // 同步执行 UI方法
        public static void SafeInvokeSync(Control? control, Action action)
        {
            if (control == null || control.IsDisposed || action == null)
                return;
            if (control.IsHandleCreated && control.InvokeRequired)
            {
                try { control.Invoke(action); }
                catch (Exception ex) { Debug.WriteLine($"[SafeInvokeSync] {ex.Message}"); }
            }
            else
            {
                try { action(); }
                catch (Exception ex) { Debug.WriteLine($"[SafeInvokeSync] {ex.Message}"); }
            }
        }

        public static void SafeInvoke(Form form, Action action)
        {
            SafeInvoke((Control)form, action);
        }

        #endregion
    }
}