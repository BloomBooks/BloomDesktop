namespace Bloom.MiscUI
{
	partial class TroubleShooterDialog
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TroubleShooterDialog));
			this.label1 = new System.Windows.Forms.Label();
			this.suppressPreviewCheckbox = new System.Windows.Forms.CheckBox();
			this.makeEmptyPageThumbnailsCheckbox = new System.Windows.Forms.CheckBox();
			this.logboxPlaceholder = new System.Windows.Forms.Label();
			this.SuspendLayout();
			// 
			// label1
			// 
			this.label1.Location = new System.Drawing.Point(12, 9);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(396, 46);
			this.label1.TabIndex = 0;
			this.label1.Text = resources.GetString("label1.Text");
			// 
			// suppressPreviewCheckbox
			// 
			this.suppressPreviewCheckbox.AutoSize = true;
			this.suppressPreviewCheckbox.Location = new System.Drawing.Point(12, 58);
			this.suppressPreviewCheckbox.Name = "suppressPreviewCheckbox";
			this.suppressPreviewCheckbox.Size = new System.Drawing.Size(246, 17);
			this.suppressPreviewCheckbox.TabIndex = 1;
			this.suppressPreviewCheckbox.Text = "Don\'t show a preview when a book is selected";
			this.suppressPreviewCheckbox.UseVisualStyleBackColor = true;
			// 
			// makeEmptyPageThumbnailsCheckbox
			// 
			this.makeEmptyPageThumbnailsCheckbox.AutoSize = true;
			this.makeEmptyPageThumbnailsCheckbox.Location = new System.Drawing.Point(12, 81);
			this.makeEmptyPageThumbnailsCheckbox.Name = "makeEmptyPageThumbnailsCheckbox";
			this.makeEmptyPageThumbnailsCheckbox.Size = new System.Drawing.Size(171, 17);
			this.makeEmptyPageThumbnailsCheckbox.TabIndex = 2;
			this.makeEmptyPageThumbnailsCheckbox.Text = "Don\'t show content in page list";
			this.makeEmptyPageThumbnailsCheckbox.UseVisualStyleBackColor = true;
			// 
			// logboxPlaceholder
			// 
			this.logboxPlaceholder.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.logboxPlaceholder.Location = new System.Drawing.Point(12, 126);
			this.logboxPlaceholder.Name = "logboxPlaceholder";
			this.logboxPlaceholder.Size = new System.Drawing.Size(396, 126);
			this.logboxPlaceholder.TabIndex = 4;
			this.logboxPlaceholder.Text = "The log box will go here";
			// 
			// TroubleShooterDialog
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(420, 261);
			this.Controls.Add(this.logboxPlaceholder);
			this.Controls.Add(this.makeEmptyPageThumbnailsCheckbox);
			this.Controls.Add(this.suppressPreviewCheckbox);
			this.Controls.Add(this.label1);
			this.Name = "TroubleShooterDialog";
			this.ShowIcon = false;
			this.Text = "Performance Troubleshooter";
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.CheckBox suppressPreviewCheckbox;
		private System.Windows.Forms.CheckBox makeEmptyPageThumbnailsCheckbox;
		private System.Windows.Forms.Label logboxPlaceholder;
	}
}
