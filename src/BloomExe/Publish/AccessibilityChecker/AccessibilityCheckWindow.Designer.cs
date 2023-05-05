namespace Bloom.Publish.AccessibilityChecker
{
	partial class AccessibilityCheckWindow
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;



		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AccessibilityCheckWindow));
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._browser = BrowserMaker.MakeBrowser();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "AccessibilityCheck";
			// 
			// _browser
			// 
			this._browser.BackColor = System.Drawing.Color.DarkGray;
			this._browser.ContextMenuProvider = null;
			this._browser.ControlKeyEvent = null;
			this._browser.Dock = System.Windows.Forms.DockStyle.Fill;
			this._browser.Location = new System.Drawing.Point(0, 0);
			this._browser.Name = "_browser";
			this._browser.Size = new System.Drawing.Size(908, 585);
			this._browser.TabIndex = 0;
			// 
			// AccessibilityCheckWindow
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(908, 585);
			this.Controls.Add(this._browser);
			this.Icon = global::Bloom.Properties.Resources.BloomIcon;
			this.KeyPreview = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "AccessibilityCheck.WindowTitle");
			this.MinimizeBox = false;
			this.Name = "AccessibilityCheckWindow";
			this.Text = "Bloom Accessibility Checker";
			this.Activated += new System.EventHandler(this.AccessibilityCheckWindow_Activated);
			this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.AccessibilityCheckWindow_FormClosed);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);

		}

		#endregion

		private Browser _browser;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
	}
}
