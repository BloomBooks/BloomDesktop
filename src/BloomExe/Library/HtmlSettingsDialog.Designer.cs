namespace Bloom.Library
{
	partial class HtmlSettingsDialog
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(HtmlSettingsDialog));
			this._browser = new Bloom.Browser();
			this.SuspendLayout();
			// 
			// _browser
			// 
			this._browser.BackColor = System.Drawing.Color.DarkGray;
			this._browser.Dock = System.Windows.Forms.DockStyle.Fill;
			this._browser.Location = new System.Drawing.Point(0, 0);
			this._browser.Margin = new System.Windows.Forms.Padding(0);
			this._browser.Name = "_browser";
			this._browser.Size = new System.Drawing.Size(693, 414);
			this._browser.TabIndex = 0;
			// 
			// SettingsDialog
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(693, 414);
			this.Controls.Add(this._browser);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "SettingsDialog";
			this.Text = "Settings";
			this.Load += new System.EventHandler(this.Settings_Load);
			this.ResumeLayout(false);

		}

		#endregion

		private Browser _browser;
	}
}