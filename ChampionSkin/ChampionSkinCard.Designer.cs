namespace League.ChampionSkin
{
    partial class ChampionSkinCard
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
            flpSkins = new FlowLayoutPanel();
            panelSkinBar = new Panel();
            panelSkinViewport = new Panel();
            btnSkinLeft = new Button();
            btnSkinRight = new Button();
            ((System.ComponentModel.ISupportInitialize)picHero).BeginInit();
            panelSkinBar.SuspendLayout();
            panelSkinViewport.SuspendLayout();
            SuspendLayout();
            // 
            // picHero
            // 
            picHero.Image = Properties.Resources.无极剑圣;
            picHero.Location = new Point(3, 3);
            picHero.Name = "picHero";
            picHero.Size = new Size(150, 150);
            picHero.SizeMode = PictureBoxSizeMode.Zoom;
            picHero.TabIndex = 1;
            picHero.TabStop = false;
            // 
            // flpSkins
            // 
            flpSkins.AutoSize = true;
            flpSkins.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flpSkins.Location = new Point(0, 4);
            flpSkins.Margin = new Padding(0);
            flpSkins.Name = "flpSkins";
            flpSkins.Size = new Size(0, 0);
            flpSkins.TabIndex = 2;
            flpSkins.WrapContents = false;
            // 
            // panelSkinBar
            // 
            panelSkinBar.Controls.Add(panelSkinViewport);
            panelSkinBar.Controls.Add(btnSkinLeft);
            panelSkinBar.Controls.Add(btnSkinRight);
            panelSkinBar.Location = new Point(5, 159);
            panelSkinBar.Name = "panelSkinBar";
            panelSkinBar.Size = new Size(150, 48);
            panelSkinBar.TabIndex = 3;
            // 
            // panelSkinViewport
            // 
            panelSkinViewport.Controls.Add(flpSkins);
            panelSkinViewport.Location = new Point(18, 0);
            panelSkinViewport.Name = "panelSkinViewport";
            panelSkinViewport.Size = new Size(114, 48);
            panelSkinViewport.TabIndex = 4;
            // 
            // btnSkinLeft
            // 
            btnSkinLeft.FlatStyle = FlatStyle.Flat;
            btnSkinLeft.Location = new Point(0, 0);
            btnSkinLeft.Name = "btnSkinLeft";
            btnSkinLeft.Size = new Size(15, 48);
            btnSkinLeft.TabIndex = 0;
            btnSkinLeft.Text = "◀";
            btnSkinLeft.UseVisualStyleBackColor = true;
            // 
            // btnSkinRight
            // 
            btnSkinRight.FlatStyle = FlatStyle.Flat;
            btnSkinRight.Location = new Point(132, 0);
            btnSkinRight.Name = "btnSkinRight";
            btnSkinRight.Size = new Size(15, 48);
            btnSkinRight.TabIndex = 1;
            btnSkinRight.Text = "▶";
            btnSkinRight.UseVisualStyleBackColor = true;
            // 
            // ChampionSkinCard
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(panelSkinBar);
            Controls.Add(picHero);
            Name = "ChampionSkinCard";
            Size = new Size(158, 215);
            ((System.ComponentModel.ISupportInitialize)picHero).EndInit();
            panelSkinBar.ResumeLayout(false);
            panelSkinViewport.ResumeLayout(false);
            panelSkinViewport.PerformLayout();
            ResumeLayout(false);
        }

        #endregion
        private PictureBox picHero;
        private FlowLayoutPanel flpSkins;
        private Panel panelSkinBar;
        private Button btnSkinLeft;
        private Button btnSkinRight;
        private Panel panelSkinViewport;
    }
}
