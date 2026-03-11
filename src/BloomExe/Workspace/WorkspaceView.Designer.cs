using Bloom.Collection;
using Bloom.web;

namespace Bloom.Workspace
{

	partial class WorkspaceView
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
			if (disposing)
			{
				_collectionTabView?.Dispose();
				_collectionTabView = null;

				_publishView?.Dispose();
				_publishView = null;
			}
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			if (_tempBookInfoHtmlPath != null && SIL.IO.RobustFile.Exists(_tempBookInfoHtmlPath))
			{
				SIL.IO.RobustFile.Delete(_tempBookInfoHtmlPath);
				_tempBookInfoHtmlPath = null;
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
			this.components = new System.ComponentModel.Container();
			this._containerPanel = new System.Windows.Forms.Panel();
			this._L10NSharpExtender = new L10NSharp.Windows.Forms.L10NSharpExtender(this.components);
			this._applicationUpdateCheckTimer = new System.Windows.Forms.Timer(this.components);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			//
			// _containerPanel
			//
			this._containerPanel.Dock = System.Windows.Forms.DockStyle.Fill;
			this._containerPanel.Location = new System.Drawing.Point(0, 0);
			this._containerPanel.Name = "_containerPanel";
			this._containerPanel.Size = new System.Drawing.Size(1098, 528);
			this._containerPanel.TabIndex = 16;
			//
			// _L10NSharpExtender
			//
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "HelpMenu";
			//
			// _applicationUpdateCheckTimer
			//
			this._applicationUpdateCheckTimer.Interval = 60000;
			this._applicationUpdateCheckTimer.Tick += new System.EventHandler(this._applicationUpdateCheckTimer_Tick);
			//
			// WorkspaceView
			//
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this._containerPanel);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "WorkspaceView.WorkspaceView");
			this.Name = "WorkspaceView";
			this.Size = new System.Drawing.Size(1098, 528);
			this.Load += new System.EventHandler(this.WorkspaceView_Load);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Panel _containerPanel;
		private L10NSharp.Windows.Forms.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.Timer _applicationUpdateCheckTimer;
	}
}
