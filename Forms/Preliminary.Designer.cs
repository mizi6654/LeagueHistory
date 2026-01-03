using League.uitls;

namespace League
{
    partial class Preliminary
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
            btnTop = new Button();
            btnMid = new Button();
            btnSupport = new Button();
            btnJungle = new Button();
            panel1 = new Panel();
            leftListView = new ListView();
            columnHeader1 = new ColumnHeader();
            columnHeader2 = new ColumnHeader();
            columnHeader5 = new ColumnHeader();
            columnHeader6 = new ColumnHeader();
            columnHeader9 = new ColumnHeader();
            panel2 = new Panel();
            rightListView = new ListView();
            columnHeader3 = new ColumnHeader();
            columnHeader4 = new ColumnHeader();
            columnHeader7 = new ColumnHeader();
            columnHeader8 = new ColumnHeader();
            columnHeader10 = new ColumnHeader();
            btnClear = new Button();
            btnADC = new Button();
            txtSearch = new TextBox();
            panel1.SuspendLayout();
            panel2.SuspendLayout();
            SuspendLayout();
            // 
            // btnTop
            // 
            btnTop.Location = new Point(12, 12);
            btnTop.Name = "btnTop";
            btnTop.Size = new Size(54, 23);
            btnTop.TabIndex = 0;
            btnTop.Text = "上单";
            btnTop.UseVisualStyleBackColor = true;
            // 
            // btnMid
            // 
            btnMid.Location = new Point(73, 12);
            btnMid.Name = "btnMid";
            btnMid.Size = new Size(54, 23);
            btnMid.TabIndex = 1;
            btnMid.Text = "中单";
            btnMid.UseVisualStyleBackColor = true;
            // 
            // btnSupport
            // 
            btnSupport.Location = new Point(195, 12);
            btnSupport.Name = "btnSupport";
            btnSupport.Size = new Size(54, 23);
            btnSupport.TabIndex = 3;
            btnSupport.Text = "辅助";
            btnSupport.UseVisualStyleBackColor = true;
            // 
            // btnJungle
            // 
            btnJungle.Location = new Point(134, 12);
            btnJungle.Name = "btnJungle";
            btnJungle.Size = new Size(54, 23);
            btnJungle.TabIndex = 2;
            btnJungle.Text = "打野";
            btnJungle.UseVisualStyleBackColor = true;
            // 
            // panel1
            // 
            panel1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            panel1.Controls.Add(leftListView);
            panel1.Location = new Point(12, 41);
            panel1.Name = "panel1";
            panel1.Size = new Size(561, 546);
            panel1.TabIndex = 4;
            // 
            // leftListView
            // 
            leftListView.Columns.AddRange(new ColumnHeader[] { columnHeader1, columnHeader2, columnHeader5, columnHeader6, columnHeader9 });
            leftListView.Dock = DockStyle.Fill;
            leftListView.FullRowSelect = true;
            leftListView.GridLines = true;
            leftListView.Location = new Point(0, 0);
            leftListView.Name = "leftListView";
            leftListView.Size = new Size(561, 546);
            leftListView.TabIndex = 0;
            leftListView.UseCompatibleStateImageBehavior = false;
            leftListView.View = View.Details;
            // 
            // columnHeader1
            // 
            columnHeader1.Text = "序";
            columnHeader1.Width = 50;
            // 
            // columnHeader2
            // 
            columnHeader2.Text = "英雄名称(中)";
            columnHeader2.Width = 90;
            // 
            // columnHeader5
            // 
            columnHeader5.Text = "简称";
            columnHeader5.Width = 120;
            // 
            // columnHeader6
            // 
            columnHeader6.Text = "英文名称";
            columnHeader6.Width = 150;
            // 
            // columnHeader9
            // 
            columnHeader9.Text = "位置";
            columnHeader9.Width = 130;
            // 
            // panel2
            // 
            panel2.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            panel2.Controls.Add(rightListView);
            panel2.Location = new Point(579, 41);
            panel2.Name = "panel2";
            panel2.Size = new Size(561, 546);
            panel2.TabIndex = 5;
            // 
            // rightListView
            // 
            rightListView.Columns.AddRange(new ColumnHeader[] { columnHeader3, columnHeader4, columnHeader7, columnHeader8, columnHeader10 });
            rightListView.Dock = DockStyle.Fill;
            rightListView.FullRowSelect = true;
            rightListView.GridLines = true;
            rightListView.Location = new Point(0, 0);
            rightListView.Name = "rightListView";
            rightListView.Size = new Size(561, 546);
            rightListView.TabIndex = 1;
            rightListView.UseCompatibleStateImageBehavior = false;
            rightListView.View = View.Details;
            // 
            // columnHeader3
            // 
            columnHeader3.Text = "序";
            columnHeader3.Width = 50;
            // 
            // columnHeader4
            // 
            columnHeader4.Text = "英雄名称(中)";
            columnHeader4.Width = 90;
            // 
            // columnHeader7
            // 
            columnHeader7.Text = "优先级";
            columnHeader7.Width = 100;
            // 
            // columnHeader8
            // 
            columnHeader8.Text = "英文名称";
            columnHeader8.Width = 160;
            // 
            // columnHeader10
            // 
            columnHeader10.Text = "位置";
            columnHeader10.Width = 130;
            // 
            // btnClear
            // 
            btnClear.Location = new Point(1066, 12);
            btnClear.Name = "btnClear";
            btnClear.Size = new Size(74, 23);
            btnClear.TabIndex = 6;
            btnClear.Text = "清空";
            btnClear.UseVisualStyleBackColor = true;
            // 
            // btnADC
            // 
            btnADC.Location = new Point(256, 12);
            btnADC.Name = "btnADC";
            btnADC.Size = new Size(54, 23);
            btnADC.TabIndex = 7;
            btnADC.Text = "射手";
            btnADC.UseVisualStyleBackColor = true;
            // 
            // txtSearch
            // 
            txtSearch.BorderStyle = BorderStyle.FixedSingle;
            txtSearch.Location = new Point(316, 12);
            txtSearch.Name = "txtSearch";
            txtSearch.Size = new Size(257, 23);
            txtSearch.TabIndex = 8;
            // 
            // Preliminary
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1155, 599);
            Controls.Add(txtSearch);
            Controls.Add(btnADC);
            Controls.Add(btnClear);
            Controls.Add(panel2);
            Controls.Add(panel1);
            Controls.Add(btnSupport);
            Controls.Add(btnJungle);
            Controls.Add(btnMid);
            Controls.Add(btnTop);
            Name = "Preliminary";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "英雄预选";
            panel1.ResumeLayout(false);
            panel2.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button btnTop;
        private Button btnMid;
        private Button btnSupport;
        private Button btnJungle;
        private Panel panel1;
        private Panel panel2;
        private Button btnClear;
        private Button btnADC;
        private ListView leftListView;
        private ColumnHeader columnHeader1;
        private ColumnHeader columnHeader2;
        private ListView rightListView;
        private ColumnHeader columnHeader3;
        private ColumnHeader columnHeader4;
        private ColumnHeader columnHeader5;
        private ColumnHeader columnHeader6;
        private ColumnHeader columnHeader7;
        private ColumnHeader columnHeader8;
        private ColumnHeader columnHeader9;
        private ColumnHeader columnHeader10;
        private TextBox txtSearch;
    }
}