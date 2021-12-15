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
            this._browser = new Microsoft.Web.WebView2.WinForms.WebView2();
            ((System.ComponentModel.ISupportInitialize)(this._browser)).BeginInit();
            this.SuspendLayout();
            // 
            // _browser
            // 
            this._browser.CreationProperties = null;
            this._browser.DefaultBackgroundColor = System.Drawing.Color.White;
            this._browser.Dock = System.Windows.Forms.DockStyle.Fill;
            this._browser.Location = new System.Drawing.Point(0, 0);
            this._browser.Name = "_browser";
            this._browser.Size = new System.Drawing.Size(150, 150);
            this._browser.TabIndex = 0;
            this._browser.ZoomFactor = 1D;
            // 
            // WebView2Browser
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this._browser);
            this.Name = "WebView2Browser";
            ((System.ComponentModel.ISupportInitialize)(this._browser)).EndInit();
            this.ResumeLayout(false);

		}

		#endregion

		private Microsoft.Web.WebView2.WinForms.WebView2 _browser;
	}
}
