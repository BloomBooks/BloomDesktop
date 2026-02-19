using Bloom.Collection;
using Bloom.TeamCollection;
using Bloom.web;

namespace Bloom.CollectionTab
{
	partial class CollectionTabView
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
				BookCollection.CollectionCreated -= OnBookCollectionCreated;

				try
				{
					var collections = _model?.GetBookCollections(true) ?? System.Linq.Enumerable.Empty<BookCollection>();
					foreach (var collection in collections)
						collection?.StopWatchingDirectory();
				}
				catch (System.Exception e)
				{
					// The exception is almost expected in ConsoleMode, but not otherwise.
					System.Diagnostics.Debug.WriteLine("Caught exception in CollectionTabView.Dispose(): {0}", e);
					if (!Program.RunningInConsoleMode)
						throw;
				}
				if (components != null)
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
			this.components = new System.ComponentModel.Container();
			this._topBarControl = new System.Windows.Forms.Panel();
			this._reactControl = new ReactControl();
			this._toolStripLeft = new System.Windows.Forms.ToolStrip();
			this._tcStatusButton = new Bloom.TeamCollection.TeamCollectionStatusButton();
			this._toolStrip = new System.Windows.Forms.ToolStrip();
            this._legacySettingsButton = new System.Windows.Forms.ToolStripButton();
			this._settingsButton = new System.Windows.Forms.ToolStripButton();
			this._openCreateCollectionButton = new System.Windows.Forms.ToolStripButton();
            this._L10NSharpExtender = new L10NSharp.Windows.Forms.L10NSharpExtender(this.components);
			this._topBarControl.SuspendLayout();
			this._toolStripLeft.SuspendLayout();
			this._toolStrip.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
            this.SuspendLayout();
            // 
            // _topBarControl
            // 
            this._topBarControl.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this._topBarControl.AutoSize = true;
            this._topBarControl.BackColor = System.Drawing.Color.Cyan;
            this._topBarControl.Controls.Add(this._toolStripLeft);
            this._topBarControl.Controls.Add(this._toolStrip);
            this._topBarControl.Location = new System.Drawing.Point(3, 159);
            this._topBarControl.Name = "_topBarControl";
            this._topBarControl.Size = new System.Drawing.Size(767, 69);
            this._topBarControl.TabIndex = 15;
            // 
            // _toolStripLeft
            // 
			this._toolStripLeft.BackColor = System.Drawing.Color.Transparent;
			this._toolStripLeft.Dock = System.Windows.Forms.DockStyle.Left;
			this._toolStripLeft.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this._toolStripLeft.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._tcStatusButton});
			this._toolStripLeft.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.HorizontalStackWithOverflow;
			this._L10NSharpExtender.SetLocalizableToolTip(this._toolStripLeft, null);
			this._L10NSharpExtender.SetLocalizationComment(this._toolStripLeft, null);
            this._L10NSharpExtender.SetLocalizationPriority(this._toolStripLeft, L10NSharp.LocalizationPriority.NotLocalizable);
            this._L10NSharpExtender.SetLocalizingId(this._toolStripLeft, "WorkspaceView._toolStripLeft");
            this._toolStripLeft.Location = new System.Drawing.Point(0, 0);
            this._toolStripLeft.Name = "_toolStripLeft";
			this._toolStripLeft.RightToLeft = System.Windows.Forms.RightToLeft.No;
			this._toolStripLeft.Size = new System.Drawing.Size(119, 69);
			this._toolStripLeft.TabIndex = 32;
			this._toolStripLeft.Text = "_toolStripLeft";
			// 
			// _tcStatusButton
			// 
			this._tcStatusButton.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
			this._tcStatusButton.ForeColor = System.Drawing.Color.White;
			this._tcStatusButton.Image = global::Bloom.Properties.Resources.Team32x32;
			this._tcStatusButton.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
			this._tcStatusButton.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._L10NSharpExtender.SetLocalizableToolTip(this._tcStatusButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._tcStatusButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._tcStatusButton, "TeamCollection.TeamCollection");
			this._tcStatusButton.Name = "_tcStatusButton";
			this._tcStatusButton.Padding = new System.Windows.Forms.Padding(10, 0, 10, 0);
			this._tcStatusButton.ShowExtraIcon = false;
			this._tcStatusButton.Size = new System.Drawing.Size(116, 66);
			this._tcStatusButton.Text = "Team Collection";
			this._tcStatusButton.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
			this._tcStatusButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._tcStatusButton.ToolTipText = "Team Collection Status";
			this._tcStatusButton.Click += new System.EventHandler(this._tcStatusButton_Click);
			// 
			// _toolStrip
			// 
			this._toolStrip.BackColor = System.Drawing.Color.Transparent;
			this._toolStrip.Dock = System.Windows.Forms.DockStyle.Right;
            this._toolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this._toolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._settingsButton,
            this._legacySettingsButton,
            this._openCreateCollectionButton});
            this._toolStrip.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.HorizontalStackWithOverflow;
            this._L10NSharpExtender.SetLocalizableToolTip(this._toolStrip, null);
            this._L10NSharpExtender.SetLocalizationComment(this._toolStrip, null);
            this._L10NSharpExtender.SetLocalizationPriority(this._toolStrip, L10NSharp.LocalizationPriority.NotLocalizable);
            this._L10NSharpExtender.SetLocalizingId(this._toolStrip, "WorkspaceView._toolStrip");
            this._toolStrip.Location = new System.Drawing.Point(507, 0);
            this._toolStrip.Name = "_toolStrip";
            this._toolStrip.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this._toolStrip.Size = new System.Drawing.Size(260, 69);
            this._toolStrip.TabIndex = 31;
            this._toolStrip.Text = "_toolStrip";
			// 
			// _legacySettingsButton
			// 
			this._legacySettingsButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this._legacySettingsButton.Image = global::Bloom.Properties.Resources.settings24x24;
            this._legacySettingsButton.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this._legacySettingsButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this._L10NSharpExtender.SetLocalizableToolTip(this._legacySettingsButton, null);
            this._L10NSharpExtender.SetLocalizationComment(this._legacySettingsButton, null);
            this._L10NSharpExtender.SetLocalizationPriority(this._legacySettingsButton, L10NSharp.LocalizationPriority.Medium);
            this._L10NSharpExtender.SetLocalizingId(this._legacySettingsButton, "CollectionTab.SettingsButton");
            this._legacySettingsButton.Name = "_legacySettingsButton";
            this._legacySettingsButton.Size = new System.Drawing.Size(75, 66);
            this._legacySettingsButton.Text = "Settings";
            this._legacySettingsButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this._legacySettingsButton.Click += new System.EventHandler(this._legacySettingsButton_Click);
			// 
			// _settingsButton
			// 
			this._settingsButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._settingsButton.Image = global::Bloom.Properties.Resources.settings24x24;
			this._settingsButton.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
			this._settingsButton.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._L10NSharpExtender.SetLocalizableToolTip(this._settingsButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._settingsButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._settingsButton, "CollectionTab.SettingsButton");
			this._settingsButton.Name = "_settingsButton";
			this._settingsButton.Size = new System.Drawing.Size(53, 66);
			this._settingsButton.Text = "Settings";
			this._settingsButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._settingsButton.Click += new System.EventHandler(this._settingsButton_Click);
			// 
			// _openCreateCollectionButton
			// 
			this._openCreateCollectionButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this._openCreateCollectionButton.Image = global::Bloom.Properties.Resources.OpenCreateCollection24x24;
            this._openCreateCollectionButton.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this._openCreateCollectionButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this._L10NSharpExtender.SetLocalizableToolTip(this._openCreateCollectionButton, "Open/Create/Get Collection");
            this._L10NSharpExtender.SetLocalizationComment(this._openCreateCollectionButton, "This is the button you use to create a new collection, open a new one, or get one" +
        " from a repository somewhere.");
            this._L10NSharpExtender.SetLocalizingId(this._openCreateCollectionButton, "CollectionTab.Open/CreateCollectionButton");
            this._openCreateCollectionButton.Name = "_openCreateCollectionButton";
            this._openCreateCollectionButton.Size = new System.Drawing.Size(98, 66);
            this._openCreateCollectionButton.Text = "Other Collection";
            this._openCreateCollectionButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this._openCreateCollectionButton.Click += new System.EventHandler(this._openCreateCollectionButton_Click);
            // 
            // _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = null;
			// 
			// reactControl2
			// 
			this._reactControl.BackColor = System.Drawing.Color.White;
			this._reactControl.Dock = System.Windows.Forms.DockStyle.Fill;
			this._reactControl.JavascriptBundleName = "collectionsTabPaneBundle";
			this._L10NSharpExtender.SetLocalizableToolTip(this._reactControl, null);
			this._L10NSharpExtender.SetLocalizationComment(this._reactControl, null);
			this._L10NSharpExtender.SetLocalizingId(this._reactControl, "ReactControl");
			this._reactControl.Location = new System.Drawing.Point(0, 0);
			this._reactControl.Name = "_reactControl";
			this._reactControl.Size = new System.Drawing.Size(773, 518);
			this._reactControl.TabIndex = 16;
			// 
			// ReactCollectionTabView
			// 
			this.BackColor = System.Drawing.SystemColors.Control;
			this.Controls.Add(this._reactControl);
			this.Controls.Add(this._topBarControl);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "CollectionTab.LibraryView");
			this.Name = "CollectionTabView";
			this.Size = new System.Drawing.Size(773, 518);
			this._topBarControl.ResumeLayout(false);
			this._topBarControl.PerformLayout();
			this._toolStripLeft.ResumeLayout(false);
			this._toolStripLeft.PerformLayout();
			this._toolStrip.ResumeLayout(false);
            this._toolStrip.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion
		public System.Windows.Forms.Panel _topBarControl;
		private L10NSharp.Windows.Forms.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.ToolStrip _toolStrip;
		private System.Windows.Forms.ToolStripButton _legacySettingsButton;
		private System.Windows.Forms.ToolStripButton _settingsButton;
		private System.Windows.Forms.ToolStripButton _openCreateCollectionButton;
		private System.Windows.Forms.ToolStrip _toolStripLeft;
		private TeamCollectionStatusButton _tcStatusButton;
		private web.ReactControl _reactControl;
	}
}
