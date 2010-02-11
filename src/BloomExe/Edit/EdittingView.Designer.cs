namespace Bloom
{
    partial class EdittingView
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.htmlPageView1 = new Bloom.HtmlPageView();
            this.splitContainer2 = new System.Windows.Forms.SplitContainer();
            this.pageListView1 = new Bloom.PageListView();
            this.templatePagesView1 = new Bloom.TemplatePagesView();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.splitContainer2.Panel1.SuspendLayout();
            this.splitContainer2.Panel2.SuspendLayout();
            this.splitContainer2.SuspendLayout();
            this.SuspendLayout();
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 0);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.htmlPageView1);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.splitContainer2);
            this.splitContainer1.Size = new System.Drawing.Size(686, 497);
            this.splitContainer1.SplitterDistance = 590;
            this.splitContainer1.TabIndex = 0;
            // 
            // htmlPageView1
            // 
            this.htmlPageView1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.htmlPageView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.htmlPageView1.Location = new System.Drawing.Point(0, 0);
            this.htmlPageView1.Name = "htmlPageView1";
            this.htmlPageView1.Size = new System.Drawing.Size(590, 497);
            this.htmlPageView1.TabIndex = 0;
            // 
            // splitContainer2
            // 
            this.splitContainer2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer2.Location = new System.Drawing.Point(0, 0);
            this.splitContainer2.Name = "splitContainer2";
            this.splitContainer2.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer2.Panel1
            // 
            this.splitContainer2.Panel1.Controls.Add(this.pageListView1);
            // 
            // splitContainer2.Panel2
            // 
            this.splitContainer2.Panel2.Controls.Add(this.templatePagesView1);
            this.splitContainer2.Size = new System.Drawing.Size(92, 497);
            this.splitContainer2.SplitterDistance = 215;
            this.splitContainer2.TabIndex = 0;
            // 
            // pageListView1
            // 
            this.pageListView1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(80)))), ((int)(((byte)(60)))), ((int)(((byte)(80)))));
            this.pageListView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pageListView1.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.pageListView1.Location = new System.Drawing.Point(0, 0);
            this.pageListView1.Name = "pageListView1";
            this.pageListView1.Size = new System.Drawing.Size(92, 215);
            this.pageListView1.TabIndex = 0;
            // 
            // templatePagesView1
            // 
            this.templatePagesView1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(80)))), ((int)(((byte)(60)))), ((int)(((byte)(80)))));
            this.templatePagesView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.templatePagesView1.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.templatePagesView1.Location = new System.Drawing.Point(0, 0);
            this.templatePagesView1.Name = "templatePagesView1";
            this.templatePagesView1.Size = new System.Drawing.Size(92, 278);
            this.templatePagesView1.TabIndex = 0;
            // 
            // EdittingView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.splitContainer1);
            this.Name = "EdittingView";
            this.Size = new System.Drawing.Size(686, 497);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            this.splitContainer1.ResumeLayout(false);
            this.splitContainer2.Panel1.ResumeLayout(false);
            this.splitContainer2.Panel2.ResumeLayout(false);
            this.splitContainer2.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.SplitContainer splitContainer2;
        private TemplatePagesView templatePagesView1;
        private PageListView pageListView1;
        private HtmlPageView htmlPageView1;

    }
}
