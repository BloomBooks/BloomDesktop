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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AccessibilityCheckWindow));
			this._browser = new Bloom.Browser();
			this.SuspendLayout();
			// 
			// _browser
			// 
			this._browser.BackColor = System.Drawing.Color.DarkGray;
			this._browser.ContextMenuProvider = null;
			this._browser.ControlKeyEvent = null;
			this._browser.Dock = System.Windows.Forms.DockStyle.Fill;
			this._browser.Isolator = null;
			this._browser.Location = new System.Drawing.Point(0, 0);
			this._browser.Name = "_browser";
			this._browser.Size = new System.Drawing.Size(800, 450);
			this._browser.TabIndex = 0;
			this._browser.VerticalScrollDistance = 0;
			// 
			// AccessibilityCheckWindow
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(800, 450);
			this.Controls.Add(this._browser);
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.KeyPreview = true;
			this.MinimizeBox = false;
			this.Name = "AccessibilityCheckWindow";
			this.Text = "Bloom Accessibility Checker";
			this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.AccessibilityCheckWindow_FormClosed);
			this.ResumeLayout(false);

		}

		#endregion

		private Browser _browser;
	}
}
