namespace Bloom.Edit
{
	partial class ProgressDialogForeground
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
			this.ProgressBar = new Palaso.Progress.SimpleProgressIndicator();
			this.Status = new Palaso.Progress.LogBox.SimpleStatusProgress();
			this.SuspendLayout();
			// 
			// ProgressBar
			// 
			this.ProgressBar.Location = new System.Drawing.Point(24, 35);
			this.ProgressBar.MarqueeAnimationSpeed = 50;
			this.ProgressBar.Name = "ProgressBar";
			this.ProgressBar.PercentCompleted = 0;
			this.ProgressBar.Size = new System.Drawing.Size(324, 20);
			this.ProgressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
			this.ProgressBar.SyncContext = null;
			this.ProgressBar.TabIndex = 1;
			this.ProgressBar.UseWaitCursor = true;
			// 
			// Status
			// 
			this.Status.AutoSize = true;
			this.Status.CancelRequested = false;
			this.Status.ErrorEncountered = false;
			this.Status.Location = new System.Drawing.Point(30, 78);
			this.Status.Name = "Status";
			this.Status.ProgressIndicator = null;
			this.Status.Size = new System.Drawing.Size(113, 13);
			this.Status.SyncContext = null;
			this.Status.TabIndex = 2;
			this.Status.Text = "simpleStatusProgress1";
			this.Status.UseWaitCursor = true;
			this.Status.WarningEncountered = false;
			// 
			// ProgressDialogForeground
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(371, 130);
			this.ControlBox = false;
			this.Controls.Add(this.Status);
			this.Controls.Add(this.ProgressBar);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
			this.Name = "ProgressDialogForeground";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Working...";
			this.UseWaitCursor = true;
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		public Palaso.Progress.SimpleProgressIndicator ProgressBar;
		public Palaso.Progress.LogBox.SimpleStatusProgress Status;

	}
}