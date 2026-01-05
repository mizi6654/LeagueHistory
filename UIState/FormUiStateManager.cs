using League.Controls;
using League.Infrastructure;
using League.UIHelpers;
using System.Diagnostics;
using System.Windows.Forms;

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

        public bool IsGame { get; set; }
        public bool LcuReady { get; set; }

        public FormUiStateManager(FormMain form)
        {
            _form = form;
            InitializeToolTip();
        }

        private void InitializeToolTip()
        {
            _toolTip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 100,
                ReshowDelay = 100,
                ShowAlways = true
            };
        }

        #region 加载提示管理
        /// <summary>
        /// 显示轻柔半透明加载提示
        /// </summary>
        public void ShowLoadingIndicator()
        {
            SafeInvoke(_form, () =>
            {
                if (_loadingOverlay != null && !_loadingOverlay.IsDisposed)
                    return;

                // 灰色半透明遮罩
                _loadingOverlay = new Panel
                {
                    Size = new Size((int)(_form.Width * 0.6), (int)(_form.Height * 0.4)),
                    BackColor = Color.FromArgb(160, 255, 255, 255),
                    BorderStyle = BorderStyle.FixedSingle,
                    Visible = false
                };

                // 居中定位
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

                // 添加到窗体顶层
                _form.Controls.Add(_loadingOverlay);
                _loadingOverlay.BringToFront();

                // 渐显动画
                _loadingOverlay.Visible = true;
                _loadingOverlay.BackColor = Color.FromArgb(0, 255, 255, 255);

                var timer = new System.Windows.Forms.Timer { Interval = 20 };
                int alpha = 0;
                timer.Tick += (s, e) =>
                {
                    alpha += 15;
                    if (alpha >= 160)
                    {
                        alpha = 160;
                        timer.Stop();
                        timer.Dispose();
                    }
                    _loadingOverlay!.BackColor = Color.FromArgb(alpha, 255, 255, 255);
                };
                timer.Start();
            });
        }

        /// <summary>
        /// 隐藏加载提示
        /// </summary>
        public void HideLoadingIndicator()
        {
            SafeInvoke(_form, () =>
            {
                if (_loadingOverlay == null) return;

                var timer = new System.Windows.Forms.Timer { Interval = 20 };
                int alpha = 160;
                timer.Tick += (s, e) =>
                {
                    alpha -= 15;
                    if (alpha <= 0)
                    {
                        timer.Stop();
                        timer.Dispose();

                        _form.Controls.Remove(_loadingOverlay);
                        _loadingOverlay.Dispose();
                        _loadingOverlay = null;
                    }
                    else
                    {
                        _loadingOverlay.BackColor = Color.FromArgb(alpha, 255, 255, 255);
                    }
                };
                timer.Start();
            });
        }
        #endregion

        #region LCU连接状态管理
        /// <summary>
        /// 设置LCU连接状态
        /// </summary>
        public void SetLcuUiState(bool connected, bool inGame)
        {
            // 先清除两个tab的所有状态消息
            ClearAllStatusMessages(_form.panelMatchList);
            ClearAllStatusMessages(_form.penalGameMatchData);

            if (!connected)
            {
                SafeInvoke(_form.panelMatchList, () =>
                {
                    ShowLcuNotConnectedMessage(_form.panelMatchList);
                });
                SafeInvoke(_form.penalGameMatchData, () =>
                {
                    ShowLcuNotConnectedMessage(_form.penalGameMatchData);
                });
            }
            else
            {
                // 连接成功，第一个tab显示战绩（由其他代码处理）
                // 第二个tab根据是否在游戏中显示不同消息
                if (!inGame)
                {
                    SafeInvoke(_form.penalGameMatchData, () =>
                    {
                        ShowWaitingForGameMessage(_form.penalGameMatchData);
                    });
                }
                // 如果在游戏中（inGame = true），第二个tab会显示玩家卡片
            }
        }

        /// <summary>
        /// 清除所有状态消息（只清除状态面板，不碰其他控件）
        /// </summary>
        private void ClearAllStatusMessages(Control parentControl)
        {
            SafeInvoke(parentControl, () =>
            {
                // 只清除状态面板，不碰 tableLayoutPanel1
                var statusPanels = parentControl.Controls.OfType<Panel>()
                    .Where(p => p != _form.tableLayoutPanel1 && p != _loadingOverlay) // 排除重要控件
                    .ToList();

                foreach (var panel in statusPanels)
                {
                    // 进一步判断是否是状态面板（根据子控件或特性）
                    if (IsStatusPanel(panel))
                    {
                        parentControl.Controls.Remove(panel);
                        panel.Dispose();
                    }
                }
            });
        }

        /// <summary>
        /// 判断是否为状态面板
        /// </summary>
        private bool IsStatusPanel(Panel panel)
        {
            // 方法1：检查是否有特定标签或文本
            var labels = panel.Controls.OfType<Label>();
            if (labels.Any(l => l.Text.Contains("正在检测客户端连接") ||
                               l.Text.Contains("正在等待加入游戏")))
            {
                return true;
            }

            // 方法2：检查是否有进度条
            if (panel.Controls.OfType<ProgressBar>().Any())
            {
                return true;
            }

            // 方法3：检查面板尺寸或位置特性
            if (panel.Anchor == AnchorStyles.None &&
                panel.Width == 500 && panel.Height == 200)
            {
                return true;
            }

            return false;
        }

        private Panel CreateStatusPanel(string message, bool showLolLauncher = false)
        {
            Panel containerPanel = new Panel
            {
                Width = 500,
                Height = 200,
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
                Width = 200,
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
                string exePath = helper.GetLOLLoginExePath();
                LinkLabel linkLolPath = new LinkLabel
                {
                    AutoSize = true,
                    Text = string.IsNullOrEmpty(exePath) ? "未检测到 LOL 登录程序" : exePath,
                    Font = new Font("微软雅黑", 10f, FontStyle.Regular)
                };

                Button btnStartLol = new Button
                {
                    Text = "启动LOL登录程序",
                    Width = 200,
                    Height = 30
                };

                linkLolPath.Left = (containerPanel.Width - linkLolPath.PreferredWidth) / 2;
                linkLolPath.Top = progress.Bottom + 30;
                btnStartLol.Left = (containerPanel.Width - btnStartLol.Width) / 2;
                btnStartLol.Top = linkLolPath.Bottom + 10;
                containerPanel.Controls.Add(linkLolPath);
                containerPanel.Controls.Add(btnStartLol);

                if (!string.IsNullOrEmpty(exePath))
                {
                    linkLolPath.LinkClicked += delegate
                    {
                        string? directoryName = Path.GetDirectoryName(exePath);
                        if (Directory.Exists(directoryName))
                        {
                            Process.Start("explorer.exe", directoryName!);
                        }
                    };

                    btnStartLol.Click += delegate
                    {
                        linkLolPath.Text = exePath;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            Debug.WriteLine("找到 LOL 登录程序：" + exePath);
                            helper.StartLOLLoginProgram(exePath);
                        }
                        else
                        {
                            Debug.WriteLine("未检测到 LOL 登录程序！");
                        }
                    };
                }
            }

            return containerPanel;
        }

        private void ShowLcuNotConnectedMessage(Control parentControl)
        {
            parentControl.Controls.Clear();

            var panel = CreateStatusPanel(
                "正在检测客户端连接，请确保登录了游戏...",
                showLolLauncher: true
            );

            panel.Left = (parentControl.Width - panel.Width) / 2;
            panel.Top = (parentControl.Height - panel.Height) / 2;
            panel.Anchor = AnchorStyles.None;

            parentControl.Controls.Add(panel);
        }

        private void ShowWaitingForGameMessage(Control parentControl)
        {
            // 只清除等待面板，不清除其他控件
            if (_waitingPanel != null)
            {
                if (parentControl.Controls.Contains(_waitingPanel))
                {
                    parentControl.Controls.Remove(_waitingPanel);
                }
                _waitingPanel.Dispose();
                _waitingPanel = null;
            }

            // 隐藏tableLayoutPanel1，但不清除它
            _form.tableLayoutPanel1.Visible = false;

            _waitingPanel = CreateStatusPanel("正在等待加入游戏，请稍后...", showLolLauncher: false);

            _waitingPanel.Left = (parentControl.Width - _waitingPanel.Width) / 2;
            _waitingPanel.Top = (parentControl.Height - _waitingPanel.Height) / 2;
            _waitingPanel.Anchor = AnchorStyles.None;

            parentControl.Controls.Add(_waitingPanel);
        }
        #endregion

        #region Tab控制相关
        /// <summary>
        /// TabControl鼠标移动事件处理
        /// </summary>
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

        /// <summary>
        /// Tab切换事件处理
        /// </summary>
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
                    Debug.WriteLine("自动化设置");
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
                catch (TaskCanceledException)
                {
                    // 忽略
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Tab1Poller轮询异常: {ex}");
                }
            }, 3000);
        }
        #endregion

        #region 工具方法
        /// <summary>
        /// 线程安全的UI调用（彻底解决“在创建窗口句柄之前不能调用 Invoke”问题）
        /// </summary>
        public static void SafeInvoke(Control? control, Action action)
        {
            if (control == null || control.IsDisposed || action == null)
                return;

            // 第一优先级：如果句柄已创建，使用标准 Invoke 流程
            if (control.IsHandleCreated)
            {
                if (control.InvokeRequired)
                {
                    try
                    {
                        control.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                action();
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[SafeInvoke 内层异常] {ex.Message}");
                            }
                        }));
                    }
                    catch (InvalidOperationException)
                    {
                        // 控件正在销毁或句柄已失效，静默忽略
                    }
                }
                else
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SafeInvoke 直接执行异常] {ex.Message}");
                    }
                }
            }
            else
            {
                // 第二优先级：句柄未创建 → 此时必然在 UI 线程（因为非 UI 线程无法创建控件）
                // 直接执行是安全的，且必须执行（否则 UI 更新会丢失）
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SafeInvoke 句柄未创建时执行异常] {ex.Message}");
                }
            }
        }

        // 重载：支持直接传 Form
        public static void SafeInvoke(Form form, Action action)
        {
            SafeInvoke((Control)form, action);
        }
        #endregion
    }
}