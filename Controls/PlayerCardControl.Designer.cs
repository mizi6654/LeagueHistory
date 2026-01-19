namespace League.uitls
{
    partial class PlayerCardControl
    {
        /// <summary> 
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        /// <summary> 
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            picHero = new PictureBox();
            lblPlayerName = new LinkLabel();
            lblSoloRank = new Label();
            lblFlexRank = new Label();
            listViewGames = new ListView();
            columnHeader1 = new ColumnHeader();
            columnHeader2 = new ColumnHeader();
            columnHeader3 = new ColumnHeader();
            columnHeader4 = new ColumnHeader();
            lblPrivacyStatus = new Label();
            ((System.ComponentModel.ISupportInitialize)picHero).BeginInit();
            SuspendLayout();
            // 
            // picHero
            // 
            picHero.Location = new Point(3, 3);
            picHero.Name = "picHero";
            picHero.Size = new Size(40, 40);
            picHero.SizeMode = PictureBoxSizeMode.Zoom;
            picHero.TabIndex = 0;
            picHero.TabStop = false;
            // 
            // lblPlayerName
            // 
            lblPlayerName.AutoSize = true;
            lblPlayerName.Location = new Point(49, 3);
            lblPlayerName.Name = "lblPlayerName";
            lblPlayerName.Size = new Size(104, 17);
            lblPlayerName.TabIndex = 1;
            lblPlayerName.TabStop = true;
            lblPlayerName.Text = "对面五人是我徒弟";
            // 
            // lblSoloRank
            // 
            lblSoloRank.AutoSize = true;
            lblSoloRank.Location = new Point(49, 26);
            lblSoloRank.Name = "lblSoloRank";
            lblSoloRank.Size = new Size(44, 17);
            lblSoloRank.TabIndex = 2;
            lblSoloRank.Text = "未定级";
            // 
            // lblFlexRank
            // 
            lblFlexRank.AutoSize = true;
            lblFlexRank.Location = new Point(143, 26);
            lblFlexRank.Name = "lblFlexRank";
            lblFlexRank.Size = new Size(44, 17);
            lblFlexRank.TabIndex = 3;
            lblFlexRank.Text = "未定级";
            // 
            // listViewGames
            // 
            listViewGames.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            listViewGames.BorderStyle = BorderStyle.None;
            listViewGames.Columns.AddRange(new ColumnHeader[] { columnHeader1, columnHeader2, columnHeader3, columnHeader4 });
            listViewGames.FullRowSelect = true;
            listViewGames.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            listViewGames.Location = new Point(3, 49);
            listViewGames.Name = "listViewGames";
            listViewGames.Size = new Size(242, 278);
            listViewGames.TabIndex = 4;
            listViewGames.UseCompatibleStateImageBehavior = false;
            listViewGames.View = View.Details;
            // 
            // columnHeader1
            // 
            columnHeader1.Text = "英雄";
            columnHeader1.Width = 25;
            // 
            // columnHeader2
            // 
            columnHeader2.Text = "模式";
            columnHeader2.Width = 73;
            // 
            // columnHeader3
            // 
            columnHeader3.Text = "KDA";
            columnHeader3.Width = 65;
            // 
            // columnHeader4
            // 
            columnHeader4.Text = "时间";
            columnHeader4.Width = 45;
            // 
            // lblPrivacyStatus
            // 
            lblPrivacyStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblPrivacyStatus.AutoSize = true;
            lblPrivacyStatus.Location = new Point(176, 3);
            lblPrivacyStatus.Name = "lblPrivacyStatus";
            lblPrivacyStatus.Size = new Size(32, 17);
            lblPrivacyStatus.TabIndex = 5;
            lblPrivacyStatus.Text = "未知";
            // 
            // PlayerCardControl
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(lblPrivacyStatus);
            Controls.Add(listViewGames);
            Controls.Add(lblFlexRank);
            Controls.Add(lblSoloRank);
            Controls.Add(lblPlayerName);
            Controls.Add(picHero);
            Name = "PlayerCardControl";
            Size = new Size(248, 330);
            ((System.ComponentModel.ISupportInitialize)picHero).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Label lblSoloRank;
        private Label lblFlexRank;
        private ListView listViewGames;
        private ColumnHeader columnHeader1;
        private ColumnHeader columnHeader2;
        private ColumnHeader columnHeader3;
        private ColumnHeader columnHeader4;
        private Label lblPrivacyStatus;
        public LinkLabel lblPlayerName;
        public PictureBox picHero;
    }
}
