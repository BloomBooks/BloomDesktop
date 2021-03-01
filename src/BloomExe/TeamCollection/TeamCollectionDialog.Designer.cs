
namespace BloomTests.TeamCollection
{
	partial class TeamCollectionDialog
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
			this.reactControl1 = new Bloom.web.ReactControl();
			this.SuspendLayout();
			// 
			// reactControl1
			// 
			this.reactControl1.BackColor = System.Drawing.Color.White;
			this.reactControl1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.reactControl1.JavascriptBundleName = "teamCollectionSettingsBundle.js";
			this.reactControl1.Location = new System.Drawing.Point(0, 0);
			this.reactControl1.Name = "reactControl1";
			this.reactControl1.ReactComponentName = "TeamCollectionDialog";
			this.reactControl1.Size = new System.Drawing.Size(800, 450);
			this.reactControl1.TabIndex = 0;
			// 
			// TeamCollectionDialog
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(800, 450);
			this.ControlBox = false;
			this.Controls.Add(this.reactControl1);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
			this.Name = "TeamCollectionDialog";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "TeamCollectionDialog";
			this.ResumeLayout(false);

		}

		#endregion

		private Bloom.web.ReactControl reactControl1;
	}
}