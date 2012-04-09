namespace Bloom.Library
{
	partial class ListHeader
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
			this.Label = new System.Windows.Forms.Label();
			this.SuspendLayout();
			// 
			// Label
			// 
			this.Label.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.Label.AutoSize = true;
			this.Label.BackColor = System.Drawing.Color.Transparent;
			this.Label.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.Label.ForeColor = System.Drawing.Color.Maroon;
			this.Label.Location = new System.Drawing.Point(3, 0);
			this.Label.Name = "Label";
			this.Label.Size = new System.Drawing.Size(81, 21);
			this.Label.TabIndex = 14;
			this.Label.Text = "Vernacular Library Name Phrase";
			// 
			// ListHeader
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.Transparent;
			this.Controls.Add(this.Label);
			this.Name = "ListHeader";
			this.Size = new System.Drawing.Size(258, 35);
			this.ForeColorChanged += new System.EventHandler(this.ListHeader_ForeColorChanged);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		public System.Windows.Forms.Label Label;

	}
}
