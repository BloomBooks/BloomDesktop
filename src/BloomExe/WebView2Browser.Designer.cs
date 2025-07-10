using SIL.Windows.Forms.Miscellaneous;
using System.IO;

namespace Bloom
{
    partial class WebView2Browser
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
			var uprocId = _webview?.CoreWebView2?.BrowserProcessId;
			var procId = uprocId.HasValue ? (int)uprocId.Value : 0;
			var userFolder = _webview?.CoreWebView2?.Environment?.UserDataFolder;
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			else if (disposing && _webview != null)
			{
				_webview.Dispose();
			}
			if (disposing && procId > 0 && userFolder != null && Directory.Exists(userFolder))
			{
				// We need to wait until the process finishes to reliably delete the folder.
				// Unit tests can produce WebView2 processes that reuse the same id.  This
				// could conceivably happen in Bloom Desktop, so I've added code to handle it.
				// If needed, the prior user folder is stored so that we can delete it after
				// all of the processes have finished.
				if (Program.WebView2ProcessToUserFolder.ContainsKey(procId))
				{
					var priorUserFolder = Program.WebView2ProcessToUserFolder[procId];
					if (priorUserFolder != userFolder)
					{
						if (Directory.Exists(priorUserFolder))
							Program.WebView2UserFoldersToDelete.Add(priorUserFolder);
						Program.WebView2ProcessToUserFolder[procId] = userFolder;
					}
				}
				else
				{
					Program.WebView2ProcessToUserFolder.Add(procId, userFolder);
				}
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
            this._webview = new Microsoft.Web.WebView2.WinForms.WebView2();
            ((System.ComponentModel.ISupportInitialize)(this._webview)).BeginInit();
            this.SuspendLayout();
            // 
            // _webview
            // 
            this._webview.BackColor = System.Drawing.Color.White;
            this._webview.CreationProperties = null;
            this._webview.DefaultBackgroundColor = System.Drawing.Color.White;
            this._webview.Dock = System.Windows.Forms.DockStyle.Fill;
            this._webview.Location = new System.Drawing.Point(0, 0);
            this._webview.Name = "_webview";
            this._webview.Size = new System.Drawing.Size(150, 150);
            this._webview.TabIndex = 0;
            this._webview.ZoomFactor = 1D;
            // 
            // WebView2Browser
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.ActiveCaption;
            this.Controls.Add(this._webview);
            this.Name = "WebView2Browser";
            ((System.ComponentModel.ISupportInitialize)(this._webview)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion

		private Microsoft.Web.WebView2.WinForms.WebView2 _webview;
	}
}
