
namespace WebView2PdfMaker
{
	partial class ConversionHost
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
			this._statusLabel = new System.Windows.Forms.Label();
			this.SuspendLayout();
			// 
			// _statusLabel
			// 
			this._statusLabel.Location = new System.Drawing.Point(26, 21);
			this._statusLabel.Name = "_statusLabel";
			this._statusLabel.Size = new System.Drawing.Size(296, 23);
			this._statusLabel.TabIndex = 1;
			this._statusLabel.Text = "status";
			// 
			// ConversionProgress
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(356, 107);
			this.Controls.Add(this._statusLabel);
			this.Name = "ConversionHost";
			this.Text = "ConversionHost";
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.Label _statusLabel;
	}
}
