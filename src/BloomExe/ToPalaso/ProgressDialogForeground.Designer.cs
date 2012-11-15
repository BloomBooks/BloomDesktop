using Palaso.Progress;
using Palaso.UI.WindowsForms.Progress;

namespace Bloom.ToPalaso.Experimental
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
			this.ProgressBar = new SimpleProgressIndicator();
			this._status = new SimpleStatusProgress();
			this._messageLabelProgress = new Bloom.ToPalaso.MessageLabelProgress();
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
			// _status
			// 
			this._status.AutoSize = true;
			this._status.CancelRequested = false;
			this._status.ErrorEncountered = false;
			this._status.Location = new System.Drawing.Point(24, 78);
			this._status.Name = "_status";
			this._status.ProgressIndicator = null;
			this._status.Size = new System.Drawing.Size(16, 13);
			this._status.SyncContext = null;
			this._status.TabIndex = 2;
			this._status.Text = "...";
			this._status.UseWaitCursor = true;
			this._status.WarningEncountered = false;
			// 
			// _messageLabelProgress
			// 
			this._messageLabelProgress.AutoSize = true;
			this._messageLabelProgress.CancelRequested = false;
			this._messageLabelProgress.ErrorEncountered = false;
			this._messageLabelProgress.Location = new System.Drawing.Point(27, 105);
			this._messageLabelProgress.Name = "_messageLabelProgress";
			this._messageLabelProgress.ProgressIndicator = null;
			this._messageLabelProgress.Size = new System.Drawing.Size(13, 13);
			this._messageLabelProgress.SyncContext = null;
			this._messageLabelProgress.TabIndex = 3;
			this._messageLabelProgress.Text = "..";
			// 
			// ProgressDialogForeground
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(371, 130);
			this.ControlBox = false;
			this.Controls.Add(this._messageLabelProgress);
			this.Controls.Add(this._status);
			this.Controls.Add(this.ProgressBar);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
			this.Name = "ProgressDialogForeground";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Working...";
			this.UseWaitCursor = true;
			this.Load += new System.EventHandler(this.ProgressDialogForeground_Load);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		public SimpleProgressIndicator ProgressBar;
		public SimpleStatusProgress _status;
		private ToPalaso.MessageLabelProgress _messageLabelProgress;
	}
}