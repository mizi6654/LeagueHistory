using League.Models;
using League.States;
using System.Diagnostics;
using static League.FormMain;

namespace League.Controls
{
    public partial class MatchTabContent : UserControl
    {
        private Dictionary<TabPage, MatchTabPageContent> _tabPageContents = new Dictionary<TabPage, MatchTabPageContent>();

        public ClosableTabControl MainTabControl => closableTabControl1;

        public MatchTabContent()
        {
            InitializeComponent();
            MainTabControl.ControlRemoved += OnTabPageRemoved;
        }

        private void OnTabPageRemoved(object sender, ControlEventArgs e)
        {
            if (e.Control is TabPage tabPage && _tabPageContents.TryGetValue(tabPage, out var content))
            {
                content.ForceCleanup();
                _tabPageContents.Remove(tabPage);
            }
        }

        #region Tab 判断辅助方法（加强版）

        private bool IsSelfTab(TabPage tab)
        {
            if (tab?.Tag == null)
                return false;

            string? tabPuuid = tab.Tag as string;
            string? currentPuuid = Globals.CurrentPuuid;

            if (string.IsNullOrWhiteSpace(tabPuuid) || string.IsNullOrWhiteSpace(currentPuuid))
                return false;

            return string.Equals(tabPuuid, currentPuuid, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        /// <summary>
        /// 正常关闭单个 Tab（保护自己）
        /// </summary>
        public void CloseTab(TabPage tab)
        {
            if (IsSelfTab(tab))
            {
                MessageBox.Show("此选项卡为自己数据，不能关闭", "提示", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                return;
            }

            PerformCloseTab(tab);
        }

        /// <summary>
        /// 强制关闭（窗体关闭时使用）
        /// </summary>
        private void CloseTabForce(TabPage tab)
        {
            PerformCloseTab(tab, force: true);
        }

        private void PerformCloseTab(TabPage tab, bool force = false)
        {
            if (tab == null) return;

            string? puuid = tab.Tag as string;
            Debug.WriteLine($"[CloseTab] 执行关闭 -> Tab: {tab.Text} | PUUID: {puuid} | Force: {force}");

            // 清理内容控件
            if (_tabPageContents.TryGetValue(tab, out var content))
            {
                content.ForceCleanup();
                _tabPageContents.Remove(tab);
            }

            // 清理子控件
            foreach (Control c in tab.Controls)
                if (c is IDisposable d) d.Dispose();
            tab.Controls.Clear();

            // 移除并释放
            MainTabControl.TabPages.Remove(tab);
            tab.Dispose();
        }

        /// <summary>
        /// 清理所有 Tab
        /// </summary>
        public void CleanupAllTabs(bool isFormClosing = false)
        {
            Debug.WriteLine($"[CleanupAllTabs] 调用 | 当前Tab数: {MainTabControl.TabPages.Count} | FormClosing={isFormClosing}");

            if (isFormClosing)
            {
                var tabs = MainTabControl.TabPages.Cast<TabPage>().ToList();
                foreach (var tab in tabs)
                {
                    CloseTabForce(tab);   // 强制关闭所有，包括自己
                }
                _tabPageContents.Clear();
                Debug.WriteLine("[CleanupAllTabs] 窗体关闭清理完成");
                return;
            }

            // 普通模式：保留自己
            if (MainTabControl.TabPages.Count == 1 && IsSelfTab(MainTabControl.TabPages[0]))
            {
                Debug.WriteLine("只剩自己 Tab，跳过 CleanupAllTabs");
                return;
            }

            var tabsToClose = MainTabControl.TabPages.Cast<TabPage>()
                                .Where(t => !IsSelfTab(t))
                                .ToList();

            foreach (var tab in tabsToClose)
            {
                CloseTab(tab);
            }

            _tabPageContents.Clear();
        }

        /// <summary>
        /// 创建新 Tab
        /// </summary>
        public void CreateNewTab(
            string summonerId,
            string gameName,
            string tagLine,
            string puuid,
            string profileIconId,
            string summonerLevel,
            string privacy,
            Dictionary<string, RankedStats> rankedStats)
        {
            if (string.IsNullOrWhiteSpace(puuid))
            {
                Debug.WriteLine("[CreateNewTab] PUUID为空，拒绝创建");
                return;
            }

            // 检查是否已存在
            foreach (TabPage page in MainTabControl.TabPages)
            {
                if (string.Equals(page.Tag as string, puuid, StringComparison.OrdinalIgnoreCase))
                {
                    MainTabControl.SelectedTab = page;
                    Debug.WriteLine($"[Tab 已存在] 切换到: {puuid}");

                    if (_tabPageContents.TryGetValue(page, out var existingContent))
                    {
                        bool isCurrentPlayer = string.Equals(puuid, Globals.CurrentPuuid, StringComparison.OrdinalIgnoreCase);

                        if (RefreshState.ForceMatchRefresh && isCurrentPlayer)
                        {
                            Debug.WriteLine($"[强制刷新] 检测到 ForceMatchRefresh=true，正在刷新当前玩家战绩");

                            existingContent.SetNeedsRefresh(true);   // 确保 MatchTabPageContent 有这个方法

                            // 延迟执行刷新
                            Task.Delay(800).ContinueWith(async _ =>
                            {
                                try
                                {
                                    if (existingContent.NeedsRefresh)
                                    {
                                        await existingContent.ForceRefreshData(puuid);
                                        existingContent.SetNeedsRefresh(false);
                                        RefreshState.ForceMatchRefresh = false;   // 刷新完重置
                                        Debug.WriteLine("[强制刷新] 当前玩家战绩刷新完成");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[强制刷新异常] {ex}");
                                }
                            }, TaskScheduler.FromCurrentSynchronizationContext());
                        }
                    }
                    return;
                }
            }

            // Tab上限处理
            const int MAX_TABS = 20;
            if (MainTabControl.TabPages.Count >= MAX_TABS)
            {
                var oldestNonSelf = MainTabControl.TabPages.Cast<TabPage>()
                    .FirstOrDefault(t => !IsSelfTab(t));

                if (oldestNonSelf != null)
                    CloseTab(oldestNonSelf);
                else
                {
                    MessageBox.Show($"已达到最大Tab数量({MAX_TABS})", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            // 创建新Tab
            var newTab = new TabPage($"{gameName}#{tagLine}") { Tag = puuid };

            var tabContent = new MatchTabPageContent();

            // 绑定事件...
            tabContent.LoadDataRequested += async (p, beg, end, q) =>
                await ((FormMain)this.ParentForm).LoadFullMatchDataAsync(p, beg, end, q);

            tabContent.ParsePanelRequested += async (match, puuidParam, index) =>
                await ((FormMain)this.ParentForm).ParseGameToPanelAsync(match, summonerId, gameName, tagLine, index);

            string fullName = $"{gameName}#{tagLine}";
            tabContent.InitiaRank(fullName, profileIconId, summonerLevel, privacy, rankedStats);

            // 异步加载数据
            Task.Run(async () =>
            {
                try { await tabContent.SafeInitializeAsync(puuid); }
                catch (Exception ex) { Debug.WriteLine($"Tab加载失败: {ex.Message}"); }
            });

            newTab.Controls.Add(tabContent);
            MainTabControl.TabPages.Add(newTab);
            MainTabControl.SelectedTab = newTab;

            _tabPageContents[newTab] = tabContent;

            Debug.WriteLine($"[CreateNewTab] 成功创建: {fullName} | PUUID: {puuid}");
        }
    }
}