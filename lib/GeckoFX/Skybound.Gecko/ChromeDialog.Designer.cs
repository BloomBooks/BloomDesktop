namespace Skybound.Gecko
{
	partial class ChromeDialog
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
			this.webBrowser = new Skybound.Gecko.GeckoWebBrowser();
			this.SuspendLayout();
			// 
			// webBrowser
			// 
			this.webBrowser.Dock = System.Windows.Forms.DockStyle.Fill;
			this.webBrowser.Location = new System.Drawing.Point(0, 0);
			this.webBrowser.Name = "webBrowser";
			this.webBrowser.Size = new System.Drawing.Size(500, 353);
			this.webBrowser.TabIndex = 0;
			this.webBrowser.WindowSetBounds += new Skybound.Gecko.GeckoWindowSetBoundsEventHandler(this.webBrowser_WindowSetBounds);
			this.webBrowser.DocumentTitleChanged += new System.EventHandler(this.webBrowser_DocumentTitleChanged);
			this.webBrowser.WindowClosed += new System.EventHandler(this.webBrowser_WindowClosed);
			// 
			// ChromeDialog
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(500, 353);
			this.Controls.Add(this.webBrowser);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "ChromeDialog";
			this.ShowIcon = false;
			this.ShowInTaskbar = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
			this.Text = "ChromeDialog";
			this.ResumeLayout(false);

		}

		#endregion

		private GeckoWebBrowser webBrowser;
	}
}