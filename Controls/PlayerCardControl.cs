using System.Diagnostics;

namespace League.Controls
{
    public partial class PlayerCardControl : UserControl
    {
        public bool IsLoading { get; private set; }
        // 用来精准判断“这个卡片到底属于谁、选了什么英雄”
        public long CurrentSummonerId { get; set; } = 0;
        public int CurrentChampionId { get; set; } = 0;
        public string CurrentPuuId { get; set; } ="";
        public int RetryCount { get; set; } = 0;

        // 保存完整玩家名称
        private string _fullPlayerName = "";
        private readonly ToolTip _nameToolTip = new ToolTip();   // 只创建一次，解决慢的问题

        public PlayerCardControl()
        {
            InitializeComponent();

            // 绑定点击事件
            lblPlayerName.LinkClicked += LblPlayerName_LinkClicked;
            lblPlayerName.Cursor = Cursors.Hand;   // 鼠标变成手型，提示可点击

            // 初始化 Tooltip 属性（可选优化）
            _nameToolTip.AutoPopDelay = 4000;
            _nameToolTip.InitialDelay = 300;      // 悬停多久开始显示（毫秒），越小越灵敏
            _nameToolTip.ReshowDelay = 200;
            _nameToolTip.ShowAlways = true;
        }

        public ListView ListViewControl
        {
            get { return listViewGames; }
        }

        public void SetAvatarOnly(Image avatar)
        {
            if (avatar != null && picHero != null)
            {
                picHero.Image = (Image)avatar.Clone();
            }
        }

        /// <summary>
        /// 设置玩家信息
        /// </summary>
        /// <param name="fullName">完整名称（带#标签），用于点击复制。如果为 null 则使用 playerName</param>
        public void SetPlayerInfo(string playerName, string soloRank, string flexRank, Image heroImage, string isPublic, List<ListViewItem> recentGames, Color nameColor, long summonerId = 0, int championId = 0, string puuid = "", string fullName = null)
        {
            // 保存关键标识（最重要！）
            CurrentSummonerId = summonerId;
            CurrentChampionId = championId;
            CurrentPuuId = puuid ?? "";

            // 保存完整名称
            _fullPlayerName = string.IsNullOrWhiteSpace(fullName) ? playerName : fullName;

            //显示短名字
            lblPlayerName.Text = playerName;

            // 设置 Tooltip（鼠标悬停提示）,只对真正有完整名称的玩家显示提示
            UpdateNameToolTip();

            // 设置同组队玩家颜色
            lblPlayerName.LinkColor = nameColor;
            lblPlayerName.VisitedLinkColor = nameColor;
            lblPlayerName.ActiveLinkColor = nameColor;

            // 设置加粗字体
            // 最好先保存原来的字体信息
            var oldFont = lblPlayerName.Font;

            // 重新创建粗体字体
            lblPlayerName.Font = new Font(
                oldFont.FontFamily,
                oldFont.Size,
                FontStyle.Bold
            );

            lblPlayerName.BorderStyle = BorderStyle.FixedSingle;

            lblSoloRank.Text = $"{soloRank}";
            lblFlexRank.Text = $"{flexRank}";
            lblPrivacyStatus.Text = $"{isPublic}";
            picHero.Image = heroImage;

            IsLoading = playerName.Contains("加载中") || soloRank.Contains("加载中");

            listViewGames.BeginUpdate();
            listViewGames.Items.Clear();

            if (recentGames != null)
            {
                // 使用克隆的 ListViewItem 防止重复引用
                foreach (var item in recentGames)
                {
                    listViewGames.Items.Add((ListViewItem)item.Clone());
                }
            }

            listViewGames.EndUpdate();
            listViewGames.Refresh();

            //Debug.WriteLine($"当前 listViewGames 中共有 {listViewGames.Items.Count} 个项");
        }

        /// <summary>
        /// 更新 Tooltip（统一入口，方便维护）
        /// </summary>
        private void UpdateNameToolTip()
        {
            // 不需要提示的情况
            if (string.IsNullOrWhiteSpace(_fullPlayerName) ||
                _fullPlayerName == "隐藏玩家" ||
                _fullPlayerName == "加载中..." ||
                _fullPlayerName == "失败" ||
                _fullPlayerName.Contains("隐藏") ||
                !_fullPlayerName.Contains("#"))   // 没有 # 标签的也不提示
            {
                _nameToolTip.SetToolTip(lblPlayerName, null);   // 清除提示
                return;
            }

            // 正常玩家才显示
            _nameToolTip.SetToolTip(lblPlayerName, $"点击复制完整名称：\n{_fullPlayerName}");
        }

        /// <summary>
        /// 点击玩家名字 → 复制完整名称到剪贴板
        /// </summary>
        private void LblPlayerName_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            // 隐藏/加载/失败状态不复制
            if (string.IsNullOrWhiteSpace(_fullPlayerName) ||
                _fullPlayerName == "隐藏玩家" ||
                _fullPlayerName == "加载中..." ||
                _fullPlayerName == "失败" ||
                _fullPlayerName.Contains("隐藏"))
            {
                return;
            }

            try
            {
                Clipboard.SetText(_fullPlayerName);

                // 可选反馈
                string originalText = lblPlayerName.Text;
                lblPlayerName.Text = "已复制!";
                Task.Delay(700).ContinueWith(_ =>
                {
                    if (!IsDisposed && lblPlayerName != null)
                    {
                        try
                        {
                            if (lblPlayerName.InvokeRequired)
                                lblPlayerName.Invoke(() => lblPlayerName.Text = originalText);
                            else
                                lblPlayerName.Text = originalText;
                        }
                        catch { }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PlayerCard] 复制名称失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 外部更新完整名称时调用
        /// </summary>
        public void SetFullPlayerName(string fullName)
        {
            _fullPlayerName = fullName ?? "";
            UpdateNameToolTip();   // 统一走这里
        }
    }
}
