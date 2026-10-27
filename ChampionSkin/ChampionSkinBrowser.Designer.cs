namespace League.ChampionSkin
{
    partial class ChampionSkinBrowser
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            label1 = new Label();
            txtSearch = new TextBox();
            groupBox1 = new GroupBox();
            label2 = new Label();
            BtnClearPrefs = new Button();
            chkHasSkinOnly = new CheckBox();
            lblStats = new Label();
            chkOwnedOnly = new CheckBox();
            btnSupport = new Button();
            btnAdc = new Button();
            btnJungle = new Button();
            btnMid = new Button();
            btnTop = new Button();
            btnAll = new Button();
            flpCards = new FlowLayoutPanel();
            groupBox1.SuspendLayout();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(16, 25);
            label1.Name = "label1";
            label1.Size = new Size(32, 17);
            label1.TabIndex = 0;
            label1.Text = "搜索";
            // 
            // txtSearch
            // 
            txtSearch.BorderStyle = BorderStyle.FixedSingle;
            txtSearch.Location = new Point(65, 22);
            txtSearch.Name = "txtSearch";
            txtSearch.Size = new Size(204, 23);
            txtSearch.TabIndex = 1;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(label2);
            groupBox1.Controls.Add(BtnClearPrefs);
            groupBox1.Controls.Add(chkHasSkinOnly);
            groupBox1.Controls.Add(lblStats);
            groupBox1.Controls.Add(chkOwnedOnly);
            groupBox1.Controls.Add(btnSupport);
            groupBox1.Controls.Add(btnAdc);
            groupBox1.Controls.Add(btnJungle);
            groupBox1.Controls.Add(btnMid);
            groupBox1.Controls.Add(btnTop);
            groupBox1.Controls.Add(btnAll);
            groupBox1.Controls.Add(txtSearch);
            groupBox1.Controls.Add(label1);
            groupBox1.Location = new Point(12, 12);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(1164, 99);
            groupBox1.TabIndex = 2;
            groupBox1.TabStop = false;
            groupBox1.Text = "查看英雄";
            // 
            // label2
            // 
            label2.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point, 134);
            label2.ForeColor = Color.IndianRed;
            label2.Location = new Point(766, 23);
            label2.Name = "label2";
            label2.Size = new Size(285, 58);
            label2.TabIndex = 12;
            label2.Text = "提示：点击哪个皮肤为高亮，并勾选了是否自动配置皮肤，那么在选人窗口时锁定英雄后就会自动切换高亮的皮肤";
            // 
            // BtnClearPrefs
            // 
            BtnClearPrefs.FlatStyle = FlatStyle.Popup;
            BtnClearPrefs.Location = new Point(599, 57);
            BtnClearPrefs.Name = "BtnClearPrefs";
            BtnClearPrefs.Size = new Size(156, 23);
            BtnClearPrefs.TabIndex = 11;
            BtnClearPrefs.Text = "清除皮肤预选恢复默认";
            BtnClearPrefs.UseVisualStyleBackColor = true;
            // 
            // chkHasSkinOnly
            // 
            chkHasSkinOnly.AutoSize = true;
            chkHasSkinOnly.Location = new Point(191, 62);
            chkHasSkinOnly.Name = "chkHasSkinOnly";
            chkHasSkinOnly.Size = new Size(135, 21);
            chkHasSkinOnly.TabIndex = 10;
            chkHasSkinOnly.Text = "仅显示有皮肤的英雄";
            chkHasSkinOnly.UseVisualStyleBackColor = true;
            // 
            // lblStats
            // 
            lblStats.AutoSize = true;
            lblStats.Location = new Point(365, 63);
            lblStats.Name = "lblStats";
            lblStats.Size = new Size(157, 17);
            lblStats.TabIndex = 9;
            lblStats.Text = "共：xxxx 英雄  /  xxxx 皮肤";
            // 
            // chkOwnedOnly
            // 
            chkOwnedOnly.AutoSize = true;
            chkOwnedOnly.Location = new Point(16, 62);
            chkOwnedOnly.Name = "chkOwnedOnly";
            chkOwnedOnly.Size = new Size(111, 21);
            chkOwnedOnly.TabIndex = 8;
            chkOwnedOnly.Text = "仅显示已有英雄";
            chkOwnedOnly.UseVisualStyleBackColor = true;
            // 
            // btnSupport
            // 
            btnSupport.FlatStyle = FlatStyle.Popup;
            btnSupport.Location = new Point(680, 22);
            btnSupport.Name = "btnSupport";
            btnSupport.Size = new Size(75, 23);
            btnSupport.TabIndex = 7;
            btnSupport.Text = "辅助";
            btnSupport.UseVisualStyleBackColor = true;
            // 
            // btnAdc
            // 
            btnAdc.FlatStyle = FlatStyle.Popup;
            btnAdc.Location = new Point(599, 22);
            btnAdc.Name = "btnAdc";
            btnAdc.Size = new Size(75, 23);
            btnAdc.TabIndex = 6;
            btnAdc.Text = "ADC";
            btnAdc.UseVisualStyleBackColor = true;
            // 
            // btnJungle
            // 
            btnJungle.FlatStyle = FlatStyle.Popup;
            btnJungle.Location = new Point(518, 22);
            btnJungle.Name = "btnJungle";
            btnJungle.Size = new Size(75, 23);
            btnJungle.TabIndex = 5;
            btnJungle.Text = "打野";
            btnJungle.UseVisualStyleBackColor = true;
            // 
            // btnMid
            // 
            btnMid.FlatStyle = FlatStyle.Popup;
            btnMid.Location = new Point(437, 22);
            btnMid.Name = "btnMid";
            btnMid.Size = new Size(75, 23);
            btnMid.TabIndex = 4;
            btnMid.Text = "中单";
            btnMid.UseVisualStyleBackColor = true;
            // 
            // btnTop
            // 
            btnTop.FlatStyle = FlatStyle.Popup;
            btnTop.Location = new Point(356, 22);
            btnTop.Name = "btnTop";
            btnTop.Size = new Size(75, 23);
            btnTop.TabIndex = 3;
            btnTop.Text = "上单";
            btnTop.UseVisualStyleBackColor = true;
            // 
            // btnAll
            // 
            btnAll.FlatStyle = FlatStyle.Popup;
            btnAll.Location = new Point(275, 22);
            btnAll.Name = "btnAll";
            btnAll.Size = new Size(75, 23);
            btnAll.TabIndex = 2;
            btnAll.Text = "全部";
            btnAll.UseVisualStyleBackColor = true;
            // 
            // flpCards
            // 
            flpCards.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            flpCards.AutoScroll = true;
            flpCards.Location = new Point(12, 117);
            flpCards.Name = "flpCards";
            flpCards.Padding = new Padding(3);
            flpCards.Size = new Size(1164, 559);
            flpCards.TabIndex = 3;
            // 
            // ChampionSkinBrowser
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1184, 688);
            Controls.Add(flpCards);
            Controls.Add(groupBox1);
            Name = "ChampionSkinBrowser";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "ChampionSkinBrowser";
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Label label1;
        private TextBox txtSearch;
        private GroupBox groupBox1;
        private Button btnSupport;
        private Button btnAdc;
        private Button btnJungle;
        private Button btnMid;
        private Button btnTop;
        private Button btnAll;
        private Label lblStats;
        private CheckBox chkOwnedOnly;
        private FlowLayoutPanel flpCards;
        private CheckBox chkHasSkinOnly;
        private Button BtnClearPrefs;
        private Label label2;
    }
}