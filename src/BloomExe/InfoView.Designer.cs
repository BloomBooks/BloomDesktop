namespace Bloom
{
    partial class InfoView
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
            this._topicsList = new System.Windows.Forms.ListView();
            this._browser = new Bloom.Browser();
            this.SuspendLayout();
            // 
            // _topicsList
            // 
            this._topicsList.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this._topicsList.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this._topicsList.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._topicsList.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
            this._topicsList.HideSelection = false;
            this._topicsList.Location = new System.Drawing.Point(3, 15);
            this._topicsList.Name = "_topicsList";
            this._topicsList.Size = new System.Drawing.Size(119, 318);
            this._topicsList.TabIndex = 1;
            this._topicsList.UseCompatibleStateImageBehavior = false;
            this._topicsList.View = System.Windows.Forms.View.List;
            this._topicsList.SelectedIndexChanged += new System.EventHandler(this._topicsList_SelectedIndexChanged);
            // 
            // _browser
            // 
            this._browser.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this._browser.BackColor = System.Drawing.Color.DarkGray;
            this._browser.Location = new System.Drawing.Point(119, 3);
            this._browser.Name = "_browser";
            this._browser.Size = new System.Drawing.Size(310, 333);
            this._browser.TabIndex = 0;
            // 
            // InfoView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this._topicsList);
            this.Controls.Add(this._browser);
            this.Name = "InfoView";
            this.Size = new System.Drawing.Size(429, 336);
            this.Load += new System.EventHandler(this.InfoView_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private Browser _browser;
        private System.Windows.Forms.ListView _topicsList;
    }
}
