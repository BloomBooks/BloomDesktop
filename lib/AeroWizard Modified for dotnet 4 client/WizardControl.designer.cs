﻿namespace AeroWizard
{
	partial class WizardControl
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

		#region Component Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this.titleImageList = new System.Windows.Forms.ImageList(this.components);
			this.commandAreaBorder = new System.Windows.Forms.Panel();
			this.contentArea = new AeroWizard.ThemedTableLayoutPanel();
			this.commandArea = new AeroWizard.ThemedTableLayoutPanel();
			this.cancelButton = new System.Windows.Forms.Button();
			this.nextButton = new System.Windows.Forms.Button();
			this.header = new AeroWizard.ThemedTableLayoutPanel();
			this.headerLabel = new System.Windows.Forms.Label();
			this.titleBar = new AeroWizard.ThemedTableLayoutPanel();
			this.title = new AeroWizard.ThemedLabel();
			this.titleImage = new AeroWizard.ThemedLabel();
			this.backButton = new AeroWizard.ThemeImageButton();
			this.commandArea.SuspendLayout();
			this.header.SuspendLayout();
			this.titleBar.SuspendLayout();
			this.SuspendLayout();
			//
			// titleImageList
			//
			this.titleImageList.ColorDepth = System.Windows.Forms.ColorDepth.Depth32Bit;
			this.titleImageList.ImageSize = new System.Drawing.Size(16, 16);
			this.titleImageList.TransparentColor = System.Drawing.Color.Transparent;
			//
			// commandAreaBorder
			//
			this.commandAreaBorder.BackColor = System.Drawing.SystemColors.ControlLight;
			this.commandAreaBorder.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.commandAreaBorder.Location = new System.Drawing.Point(0, 368);
			this.commandAreaBorder.Margin = new System.Windows.Forms.Padding(0);
			this.commandAreaBorder.Name = "commandAreaBorder";
			this.commandAreaBorder.Size = new System.Drawing.Size(609, 1);
			this.commandAreaBorder.TabIndex = 2;
			//
			// contentArea
			//
			this.contentArea.BackColor = System.Drawing.SystemColors.Window;
			this.contentArea.ColumnCount = 3;
			this.contentArea.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 38F));
			this.contentArea.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.contentArea.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 19F));
			this.contentArea.Dock = System.Windows.Forms.DockStyle.Fill;
			this.contentArea.Location = new System.Drawing.Point(0, 91);
			this.contentArea.Name = "contentArea";
			this.contentArea.RowCount = 2;
			this.contentArea.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.contentArea.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 19F));
			this.contentArea.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
			this.contentArea.Size = new System.Drawing.Size(609, 278);
			this.contentArea.TabIndex = 1;
			this.contentArea.Paint += new System.Windows.Forms.PaintEventHandler(this.contentArea_Paint);
			//
			// commandArea
			//
			this.commandArea.AutoSize = true;
			this.commandArea.BackColor = System.Drawing.SystemColors.Control;
			this.commandArea.ColumnCount = 3;
			this.commandArea.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.commandArea.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this.commandArea.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 19F));
			this.commandArea.Controls.Add(this.cancelButton, 1, 1);
			this.commandArea.Controls.Add(this.nextButton, 0, 1);
			this.commandArea.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.commandArea.Location = new System.Drawing.Point(0, 369);
			this.commandArea.Name = "commandArea";
			this.commandArea.RowCount = 3;
			this.commandArea.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 10F));
			this.commandArea.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.commandArea.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 10F));
			this.commandArea.Size = new System.Drawing.Size(609, 45);
			this.commandArea.TabIndex = 2;
			//
			// cancelButton
			//
			this.cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.cancelButton.AutoSize = true;
			this.cancelButton.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.cancelButton.Location = new System.Drawing.Point(520, 10);
			this.cancelButton.Margin = new System.Windows.Forms.Padding(7, 0, 0, 0);
			this.cancelButton.MinimumSize = new System.Drawing.Size(70, 0);
			this.cancelButton.Name = "cancelButton";
			this.cancelButton.Size = new System.Drawing.Size(70, 25);
			this.cancelButton.TabIndex = 1;
			this.cancelButton.Text = "Cancel";
			this.cancelButton.UseVisualStyleBackColor = true;
			this.cancelButton.Click += new System.EventHandler(this.cancelButton_Click);
			//
			// nextButton
			//
			this.nextButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.nextButton.AutoSize = true;
			this.nextButton.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.nextButton.Location = new System.Drawing.Point(443, 10);
			this.nextButton.Margin = new System.Windows.Forms.Padding(0);
			this.nextButton.MinimumSize = new System.Drawing.Size(70, 0);
			this.nextButton.Name = "nextButton";
			this.nextButton.Size = new System.Drawing.Size(70, 25);
			this.nextButton.TabIndex = 0;
			this.nextButton.Text = "&Next >";
			this.nextButton.UseVisualStyleBackColor = true;
			this.nextButton.Click += new System.EventHandler(this.nextButton_Click);
			//
			// header
			//
			this.header.AutoSize = true;
			this.header.BackColor = System.Drawing.SystemColors.Window;
			this.header.ColumnCount = 1;
			this.header.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.header.Controls.Add(this.headerLabel, 0, 0);
			this.header.Dock = System.Windows.Forms.DockStyle.Top;
			this.header.Location = new System.Drawing.Point(0, 32);
			this.header.Name = "header";
			this.header.RowCount = 1;
			this.header.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.header.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 59F));
			this.header.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 59F));
			this.header.Size = new System.Drawing.Size(609, 59);
			this.header.TabIndex = 0;
			//
			// headerLabel
			//
			this.headerLabel.AutoSize = true;
			this.headerLabel.BackColor = System.Drawing.Color.Transparent;
			this.headerLabel.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.headerLabel.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(19)))), ((int)(((byte)(112)))), ((int)(((byte)(171)))));
			this.headerLabel.Location = new System.Drawing.Point(38, 19);
			this.headerLabel.Margin = new System.Windows.Forms.Padding(38, 19, 0, 19);
			this.headerLabel.Name = "headerLabel";
			this.headerLabel.Size = new System.Drawing.Size(77, 21);
			this.headerLabel.TabIndex = 0;
			this.headerLabel.Text = "Page Title";
			//
			// titleBar
			//
			this.titleBar.AutoSize = true;
			this.titleBar.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.titleBar.BackColor = System.Drawing.SystemColors.ActiveCaption;
			this.titleBar.ColumnCount = 3;
			this.titleBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 32F));
			this.titleBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 23F));
			this.titleBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this.titleBar.Controls.Add(this.title, 2, 0);
			this.titleBar.Controls.Add(this.titleImage, 1, 0);
			this.titleBar.Controls.Add(this.backButton, 0, 0);
			this.titleBar.Dock = System.Windows.Forms.DockStyle.Top;
			this.titleBar.Location = new System.Drawing.Point(0, 0);
			this.titleBar.Name = "titleBar";
			this.titleBar.RowCount = 1;
			this.titleBar.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.titleBar.Size = new System.Drawing.Size(609, 32);
			this.titleBar.SupportGlass = true;
			this.titleBar.TabIndex = 0;
			this.titleBar.WatchFocus = true;
			this.titleBar.MouseDown += new System.Windows.Forms.MouseEventHandler(this.TitleBar_MouseDown);
			this.titleBar.MouseMove += new System.Windows.Forms.MouseEventHandler(this.TitleBar_MouseMove);
			this.titleBar.MouseUp += new System.Windows.Forms.MouseEventHandler(this.TitleBar_MouseUp);
			//
			// title
			//
			this.title.Anchor = System.Windows.Forms.AnchorStyles.Left;
			this.title.AutoSize = true;
			this.title.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
			this.title.Location = new System.Drawing.Point(55, 6);
			this.title.Margin = new System.Windows.Forms.Padding(0);
			this.title.Name = "title";
			this.title.Padding = new System.Windows.Forms.Padding(0, 2, 0, 2);
			this.title.Size = new System.Drawing.Size(69, 19);
			this.title.TabIndex = 2;
			this.title.Text = "Wizard Title";
			this.title.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			//
			// titleImage
			//
			this.titleImage.Anchor = System.Windows.Forms.AnchorStyles.Left;
			this.titleImage.AutoSize = true;
			this.titleImage.ImageAlign = System.Drawing.ContentAlignment.TopLeft;
			this.titleImage.ImageList = this.titleImageList;
			this.titleImage.Location = new System.Drawing.Point(32, 8);
			this.titleImage.Margin = new System.Windows.Forms.Padding(0, 0, 7, 0);
			this.titleImage.MinimumSize = new System.Drawing.Size(16, 16);
			this.titleImage.Name = "titleImage";
			this.titleImage.Size = new System.Drawing.Size(16, 16);
			this.titleImage.TabIndex = 1;
			//
			// backButton
			//
			this.backButton.Anchor = System.Windows.Forms.AnchorStyles.Left;
			this.backButton.Enabled = false;
			this.backButton.Location = new System.Drawing.Point(0, 0);
			this.backButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 2);
			this.backButton.Name = "backButton";
			this.backButton.Size = new System.Drawing.Size(30, 30);
			this.backButton.StyleClass = "NAVIGATION";
			this.backButton.TabIndex = 0;
			this.backButton.UseVisualStyleBackColor = true;
			this.backButton.Click += new System.EventHandler(this.backButton_Click);
			//
			// WizardControl
			//
			this.Controls.Add(this.commandAreaBorder);
			this.Controls.Add(this.contentArea);
			this.Controls.Add(this.commandArea);
			this.Controls.Add(this.header);
			this.Controls.Add(this.titleBar);
			this.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.Name = "WizardControl";
			this.Size = new System.Drawing.Size(609, 414);
			this.commandArea.ResumeLayout(false);
			this.commandArea.PerformLayout();
			this.header.ResumeLayout(false);
			this.header.PerformLayout();
			this.titleBar.ResumeLayout(false);
			this.titleBar.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private AeroWizard.ThemedTableLayoutPanel titleBar;
		private AeroWizard.ThemedTableLayoutPanel header;
		internal System.Windows.Forms.Label headerLabel;
		private AeroWizard.ThemedTableLayoutPanel commandArea;
		private System.Windows.Forms.Button cancelButton;
		internal System.Windows.Forms.Button nextButton;
		private AeroWizard.ThemedLabel title;
		private AeroWizard.ThemedLabel titleImage;
		internal AeroWizard.ThemeImageButton backButton;
		private AeroWizard.ThemedTableLayoutPanel contentArea;
		private System.Windows.Forms.Panel commandAreaBorder;
		private System.Windows.Forms.ImageList titleImageList;

	}
}
