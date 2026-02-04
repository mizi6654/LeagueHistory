using League.Controls;
using League.Models;
using System.Diagnostics;
using static League.FormMain;

namespace League.uitls
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
            if (e.Control is TabPage tabPage)
            {
                if (_tabPageContents.TryGetValue(tabPage, out var content))
                {
                    // 调用清理方法
                    content.ForceCleanup();
                    _tabPageContents.Remove(tabPage);
                }
            }
        }

        // 🔥 修改访问权限为 public
        public void CloseTab(TabPage tab)
        {
            string? puuid = tab.Tag as string;
            if (puuid != null && string.Equals(puuid, Globals.CurrentPuuid, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("此选项卡为自己数据，不能关闭", "提示", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                return;
            }

            // 1. 停止并清理内容控件
            if (_tabPageContents.TryGetValue(tab, out var content))
            {
                content.ForceCleanup();
                _tabPageContents.Remove(tab);
            }

            // 2. 移除 Tab 前先清理其内容
            foreach (Control control in tab.Controls)
            {
                if (control is IDisposable disposable)
                    disposable.Dispose();
            }
            tab.Controls.Clear();

            // 3. 移除 Tab
            MainTabControl.TabPages.Remove(tab);

            // 4. 完全释放 Tab 页
            tab.Dispose();

            // 5. 强制 GC
            GC.Collect(2, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();

            Debug.WriteLine($"[内存清理] Tab已关闭，当前内存使用: {Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024}MB");
        }

        public void CleanupAllTabs()
        {
            if (MainTabControl.TabPages.Count == 1 &&
                string.Equals(MainTabControl.TabPages[0].Tag as string, Globals.CurrentPuuid, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("检测到只剩自己 Tab，跳过 CleanupAllTabs");
                return;
            }

            // 先获取所有Tab页的副本
            var tabs = MainTabControl.TabPages.Cast<TabPage>().ToList();

            foreach (var tab in tabs)
            {
                CloseTab(tab);
            }

            _tabPageContents.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // 修改 CreateNewTab 方法，移除或修改上限逻辑
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
            // 检查是否已存在
            foreach (TabPage page in MainTabControl.TabPages)
            {
                if (string.Equals(page.Tag as string, puuid, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[Tab 已存在] puuid = {puuid}，切换到现有 Tab");
                    MainTabControl.SelectedTab = page;

                    if (_tabPageContents.TryGetValue(page, out var existingContent))
                    {
                        // 判断是否是当前玩家
                        bool isCurrentPlayer = string.Equals(puuid, Globals.CurrentPuuid, StringComparison.OrdinalIgnoreCase);

                        string fullGameName = $"{gameName}#{tagLine}";

                        // 更新段位信息
                        _ = existingContent.InitiaRank(fullGameName, profileIconId, summonerLevel, privacy, rankedStats);

                        // 如果是当前玩家，可以选择是否刷新数据
                        if (isCurrentPlayer)
                        {
                            Debug.WriteLine("[当前玩家] 已切换到当前玩家标签页");
                            // 可以选择性地刷新数据
                            // _ = existingContent.RefreshIfNeeded();
                        }
                    }
                    return;
                }
            }

            // 🔥 移除或修改上限逻辑
            // 可选：设置一个较大的上限，比如20个
            const int MAX_TABS = 20;
            if (MainTabControl.TabPages.Count >= MAX_TABS)
            {
                // 找到第一个非自己的Tab页
                TabPage oldestNonSelfTab = null;
                foreach (TabPage tab in MainTabControl.TabPages)
                {
                    string? tabPuuid = tab.Tag as string;
                    if (tabPuuid != null && !string.Equals(tabPuuid, Globals.CurrentPuuid, StringComparison.OrdinalIgnoreCase))
                    {
                        oldestNonSelfTab = tab;
                        break;
                    }
                }

                if (oldestNonSelfTab != null)
                {
                    CloseTab(oldestNonSelfTab);
                }
                else
                {
                    // 所有都是自己的Tab（理论上不可能，但做保护）
                    MessageBox.Show($"已达到最大Tab数量({MAX_TABS})，请先关闭一些标签页", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            // 创建新标签页
            var newTab = new TabPage($"{gameName}#{tagLine}")
            {
                Tag = puuid
            };

            // 创建内容控件
            var tabContent = new MatchTabPageContent();

            // 绑定数据加载方法
            tabContent.LoadDataRequested += async (p, beg, end, q) =>
            {
                return await ((FormMain)this.ParentForm).LoadFullMatchDataAsync(p, beg, end, q);
            };

            // 绑定解析事件
            tabContent.ParsePanelRequested += async (match, puuidParam, index) =>
            {
                return await ((FormMain)this.ParentForm).ParseGameToPanelAsync(match, summonerId, gameName, tagLine, index);
            };

            // 初始化段位信息
            string fullName = gameName + "#" + tagLine;
            tabContent.InitiaRank(fullName, profileIconId, summonerLevel, privacy, rankedStats);

            // 延迟加载比赛数据
            Task.Run(async () =>
            {
                try
                {
                    await tabContent.SafeInitializeAsync(puuid);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"延迟加载失败: {ex.Message}");
                }
            });

            // 添加控件
            newTab.Controls.Add(tabContent);
            MainTabControl.TabPages.Add(newTab);
            MainTabControl.SelectedTab = newTab;
            _tabPageContents[newTab] = tabContent;
        }
    }
}