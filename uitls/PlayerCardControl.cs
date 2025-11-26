namespace League.uitls
{
    public partial class PlayerCardControl : UserControl
    {
        public bool IsLoading { get; private set; }
        public PlayerCardControl()
        {
            InitializeComponent();
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

        //public void SetPlayerInfo(string playerName, string soloRank, string flexRank, Image heroImage, string isPublic, List<ListViewItem> recentGames, Color nameColor)
        //{
        //    //lblPlayerName 是一个LinkLabel控件
        //    lblPlayerName.Text = playerName;

        //    // 设置同组队玩家颜色
        //    lblPlayerName.LinkColor = nameColor;
        //    lblPlayerName.VisitedLinkColor = nameColor;
        //    lblPlayerName.ActiveLinkColor = nameColor;

        //    // 设置加粗字体
        //    // 最好先保存原来的字体信息
        //    var oldFont = lblPlayerName.Font;

        //    // 重新创建粗体字体
        //    lblPlayerName.Font = new Font(
        //        oldFont.FontFamily,
        //        oldFont.Size,
        //        FontStyle.Bold
        //    );

        //    lblPlayerName.BorderStyle = BorderStyle.FixedSingle;

        //    lblSoloRank.Text = $"{soloRank}";
        //    lblFlexRank.Text = $"{flexRank}";
        //    lblPrivacyStatus.Text = $"{isPublic}";
        //    picHero.Image = heroImage;

        //    IsLoading = playerName.Contains("加载中") || soloRank.Contains("加载中");

        //    listViewGames.BeginUpdate();
        //    listViewGames.Items.Clear();

        //    if (recentGames != null)
        //    {
        //        // 使用克隆的 ListViewItem 防止重复引用
        //        foreach (var item in recentGames)
        //        {
        //            listViewGames.Items.Add((ListViewItem)item.Clone());
        //        }
        //    }

        //    listViewGames.EndUpdate();
        //    listViewGames.Refresh();

        //    //Debug.WriteLine($"当前 listViewGames 中共有 {listViewGames.Items.Count} 个项");
        //}

        //带布局调整，让里面的列随着窗体变化显示宽度
        public void SetPlayerInfo(string playerName, string soloRank, string flexRank, Image heroImage, string isPublic, List<ListViewItem> recentGames, Color nameColor)
        {
            // 确保在UI线程执行
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<string, string, string, Image, string, List<ListViewItem>, Color>(
                    SetPlayerInfo), playerName, soloRank, flexRank, heroImage, isPublic, recentGames, nameColor);
                return;
            }

            lblPlayerName.Text = playerName;

            // 设置同组队玩家颜色
            lblPlayerName.LinkColor = nameColor;
            lblPlayerName.VisitedLinkColor = nameColor;
            lblPlayerName.ActiveLinkColor = nameColor;

            // 设置加粗字体
            var oldFont = lblPlayerName.Font;
            lblPlayerName.Font = new Font(oldFont.FontFamily, oldFont.Size, FontStyle.Bold);
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
                foreach (var item in recentGames)
                {
                    listViewGames.Items.Add((ListViewItem)item.Clone());
                }
            }

            listViewGames.EndUpdate();

            // 关键修改：自动调整列宽（确保在UI线程）
            listViewGames.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);

            listViewGames.Refresh();
        }

        // 可选：添加百分比分配方法
        private void AdjustListViewColumns()
        {
            if (listViewGames.Width > 0 && listViewGames.Columns.Count == 3)
            {
                int totalWidth = listViewGames.Width - 20; // 减去滚动条宽度
                                                           // 分配比例：模式45%，KDA35%，时间20%
                listViewGames.Columns[0].Width = (int)(totalWidth * 0.45);
                listViewGames.Columns[1].Width = (int)(totalWidth * 0.35);
                listViewGames.Columns[2].Width = (int)(totalWidth * 0.20);
            }
        }
    }
}
