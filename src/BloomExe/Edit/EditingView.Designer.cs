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
			System.Drawing.Imaging.ImageAttributes imageAttributes1 = new System.Drawing.Imaging.ImageAttributes();
			System.Drawing.Imaging.ImageAttributes imageAttributes2 = new System.Drawing.Imaging.ImageAttributes();
			System.Drawing.Imaging.ImageAttributes imageAttributes3 = new System.Drawing.Imaging.ImageAttributes();
			System.Drawing.Imaging.ImageAttributes imageAttributes4 = new System.Drawing.Imaging.ImageAttributes();
			this._editButtonsUpdateTimer = new System.Windows.Forms.Timer(this.components);
			this._handleMessageTimer = new System.Windows.Forms.Timer(this.components);
			this.settingsLauncherHelper1 = new SIL.Windows.Forms.SettingProtection.SettingsProtectionHelper(this.components);
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._splitContainer2 = new Bloom.ToPalaso.BetterSplitContainer(this.components);
			this._topBarPanel = new System.Windows.Forms.Panel();
			this._rightToolStrip = new System.Windows.Forms.ToolStrip();
			this._bookSettingsButton = new System.Windows.Forms.ToolStripButton();
			this._undoButton = new SIL.Windows.Forms.Widgets.BitmapButton();
			this._cutButton = new SIL.Windows.Forms.Widgets.BitmapButton();
			this._pasteButton = new SIL.Windows.Forms.Widgets.BitmapButton();
			this._copyButton = new SIL.Windows.Forms.Widgets.BitmapButton();
			this._menusToolStrip = new System.Windows.Forms.ToolStrip();
			this._contentLanguagesDropdown = new System.Windows.Forms.ToolStripDropDownButton();
			this._layoutChoices = new System.Windows.Forms.ToolStripDropDownButton();
			this._splitTemplateAndSource = new Bloom.ToPalaso.BetterSplitContainer(this.components);
			this._betterToolTip1 = new Bloom.ToPalaso.BetterToolTip(this.components);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this._splitContainer2)).BeginInit();
			this._splitContainer2.Panel1.SuspendLayout();
			this._splitContainer2.Panel2.SuspendLayout();
			this._splitContainer2.SuspendLayout();
			this._topBarPanel.SuspendLayout();
			this._rightToolStrip.SuspendLayout();
			this._menusToolStrip.SuspendLayout();
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
			this._topBarPanel.Controls.Add(this._undoButton);
			this._topBarPanel.Controls.Add(this._cutButton);
			this._topBarPanel.Controls.Add(this._pasteButton);
			this._topBarPanel.Controls.Add(this._copyButton);
			this._topBarPanel.Controls.Add(this._menusToolStrip);
			this._topBarPanel.Location = new System.Drawing.Point(83, 186);
			this._topBarPanel.Name = "_topBarPanel";
			this._topBarPanel.Size = new System.Drawing.Size(468, 66);
			this._topBarPanel.TabIndex = 3;
			//
			// _rightToolStrip
			//
			this._rightToolStrip.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(102)))), ((int)(((byte)(143)))));
			this._rightToolStrip.Dock = System.Windows.Forms.DockStyle.Right;
			this._rightToolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this._rightToolStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
			this._rightToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
			this._bookSettingsButton});
			this._rightToolStrip.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.HorizontalStackWithOverflow;
			this._L10NSharpExtender.SetLocalizableToolTip(this._rightToolStrip, null);
			this._L10NSharpExtender.SetLocalizationComment(this._rightToolStrip, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._rightToolStrip, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._rightToolStrip, "WorkspaceView._toolStrip");
			this._rightToolStrip.Location = new System.Drawing.Point(292, 0);
			this._rightToolStrip.Name = "_rightToolStrip";
			this._rightToolStrip.RightToLeft = System.Windows.Forms.RightToLeft.No;
			this._rightToolStrip.Size = new System.Drawing.Size(176, 66);
			this._rightToolStrip.TabIndex = 32;
			this._rightToolStrip.Text = "_toolStrip";
			this._rightToolStrip.Visible = false;
			//
			// _bookSettingsButton
			//
			this._bookSettingsButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._bookSettingsButton.Image = global::Bloom.Properties.Resources.gear_black_24x24;
			this._bookSettingsButton.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
			this._bookSettingsButton.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._L10NSharpExtender.SetLocalizableToolTip(this._bookSettingsButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._bookSettingsButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._bookSettingsButton, "Common.BookSettings");
			this._bookSettingsButton.Name = "_bookSettingsButton";
			this._bookSettingsButton.Size = new System.Drawing.Size(126, 61);
			this._bookSettingsButton.Text = "Book Settings";
			this._bookSettingsButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._bookSettingsButton.Visible = false;
			this._bookSettingsButton.Click += new System.EventHandler(this._bookSettingsButton_Click);
			//
			// _undoButton
			//
			this._undoButton.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(102)))), ((int)(((byte)(143)))));
			this._undoButton.BorderColor = System.Drawing.Color.Transparent;
			this._undoButton.DisabledTextColor = System.Drawing.Color.FromArgb(((int)(((byte)(114)))), ((int)(((byte)(74)))), ((int)(((byte)(106)))));
			this._undoButton.FlatAppearance.BorderSize = 0;
			this._undoButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._undoButton.FocusRectangleEnabled = true;
			this._undoButton.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._undoButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(49)))), ((int)(((byte)(32)))), ((int)(((byte)(46)))));
			this._undoButton.Image = null;
			this._undoButton.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
			this._undoButton.ImageAttributes = imageAttributes1;
			this._undoButton.ImageBorderColor = System.Drawing.Color.Transparent;
			this._undoButton.ImageBorderEnabled = false;
			this._undoButton.ImageDropShadow = false;
			this._undoButton.ImageFocused = null;
			this._undoButton.ImageInactive = global::Bloom.Properties.Resources.undoDisabled32x32;
			this._undoButton.ImageMouseOver = null;
			this._undoButton.ImageNormal = global::Bloom.Properties.Resources.undo32x32;
			this._undoButton.ImagePressed = null;
			this._undoButton.InnerBorderColor = System.Drawing.Color.Transparent;
			this._undoButton.InnerBorderColor_Focus = System.Drawing.Color.LightBlue;
			this._undoButton.InnerBorderColor_MouseOver = System.Drawing.Color.Gold;
			this._L10NSharpExtender.SetLocalizableToolTip(this._undoButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._undoButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._undoButton, "EditTab.UndoButton");
			this._undoButton.Location = new System.Drawing.Point(128, 0);
			this._undoButton.Name = "_undoButton";
			this._undoButton.OffsetPressedContent = true;
			this._undoButton.Size = new System.Drawing.Size(54, 66);
			this._undoButton.StretchImage = false;
			this._undoButton.TabIndex = 9;
			this._undoButton.Text = "Undo";
			this._undoButton.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
			this._undoButton.TextDropShadow = false;
			this._undoButton.TextWordWrap = false;
			this._betterToolTip1.SetToolTip(this._undoButton, "Undo (Ctrl+Z)");
			this._betterToolTip1.SetToolTipWhenDisabled(this._undoButton, "There is nothing to undo");
			this._undoButton.UseVisualStyleBackColor = false;
			this._undoButton.Click += new System.EventHandler(this._undoButton_Click);
			//
			// _cutButton
			//
			this._cutButton.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(102)))), ((int)(((byte)(143)))));
			this._cutButton.BorderColor = System.Drawing.Color.Transparent;
			this._cutButton.DisabledTextColor = System.Drawing.Color.FromArgb(((int)(((byte)(114)))), ((int)(((byte)(74)))), ((int)(((byte)(106)))));
			this._cutButton.FlatAppearance.BorderSize = 0;
			this._cutButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._cutButton.FocusRectangleEnabled = true;
			this._cutButton.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._cutButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(49)))), ((int)(((byte)(32)))), ((int)(((byte)(46)))));
			this._cutButton.Image = null;
			this._cutButton.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this._cutButton.ImageAttributes = imageAttributes2;
			this._cutButton.ImageBorderColor = System.Drawing.Color.Transparent;
			this._cutButton.ImageBorderEnabled = false;
			this._cutButton.ImageDropShadow = false;
			this._cutButton.ImageFocused = null;
			this._cutButton.ImageInactive = global::Bloom.Properties.Resources.cutDisable16x16;
			this._cutButton.ImageMouseOver = null;
			this._cutButton.ImageNormal = global::Bloom.Properties.Resources.Cut16x16;
			this._cutButton.ImagePressed = null;
			this._cutButton.InnerBorderColor = System.Drawing.Color.Transparent;
			this._cutButton.InnerBorderColor_Focus = System.Drawing.Color.LightBlue;
			this._cutButton.InnerBorderColor_MouseOver = System.Drawing.Color.Gold;
			this._L10NSharpExtender.SetLocalizableToolTip(this._cutButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._cutButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._cutButton, "EditTab.CutButton");
			this._cutButton.Location = new System.Drawing.Point(52, 7);
			this._cutButton.Name = "_cutButton";
			this._cutButton.OffsetPressedContent = true;
			this._cutButton.Size = new System.Drawing.Size(75, 20);
			this._cutButton.StretchImage = false;
			this._cutButton.TabIndex = 8;
			this._cutButton.Text = "Cut";
			this._cutButton.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this._cutButton.TextDropShadow = false;
			this._cutButton.TextWordWrap = false;
			this._betterToolTip1.SetToolTip(this._cutButton, "Cut (Ctrl+X)");
			this._cutButton.UseVisualStyleBackColor = false;
			this._cutButton.Click += new System.EventHandler(this._cutButton_Click);
			//
			// _pasteButton
			//
			this._pasteButton.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(102)))), ((int)(((byte)(143)))));
			this._pasteButton.BorderColor = System.Drawing.Color.Transparent;
			this._pasteButton.DisabledTextColor = System.Drawing.Color.FromArgb(((int)(((byte)(114)))), ((int)(((byte)(74)))), ((int)(((byte)(106)))));
			this._pasteButton.FlatAppearance.BorderSize = 0;
			this._pasteButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._pasteButton.FocusRectangleEnabled = true;
			this._pasteButton.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._pasteButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(49)))), ((int)(((byte)(32)))), ((int)(((byte)(46)))));
			this._pasteButton.Image = null;
			this._pasteButton.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
			this._pasteButton.ImageAttributes = imageAttributes3;
			this._pasteButton.ImageBorderColor = System.Drawing.Color.Transparent;
			this._pasteButton.ImageBorderEnabled = false;
			this._pasteButton.ImageDropShadow = false;
			this._pasteButton.ImageFocused = null;
			this._pasteButton.ImageInactive = global::Bloom.Properties.Resources.pasteDisabled32x32;
			this._pasteButton.ImageMouseOver = null;
			this._pasteButton.ImageNormal = global::Bloom.Properties.Resources.paste32x32;
			this._pasteButton.ImagePressed = null;
			this._pasteButton.InnerBorderColor = System.Drawing.Color.Transparent;
			this._pasteButton.InnerBorderColor_Focus = System.Drawing.Color.LightBlue;
			this._pasteButton.InnerBorderColor_MouseOver = System.Drawing.Color.Gold;
			this._L10NSharpExtender.SetLocalizableToolTip(this._pasteButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._pasteButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._pasteButton, "EditTab.PasteButton");
			this._pasteButton.Location = new System.Drawing.Point(2, 5);
			this._pasteButton.Name = "_pasteButton";
			this._pasteButton.OffsetPressedContent = true;
			this._pasteButton.Size = new System.Drawing.Size(54, 61);
			this._pasteButton.StretchImage = false;
			this._pasteButton.TabIndex = 7;
			this._pasteButton.Text = "Paste";
			this._pasteButton.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
			this._pasteButton.TextDropShadow = false;
			this._pasteButton.TextWordWrap = false;
			this._betterToolTip1.SetToolTip(this._pasteButton, "Paste (Ctrl+V)");
			this._betterToolTip1.SetToolTipWhenDisabled(this._pasteButton, "There is nothing on the Clipboard that you can paste here.");
			this._pasteButton.UseVisualStyleBackColor = false;
			this._pasteButton.Click += new System.EventHandler(this._pasteButton_Click);
			//
			// _copyButton
			//
			this._copyButton.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(102)))), ((int)(((byte)(143)))));
			this._copyButton.BorderColor = System.Drawing.Color.Transparent;
			this._copyButton.DisabledTextColor = System.Drawing.Color.FromArgb(((int)(((byte)(114)))), ((int)(((byte)(74)))), ((int)(((byte)(106)))));
			this._copyButton.FlatAppearance.BorderSize = 0;
			this._copyButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._copyButton.FocusRectangleEnabled = true;
			this._copyButton.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._copyButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(49)))), ((int)(((byte)(32)))), ((int)(((byte)(46)))));
			this._copyButton.Image = null;
			this._copyButton.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this._copyButton.ImageAttributes = imageAttributes4;
			this._copyButton.ImageBorderColor = System.Drawing.Color.Transparent;
			this._copyButton.ImageBorderEnabled = false;
			this._copyButton.ImageDropShadow = false;
			this._copyButton.ImageFocused = null;
			this._copyButton.ImageInactive = global::Bloom.Properties.Resources.copyDisable16x16;
			this._copyButton.ImageMouseOver = null;
			this._copyButton.ImageNormal = global::Bloom.Properties.Resources.Copy16x16;
			this._copyButton.ImagePressed = null;
			this._copyButton.InnerBorderColor = System.Drawing.Color.Transparent;
			this._copyButton.InnerBorderColor_Focus = System.Drawing.Color.LightBlue;
			this._copyButton.InnerBorderColor_MouseOver = System.Drawing.Color.Gold;
			this._L10NSharpExtender.SetLocalizableToolTip(this._copyButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._copyButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._copyButton, "EditTab.CopyButton");
			this._copyButton.Location = new System.Drawing.Point(52, 29);
			this._copyButton.Name = "_copyButton";
			this._copyButton.OffsetPressedContent = true;
			this._copyButton.Size = new System.Drawing.Size(75, 19);
			this._copyButton.StretchImage = false;
			this._copyButton.TabIndex = 6;
			this._copyButton.Text = "Copy";
			this._copyButton.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this._copyButton.TextDropShadow = false;
			this._copyButton.TextWordWrap = false;
			this._betterToolTip1.SetToolTip(this._copyButton, "Copy (Ctrl+C)");
			this._betterToolTip1.SetToolTipWhenDisabled(this._copyButton, "You need to select some text before you can copy it");
			this._copyButton.UseVisualStyleBackColor = false;
			this._copyButton.Click += new System.EventHandler(this._copyButton_Click);
			//
			// _menusToolStrip
			//
			this._menusToolStrip.AutoSize = false;
			this._menusToolStrip.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(102)))), ((int)(((byte)(143)))));
			this._menusToolStrip.CanOverflow = false;
			this._menusToolStrip.Dock = System.Windows.Forms.DockStyle.None;
			this._menusToolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this._menusToolStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
			this._menusToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
			this._contentLanguagesDropdown,
			this._layoutChoices});
			this._menusToolStrip.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.Table;
			this._L10NSharpExtender.SetLocalizableToolTip(this._menusToolStrip, null);
			this._L10NSharpExtender.SetLocalizationComment(this._menusToolStrip, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._menusToolStrip, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._menusToolStrip, "EditTab.MenusToolStrip");
			this._menusToolStrip.Location = new System.Drawing.Point(200, 0);
			this._menusToolStrip.Name = "_menusToolStrip";
			this._menusToolStrip.Size = new System.Drawing.Size(165, 56);
			this._menusToolStrip.TabIndex = 2;
			//
			// _contentLanguagesDropdown
			//
			this._contentLanguagesDropdown.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this._contentLanguagesDropdown.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(49)))), ((int)(((byte)(32)))), ((int)(((byte)(46)))));
			this._contentLanguagesDropdown.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._L10NSharpExtender.SetLocalizableToolTip(this._contentLanguagesDropdown, "Choose language to make this a bilingual or trilingual book");
			this._L10NSharpExtender.SetLocalizationComment(this._contentLanguagesDropdown, null);
			this._L10NSharpExtender.SetLocalizingId(this._contentLanguagesDropdown, "EditTab.ContentLanguagesDropdown");
			this._contentLanguagesDropdown.Name = "_contentLanguagesDropdown";
			this._contentLanguagesDropdown.Size = new System.Drawing.Size(191, 29);
			this._contentLanguagesDropdown.Text = "Multilingual Settings";
			//
			// _layoutChoices
			//
			this._layoutChoices.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this._layoutChoices.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(49)))), ((int)(((byte)(32)))), ((int)(((byte)(46)))));
			this._layoutChoices.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._L10NSharpExtender.SetLocalizableToolTip(this._layoutChoices, "");
			this._L10NSharpExtender.SetLocalizationComment(this._layoutChoices, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._layoutChoices, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._layoutChoices, "EditTab.PageSizeAndOrientationChoices");
			this._layoutChoices.Name = "_layoutChoices";
			this._layoutChoices.Size = new System.Drawing.Size(74, 29);
			this._layoutChoices.Text = "Paper";
			this._layoutChoices.ToolTipText = "(set dynamically, see code)";
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
			// _betterToolTip1
			//
			this._L10NSharpExtender.SetLocalizableToolTip(this._betterToolTip1, null);
			this._L10NSharpExtender.SetLocalizationComment(this._betterToolTip1, null);
			this._L10NSharpExtender.SetLocalizingId(this._betterToolTip1, "BTT_id");
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
			this._menusToolStrip.ResumeLayout(false);
			this._menusToolStrip.PerformLayout();
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
		private System.Windows.Forms.ToolStrip _menusToolStrip;
		private System.Windows.Forms.ToolStripDropDownButton _contentLanguagesDropdown;
		private System.Windows.Forms.ToolStripDropDownButton _layoutChoices;
		private ToPalaso.BetterToolTip _betterToolTip1;
		private SIL.Windows.Forms.Widgets.BitmapButton _copyButton;
		private SIL.Windows.Forms.Widgets.BitmapButton _pasteButton;
		private SIL.Windows.Forms.Widgets.BitmapButton _cutButton;
		private SIL.Windows.Forms.Widgets.BitmapButton _undoButton;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.ToolStrip _rightToolStrip;
		private System.Windows.Forms.ToolStripButton _bookSettingsButton;
	}
}
