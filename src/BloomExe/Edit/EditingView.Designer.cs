namespace Bloom.Edit
{
	partial class EditingView
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
				if (_editButtonsUpdateTimer != null)
				{
					_editButtonsUpdateTimer.Stop();
					_editButtonsUpdateTimer.Dispose();
					_editButtonsUpdateTimer = null;
				}

				if (_handleMessageTimer != null)
				{
					_handleMessageTimer.Stop();
					_handleMessageTimer.Dispose();
					_handleMessageTimer = null;
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
            this._editButtonsUpdateTimer = new System.Windows.Forms.Timer(this.components);
            this._handleMessageTimer = new System.Windows.Forms.Timer(this.components);
            this.settingsLauncherHelper1 = new SIL.Windows.Forms.SettingProtection.SettingsProtectionHelper(this.components);
            this._L10NSharpExtender = new L10NSharp.Windows.Forms.L10NSharpExtender(this.components);
            this._splitContainer2 = new Bloom.ToPalaso.BetterSplitContainer(this.components);
            this._topBarPanel = new System.Windows.Forms.Panel();
            this._rightToolStrip = new System.Windows.Forms.ToolStrip();
            this._editControlsReactControl = new Bloom.web.ReactControl();
            this._bookSettingsButton = new System.Windows.Forms.ToolStripButton();
            this._splitTemplateAndSource = new Bloom.ToPalaso.BetterSplitContainer(this.components);
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this._splitContainer2)).BeginInit();
            this._splitContainer2.Panel1.SuspendLayout();
            this._splitContainer2.Panel2.SuspendLayout();
            this._splitContainer2.SuspendLayout();
            this._topBarPanel.SuspendLayout();
            this._rightToolStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this._splitTemplateAndSource)).BeginInit();
            this._splitTemplateAndSource.SuspendLayout();
            this.SuspendLayout();
            // 
            // _editButtonsUpdateTimer
            // 
            this._editButtonsUpdateTimer.Tick += new System.EventHandler(this._editButtonsUpdateTimer_Tick);
            // 
            // _handleMessageTimer
            // 
            this._handleMessageTimer.Tick += new System.EventHandler(this._handleMessageTimer_Tick);
            // 
            // _L10NSharpExtender
            // 
            this._L10NSharpExtender.LocalizationManagerId = "Bloom";
            this._L10NSharpExtender.PrefixForNewItems = null;
            // 
            // _splitContainer2
            // 
            this._splitContainer2.Dock = System.Windows.Forms.DockStyle.Fill;
            this._L10NSharpExtender.SetLocalizableToolTip(this._splitContainer2, null);
            this._L10NSharpExtender.SetLocalizationComment(this._splitContainer2, null);
            this._L10NSharpExtender.SetLocalizingId(this._splitContainer2, "EditTab._splitContainer2");
            this._splitContainer2.Location = new System.Drawing.Point(0, 0);
            this._splitContainer2.Margin = new System.Windows.Forms.Padding(4);
            this._splitContainer2.Name = "_splitContainer2";
            // 
            // _splitContainer2.Panel1
            // 
            this._splitContainer2.Panel1.Controls.Add(this._topBarPanel);
            // 
            // _splitContainer2.Panel2
            // 
            this._splitContainer2.Panel2.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(63)))), ((int)(((byte)(64)))));
            this._splitContainer2.Panel2.Controls.Add(this._splitTemplateAndSource);
            this._splitContainer2.Size = new System.Drawing.Size(1200, 561);
            this._splitContainer2.SplitterDistance = 810;
            this._splitContainer2.SplitterWidth = 10;
            this._splitContainer2.TabIndex = 0;
            this._splitContainer2.TabStop = false;
            // 
            // _topBarPanel
            // 
            this._topBarPanel.Controls.Add(this._rightToolStrip);
            this._topBarPanel.Controls.Add(this._editControlsReactControl);
            this._topBarPanel.Location = new System.Drawing.Point(83, 186);
            this._topBarPanel.Name = "_topBarPanel";
            this._topBarPanel.Size = new System.Drawing.Size(724, 66);
            this._topBarPanel.TabIndex = 3;
            this._topBarPanel.Click += new System.EventHandler(this._topBarPanel_Click);
            // 
            // _rightToolStrip
            // 
            this._rightToolStrip.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(102)))), ((int)(((byte)(143)))));
            this._rightToolStrip.Dock = System.Windows.Forms.DockStyle.Right;
            this._rightToolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this._rightToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._bookSettingsButton});
            this._rightToolStrip.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.HorizontalStackWithOverflow;
            this._L10NSharpExtender.SetLocalizableToolTip(this._rightToolStrip, null);
            this._L10NSharpExtender.SetLocalizationComment(this._rightToolStrip, null);
            this._L10NSharpExtender.SetLocalizationPriority(this._rightToolStrip, L10NSharp.LocalizationPriority.NotLocalizable);
            this._L10NSharpExtender.SetLocalizingId(this._rightToolStrip, "EditingView._rightToolStrip");
            this._rightToolStrip.Location = new System.Drawing.Point(638, 0);
            this._rightToolStrip.Name = "_rightToolStrip";
            this._rightToolStrip.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this._rightToolStrip.Size = new System.Drawing.Size(86, 66);
            this._rightToolStrip.TabIndex = 32;
            this._rightToolStrip.Text = "_rightToolStrip";
            // 
            // _bookSettingsButton
            // 
            this._bookSettingsButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(49)))), ((int)(((byte)(32)))), ((int)(((byte)(46)))));
            this._bookSettingsButton.Image = global::Bloom.Properties.Resources.book_settings;
            this._bookSettingsButton.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this._bookSettingsButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this._L10NSharpExtender.SetLocalizableToolTip(this._bookSettingsButton, null);
            this._L10NSharpExtender.SetLocalizationComment(this._bookSettingsButton, null);
            this._L10NSharpExtender.SetLocalizingId(this._bookSettingsButton, "Common.BookSettings");
            this._bookSettingsButton.Name = "_bookSettingsButton";
            this._bookSettingsButton.Size = new System.Drawing.Size(83, 63);
            this._bookSettingsButton.Text = "Book Settings";
            this._bookSettingsButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this._bookSettingsButton.Click += new System.EventHandler(this._bookSettingsButton_Click);
            // 
            // _editControlsReactControl
            // 
            this._editControlsReactControl.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(102)))), ((int)(((byte)(143)))));
            this._editControlsReactControl.HideVerticalOverflow = true;
            this._editControlsReactControl.JavascriptBundleName = "editTopBarControlsBundle";
            this._L10NSharpExtender.SetLocalizableToolTip(this._editControlsReactControl, null);
            this._L10NSharpExtender.SetLocalizationComment(this._editControlsReactControl, null);
            this._L10NSharpExtender.SetLocalizationPriority(this._editControlsReactControl, L10NSharp.LocalizationPriority.NotLocalizable);
            this._L10NSharpExtender.SetLocalizingId(this._editControlsReactControl, "EditingView._editControlsReactControl");
            this._editControlsReactControl.Location = new System.Drawing.Point(0, 0);
            this._editControlsReactControl.Name = "_editControlsReactControl";
            this._editControlsReactControl.Size = new System.Drawing.Size(420, 66);
            this._editControlsReactControl.TabIndex = 33;
            // 
            // _splitTemplateAndSource
            // 
            this._splitTemplateAndSource.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(102)))), ((int)(((byte)(143)))));
            this._splitTemplateAndSource.Dock = System.Windows.Forms.DockStyle.Fill;
            this._L10NSharpExtender.SetLocalizableToolTip(this._splitTemplateAndSource, null);
            this._L10NSharpExtender.SetLocalizationComment(this._splitTemplateAndSource, null);
            this._L10NSharpExtender.SetLocalizingId(this._splitTemplateAndSource, "EditTab.SplitTemplateAndSource");
            this._splitTemplateAndSource.Location = new System.Drawing.Point(0, 0);
            this._splitTemplateAndSource.Margin = new System.Windows.Forms.Padding(4);
            this._splitTemplateAndSource.Name = "_splitTemplateAndSource";
            this._splitTemplateAndSource.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // _splitTemplateAndSource.Panel1
            // 
            this._splitTemplateAndSource.Panel1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            // 
            // _splitTemplateAndSource.Panel2
            // 
            this._splitTemplateAndSource.Panel2.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this._splitTemplateAndSource.Size = new System.Drawing.Size(380, 561);
            this._splitTemplateAndSource.SplitterDistance = 230;
            this._splitTemplateAndSource.SplitterWidth = 10;
            this._splitTemplateAndSource.TabIndex = 0;
            this._splitTemplateAndSource.TabStop = false;
            // 
            // EditingView
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.Controls.Add(this._splitContainer2);
            this._L10NSharpExtender.SetLocalizableToolTip(this, null);
            this._L10NSharpExtender.SetLocalizationComment(this, null);
            this._L10NSharpExtender.SetLocalizingId(this, "EditTab.EditingView");
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "EditingView";
            this.Size = new System.Drawing.Size(1200, 561);
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
            this._splitContainer2.Panel1.ResumeLayout(false);
            this._splitContainer2.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this._splitContainer2)).EndInit();
            this._splitContainer2.ResumeLayout(false);
            this._topBarPanel.ResumeLayout(false);
            this._topBarPanel.PerformLayout();
            this._rightToolStrip.ResumeLayout(false);
            this._rightToolStrip.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this._splitTemplateAndSource)).EndInit();
            this._splitTemplateAndSource.ResumeLayout(false);
            this.ResumeLayout(false);

		}

		#endregion

		private Bloom.ToPalaso.BetterSplitContainer _splitContainer2;
		private Browser _browser1;
		private Bloom.ToPalaso.BetterSplitContainer _splitTemplateAndSource;
		private System.Windows.Forms.Timer _editButtonsUpdateTimer;
		private System.Windows.Forms.Timer _handleMessageTimer;
		private System.Windows.Forms.Panel _topBarPanel;
		private SIL.Windows.Forms.SettingProtection.SettingsProtectionHelper settingsLauncherHelper1;
		private L10NSharp.Windows.Forms.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.ToolStrip _rightToolStrip;
        private Bloom.web.ReactControl _editControlsReactControl;
        private System.Windows.Forms.ToolStripButton _bookSettingsButton;
	}
}
