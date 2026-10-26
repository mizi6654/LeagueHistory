using League.Controls;

namespace League
{
    partial class FormMain
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormMain));
            panel1 = new Panel();
            statusStrip1 = new StatusStrip();
            toolStripStatusLabelConnection = new ToolStripStatusLabel();
            toolStripStatusLabelTip = new ToolStripStatusLabel();
            imageTabControl1 = new ImageTabControl();
            tabPage1 = new TabPage();
            panelTab1 = new Panel();
            btn_search = new Button();
            label1 = new Label();
            panelMatchList = new Panel();
            txtGameName = new TextBox();
            tabPage2 = new TabPage();
            penalGameMatchData = new Panel();
            tableLayoutPanel1 = new TableLayoutPanel();
            tabPage3 = new TabPage();
            tabControl1 = new TabControl();
            tabPage4 = new TabPage();
            groupBox3 = new GroupBox();
            label9 = new Label();
            cboCardMatchCount = new ComboBox();
            cboAutoAcceptDelay = new ComboBox();
            lblAutoAcceptDelay = new Label();
            checkBoxFilterMode = new CheckBox();
            chkAutoAccept = new CheckBox();
            chkSkipEndOfGameStats = new CheckBox();
            chkSkipHonor = new CheckBox();
            groupBox2 = new GroupBox();
            label7 = new Label();
            label6 = new Label();
            btnCloseClients = new Button();
            btnRestartUx = new Button();
            lk_QQGroup = new LinkLabel();
            label5 = new Label();
            lk_GitPro = new LinkLabel();
            label4 = new Label();
            groupBox1 = new GroupBox();
            chkBot = new CheckBox();
            chkClassic = new CheckBox();
            chkAutoSkinSelect = new CheckBox();
            labChampionSkinBrowser = new LinkLabel();
            chkPickUrf = new CheckBox();
            chkUrf = new CheckBox();
            chkNexus = new CheckBox();
            chkAram = new CheckBox();
            chkRanked = new CheckBox();
            chkNormal = new CheckBox();
            lkbPreliminary = new LinkLabel();
            tabPage5 = new TabPage();
            label8 = new Label();
            groupBox4 = new GroupBox();
            txtHotkeyFullTeam = new TextBox();
            lblHotkeyFullTeam = new Label();
            txtHotkeyMyTeam = new TextBox();
            lblHotkeyMyTeam = new Label();
            txtHotkeyChampSelect = new TextBox();
            lblHotkeyChampSelect = new Label();
            chkUseChampionName = new CheckBox();
            chkHideSelf = new CheckBox();
            label3 = new Label();
            label2 = new Label();
            txtCustomContent = new TextBox();
            rbModeCustom = new RadioButton();
            rbModeMatch = new RadioButton();
            panel1.SuspendLayout();
            statusStrip1.SuspendLayout();
            imageTabControl1.SuspendLayout();
            tabPage1.SuspendLayout();
            panelTab1.SuspendLayout();
            tabPage2.SuspendLayout();
            penalGameMatchData.SuspendLayout();
            tabPage3.SuspendLayout();
            tabControl1.SuspendLayout();
            tabPage4.SuspendLayout();
            groupBox3.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBox1.SuspendLayout();
            tabPage5.SuspendLayout();
            groupBox4.SuspendLayout();
            SuspendLayout();
            // 
            // panel1
            // 
            panel1.BorderStyle = BorderStyle.FixedSingle;
            panel1.Controls.Add(statusStrip1);
            panel1.Controls.Add(imageTabControl1);
            panel1.Dock = DockStyle.Fill;
            panel1.Location = new Point(0, 0);
            panel1.Name = "panel1";
            panel1.Size = new Size(1247, 826);
            panel1.TabIndex = 12;
            // 
            // statusStrip1
            // 
            statusStrip1.Items.AddRange(new ToolStripItem[] { toolStripStatusLabelConnection, toolStripStatusLabelTip });
            statusStrip1.Location = new Point(0, 802);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(1245, 22);
            statusStrip1.TabIndex = 1;
            statusStrip1.Text = "statusStrip1";
            // 
            // toolStripStatusLabelConnection
            // 
            toolStripStatusLabelConnection.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 134);
            toolStripStatusLabelConnection.ForeColor = Color.Blue;
            toolStripStatusLabelConnection.Name = "toolStripStatusLabelConnection";
            toolStripStatusLabelConnection.Size = new Size(53, 17);
            toolStripStatusLabelConnection.Text = "检测中...";
            // 
            // toolStripStatusLabelTip
            // 
            toolStripStatusLabelTip.Name = "toolStripStatusLabelTip";
            toolStripStatusLabelTip.Size = new Size(1146, 17);
            toolStripStatusLabelTip.Spring = true;
            toolStripStatusLabelTip.TextAlign = ContentAlignment.MiddleRight;
            // 
            // imageTabControl1
            // 
            imageTabControl1.Alignment = TabAlignment.Left;
            imageTabControl1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            imageTabControl1.Controls.Add(tabPage1);
            imageTabControl1.Controls.Add(tabPage2);
            imageTabControl1.Controls.Add(tabPage3);
            imageTabControl1.DrawMode = TabDrawMode.OwnerDrawFixed;
            imageTabControl1.ItemSize = new Size(50, 50);
            imageTabControl1.Location = new Point(-1, 3);
            imageTabControl1.Multiline = true;
            imageTabControl1.Name = "imageTabControl1";
            imageTabControl1.SelectedIndex = 0;
            imageTabControl1.Size = new Size(1243, 796);
            imageTabControl1.SizeMode = TabSizeMode.Fixed;
            imageTabControl1.TabIndex = 0;
            imageTabControl1.Tag = "D:\\Develop\\csharp\\League\\bin\\Debug\\net8.0-windows\\Assets\\Defaults\\01.png";
            imageTabControl1.SelectedIndexChanged += imageTabControl1_SelectedIndexChanged;
            // 
            // tabPage1
            // 
            tabPage1.BackColor = SystemColors.Control;
            tabPage1.Controls.Add(panelTab1);
            tabPage1.Location = new Point(54, 4);
            tabPage1.Name = "tabPage1";
            tabPage1.Padding = new Padding(3);
            tabPage1.Size = new Size(1185, 788);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "查看历史";
            // 
            // panelTab1
            // 
            panelTab1.Controls.Add(btn_search);
            panelTab1.Controls.Add(label1);
            panelTab1.Controls.Add(panelMatchList);
            panelTab1.Controls.Add(txtGameName);
            panelTab1.Dock = DockStyle.Fill;
            panelTab1.Location = new Point(3, 3);
            panelTab1.Name = "panelTab1";
            panelTab1.Size = new Size(1179, 782);
            panelTab1.TabIndex = 9;
            // 
            // btn_search
            // 
            btn_search.Location = new Point(651, 5);
            btn_search.Name = "btn_search";
            btn_search.Size = new Size(75, 23);
            btn_search.TabIndex = 7;
            btn_search.Text = "查询";
            btn_search.UseVisualStyleBackColor = true;
            btn_search.Click += btn_search_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(358, 8);
            label1.Name = "label1";
            label1.RightToLeft = RightToLeft.No;
            label1.Size = new Size(104, 17);
            label1.TabIndex = 5;
            label1.Text = "请输入玩家名称：";
            // 
            // panelMatchList
            // 
            panelMatchList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            panelMatchList.AutoScroll = true;
            panelMatchList.Location = new Point(3, 34);
            panelMatchList.Name = "panelMatchList";
            panelMatchList.Size = new Size(1172, 755);
            panelMatchList.TabIndex = 8;
            // 
            // txtGameName
            // 
            txtGameName.Location = new Point(468, 5);
            txtGameName.Name = "txtGameName";
            txtGameName.RightToLeft = RightToLeft.No;
            txtGameName.Size = new Size(177, 23);
            txtGameName.TabIndex = 6;
            // 
            // tabPage2
            // 
            tabPage2.BackColor = SystemColors.Control;
            tabPage2.Controls.Add(penalGameMatchData);
            tabPage2.Location = new Point(54, 4);
            tabPage2.Name = "tabPage2";
            tabPage2.Padding = new Padding(3);
            tabPage2.Size = new Size(1185, 788);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "查看玩家";
            // 
            // penalGameMatchData
            // 
            penalGameMatchData.Controls.Add(tableLayoutPanel1);
            penalGameMatchData.Dock = DockStyle.Fill;
            penalGameMatchData.Location = new Point(3, 3);
            penalGameMatchData.Name = "penalGameMatchData";
            penalGameMatchData.Size = new Size(1179, 782);
            penalGameMatchData.TabIndex = 1;
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            tableLayoutPanel1.ColumnCount = 5;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            tableLayoutPanel1.Location = new Point(5, 51);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 2;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.Size = new Size(1170, 727);
            tableLayoutPanel1.TabIndex = 0;
            // 
            // tabPage3
            // 
            tabPage3.Controls.Add(tabControl1);
            tabPage3.Location = new Point(54, 4);
            tabPage3.Name = "tabPage3";
            tabPage3.Size = new Size(1185, 788);
            tabPage3.TabIndex = 2;
            tabPage3.Text = "功能配置";
            tabPage3.UseVisualStyleBackColor = true;
            // 
            // tabControl1
            // 
            tabControl1.Controls.Add(tabPage4);
            tabControl1.Controls.Add(tabPage5);
            tabControl1.Dock = DockStyle.Fill;
            tabControl1.Location = new Point(0, 0);
            tabControl1.Name = "tabControl1";
            tabControl1.SelectedIndex = 0;
            tabControl1.Size = new Size(1185, 788);
            tabControl1.TabIndex = 0;
            // 
            // tabPage4
            // 
            tabPage4.Controls.Add(groupBox3);
            tabPage4.Controls.Add(groupBox2);
            tabPage4.Controls.Add(lk_QQGroup);
            tabPage4.Controls.Add(label5);
            tabPage4.Controls.Add(lk_GitPro);
            tabPage4.Controls.Add(label4);
            tabPage4.Controls.Add(groupBox1);
            tabPage4.Location = new Point(4, 26);
            tabPage4.Name = "tabPage4";
            tabPage4.Padding = new Padding(3);
            tabPage4.Size = new Size(1177, 758);
            tabPage4.TabIndex = 0;
            tabPage4.Text = "功能辅助";
            tabPage4.UseVisualStyleBackColor = true;
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(label9);
            groupBox3.Controls.Add(cboCardMatchCount);
            groupBox3.Controls.Add(cboAutoAcceptDelay);
            groupBox3.Controls.Add(lblAutoAcceptDelay);
            groupBox3.Controls.Add(checkBoxFilterMode);
            groupBox3.Controls.Add(chkAutoAccept);
            groupBox3.Controls.Add(chkSkipEndOfGameStats);
            groupBox3.Controls.Add(chkSkipHonor);
            groupBox3.Location = new Point(16, 6);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(1077, 126);
            groupBox3.TabIndex = 15;
            groupBox3.TabStop = false;
            groupBox3.Text = "自动化功能";
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(306, 22);
            label9.Name = "label9";
            label9.Size = new Size(128, 17);
            label9.TabIndex = 17;
            label9.Text = "卡片战绩要查询的数量";
            // 
            // cboCardMatchCount
            // 
            cboCardMatchCount.DropDownStyle = ComboBoxStyle.DropDownList;
            cboCardMatchCount.FormattingEnabled = true;
            cboCardMatchCount.Items.AddRange(new object[] { "10场", "20场", "30场", "50场" });
            cboCardMatchCount.Location = new Point(443, 18);
            cboCardMatchCount.Name = "cboCardMatchCount";
            cboCardMatchCount.Size = new Size(79, 25);
            cboCardMatchCount.TabIndex = 16;
            // 
            // cboAutoAcceptDelay
            // 
            cboAutoAcceptDelay.DropDownStyle = ComboBoxStyle.DropDownList;
            cboAutoAcceptDelay.FormattingEnabled = true;
            cboAutoAcceptDelay.Items.AddRange(new object[] { "0秒（立即）", "5秒", "10秒" });
            cboAutoAcceptDelay.Location = new Point(306, 54);
            cboAutoAcceptDelay.Name = "cboAutoAcceptDelay";
            cboAutoAcceptDelay.Size = new Size(79, 25);
            cboAutoAcceptDelay.TabIndex = 15;
            // 
            // lblAutoAcceptDelay
            // 
            lblAutoAcceptDelay.AutoSize = true;
            lblAutoAcceptDelay.Location = new Point(196, 58);
            lblAutoAcceptDelay.Name = "lblAutoAcceptDelay";
            lblAutoAcceptDelay.Size = new Size(104, 17);
            lblAutoAcceptDelay.TabIndex = 14;
            lblAutoAcceptDelay.Text = "自动接受等待时间";
            // 
            // checkBoxFilterMode
            // 
            checkBoxFilterMode.AutoSize = true;
            checkBoxFilterMode.Location = new Point(22, 22);
            checkBoxFilterMode.Name = "checkBoxFilterMode";
            checkBoxFilterMode.Size = new Size(267, 21);
            checkBoxFilterMode.TabIndex = 3;
            checkBoxFilterMode.Text = "选人窗口卡片战绩显示是否根据游戏模式查询";
            checkBoxFilterMode.UseVisualStyleBackColor = true;
            checkBoxFilterMode.CheckedChanged += checkBoxFilterMode_CheckedChanged;
            // 
            // chkAutoAccept
            // 
            chkAutoAccept.AutoSize = true;
            chkAutoAccept.Location = new Point(22, 58);
            chkAutoAccept.Name = "chkAutoAccept";
            chkAutoAccept.Size = new Size(147, 21);
            chkAutoAccept.TabIndex = 7;
            chkAutoAccept.Text = "是否启用自动接受对局";
            chkAutoAccept.UseVisualStyleBackColor = true;
            // 
            // chkSkipEndOfGameStats
            // 
            chkSkipEndOfGameStats.AutoSize = true;
            chkSkipEndOfGameStats.Location = new Point(238, 99);
            chkSkipEndOfGameStats.Name = "chkSkipEndOfGameStats";
            chkSkipEndOfGameStats.Size = new Size(147, 21);
            chkSkipEndOfGameStats.TabIndex = 13;
            chkSkipEndOfGameStats.Text = "自动跳过结算统计界面";
            chkSkipEndOfGameStats.UseVisualStyleBackColor = true;
            // 
            // chkSkipHonor
            // 
            chkSkipHonor.AutoSize = true;
            chkSkipHonor.Location = new Point(22, 99);
            chkSkipHonor.Name = "chkSkipHonor";
            chkSkipHonor.Size = new Size(123, 21);
            chkSkipHonor.TabIndex = 12;
            chkSkipHonor.Text = "自动跳过点赞界面";
            chkSkipHonor.UseVisualStyleBackColor = true;
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(label7);
            groupBox2.Controls.Add(label6);
            groupBox2.Controls.Add(btnCloseClients);
            groupBox2.Controls.Add(btnRestartUx);
            groupBox2.Location = new Point(16, 354);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(1077, 116);
            groupBox2.TabIndex = 14;
            groupBox2.TabStop = false;
            groupBox2.Text = "UI辅助";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(125, 83);
            label7.Name = "label7";
            label7.Size = new Size(380, 17);
            label7.TabIndex = 7;
            label7.Text = "适用范围：退出游戏并结束还有进程占用资源，点击结束即可释放资源";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(125, 54);
            label6.Name = "label6";
            label6.Size = new Size(440, 17);
            label6.TabIndex = 6;
            label6.Text = "适用范围：大厅卡顿，选人窗口卡顿，读秒界面卡顿时使用，不需要登录就能重连";
            // 
            // btnCloseClients
            // 
            btnCloseClients.Location = new Point(9, 80);
            btnCloseClients.Name = "btnCloseClients";
            btnCloseClients.Size = new Size(101, 23);
            btnCloseClients.TabIndex = 3;
            btnCloseClients.Text = "退出客户端";
            btnCloseClients.UseVisualStyleBackColor = true;
            btnCloseClients.Click += btnCloseClients_Click;
            // 
            // btnRestartUx
            // 
            btnRestartUx.Location = new Point(9, 51);
            btnRestartUx.Name = "btnRestartUx";
            btnRestartUx.Size = new Size(101, 23);
            btnRestartUx.TabIndex = 5;
            btnRestartUx.Text = "重启客户端";
            btnRestartUx.Click += btnRestartUx_Click;
            // 
            // lk_QQGroup
            // 
            lk_QQGroup.AutoSize = true;
            lk_QQGroup.Location = new Point(73, 492);
            lk_QQGroup.Name = "lk_QQGroup";
            lk_QQGroup.Size = new Size(78, 17);
            lk_QQGroup.TabIndex = 11;
            lk_QQGroup.TabStop = true;
            lk_QQGroup.Text = "1035850775";
            lk_QQGroup.LinkClicked += lk_QQGroup_LinkClicked;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(15, 519);
            label5.Name = "label5";
            label5.Size = new Size(56, 17);
            label5.TabIndex = 10;
            label5.Text = "项目地址";
            // 
            // lk_GitPro
            // 
            lk_GitPro.AutoSize = true;
            lk_GitPro.Location = new Point(73, 519);
            lk_GitPro.Name = "lk_GitPro";
            lk_GitPro.Size = new Size(264, 17);
            lk_GitPro.TabIndex = 9;
            lk_GitPro.TabStop = true;
            lk_GitPro.Text = "https://github.com/mizi6654/LeagueHistory/";
            lk_GitPro.LinkClicked += lk_GitPro_LinkClicked;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(31, 492);
            label4.Name = "label4";
            label4.Size = new Size(40, 17);
            label4.TabIndex = 8;
            label4.Text = "QQ群";
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(chkBot);
            groupBox1.Controls.Add(chkClassic);
            groupBox1.Controls.Add(chkAutoSkinSelect);
            groupBox1.Controls.Add(labChampionSkinBrowser);
            groupBox1.Controls.Add(chkPickUrf);
            groupBox1.Controls.Add(chkUrf);
            groupBox1.Controls.Add(chkNexus);
            groupBox1.Controls.Add(chkAram);
            groupBox1.Controls.Add(chkRanked);
            groupBox1.Controls.Add(chkNormal);
            groupBox1.Controls.Add(lkbPreliminary);
            groupBox1.Location = new Point(16, 154);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(1077, 179);
            groupBox1.TabIndex = 6;
            groupBox1.TabStop = false;
            groupBox1.Text = "英雄预选 - 位置信息根据英雄联盟攻略中心数据划分";
            // 
            // chkBot
            // 
            chkBot.AutoSize = true;
            chkBot.Location = new Point(6, 141);
            chkBot.Name = "chkBot";
            chkBot.Size = new Size(147, 21);
            chkBot.TabIndex = 15;
            chkBot.Text = "启用普通模式人机预选";
            chkBot.UseVisualStyleBackColor = true;
            // 
            // chkClassic
            // 
            chkClassic.AutoSize = true;
            chkClassic.Location = new Point(6, 114);
            chkClassic.Name = "chkClassic";
            chkClassic.Size = new Size(171, 21);
            chkClassic.TabIndex = 14;
            chkClassic.Text = "启用经典模式预选，含人机";
            chkClassic.UseVisualStyleBackColor = true;
            // 
            // chkAutoSkinSelect
            // 
            chkAutoSkinSelect.AutoSize = true;
            chkAutoSkinSelect.Location = new Point(710, 60);
            chkAutoSkinSelect.Name = "chkAutoSkinSelect";
            chkAutoSkinSelect.Size = new Size(123, 21);
            chkAutoSkinSelect.TabIndex = 13;
            chkAutoSkinSelect.Text = "是否自动配置皮肤";
            chkAutoSkinSelect.UseVisualStyleBackColor = true;
            // 
            // labChampionSkinBrowser
            // 
            labChampionSkinBrowser.AutoSize = true;
            labChampionSkinBrowser.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            labChampionSkinBrowser.Location = new Point(710, 31);
            labChampionSkinBrowser.Name = "labChampionSkinBrowser";
            labChampionSkinBrowser.Size = new Size(361, 17);
            labChampionSkinBrowser.TabIndex = 12;
            labChampionSkinBrowser.TabStop = true;
            labChampionSkinBrowser.Text = "浏览已有英雄及皮肤 + 为每个英雄设置选默认皮肤（仅限已拥有）";
            labChampionSkinBrowser.LinkClicked += labChampionSkinBrowser_LinkClicked;
            // 
            // chkPickUrf
            // 
            chkPickUrf.AutoSize = true;
            chkPickUrf.Location = new Point(314, 89);
            chkPickUrf.Name = "chkPickUrf";
            chkPickUrf.Size = new Size(171, 21);
            chkPickUrf.TabIndex = 11;
            chkPickUrf.Text = "启用无限火力（自选）预选";
            chkPickUrf.UseVisualStyleBackColor = true;
            // 
            // chkUrf
            // 
            chkUrf.AutoSize = true;
            chkUrf.Location = new Point(314, 62);
            chkUrf.Name = "chkUrf";
            chkUrf.Size = new Size(171, 21);
            chkUrf.TabIndex = 10;
            chkUrf.Text = "启用无限乱斗（随机）预选";
            chkUrf.UseVisualStyleBackColor = true;
            // 
            // chkNexus
            // 
            chkNexus.AutoSize = true;
            chkNexus.Location = new Point(132, 87);
            chkNexus.Name = "chkNexus";
            chkNexus.Size = new Size(147, 21);
            chkNexus.TabIndex = 9;
            chkNexus.Text = "启用海克斯大乱斗预选";
            chkNexus.UseVisualStyleBackColor = true;
            // 
            // chkAram
            // 
            chkAram.AutoSize = true;
            chkAram.Location = new Point(6, 87);
            chkAram.Name = "chkAram";
            chkAram.Size = new Size(111, 21);
            chkAram.TabIndex = 8;
            chkAram.Text = "启用大乱斗预选";
            chkAram.UseVisualStyleBackColor = true;
            // 
            // chkRanked
            // 
            chkRanked.AutoSize = true;
            chkRanked.Location = new Point(132, 62);
            chkRanked.Name = "chkRanked";
            chkRanked.Size = new Size(176, 21);
            chkRanked.TabIndex = 7;
            chkRanked.Text = "启用排位预选（单双/灵活）";
            chkRanked.UseVisualStyleBackColor = true;
            // 
            // chkNormal
            // 
            chkNormal.AutoSize = true;
            chkNormal.Location = new Point(6, 60);
            chkNormal.Name = "chkNormal";
            chkNormal.Size = new Size(99, 21);
            chkNormal.TabIndex = 6;
            chkNormal.Text = "启用匹配预选";
            chkNormal.UseVisualStyleBackColor = true;
            // 
            // lkbPreliminary
            // 
            lkbPreliminary.AutoSize = true;
            lkbPreliminary.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            lkbPreliminary.Location = new Point(6, 31);
            lkbPreliminary.Name = "lkbPreliminary";
            lkbPreliminary.Size = new Size(589, 17);
            lkbPreliminary.TabIndex = 5;
            lkbPreliminary.TabStop = true;
            lkbPreliminary.Text = "点击英雄预选/抢英雄配置，目前支持【峡谷人机、匹配、排位、大乱斗、海克斯乱斗、无线乱斗、怀旧峡谷】";
            lkbPreliminary.LinkClicked += lkbPreliminary_LinkClicked;
            // 
            // tabPage5
            // 
            tabPage5.Controls.Add(label8);
            tabPage5.Controls.Add(groupBox4);
            tabPage5.Controls.Add(chkUseChampionName);
            tabPage5.Controls.Add(chkHideSelf);
            tabPage5.Controls.Add(label3);
            tabPage5.Controls.Add(label2);
            tabPage5.Controls.Add(txtCustomContent);
            tabPage5.Controls.Add(rbModeCustom);
            tabPage5.Controls.Add(rbModeMatch);
            tabPage5.Location = new Point(4, 26);
            tabPage5.Name = "tabPage5";
            tabPage5.Padding = new Padding(3);
            tabPage5.Size = new Size(1177, 758);
            tabPage5.TabIndex = 1;
            tabPage5.Text = "消息发送";
            tabPage5.UseVisualStyleBackColor = true;
            // 
            // label8
            // 
            label8.Location = new Point(563, 310);
            label8.Name = "label8";
            label8.Size = new Size(598, 323);
            label8.TabIndex = 9;
            label8.Text = resources.GetString("label8.Text");
            // 
            // groupBox4
            // 
            groupBox4.Controls.Add(txtHotkeyFullTeam);
            groupBox4.Controls.Add(lblHotkeyFullTeam);
            groupBox4.Controls.Add(txtHotkeyMyTeam);
            groupBox4.Controls.Add(lblHotkeyMyTeam);
            groupBox4.Controls.Add(txtHotkeyChampSelect);
            groupBox4.Controls.Add(lblHotkeyChampSelect);
            groupBox4.Location = new Point(12, 72);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(545, 120);
            groupBox4.TabIndex = 7;
            groupBox4.TabStop = false;
            groupBox4.Text = "快捷键配置（点击输入框后按下要绑定的快捷键，支持 Ctrl+Fx...）";
            // 
            // txtHotkeyFullTeam
            // 
            txtHotkeyFullTeam.Location = new Point(204, 84);
            txtHotkeyFullTeam.Name = "txtHotkeyFullTeam";
            txtHotkeyFullTeam.ReadOnly = true;
            txtHotkeyFullTeam.Size = new Size(160, 23);
            txtHotkeyFullTeam.TabIndex = 5;
            // 
            // lblHotkeyFullTeam
            // 
            lblHotkeyFullTeam.AutoSize = true;
            lblHotkeyFullTeam.Location = new Point(7, 86);
            lblHotkeyFullTeam.Name = "lblHotkeyFullTeam";
            lblHotkeyFullTeam.Size = new Size(193, 17);
            lblHotkeyFullTeam.TabIndex = 4;
            lblHotkeyFullTeam.Text = "游戏内，发送全队战绩/消息快捷键";
            // 
            // txtHotkeyMyTeam
            // 
            txtHotkeyMyTeam.Location = new Point(204, 55);
            txtHotkeyMyTeam.Name = "txtHotkeyMyTeam";
            txtHotkeyMyTeam.ReadOnly = true;
            txtHotkeyMyTeam.Size = new Size(160, 23);
            txtHotkeyMyTeam.TabIndex = 3;
            // 
            // lblHotkeyMyTeam
            // 
            lblHotkeyMyTeam.AutoSize = true;
            lblHotkeyMyTeam.Location = new Point(7, 57);
            lblHotkeyMyTeam.Name = "lblHotkeyMyTeam";
            lblHotkeyMyTeam.Size = new Size(193, 17);
            lblHotkeyMyTeam.TabIndex = 2;
            lblHotkeyMyTeam.Text = "游戏内，发送我方战绩/消息快捷键";
            // 
            // txtHotkeyChampSelect
            // 
            txtHotkeyChampSelect.Location = new Point(204, 26);
            txtHotkeyChampSelect.Name = "txtHotkeyChampSelect";
            txtHotkeyChampSelect.ReadOnly = true;
            txtHotkeyChampSelect.Size = new Size(160, 23);
            txtHotkeyChampSelect.TabIndex = 1;
            // 
            // lblHotkeyChampSelect
            // 
            lblHotkeyChampSelect.AutoSize = true;
            lblHotkeyChampSelect.Location = new Point(7, 28);
            lblHotkeyChampSelect.Name = "lblHotkeyChampSelect";
            lblHotkeyChampSelect.Size = new Size(193, 17);
            lblHotkeyChampSelect.TabIndex = 0;
            lblHotkeyChampSelect.Text = "选人窗，发送我方战绩/消息快捷键";
            // 
            // chkUseChampionName
            // 
            chkUseChampionName.AutoSize = true;
            chkUseChampionName.Location = new Point(179, 11);
            chkUseChampionName.Name = "chkUseChampionName";
            chkUseChampionName.Size = new Size(303, 21);
            chkUseChampionName.TabIndex = 6;
            chkUseChampionName.Text = "发送时使用英雄名称（能防止名称屏蔽，限游戏内）";
            chkUseChampionName.UseVisualStyleBackColor = true;
            // 
            // chkHideSelf
            // 
            chkHideSelf.AutoSize = true;
            chkHideSelf.Location = new Point(12, 11);
            chkHideSelf.Name = "chkHideSelf";
            chkHideSelf.Size = new Size(135, 21);
            chkHideSelf.TabIndex = 5;
            chkHideSelf.Text = "发送战绩时隐藏自己";
            chkHideSelf.UseVisualStyleBackColor = true;
            // 
            // label3
            // 
            label3.Location = new Point(563, 198);
            label3.Name = "label3";
            label3.Size = new Size(598, 73);
            label3.TabIndex = 4;
            label3.Text = "发送自定义信息：\r\n· 在左侧输入要发送的内容，默认一行一条。\r\n· 选人阶段 / 游戏内按对应快捷键发送（与战绩模式使用同一套快捷键）。\r\n· 游戏内如需发到队伍或所有人，请先按 Tab 切换聊天频道。";
            // 
            // label2
            // 
            label2.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            label2.Location = new Point(563, 98);
            label2.Name = "label2";
            label2.Size = new Size(598, 81);
            label2.TabIndex = 3;
            label2.Text = "发送战绩信息：\r\n· 选人阶段：按「选人阶段发送」快捷键，把我方近期战绩/消息发到选人聊天窗\r\n· 游戏内：按「游戏内·我方」只发我方；   按「游戏内·全队」发我方+敌方\r\n· 具体按键以左侧「快捷键」配置为准（点击编辑框后按下要绑定的键即可修改）";
            // 
            // txtCustomContent
            // 
            txtCustomContent.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            txtCustomContent.Location = new Point(12, 198);
            txtCustomContent.Multiline = true;
            txtCustomContent.Name = "txtCustomContent";
            txtCustomContent.Size = new Size(545, 554);
            txtCustomContent.TabIndex = 2;
            // 
            // rbModeCustom
            // 
            rbModeCustom.AutoSize = true;
            rbModeCustom.Location = new Point(179, 38);
            rbModeCustom.Name = "rbModeCustom";
            rbModeCustom.Size = new Size(110, 21);
            rbModeCustom.TabIndex = 1;
            rbModeCustom.Text = "发送自定义信息";
            rbModeCustom.UseVisualStyleBackColor = true;
            // 
            // rbModeMatch
            // 
            rbModeMatch.AutoSize = true;
            rbModeMatch.Checked = true;
            rbModeMatch.Location = new Point(12, 38);
            rbModeMatch.Name = "rbModeMatch";
            rbModeMatch.Size = new Size(98, 21);
            rbModeMatch.TabIndex = 0;
            rbModeMatch.TabStop = true;
            rbModeMatch.Text = "发送战绩信息";
            rbModeMatch.UseVisualStyleBackColor = true;
            // 
            // FormMain
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1247, 826);
            Controls.Add(panel1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "FormMain";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "联盟视界 League v1.1.4";
            Load += FormMain_Load;
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            imageTabControl1.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            panelTab1.ResumeLayout(false);
            panelTab1.PerformLayout();
            tabPage2.ResumeLayout(false);
            penalGameMatchData.ResumeLayout(false);
            tabPage3.ResumeLayout(false);
            tabControl1.ResumeLayout(false);
            tabPage4.ResumeLayout(false);
            tabPage4.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            tabPage5.ResumeLayout(false);
            tabPage5.PerformLayout();
            groupBox4.ResumeLayout(false);
            groupBox4.PerformLayout();
            ResumeLayout(false);
        }

        #endregion
        private Button btn_search;
        private TextBox txtGameName;
        private Label label1;
        private Panel panel1;
        private TabPage tabPage1;
        private TabPage tabPage2;
        private TabPage tabPage3;
        private TabControl tabControl1;
        private TabPage tabPage4;
        private TabPage tabPage5;
        private CheckBox checkBoxFilterMode;
        private LinkLabel lkbPreliminary;
        public Panel panelMatchList;
        public Panel penalGameMatchData;
        public TableLayoutPanel tableLayoutPanel1;
        public ImageTabControl imageTabControl1;
        private GroupBox groupBox1;
        private CheckBox chkNexus;
        private CheckBox chkAram;
        private CheckBox chkRanked;
        private CheckBox chkNormal;
        private TextBox txtCustomContent;
        private RadioButton rbModeCustom;
        private RadioButton rbModeMatch;
        private Label label3;
        private Label label2;
        private CheckBox chkAutoAccept;
        private LinkLabel lk_GitPro;
        private Label label4;
        private LinkLabel lk_QQGroup;
        private Label label5;
        private CheckBox chkUseChampionName;
        private CheckBox chkHideSelf;
        private CheckBox chkSkipEndOfGameStats;
        private CheckBox chkSkipHonor;
        private GroupBox groupBox2;
        private Button btnCloseClients;
        private Button btnRestartUx;
        private Label label7;
        private Label label6;
        private GroupBox groupBox3;
        private Label lblHotkeyChampSelect;
        private Button btnDodgeNow;
        private ComboBox cboAutoAcceptDelay;
        private Label lblAutoAcceptDelay;
        private Label label9;
        private ComboBox cboCardMatchCount;
        private CheckBox chkPickUrf;
        private CheckBox chkUrf;
        private LinkLabel labChampionSkinBrowser;
        private CheckBox chkAutoSkinSelect;
        private CheckBox chkClassic;
        private CheckBox chkBot;
        private GroupBox groupBox4;
        private TextBox txtHotkeyChampSelect;
        private TextBox txtHotkeyFullTeam;
        private Label lblHotkeyFullTeam;
        private TextBox txtHotkeyMyTeam;
        private Label lblHotkeyMyTeam;
        private Panel panelTab1;
        private Label label8;
        private StatusStrip statusStrip1;
        private ToolStripStatusLabel toolStripStatusLabelConnection;
        private ToolStripStatusLabel toolStripStatusLabelTip;
    }
}
