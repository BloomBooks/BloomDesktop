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
			// TroubleShooterDialog
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(420, 261);
			this.Controls.Add(this.suppressPreviewCheckbox);
			this.Controls.Add(this.label1);
			this.Name = "TroubleShooterDialog";
			this.Text = "Trouble Shooter";
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.CheckBox suppressPreviewCheckbox;
	}
}