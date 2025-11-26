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
            button1 = new Button();
            button2 = new Button();
            button3 = new Button();
            button4 = new Button();
            panel1 = new Panel();
            panel2 = new Panel();
            button5 = new Button();
            SuspendLayout();
            // 
            // button1
            // 
            button1.Location = new Point(12, 12);
            button1.Name = "button1";
            button1.Size = new Size(36, 23);
            button1.TabIndex = 0;
            button1.Text = "上";
            button1.UseVisualStyleBackColor = true;
            // 
            // button2
            // 
            button2.Location = new Point(54, 12);
            button2.Name = "button2";
            button2.Size = new Size(36, 23);
            button2.TabIndex = 1;
            button2.Text = "中";
            button2.UseVisualStyleBackColor = true;
            // 
            // button3
            // 
            button3.Location = new Point(138, 12);
            button3.Name = "button3";
            button3.Size = new Size(36, 23);
            button3.TabIndex = 3;
            button3.Text = "辅";
            button3.UseVisualStyleBackColor = true;
            // 
            // button4
            // 
            button4.Location = new Point(96, 12);
            button4.Name = "button4";
            button4.Size = new Size(36, 23);
            button4.TabIndex = 2;
            button4.Text = "野";
            button4.UseVisualStyleBackColor = true;
            // 
            // panel1
            // 
            panel1.Location = new Point(12, 41);
            panel1.Name = "panel1";
            panel1.Size = new Size(222, 488);
            panel1.TabIndex = 4;
            // 
            // panel2
            // 
            panel2.Location = new Point(261, 41);
            panel2.Name = "panel2";
            panel2.Size = new Size(222, 488);
            panel2.TabIndex = 5;
            // 
            // button5
            // 
            button5.Location = new Point(261, 12);
            button5.Name = "button5";
            button5.Size = new Size(75, 23);
            button5.TabIndex = 6;
            button5.Text = "清空";
            button5.UseVisualStyleBackColor = true;
            // 
            // Preliminary
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(500, 541);
            Controls.Add(button5);
            Controls.Add(panel2);
            Controls.Add(panel1);
            Controls.Add(button3);
            Controls.Add(button4);
            Controls.Add(button2);
            Controls.Add(button1);
            Name = "Preliminary";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "英雄预选";
            ResumeLayout(false);
        }

        #endregion

        private Button button1;
        private Button button2;
        private Button button3;
        private Button button4;
        private Panel panel1;
        private Panel panel2;
        private Button button5;
    }
}