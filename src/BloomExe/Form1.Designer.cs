namespace Bloom
{
    partial class Form1
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
            this.pdfPage = new System.Windows.Forms.TabPage();
            this.htmlPreviewPage = new System.Windows.Forms.TabPage();
            this.editPage = new System.Windows.Forms.TabPage();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabControl1.SuspendLayout();
            this.SuspendLayout();
            // 
            // pdfPage
            // 
            this.pdfPage.Location = new System.Drawing.Point(4, 22);
            this.pdfPage.Name = "pdfPage";
            this.pdfPage.Size = new System.Drawing.Size(818, 531);
            this.pdfPage.TabIndex = 2;
            this.pdfPage.Text = "PDF Preview";
            this.pdfPage.UseVisualStyleBackColor = true;
            // 
            // htmlPreviewPage
            // 
            this.htmlPreviewPage.Location = new System.Drawing.Point(4, 22);
            this.htmlPreviewPage.Name = "htmlPreviewPage";
            this.htmlPreviewPage.Size = new System.Drawing.Size(818, 531);
            this.htmlPreviewPage.TabIndex = 1;
            this.htmlPreviewPage.Text = "Html Preview";
            this.htmlPreviewPage.UseVisualStyleBackColor = true;
            // 
            // editPage
            // 
            this.editPage.Location = new System.Drawing.Point(4, 22);
            this.editPage.Name = "editPage";
            this.editPage.Padding = new System.Windows.Forms.Padding(3);
            this.editPage.Size = new System.Drawing.Size(818, 554);
            this.editPage.TabIndex = 0;
            this.editPage.Text = "Html Edit";
            this.editPage.UseVisualStyleBackColor = true;
            // 
            // tabControl1
            // 
            this.tabControl1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.tabControl1.Controls.Add(this.editPage);
            this.tabControl1.Controls.Add(this.htmlPreviewPage);
            this.tabControl1.Controls.Add(this.pdfPage);
            this.tabControl1.Location = new System.Drawing.Point(2, 4);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(826, 580);
            this.tabControl1.TabIndex = 1;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(826, 583);
            this.Controls.Add(this.tabControl1);
            this.Name = "Form1";
            this.Text = "Bloom Test";
            this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
            this.tabControl1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TabPage pdfPage;
        private System.Windows.Forms.TabPage htmlPreviewPage;
        private System.Windows.Forms.TabPage editPage;
        private System.Windows.Forms.TabControl tabControl1;

    }
}

