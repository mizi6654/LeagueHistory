namespace League.uitls
{
    partial class MatchTabPageContent
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
            comboPage = new ComboBox();
            btnNext = new Button();
            btnPrev = new Button();
            comboFilter = new ComboBox();
            flowLayoutPanelRight = new FlowLayoutPanel();
            linkGameName = new LinkLabel();
            picChampionId = new PictureBox();
            lblLevel = new Label();
            groupBox1 = new GroupBox();
            lblSoloTier = new Label();
            lblSoloLeaguePoints = new Label();
            lblSoloWinRate = new Label();
            lblSoloLosses = new Label();
            lblSoloWins = new Label();
            lblSoloGames = new Label();
            label7 = new Label();
            label6 = new Label();
            label5 = new Label();
            label4 = new Label();
            label3 = new Label();
            label2 = new Label();
            groupBox2 = new GroupBox();
            lblFlexTier = new Label();
            lblFlexLeaguePoints = new Label();
            lblFlexWinRate = new Label();
            lblFlexLosses = new Label();
            lblFlexWins = new Label();
            lblFlexGames = new Label();
            label14 = new Label();
            label15 = new Label();
            label16 = new Label();
            label17 = new Label();
            label18 = new Label();
            label19 = new Label();
            lblPrivacy = new Label();
            label8 = new Label();
            ((System.ComponentModel.ISupportInitialize)picChampionId).BeginInit();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            SuspendLayout();
            // 
            // comboPage
            // 
            comboPage.FormattingEnabled = true;
            comboPage.Location = new Point(940, 5);
            comboPage.Name = "comboPage";
            comboPage.Size = new Size(90, 25);
            comboPage.TabIndex = 30;
            // 
            // btnNext
            // 
            btnNext.Location = new Point(859, 7);
            btnNext.Name = "btnNext";
            btnNext.Size = new Size(75, 23);
            btnNext.TabIndex = 29;
            btnNext.Text = "下一页";
            btnNext.UseVisualStyleBackColor = true;
            // 
            // btnPrev
            // 
            btnPrev.Location = new Point(778, 7);
            btnPrev.Name = "btnPrev";
            btnPrev.Size = new Size(75, 23);
            btnPrev.TabIndex = 28;
            btnPrev.Text = "上一页";
            btnPrev.UseVisualStyleBackColor = true;
            // 
            // comboFilter
            // 
            comboFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            comboFilter.FormattingEnabled = true;
            comboFilter.Location = new Point(1036, 5);
            comboFilter.Name = "comboFilter";
            comboFilter.Size = new Size(103, 25);
            comboFilter.TabIndex = 27;
            // 
            // flowLayoutPanelRight
            // 
            flowLayoutPanelRight.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            flowLayoutPanelRight.AutoScroll = true;
            flowLayoutPanelRight.Location = new Point(187, 36);
            flowLayoutPanelRight.Name = "flowLayoutPanelRight";
            flowLayoutPanelRight.RightToLeft = RightToLeft.No;
            flowLayoutPanelRight.Size = new Size(979, 676);
            flowLayoutPanelRight.TabIndex = 25;
            // 
            // linkGameName
            // 
            linkGameName.AutoSize = true;
            linkGameName.Location = new Point(34, 97);
            linkGameName.Name = "linkGameName";
            linkGameName.Size = new Size(44, 17);
            linkGameName.TabIndex = 31;
            linkGameName.TabStop = true;
            linkGameName.Text = "待查询";
            linkGameName.LinkClicked += linkGameName_LinkClicked;
            // 
            // picChampionId
            // 
            picChampionId.Location = new Point(3, 36);
            picChampionId.Name = "picChampionId";
            picChampionId.Size = new Size(50, 50);
            picChampionId.SizeMode = PictureBoxSizeMode.Zoom;
            picChampionId.TabIndex = 32;
            picChampionId.TabStop = false;
            // 
            // lblLevel
            // 
            lblLevel.AutoSize = true;
            lblLevel.Location = new Point(59, 69);
            lblLevel.Name = "lblLevel";
            lblLevel.Size = new Size(44, 17);
            lblLevel.TabIndex = 33;
            lblLevel.Text = "等级：";
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(lblSoloTier);
            groupBox1.Controls.Add(lblSoloLeaguePoints);
            groupBox1.Controls.Add(lblSoloWinRate);
            groupBox1.Controls.Add(lblSoloLosses);
            groupBox1.Controls.Add(lblSoloWins);
            groupBox1.Controls.Add(lblSoloGames);
            groupBox1.Controls.Add(label7);
            groupBox1.Controls.Add(label6);
            groupBox1.Controls.Add(label5);
            groupBox1.Controls.Add(label4);
            groupBox1.Controls.Add(label3);
            groupBox1.Controls.Add(label2);
            groupBox1.Location = new Point(3, 127);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(178, 226);
            groupBox1.TabIndex = 34;
            groupBox1.TabStop = false;
            groupBox1.Text = "单双排位";
            // 
            // lblSoloTier
            // 
            lblSoloTier.AutoSize = true;
            lblSoloTier.Location = new Point(92, 28);
            lblSoloTier.Name = "lblSoloTier";
            lblSoloTier.Size = new Size(32, 17);
            lblSoloTier.TabIndex = 11;
            lblSoloTier.Text = "暂无";
            // 
            // lblSoloLeaguePoints
            // 
            lblSoloLeaguePoints.AutoSize = true;
            lblSoloLeaguePoints.Location = new Point(92, 193);
            lblSoloLeaguePoints.Name = "lblSoloLeaguePoints";
            lblSoloLeaguePoints.Size = new Size(32, 17);
            lblSoloLeaguePoints.TabIndex = 10;
            lblSoloLeaguePoints.Text = "暂无";
            // 
            // lblSoloWinRate
            // 
            lblSoloWinRate.AutoSize = true;
            lblSoloWinRate.Location = new Point(92, 160);
            lblSoloWinRate.Name = "lblSoloWinRate";
            lblSoloWinRate.Size = new Size(32, 17);
            lblSoloWinRate.TabIndex = 9;
            lblSoloWinRate.Text = "暂无";
            // 
            // lblSoloLosses
            // 
            lblSoloLosses.AutoSize = true;
            lblSoloLosses.Location = new Point(92, 127);
            lblSoloLosses.Name = "lblSoloLosses";
            lblSoloLosses.Size = new Size(32, 17);
            lblSoloLosses.TabIndex = 8;
            lblSoloLosses.Text = "暂无";
            // 
            // lblSoloWins
            // 
            lblSoloWins.AutoSize = true;
            lblSoloWins.Location = new Point(92, 94);
            lblSoloWins.Name = "lblSoloWins";
            lblSoloWins.Size = new Size(32, 17);
            lblSoloWins.TabIndex = 7;
            lblSoloWins.Text = "暂无";
            // 
            // lblSoloGames
            // 
            lblSoloGames.AutoSize = true;
            lblSoloGames.Location = new Point(92, 61);
            lblSoloGames.Name = "lblSoloGames";
            lblSoloGames.Size = new Size(32, 17);
            lblSoloGames.TabIndex = 6;
            lblSoloGames.Text = "暂无";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(22, 28);
            label7.Name = "label7";
            label7.Size = new Size(32, 17);
            label7.TabIndex = 5;
            label7.Text = "段位";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(22, 193);
            label6.Name = "label6";
            label6.Size = new Size(32, 17);
            label6.TabIndex = 4;
            label6.Text = "胜点";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(22, 160);
            label5.Name = "label5";
            label5.Size = new Size(32, 17);
            label5.TabIndex = 3;
            label5.Text = "胜率";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(22, 127);
            label4.Name = "label4";
            label4.Size = new Size(32, 17);
            label4.TabIndex = 2;
            label4.Text = "负场";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(22, 94);
            label3.Name = "label3";
            label3.Size = new Size(32, 17);
            label3.TabIndex = 1;
            label3.Text = "胜场";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(22, 61);
            label2.Name = "label2";
            label2.Size = new Size(32, 17);
            label2.TabIndex = 0;
            label2.Text = "场次";
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(lblFlexTier);
            groupBox2.Controls.Add(lblFlexLeaguePoints);
            groupBox2.Controls.Add(lblFlexWinRate);
            groupBox2.Controls.Add(lblFlexLosses);
            groupBox2.Controls.Add(lblFlexWins);
            groupBox2.Controls.Add(lblFlexGames);
            groupBox2.Controls.Add(label14);
            groupBox2.Controls.Add(label15);
            groupBox2.Controls.Add(label16);
            groupBox2.Controls.Add(label17);
            groupBox2.Controls.Add(label18);
            groupBox2.Controls.Add(label19);
            groupBox2.Location = new Point(3, 359);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(178, 226);
            groupBox2.TabIndex = 35;
            groupBox2.TabStop = false;
            groupBox2.Text = "灵活排位";
            // 
            // lblFlexTier
            // 
            lblFlexTier.AutoSize = true;
            lblFlexTier.Location = new Point(92, 28);
            lblFlexTier.Name = "lblFlexTier";
            lblFlexTier.Size = new Size(32, 17);
            lblFlexTier.TabIndex = 11;
            lblFlexTier.Text = "暂无";
            // 
            // lblFlexLeaguePoints
            // 
            lblFlexLeaguePoints.AutoSize = true;
            lblFlexLeaguePoints.Location = new Point(92, 193);
            lblFlexLeaguePoints.Name = "lblFlexLeaguePoints";
            lblFlexLeaguePoints.Size = new Size(32, 17);
            lblFlexLeaguePoints.TabIndex = 10;
            lblFlexLeaguePoints.Text = "暂无";
            // 
            // lblFlexWinRate
            // 
            lblFlexWinRate.AutoSize = true;
            lblFlexWinRate.Location = new Point(92, 160);
            lblFlexWinRate.Name = "lblFlexWinRate";
            lblFlexWinRate.Size = new Size(32, 17);
            lblFlexWinRate.TabIndex = 9;
            lblFlexWinRate.Text = "暂无";
            // 
            // lblFlexLosses
            // 
            lblFlexLosses.AutoSize = true;
            lblFlexLosses.Location = new Point(92, 127);
            lblFlexLosses.Name = "lblFlexLosses";
            lblFlexLosses.Size = new Size(32, 17);
            lblFlexLosses.TabIndex = 8;
            lblFlexLosses.Text = "暂无";
            // 
            // lblFlexWins
            // 
            lblFlexWins.AutoSize = true;
            lblFlexWins.Location = new Point(92, 94);
            lblFlexWins.Name = "lblFlexWins";
            lblFlexWins.Size = new Size(32, 17);
            lblFlexWins.TabIndex = 7;
            lblFlexWins.Text = "暂无";
            // 
            // lblFlexGames
            // 
            lblFlexGames.AutoSize = true;
            lblFlexGames.Location = new Point(92, 61);
            lblFlexGames.Name = "lblFlexGames";
            lblFlexGames.Size = new Size(32, 17);
            lblFlexGames.TabIndex = 6;
            lblFlexGames.Text = "暂无";
            // 
            // label14
            // 
            label14.AutoSize = true;
            label14.Location = new Point(22, 28);
            label14.Name = "label14";
            label14.Size = new Size(32, 17);
            label14.TabIndex = 5;
            label14.Text = "段位";
            // 
            // label15
            // 
            label15.AutoSize = true;
            label15.Location = new Point(22, 193);
            label15.Name = "label15";
            label15.Size = new Size(32, 17);
            label15.TabIndex = 4;
            label15.Text = "胜点";
            // 
            // label16
            // 
            label16.AutoSize = true;
            label16.Location = new Point(22, 160);
            label16.Name = "label16";
            label16.Size = new Size(32, 17);
            label16.TabIndex = 3;
            label16.Text = "胜率";
            // 
            // label17
            // 
            label17.AutoSize = true;
            label17.Location = new Point(22, 127);
            label17.Name = "label17";
            label17.Size = new Size(32, 17);
            label17.TabIndex = 2;
            label17.Text = "负场";
            // 
            // label18
            // 
            label18.AutoSize = true;
            label18.Location = new Point(22, 94);
            label18.Name = "label18";
            label18.Size = new Size(32, 17);
            label18.TabIndex = 1;
            label18.Text = "胜场";
            // 
            // label19
            // 
            label19.AutoSize = true;
            label19.Location = new Point(22, 61);
            label19.Name = "label19";
            label19.Size = new Size(32, 17);
            label19.TabIndex = 0;
            label19.Text = "场次";
            // 
            // lblPrivacy
            // 
            lblPrivacy.AutoSize = true;
            lblPrivacy.Location = new Point(59, 36);
            lblPrivacy.Name = "lblPrivacy";
            lblPrivacy.Size = new Size(68, 17);
            lblPrivacy.TabIndex = 36;
            lblPrivacy.Text = "身份状态：";
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(4, 97);
            label8.Name = "label8";
            label8.Size = new Size(33, 17);
            label8.TabIndex = 39;
            label8.Text = "ID：";
            // 
            // MatchTabPageContent
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(label8);
            Controls.Add(flowLayoutPanelRight);
            Controls.Add(lblPrivacy);
            Controls.Add(groupBox2);
            Controls.Add(groupBox1);
            Controls.Add(lblLevel);
            Controls.Add(picChampionId);
            Controls.Add(linkGameName);
            Controls.Add(comboPage);
            Controls.Add(btnNext);
            Controls.Add(btnPrev);
            Controls.Add(comboFilter);
            Name = "MatchTabPageContent";
            Size = new Size(1200, 727);
            ((System.ComponentModel.ISupportInitialize)picChampionId).EndInit();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        public ComboBox comboPage;
        public Button btnNext;
        public Button btnPrev;
        public ComboBox comboFilter;
        public FlowLayoutPanel flowLayoutPanelRight;
        private LinkLabel linkGameName;
        private PictureBox picChampionId;
        private Label lblLevel;
        private GroupBox groupBox1;
        private Label label6;
        private Label label5;
        private Label label4;
        private Label label3;
        private Label label2;
        private Label label7;
        private Label lblSoloTier;
        private Label lblSoloLeaguePoints;
        private Label lblSoloWinRate;
        private Label lblSoloLosses;
        private Label lblSoloWins;
        private Label lblSoloGames;
        private GroupBox groupBox2;
        private Label lblFlexTier;
        private Label lblFlexLeaguePoints;
        private Label lblFlexWinRate;
        private Label lblFlexLosses;
        private Label lblFlexWins;
        private Label lblFlexGames;
        private Label label14;
        private Label label15;
        private Label label16;
        private Label label17;
        private Label label18;
        private Label label19;
        private Label lblPrivacy;
        private Label label8;
    }
}
