namespace Bloom.Publish
{
    partial class PublishView
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
            this.components = new System.ComponentModel.Container();
            this._browser = new Bloom.Browser();
            this._loadTimer = new System.Windows.Forms.Timer(this.components);
            this._noBookletRadio = new System.Windows.Forms.RadioButton();
            this._coverRadio = new System.Windows.Forms.RadioButton();
            this._bodyRadio = new System.Windows.Forms.RadioButton();
            this.SuspendLayout();
            // 
            // _browser
            // 
            this._browser.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this._browser.BackColor = System.Drawing.Color.DarkGray;
            this._browser.Location = new System.Drawing.Point(0, 26);
            this._browser.Name = "_browser";
            this._browser.Size = new System.Drawing.Size(652, 302);
            this._browser.TabIndex = 0;
            this._browser.VisibleChanged += new System.EventHandler(this._browser_VisibleChanged);
            // 
            // _loadTimer
            // 
            this._loadTimer.Tick += new System.EventHandler(this._loadTimer_Tick);
            // 
            // _noBookletRadio
            // 
            this._noBookletRadio.AutoSize = true;
            this._noBookletRadio.Location = new System.Drawing.Point(12, 3);
            this._noBookletRadio.Name = "_noBookletRadio";
            this._noBookletRadio.Size = new System.Drawing.Size(161, 17);
            this._noBookletRadio.TabIndex = 2;
            this._noBookletRadio.TabStop = true;
            this._noBookletRadio.Text = "One page per piece of paper";
            this._noBookletRadio.UseVisualStyleBackColor = true;
            this._noBookletRadio.CheckedChanged += new System.EventHandler(this._bookletRadio_CheckedChanged);
            // 
            // _coverRadio
            // 
            this._coverRadio.AutoSize = true;
            this._coverRadio.Location = new System.Drawing.Point(199, 3);
            this._coverRadio.Name = "_coverRadio";
            this._coverRadio.Size = new System.Drawing.Size(120, 17);
            this._coverRadio.TabIndex = 3;
            this._coverRadio.TabStop = true;
            this._coverRadio.Text = "Booklet Cover Page";
            this._coverRadio.UseVisualStyleBackColor = true;
            this._coverRadio.CheckedChanged += new System.EventHandler(this._bookletRadio_CheckedChanged);
            // 
            // _bodyRadio
            // 
            this._bodyRadio.AutoSize = true;
            this._bodyRadio.Location = new System.Drawing.Point(345, 3);
            this._bodyRadio.Name = "_bodyRadio";
            this._bodyRadio.Size = new System.Drawing.Size(125, 17);
            this._bodyRadio.TabIndex = 4;
            this._bodyRadio.TabStop = true;
            this._bodyRadio.Text = "Booklet Inside Pages";
            this._bodyRadio.UseVisualStyleBackColor = true;
            this._bodyRadio.CheckedChanged += new System.EventHandler(this._bookletRadio_CheckedChanged);
            // 
            // PublishView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this._bodyRadio);
            this.Controls.Add(this._coverRadio);
            this.Controls.Add(this._noBookletRadio);
            this.Controls.Add(this._browser);
            this.Name = "PublishView";
            this.Size = new System.Drawing.Size(652, 328);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Browser _browser;
        private System.Windows.Forms.Timer _loadTimer;
        private System.Windows.Forms.RadioButton _noBookletRadio;
        private System.Windows.Forms.RadioButton _coverRadio;
        private System.Windows.Forms.RadioButton _bodyRadio;
    }
}