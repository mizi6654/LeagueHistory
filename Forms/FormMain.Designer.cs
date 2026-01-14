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
            imageTabControl1 = new ImageTabControl();
            tabPage1 = new TabPage();
            panelMatchList = new Panel();
            btn_search = new Button();
            txtGameName = new TextBox();
            label1 = new Label();
            tabPage2 = new TabPage();
            penalGameMatchData = new Panel();
            tableLayoutPanel1 = new TableLayoutPanel();
            tabPage3 = new TabPage();
            tabControl1 = new TabControl();
            tabPage4 = new TabPage();
            groupBox1 = new GroupBox();
            chkNexus = new CheckBox();
            chkAram = new CheckBox();
            chkRanked = new CheckBox();
            chkNormal = new CheckBox();
            lkbPreliminary = new LinkLabel();
            checkBoxFilterMode = new CheckBox();
            tabPage5 = new TabPage();
            panel1.SuspendLayout();
            imageTabControl1.SuspendLayout();
            tabPage1.SuspendLayout();
            tabPage2.SuspendLayout();
            penalGameMatchData.SuspendLayout();
            tabPage3.SuspendLayout();
            tabControl1.SuspendLayout();
            tabPage4.SuspendLayout();
            groupBox1.SuspendLayout();
            SuspendLayout();
            // 
            // panel1
            // 
            panel1.BorderStyle = BorderStyle.FixedSingle;
            panel1.Controls.Add(imageTabControl1);
            panel1.Dock = DockStyle.Fill;
            panel1.Location = new Point(0, 0);
            panel1.Name = "panel1";
            panel1.Size = new Size(1235, 810);
            panel1.TabIndex = 12;
            // 
            // imageTabControl1
            // 
            imageTabControl1.Alignment = TabAlignment.Left;
            imageTabControl1.Controls.Add(tabPage1);
            imageTabControl1.Controls.Add(tabPage2);
            imageTabControl1.Controls.Add(tabPage3);
            imageTabControl1.Dock = DockStyle.Fill;
            imageTabControl1.DrawMode = TabDrawMode.OwnerDrawFixed;
            imageTabControl1.ItemSize = new Size(50, 50);
            imageTabControl1.Location = new Point(0, 0);
            imageTabControl1.Multiline = true;
            imageTabControl1.Name = "imageTabControl1";
            imageTabControl1.SelectedIndex = 0;
            imageTabControl1.Size = new Size(1233, 808);
            imageTabControl1.SizeMode = TabSizeMode.Fixed;
            imageTabControl1.TabIndex = 0;
            imageTabControl1.Tag = "D:\\Develop\\csharp\\League\\bin\\Debug\\net8.0-windows\\Assets\\Defaults\\01.png";
            imageTabControl1.SelectedIndexChanged += imageTabControl1_SelectedIndexChanged;
            // 
            // tabPage1
            // 
            tabPage1.BackColor = SystemColors.Control;
            tabPage1.Controls.Add(panelMatchList);
            tabPage1.Controls.Add(btn_search);
            tabPage1.Controls.Add(txtGameName);
            tabPage1.Controls.Add(label1);
            tabPage1.Location = new Point(54, 4);
            tabPage1.Name = "tabPage1";
            tabPage1.Padding = new Padding(3);
            tabPage1.Size = new Size(1175, 800);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "查看历史";
            // 
            // panelMatchList
            // 
            panelMatchList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            panelMatchList.AutoScroll = true;
            panelMatchList.Location = new Point(6, 36);
            panelMatchList.Name = "panelMatchList";
            panelMatchList.Size = new Size(1166, 764);
            panelMatchList.TabIndex = 8;
            // 
            // btn_search
            // 
            btn_search.Location = new Point(581, 7);
            btn_search.Name = "btn_search";
            btn_search.Size = new Size(75, 23);
            btn_search.TabIndex = 7;
            btn_search.Text = "查询";
            btn_search.UseVisualStyleBackColor = true;
            btn_search.Click += btn_search_Click;
            // 
            // txtGameName
            // 
            txtGameName.Location = new Point(398, 7);
            txtGameName.Name = "txtGameName";
            txtGameName.RightToLeft = RightToLeft.No;
            txtGameName.Size = new Size(177, 23);
            txtGameName.TabIndex = 6;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(288, 10);
            label1.Name = "label1";
            label1.RightToLeft = RightToLeft.No;
            label1.Size = new Size(104, 17);
            label1.TabIndex = 5;
            label1.Text = "请输入玩家名称：";
            // 
            // tabPage2
            // 
            tabPage2.BackColor = SystemColors.Control;
            tabPage2.Controls.Add(penalGameMatchData);
            tabPage2.Location = new Point(54, 4);
            tabPage2.Name = "tabPage2";
            tabPage2.Padding = new Padding(3);
            tabPage2.Size = new Size(1175, 800);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "查看玩家";
            // 
            // penalGameMatchData
            // 
            penalGameMatchData.Controls.Add(tableLayoutPanel1);
            penalGameMatchData.Dock = DockStyle.Fill;
            penalGameMatchData.Location = new Point(3, 3);
            penalGameMatchData.Name = "penalGameMatchData";
            penalGameMatchData.Size = new Size(1169, 794);
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
            tableLayoutPanel1.Size = new Size(1160, 739);
            tableLayoutPanel1.TabIndex = 0;
            // 
            // tabPage3
            // 
            tabPage3.Controls.Add(tabControl1);
            tabPage3.Location = new Point(54, 4);
            tabPage3.Name = "tabPage3";
            tabPage3.Size = new Size(1175, 800);
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
            tabControl1.Size = new Size(1175, 800);
            tabControl1.TabIndex = 0;
            // 
            // tabPage4
            // 
            tabPage4.Controls.Add(groupBox1);
            tabPage4.Controls.Add(checkBoxFilterMode);
            tabPage4.Location = new Point(4, 26);
            tabPage4.Name = "tabPage4";
            tabPage4.Padding = new Padding(3);
            tabPage4.Size = new Size(1167, 770);
            tabPage4.TabIndex = 0;
            tabPage4.Text = "功能辅助";
            tabPage4.UseVisualStyleBackColor = true;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(chkNexus);
            groupBox1.Controls.Add(chkAram);
            groupBox1.Controls.Add(chkRanked);
            groupBox1.Controls.Add(chkNormal);
            groupBox1.Controls.Add(lkbPreliminary);
            groupBox1.Location = new Point(16, 55);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(363, 124);
            groupBox1.TabIndex = 6;
            groupBox1.TabStop = false;
            groupBox1.Text = "英雄预选 - 位置信息根据英雄联盟攻略中心数据划分";
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
            lkbPreliminary.Size = new Size(344, 17);
            lkbPreliminary.TabIndex = 5;
            lkbPreliminary.TabStop = true;
            lkbPreliminary.Text = "点击英雄预选配置，目前支持匹配、排位、大乱斗、海克斯乱斗";
            lkbPreliminary.LinkClicked += lkbPreliminary_LinkClicked;
            // 
            // checkBoxFilterMode
            // 
            checkBoxFilterMode.AutoSize = true;
            checkBoxFilterMode.Location = new Point(16, 15);
            checkBoxFilterMode.Name = "checkBoxFilterMode";
            checkBoxFilterMode.Size = new Size(363, 21);
            checkBoxFilterMode.TabIndex = 3;
            checkBoxFilterMode.Text = "卡片战绩显示是否根据游戏模式查询，不勾选默认查询所有模式";
            checkBoxFilterMode.UseVisualStyleBackColor = true;
            checkBoxFilterMode.CheckedChanged += checkBoxFilterMode_CheckedChanged;
            // 
            // tabPage5
            // 
            tabPage5.Location = new Point(4, 26);
            tabPage5.Name = "tabPage5";
            tabPage5.Padding = new Padding(3);
            tabPage5.Size = new Size(1167, 770);
            tabPage5.TabIndex = 1;
            tabPage5.Text = "tabPage5";
            tabPage5.UseVisualStyleBackColor = true;
            // 
            // FormMain
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1235, 810);
            Controls.Add(panel1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "FormMain";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "League v1.0.6";
            Load += FormMain_Load;
            panel1.ResumeLayout(false);
            imageTabControl1.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            tabPage1.PerformLayout();
            tabPage2.ResumeLayout(false);
            penalGameMatchData.ResumeLayout(false);
            tabPage3.ResumeLayout(false);
            tabControl1.ResumeLayout(false);
            tabPage4.ResumeLayout(false);
            tabPage4.PerformLayout();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
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
    }
}
