using League.Controls;

namespace League.uitls
{
    partial class MatchTabContent
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
            closableTabControl1 = new ClosableTabControl();
            SuspendLayout();
            // 
            // closableTabControl1
            // 
            closableTabControl1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            closableTabControl1.DrawMode = TabDrawMode.OwnerDrawFixed;
            closableTabControl1.Location = new Point(0, 0);
            closableTabControl1.Name = "closableTabControl1";
            closableTabControl1.Padding = new Point(20, 4);
            closableTabControl1.SelectedIndex = 0;
            closableTabControl1.Size = new Size(1101, 776);
            closableTabControl1.TabIndex = 17;
            // 
            // MatchTabContent
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(closableTabControl1);
            Name = "MatchTabContent";
            Size = new Size(1101, 776);
            ResumeLayout(false);
        }

        #endregion

        private ClosableTabControl closableTabControl1;
    }
}
