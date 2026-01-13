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

            // 手动处理 Tab 页关闭
            MainTabControl.ControlRemoved += OnTabPageRemoved;
        }

        private void OnTabPageRemoved(object sender, ControlEventArgs e)
        {
            if (e.Control is TabPage tabPage)
            {
                // 当 Tab 页被移除时清理资源
                if (_tabPageContents.TryGetValue(tabPage, out var content))
                {
                    content.Cleanup();
                    _tabPageContents.Remove(tabPage);
                }
            }
        }

        private void CloseTab(TabPage tab)
        {
            string? puuid = tab.Tag as string;
            if (puuid != null && string.Equals(puuid, Globals.CurrentPuuid, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("此选项卡为自己数据，不能关闭", "提示", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                return;
            }

            if (_tabPageContents.TryGetValue(tab, out var content))
            {
                content.Cleanup();
                content.Dispose();
                _tabPageContents.Remove(tab);
            }
            MainTabControl.TabPages.Remove(tab);
            tab.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // 手动清理所有 Tab 页
        public void CleanupAllTabs()
        {
            // 如果只剩自己 Tab，就不清理
            if (MainTabControl.TabPages.Count == 1 &&
                string.Equals(MainTabControl.TabPages[0].Tag as string, Globals.CurrentPuuid, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("检测到只剩自己 Tab，跳过 CleanupAllTabs");
                return;
            }

            foreach (var tabContent in _tabPageContents.Values)
            {
                tabContent.Cleanup();
            }
            _tabPageContents.Clear();
            MainTabControl.TabPages.Clear();

            // 强制 GC 回收
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public void CreateNewTab(
            string summonerId,
            string gameName,
            string tagLine,
            string puuid,
            string profileIconId,
            string summonerLevel,
            string privacy, Dictionary<string, RankedStats> rankedStats)
        {

            // 检查是否已存在（只检查一次）
            foreach (TabPage page in MainTabControl.TabPages)
            {
                if (string.Equals(page.Tag as string, puuid, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[Tab 已存在] puuid = {puuid}，切换到现有 Tab");
                    MainTabControl.SelectedTab = page;

                    if (_tabPageContents.TryGetValue(page, out var existingContent))
                    {
                        string fullGameName = $"{gameName}#{tagLine}";
                        existingContent.InitiaRank(fullGameName, profileIconId, summonerLevel, privacy, rankedStats);
                        Task.Run(async () =>
                        {
                            try
                            {
                                await existingContent.SafeInitializeAsync(puuid);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"延迟加载失败: {ex.Message}");
                            }
                        });
                    }
                    return;
                }

                // 限制最大 Tab 数量（放在循环里，但建议移到外面更安全）
                if (MainTabControl.TabPages.Count >= 8)
                {
                    var oldestTab = MainTabControl.TabPages[0];
                    CloseTab(oldestTab);
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
                //return await ((FormMain)this.ParentForm).LoadMatchDataAsync(p, beg, end, q);
                return await ((FormMain)this.ParentForm).LoadFullMatchDataAsync(p, beg, end, q);
            };

            // 绑定解析事件（带puuid参数）
            tabContent.ParsePanelRequested += async (match, puuidParam,index) =>
            {
                return await ((FormMain)this.ParentForm).ParseGameToPanelAsync(match, summonerId,gameName, tagLine,index);
            };

            // 初始化段位信息（立即显示基础资料）
            string fullName = gameName + "#" + tagLine;
            tabContent.InitiaRank(fullName, profileIconId, summonerLevel, privacy, rankedStats);

            // 延迟加载比赛数据（不在主线程阻塞）
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
            _tabPageContents[newTab] = tabContent; // ❗你没有加这个，导致后续刷新失败

        }
    }
}
