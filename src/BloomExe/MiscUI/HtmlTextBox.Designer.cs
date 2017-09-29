namespace Bloom.MiscUI
{
	partial class HtmlTextBox
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
			if(disposing && (components != null))
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
			this._htmlLabel = new SIL.Windows.Forms.Widgets.HtmlLabel();
			this.SuspendLayout();
			// 
			// _htmlLabel
			// 
			this._htmlLabel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this._htmlLabel.Dock = System.Windows.Forms.DockStyle.Fill;
			this._htmlLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._htmlLabel.HTML = null;
			this._htmlLabel.Location = new System.Drawing.Point(0, 0);
			this._htmlLabel.Margin = new System.Windows.Forms.Padding(0);
			this._htmlLabel.Name = "_htmlLabel";
			this._htmlLabel.Size = new System.Drawing.Size(150, 150);
			this._htmlLabel.TabIndex = 0;
			// 
			// HtmlTextBox
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this._htmlLabel);
			this.Name = "HtmlTextBox";
			this.Load += new System.EventHandler(this.HtmlTextBox_Load);
			this.ResumeLayout(false);

		}

		#endregion

		private SIL.Windows.Forms.Widgets.HtmlLabel _htmlLabel;
	}
}
