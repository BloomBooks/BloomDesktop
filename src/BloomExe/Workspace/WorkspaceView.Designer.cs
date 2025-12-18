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
			this._topBarReactControl = new Bloom.web.ReactControl();
			this._L10NSharpExtender = new L10NSharp.Windows.Forms.L10NSharpExtender(this.components);
			this._documentationMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._bloomDocsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._trainingVideosMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._buildingReaderTemplatesMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._usingReaderTemplatesMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
			this._askAQuestionMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._requestAFeatureMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._reportAProblemMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._divider1 = new System.Windows.Forms.ToolStripSeparator();
			this._releaseNotesMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._checkForNewVersionMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._registrationMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._divider2 = new System.Windows.Forms.ToolStripSeparator();
			this._webSiteMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._aboutBloomMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._applicationUpdateCheckTimer = new System.Windows.Forms.Timer(this.components);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			//
			// _containerPanel
			//
			this._containerPanel.Dock = System.Windows.Forms.DockStyle.Fill;
			this._containerPanel.Location = new System.Drawing.Point(0, 64);
			this._containerPanel.Name = "_containerPanel";
			this._containerPanel.Size = new System.Drawing.Size(1098, 457);
			this._containerPanel.TabIndex = 16;
			//
			// _topBarReactControl
			//
			this._topBarReactControl.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(29)))), ((int)(((byte)(148)))), ((int)(((byte)(164)))));
			this._topBarReactControl.Dock = System.Windows.Forms.DockStyle.Top;
			this._topBarReactControl.HideVerticalOverflow = true;
			this._topBarReactControl.JavascriptBundleName = "topBarBundle";
			this._topBarReactControl.Location = new System.Drawing.Point(0, 0);
			this._topBarReactControl.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
			this._topBarReactControl.Name = "_topBarReactControl";
			this._topBarReactControl.Size = new System.Drawing.Size(1098, 64);
			this._topBarReactControl.TabIndex = 17;
			//
			// _L10NSharpExtender
			//
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "HelpMenu";
			//
			// _documentationMenuItem
			//
			this._documentationMenuItem.Image = global::Bloom.Properties.Resources.help24x24;
			this._L10NSharpExtender.SetLocalizableToolTip(this._documentationMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._documentationMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._documentationMenuItem, "HelpMenu.DocumentationMenuItem");
			this._documentationMenuItem.Name = "_documentationMenuItem";
			this._documentationMenuItem.Size = new System.Drawing.Size(213, 22);
			this._documentationMenuItem.Text = "Help...";
			this._documentationMenuItem.Click += new System.EventHandler(this._documentationMenuItem_Click);
			//
			// _bloomDocsMenuItem
			//
			this._bloomDocsMenuItem.Image = global::Bloom.Properties.Resources.help24x24;
			this._L10NSharpExtender.SetLocalizableToolTip(this._bloomDocsMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._bloomDocsMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._bloomDocsMenuItem, "HelpMenu.OnlineHelpMenuItem");
			this._bloomDocsMenuItem.Name = "_bloomDocsMenuItem";
			this._bloomDocsMenuItem.Size = new System.Drawing.Size(321, 34);
			this._bloomDocsMenuItem.Text = "More Help (Web)";
			this._bloomDocsMenuItem.Click += new System.EventHandler(this._bloom_docs_Click);
			//
			// _trainingVideosMenuItem
			//
			this._trainingVideosMenuItem.Image = global::Bloom.Properties.Resources.videos;
			this._L10NSharpExtender.SetLocalizableToolTip(this._trainingVideosMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._trainingVideosMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._trainingVideosMenuItem, "HelpMenu.trainingVideos");
			this._trainingVideosMenuItem.Name = "_trainingVideosMenuItem";
			this._trainingVideosMenuItem.Size = new System.Drawing.Size(213, 22);
			this._trainingVideosMenuItem.Text = "Training Videos";
			this._trainingVideosMenuItem.Click += new System.EventHandler(this._trainingVideosMenuItem_Click);
			//
			// _buildingReaderTemplatesMenuItem
			//
			this._buildingReaderTemplatesMenuItem.Image = global::Bloom.Properties.Resources.pdf16x16;
			this._L10NSharpExtender.SetLocalizableToolTip(this._buildingReaderTemplatesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._buildingReaderTemplatesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._buildingReaderTemplatesMenuItem, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._buildingReaderTemplatesMenuItem, "HelpMenu.BuildingReaderTemplatesMenuItem");
			this._buildingReaderTemplatesMenuItem.Name = "_buildingReaderTemplatesMenuItem";
			this._buildingReaderTemplatesMenuItem.Size = new System.Drawing.Size(213, 22);
			this._buildingReaderTemplatesMenuItem.Text = "Building Reader Templates";
			this._buildingReaderTemplatesMenuItem.Click += new System.EventHandler(this.buildingReaderTemplatesMenuItem_Click);
			//
			// _usingReaderTemplatesMenuItem
			//
			this._usingReaderTemplatesMenuItem.Image = global::Bloom.Properties.Resources.pdf16x16;
			this._L10NSharpExtender.SetLocalizableToolTip(this._usingReaderTemplatesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._usingReaderTemplatesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._usingReaderTemplatesMenuItem, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._usingReaderTemplatesMenuItem, "HelpMenu.UsingReaderTemplatesMenuItem");
			this._usingReaderTemplatesMenuItem.Name = "_usingReaderTemplatesMenuItem";
			this._usingReaderTemplatesMenuItem.Size = new System.Drawing.Size(213, 22);
			this._usingReaderTemplatesMenuItem.Text = "Using Reader Templates ";
			this._usingReaderTemplatesMenuItem.Click += new System.EventHandler(this.usingReaderTemplatesMenuItem_Click);
			//
			// _toolStripSeparator1
			//
			this._toolStripSeparator1.Name = "toolStripSeparator1";
			this._toolStripSeparator1.Size = new System.Drawing.Size(210, 6);
			//
			// _askAQuestionMenuItem
			//
			this._askAQuestionMenuItem.Image = global::Bloom.Properties.Resources.weblink;
			this._L10NSharpExtender.SetLocalizableToolTip(this._askAQuestionMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._askAQuestionMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._askAQuestionMenuItem, "HelpMenu.AskAQuestionMenuItem");
			this._askAQuestionMenuItem.Name = "_askAQuestionMenuItem";
			this._askAQuestionMenuItem.Size = new System.Drawing.Size(213, 22);
			this._askAQuestionMenuItem.Text = "Ask a Question";
			this._askAQuestionMenuItem.Click += new System.EventHandler(this._askAQuestionMenuItem_Click);
			//
			// _requestAFeatureMenuItem
			//
			this._requestAFeatureMenuItem.Image = global::Bloom.Properties.Resources.weblink;
			this._L10NSharpExtender.SetLocalizableToolTip(this._requestAFeatureMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._requestAFeatureMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._requestAFeatureMenuItem, "HelpMenu.MakeASuggestionMenuItem");
			this._requestAFeatureMenuItem.Name = "_requestAFeatureMenuItem";
			this._requestAFeatureMenuItem.Size = new System.Drawing.Size(213, 22);
			this._requestAFeatureMenuItem.Text = "Request a Feature";
			this._requestAFeatureMenuItem.Click += new System.EventHandler(this._requestAFeatureMenuItem_Click);
			//
			// _reportAProblemMenuItem
			//
			this._reportAProblemMenuItem.Image = global::Bloom.Properties.Resources.sad16x16;
			this._L10NSharpExtender.SetLocalizableToolTip(this._reportAProblemMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._reportAProblemMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._reportAProblemMenuItem, "HelpMenu.ReportAProblemToolStripMenuItem");
			this._reportAProblemMenuItem.Name = "_reportAProblemMenuItem";
			this._reportAProblemMenuItem.Size = new System.Drawing.Size(213, 22);
			this._reportAProblemMenuItem.Text = "Report a Problem...";
			this._reportAProblemMenuItem.Click += new System.EventHandler(this._reportAProblemMenuItem_Click);
			//
			// _divider1
			//
			this._divider1.Name = "_divider1";
			this._divider1.Size = new System.Drawing.Size(210, 6);
			//
			// _releaseNotesMenuItem
			//
			this._L10NSharpExtender.SetLocalizableToolTip(this._releaseNotesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._releaseNotesMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._releaseNotesMenuItem, "HelpMenu.ReleaseNotesWebMenuItem");
			this._releaseNotesMenuItem.Name = "_releaseNotesMenuItem";
			this._releaseNotesMenuItem.Size = new System.Drawing.Size(321, 34);
			this._releaseNotesMenuItem.Text = "Release Notes (Web)";
			this._releaseNotesMenuItem.Click += new System.EventHandler(this._releaseNotesMenuItem_Click);
			//
			// _checkForNewVersionMenuItem
			//
			this._L10NSharpExtender.SetLocalizableToolTip(this._checkForNewVersionMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._checkForNewVersionMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._checkForNewVersionMenuItem, "HelpMenu.CheckForNewVersionMenuItem");
			this._checkForNewVersionMenuItem.Name = "_checkForNewVersionMenuItem";
			this._checkForNewVersionMenuItem.Size = new System.Drawing.Size(213, 22);
			this._checkForNewVersionMenuItem.Text = "Check For New Version";
			this._checkForNewVersionMenuItem.Click += new System.EventHandler(this._checkForNewVersionMenuItem_Click);
			//
			// _registrationMenuItem
			//
			this._L10NSharpExtender.SetLocalizableToolTip(this._registrationMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._registrationMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._registrationMenuItem, "HelpMenu.RegistrationMenuItem");
			this._registrationMenuItem.Name = "_registrationMenuItem";
			this._registrationMenuItem.Size = new System.Drawing.Size(213, 22);
			this._registrationMenuItem.Text = "Registration...";
			this._registrationMenuItem.Click += new System.EventHandler(this.OnRegistrationMenuItem_Click);
			//
			// _divider2
			//
			this._divider2.Name = "_divider2";
			this._divider2.Size = new System.Drawing.Size(210, 6);
			//
			// _webSiteMenuItem
			//
			this._webSiteMenuItem.Image = global::Bloom.Properties.Resources.weblink;
			this._L10NSharpExtender.SetLocalizableToolTip(this._webSiteMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._webSiteMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._webSiteMenuItem, "HelpMenu.WebSiteMenuItem");
			this._webSiteMenuItem.Name = "_webSiteMenuItem";
			this._webSiteMenuItem.Size = new System.Drawing.Size(213, 22);
			this._webSiteMenuItem.Text = "BloomLibrary.org";
			this._webSiteMenuItem.Click += new System.EventHandler(this._webSiteMenuItem_Click);
			//
			// _aboutBloomMenuItem
			//
			this._L10NSharpExtender.SetLocalizableToolTip(this._aboutBloomMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._aboutBloomMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._aboutBloomMenuItem, "HelpMenu.CreditsMenuItem");
			this._aboutBloomMenuItem.Name = "_aboutBloomMenuItem";
			this._aboutBloomMenuItem.Size = new System.Drawing.Size(213, 22);
			this._aboutBloomMenuItem.Text = "About Bloom...";
			this._aboutBloomMenuItem.Click += new System.EventHandler(this.OnAboutBoxClick);
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
			this.Controls.Add(this._topBarReactControl);
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
		private ReactControl _topBarReactControl;
		private L10NSharp.Windows.Forms.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.Timer _applicationUpdateCheckTimer;
		private System.Windows.Forms.ToolStripMenuItem _documentationMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _trainingVideosMenuItem;
		private System.Windows.Forms.ToolStripSeparator _toolStripSeparator1;
		private System.Windows.Forms.ToolStripMenuItem _releaseNotesMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _buildingReaderTemplatesMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _usingReaderTemplatesMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _reportAProblemMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _requestAFeatureMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _webSiteMenuItem;
		private System.Windows.Forms.ToolStripSeparator _divider1;
		private System.Windows.Forms.ToolStripSeparator _divider2;
		private System.Windows.Forms.ToolStripMenuItem _checkForNewVersionMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _registrationMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _aboutBloomMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _askAQuestionMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _bloomDocsMenuItem;
	}
}
