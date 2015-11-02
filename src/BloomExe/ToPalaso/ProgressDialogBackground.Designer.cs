﻿using SIL.Progress;
using SIL.Windows.Forms.Progress;

namespace Bloom.ToPalaso
{
	partial class ProgressDialogBackground
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
			this.StatusLabel = new SimpleStatusProgress();
			this._backgroundWorker = new System.ComponentModel.BackgroundWorker();
			this.SuspendLayout();
			// 
			// ProgressBar
			// 
			this.ProgressBar.Location = new System.Drawing.Point(24, 35);
			this.ProgressBar.MarqueeAnimationSpeed = 50;
			this.ProgressBar.Name = "ProgressBar";
			this.ProgressBar.PercentCompleted = 0;
			this.ProgressBar.Size = new System.Drawing.Size(374, 20);
			this.ProgressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
			this.ProgressBar.SyncContext = null;
			this.ProgressBar.TabIndex = 1;
			this.ProgressBar.UseWaitCursor = true;
			// 
			// _statusLabel
			// 
			this.StatusLabel.AutoSize = true;
			this.StatusLabel.CancelRequested = false;
			this.StatusLabel.ErrorEncountered = false;
			this.StatusLabel.Location = new System.Drawing.Point(30, 78);
			this.StatusLabel.Name = "StatusLabel";
			this.StatusLabel.ProgressIndicator = null;
			this.StatusLabel.Size = new System.Drawing.Size(16, 13);
			this.StatusLabel.SyncContext = null;
			this.StatusLabel.TabIndex = 2;
			this.StatusLabel.Text = "...";
			this.StatusLabel.UseWaitCursor = true;
			this.StatusLabel.WarningEncountered = false;
			// 
			// ProgressDialogBackground
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(421, 130);
			this.ControlBox = false;
			this.Controls.Add(this.StatusLabel);
			this.Controls.Add(this.ProgressBar);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
			this.Name = "ProgressDialogBackground";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Working...";
			this.UseWaitCursor = true;
			this.Load += new System.EventHandler(this.ProgressDialog_Load);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		public SimpleProgressIndicator ProgressBar;
		public SimpleStatusProgress StatusLabel;
		private System.ComponentModel.BackgroundWorker _backgroundWorker;

	}
}