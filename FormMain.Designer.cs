using League.uitls;

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
            linkLabel1 = new LinkLabel();
            panel1.SuspendLayout();
            imageTabControl1.SuspendLayout();
            tabPage1.SuspendLayout();
            tabPage2.SuspendLayout();
            penalGameMatchData.SuspendLayout();
            tabPage3.SuspendLayout();
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
            tableLayoutPanel1.Location = new Point(5, 4);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 2;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.Size = new Size(1160, 786);
            tableLayoutPanel1.TabIndex = 0;
            // 
            // tabPage3
            // 
            tabPage3.Controls.Add(linkLabel1);
            tabPage3.Location = new Point(54, 4);
            tabPage3.Name = "tabPage3";
            tabPage3.Size = new Size(1175, 800);
            tabPage3.TabIndex = 2;
            tabPage3.Text = "tabPage3";
            tabPage3.UseVisualStyleBackColor = true;
            // 
            // linkLabel1
            // 
            linkLabel1.AutoSize = true;
            linkLabel1.Location = new Point(3, 4);
            linkLabel1.Name = "linkLabel1";
            linkLabel1.Size = new Size(44, 17);
            linkLabel1.TabIndex = 0;
            linkLabel1.TabStop = true;
            linkLabel1.Text = "等开发";
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
            Text = "League v1.0.3";
            Load += FormMain_Load;
            panel1.ResumeLayout(false);
            imageTabControl1.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            tabPage1.PerformLayout();
            tabPage2.ResumeLayout(false);
            penalGameMatchData.ResumeLayout(false);
            tabPage3.ResumeLayout(false);
            tabPage3.PerformLayout();
            ResumeLayout(false);
        }

        #endregion
        private Button btn_search;
        private TextBox txtGameName;
        private Label label1;
        private Panel panel1;
        private ImageTabControl imageTabControl1;
        private TabPage tabPage1;
        private TabPage tabPage2;
        private TabPage tabPage3;
        private LinkLabel linkLabel1;
        private Panel panelMatchList;
        private TableLayoutPanel tableLayoutPanel1;
        private Panel penalGameMatchData;
    }
}
