using League.uitls;

namespace League
{
    partial class Search
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
            button1 = new Button();
            closableTabControl1 = new League.uitls.ClosableTabControl();
            tabPage1 = new TabPage();
            txtResult = new TextBox();
            tabPage2 = new TabPage();
            comboBox1 = new ComboBox();
            tabPage3 = new TabPage();
            checkBox1 = new CheckBox();
            button2 = new Button();
            tbPath = new TextBox();
            btnFetchMatches = new Button();
            txtOutput = new TextBox();
            closableTabControl1.SuspendLayout();
            tabPage1.SuspendLayout();
            tabPage2.SuspendLayout();
            tabPage3.SuspendLayout();
            SuspendLayout();
            // 
            // button1
            // 
            button1.Location = new Point(679, 12);
            button1.Name = "button1";
            button1.Size = new Size(75, 23);
            button1.TabIndex = 1;
            button1.Text = "button1";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // closableTabControl1
            // 
            closableTabControl1.Controls.Add(tabPage1);
            closableTabControl1.Controls.Add(tabPage2);
            closableTabControl1.Controls.Add(tabPage3);
            closableTabControl1.DrawMode = TabDrawMode.OwnerDrawFixed;
            closableTabControl1.Location = new Point(12, 12);
            closableTabControl1.Name = "closableTabControl1";
            closableTabControl1.Padding = new Point(20, 4);
            closableTabControl1.SelectedIndex = 0;
            closableTabControl1.Size = new Size(537, 426);
            closableTabControl1.TabIndex = 2;
            // 
            // tabPage1
            // 
            tabPage1.Controls.Add(txtResult);
            tabPage1.Location = new Point(4, 28);
            tabPage1.Name = "tabPage1";
            tabPage1.Padding = new Padding(3);
            tabPage1.Size = new Size(529, 394);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "tabPage1";
            tabPage1.UseVisualStyleBackColor = true;
            // 
            // txtResult
            // 
            txtResult.Location = new Point(26, 29);
            txtResult.Multiline = true;
            txtResult.Name = "txtResult";
            txtResult.Size = new Size(481, 336);
            txtResult.TabIndex = 0;
            // 
            // tabPage2
            // 
            tabPage2.Controls.Add(comboBox1);
            tabPage2.Location = new Point(4, 28);
            tabPage2.Name = "tabPage2";
            tabPage2.Padding = new Padding(3);
            tabPage2.Size = new Size(529, 394);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "tabPage2";
            tabPage2.UseVisualStyleBackColor = true;
            // 
            // comboBox1
            // 
            comboBox1.FormattingEnabled = true;
            comboBox1.Location = new Point(61, 72);
            comboBox1.Name = "comboBox1";
            comboBox1.Size = new Size(121, 25);
            comboBox1.TabIndex = 0;
            // 
            // tabPage3
            // 
            tabPage3.Controls.Add(checkBox1);
            tabPage3.Location = new Point(4, 28);
            tabPage3.Name = "tabPage3";
            tabPage3.Size = new Size(529, 394);
            tabPage3.TabIndex = 2;
            tabPage3.Text = "tabPage3";
            tabPage3.UseVisualStyleBackColor = true;
            // 
            // checkBox1
            // 
            checkBox1.AutoSize = true;
            checkBox1.Location = new Point(178, 96);
            checkBox1.Name = "checkBox1";
            checkBox1.Size = new Size(89, 21);
            checkBox1.TabIndex = 0;
            checkBox1.Text = "checkBox1";
            checkBox1.UseVisualStyleBackColor = true;
            // 
            // button2
            // 
            button2.Location = new Point(736, 92);
            button2.Name = "button2";
            button2.Size = new Size(75, 23);
            button2.TabIndex = 3;
            button2.Text = "登录";
            button2.UseVisualStyleBackColor = true;
            button2.Click += button2_Click;
            // 
            // tbPath
            // 
            tbPath.Location = new Point(555, 121);
            tbPath.Name = "tbPath";
            tbPath.Size = new Size(430, 23);
            tbPath.TabIndex = 4;
            // 
            // btnFetchMatches
            // 
            btnFetchMatches.Location = new Point(767, 163);
            btnFetchMatches.Name = "btnFetchMatches";
            btnFetchMatches.Size = new Size(105, 23);
            btnFetchMatches.TabIndex = 5;
            btnFetchMatches.Text = "Sgp调用测试";
            btnFetchMatches.UseVisualStyleBackColor = true;
            btnFetchMatches.Click += btnFetchMatches_Click;
            // 
            // txtOutput
            // 
            txtOutput.Location = new Point(565, 192);
            txtOutput.Multiline = true;
            txtOutput.Name = "txtOutput";
            txtOutput.Size = new Size(307, 242);
            txtOutput.TabIndex = 6;
            // 
            // Search
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1126, 450);
            Controls.Add(txtOutput);
            Controls.Add(btnFetchMatches);
            Controls.Add(tbPath);
            Controls.Add(button2);
            Controls.Add(closableTabControl1);
            Controls.Add(button1);
            Name = "Search";
            Text = "Search";
            closableTabControl1.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            tabPage1.PerformLayout();
            tabPage2.ResumeLayout(false);
            tabPage3.ResumeLayout(false);
            tabPage3.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button button1;
        private ClosableTabControl closableTabControl1;
        private TabPage tabPage1;
        private TabPage tabPage2;
        private ComboBox comboBox1;
        private TabPage tabPage3;
        private CheckBox checkBox1;
        private TextBox txtResult;
        private Button button2;
        private TextBox tbPath;
        private Button btnFetchMatches;
        private TextBox txtOutput;
    }
}