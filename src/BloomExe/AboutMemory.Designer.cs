namespace Bloom
{
	partial class AboutMemory
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
			this._browser1 = new Bloom.Browser();
			this._linkLabel1 = new System.Windows.Forms.LinkLabel();
			this._linkLabel2 = new System.Windows.Forms.LinkLabel();
			this.SuspendLayout();
			// 
			// _browser1
			// 
			this._browser1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._browser1.BackColor = System.Drawing.Color.DarkGray;
			this._browser1.ContextMenuProvider = null;
			this._browser1.ControlKeyEvent = null;
			this._browser1.Location = new System.Drawing.Point(0, 0);
			this._browser1.Name = "_browser1";
			this._browser1.Size = new System.Drawing.Size(624, 294);
			this._browser1.TabIndex = 0;
			this._browser1.VerticalScrollDistance = 0;
			// 
			// _linkLabel1
			// 
			this._linkLabel1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this._linkLabel1.AutoSize = true;
			this._linkLabel1.Location = new System.Drawing.Point(10, 305);
			this._linkLabel1.Name = "_linkLabel1";
			this._linkLabel1.Size = new System.Drawing.Size(512, 13);
			this._linkLabel1.TabIndex = 1;
			this._linkLabel1.TabStop = true;
			this._linkLabel1.Text = "See https://developer.mozilla.org/en-US/docs/Mozilla/Performance/about:memory for" +
    " a basic explanation.";
			this._linkLabel1.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabel1_LinkClicked);
			// 
			// _linkLabel2
			// 
			this._linkLabel2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this._linkLabel2.AutoSize = true;
			this._linkLabel2.Location = new System.Drawing.Point(10, 330);
			this._linkLabel2.Name = "_linkLabel2";
			this._linkLabel2.Size = new System.Drawing.Size(495, 13);
			this._linkLabel2.TabIndex = 2;
			this._linkLabel2.TabStop = true;
			this._linkLabel2.Text = "See https://developer.mozilla.org/en-US/docs/Mozilla/Performance/GC_and_CC_logs f" +
    "or more details.";
			this._linkLabel2.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._linkLabel2_LinkClicked);
			// 
			// AboutMemory
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(624, 361);
			this.Controls.Add(this._linkLabel2);
			this.Controls.Add(this._linkLabel1);
			this.Controls.Add(this._browser1);
			this.Name = "AboutMemory";
			this.ShowIcon = false;
			this.Text = "Bloom Browser Memory Diagnostics (\"about:memory\")";
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private Browser _browser1;
		private System.Windows.Forms.LinkLabel _linkLabel1;
		private System.Windows.Forms.LinkLabel _linkLabel2;
	}
}
