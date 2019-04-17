namespace Bloom.CollectionChoosing
{
	partial class OpenCreateCloneControl
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
			this._debounceListIndexChangedEvent = new System.Windows.Forms.Timer(this.components);
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			this.tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
			this._browseButton = new System.Windows.Forms.Button();
			this.button9 = new System.Windows.Forms.Button();
			this._templateButton = new System.Windows.Forms.Button();
			this._topRightPanel = new System.Windows.Forms.Panel();
			this._toolStrip1 = new System.Windows.Forms.ToolStrip();
			this._uiLanguageMenu = new System.Windows.Forms.ToolStripDropDownButton();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this.tableLayoutPanel2.SuspendLayout();
			this._topRightPanel.SuspendLayout();
			this._toolStrip1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// toolTip1
			// 
			this.toolTip1.AutomaticDelay = 300;
			// 
			// tableLayoutPanel2
			// 
			this.tableLayoutPanel2.BackColor = System.Drawing.Color.White;
			this.tableLayoutPanel2.ColumnCount = 3;
			this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 260F));
			this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 272F));
			this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.tableLayoutPanel2.Controls.Add(this._browseButton, 0, 5);
			this.tableLayoutPanel2.Controls.Add(this.button9, 0, 0);
			this.tableLayoutPanel2.Controls.Add(this._templateButton, 0, 1);
			this.tableLayoutPanel2.Controls.Add(this._topRightPanel, 2, 0);
			this.tableLayoutPanel2.Dock = System.Windows.Forms.DockStyle.Fill;
			this.tableLayoutPanel2.Location = new System.Drawing.Point(0, 0);
			this.tableLayoutPanel2.Name = "tableLayoutPanel2";
			this.tableLayoutPanel2.RowCount = 7;
			this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 60F));
			this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 45F));
			this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 45F));
			this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 45F));
			this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 55F));
			this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 55F));
			this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
			this.tableLayoutPanel2.Size = new System.Drawing.Size(889, 343);
			this.tableLayoutPanel2.TabIndex = 19;
			// 
			// _browseButton
			// 
			this._browseButton.Dock = System.Windows.Forms.DockStyle.Fill;
			this._browseButton.FlatAppearance.BorderSize = 0;
			this._browseButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._browseButton.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._browseButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._browseButton.Image = global::Bloom.Properties.Resources.open;
			this._browseButton.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this._L10NSharpExtender.SetLocalizableToolTip(this._browseButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._browseButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._browseButton, "OpenCreateNewCollectionsDialog.BrowseForOtherCollections");
			this._browseButton.Location = new System.Drawing.Point(3, 253);
			this._browseButton.Name = "_browseButton";
			this._browseButton.Size = new System.Drawing.Size(254, 49);
			this._browseButton.TabIndex = 29;
			this._browseButton.Text = "Browse for another collection on this computer";
			this._browseButton.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this._browseButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
			this._browseButton.UseVisualStyleBackColor = true;
			this._browseButton.Click += new System.EventHandler(this.OnBrowseForExistingLibraryClick);
			// 
			// button9
			// 
			this.button9.FlatAppearance.BorderSize = 0;
			this.button9.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.button9.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.button9.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.button9.Image = global::Bloom.Properties.Resources.newLibrary32x32;
			this.button9.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this._L10NSharpExtender.SetLocalizableToolTip(this.button9, null);
			this._L10NSharpExtender.SetLocalizationComment(this.button9, null);
			this._L10NSharpExtender.SetLocalizingId(this.button9, "OpenCreateNewCollectionsDialog.CreateNewCollection");
			this.button9.Location = new System.Drawing.Point(3, 3);
			this.button9.Name = "button9";
			this.button9.Size = new System.Drawing.Size(244, 36);
			this.button9.TabIndex = 22;
			this.button9.Text = "Create New Collection";
			this.button9.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
			this.button9.UseVisualStyleBackColor = true;
			this.button9.Click += new System.EventHandler(this.CreateNewLibrary_LinkClicked);
			// 
			// _templateButton
			// 
			this._templateButton.Dock = System.Windows.Forms.DockStyle.Fill;
			this._templateButton.FlatAppearance.BorderSize = 0;
			this._templateButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._templateButton.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._templateButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._templateButton.Image = global::Bloom.Properties.Resources.library32x32;
			this._templateButton.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this._L10NSharpExtender.SetLocalizableToolTip(this._templateButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._templateButton, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._templateButton, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._templateButton, "OpenCreateNewCollectionsDialog.OpenCreateCloneControl._templateButton");
			this._templateButton.Location = new System.Drawing.Point(3, 63);
			this._templateButton.Name = "_templateButton";
			this._templateButton.Size = new System.Drawing.Size(254, 39);
			this._templateButton.TabIndex = 23;
			this._templateButton.Text = "template collection button";
			this._templateButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
			this._templateButton.UseVisualStyleBackColor = true;
			// 
			// _topRightPanel
			// 
			this._topRightPanel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._topRightPanel.Controls.Add(this._toolStrip1);
			this._topRightPanel.Location = new System.Drawing.Point(535, 3);
			this._topRightPanel.Name = "_topRightPanel";
			this._topRightPanel.Size = new System.Drawing.Size(351, 54);
			this._topRightPanel.TabIndex = 31;
			// 
			// _toolStrip1
			// 
			this._toolStrip1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._toolStrip1.BackColor = System.Drawing.Color.Transparent;
			this._toolStrip1.Dock = System.Windows.Forms.DockStyle.None;
			this._toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this._toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._uiLanguageMenu});
			this._L10NSharpExtender.SetLocalizableToolTip(this._toolStrip1, "Change user interface language");
			this._L10NSharpExtender.SetLocalizationComment(this._toolStrip1, null);
			this._L10NSharpExtender.SetLocalizingId(this._toolStrip1, "OpenCreateNewCollectionsDialog.UILanguageMenu");
			this._toolStrip1.Location = new System.Drawing.Point(286, 9);
			this._toolStrip1.Name = "_toolStrip1";
			this._toolStrip1.Size = new System.Drawing.Size(65, 25);
			this._toolStrip1.TabIndex = 25;
			// 
			// _uiLanguageMenu
			// 
			this._uiLanguageMenu.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
			this._uiLanguageMenu.AutoToolTip = false;
			this._uiLanguageMenu.BackColor = System.Drawing.Color.Transparent;
			this._uiLanguageMenu.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this._uiLanguageMenu.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._uiLanguageMenu.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._L10NSharpExtender.SetLocalizableToolTip(this._uiLanguageMenu, null);
			this._L10NSharpExtender.SetLocalizationComment(this._uiLanguageMenu, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._uiLanguageMenu, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._uiLanguageMenu, "OpenCreateNewCollectionsDialog._uiLanguageMenu");
			this._uiLanguageMenu.Name = "_uiLanguageMenu";
			this._uiLanguageMenu.Size = new System.Drawing.Size(62, 22);
			this._uiLanguageMenu.Text = "English";
			this._uiLanguageMenu.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "OpenCreateNewCollectionsDialog";
			// 
			// OpenCreateCloneControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			this.BackColor = System.Drawing.Color.White;
			this.Controls.Add(this.tableLayoutPanel2);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "OpenCreateNewCollectionsDialog.OpenCreateCloneControl.OpenCreateCloneControl");
			this.Name = "OpenCreateCloneControl";
			this.Size = new System.Drawing.Size(889, 343);
			this.Load += new System.EventHandler(this.OnLoad);
			this.tableLayoutPanel2.ResumeLayout(false);
			this._topRightPanel.ResumeLayout(false);
			this._topRightPanel.PerformLayout();
			this._toolStrip1.ResumeLayout(false);
			this._toolStrip1.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);

		}

#endregion

		private System.Windows.Forms.Timer _debounceListIndexChangedEvent;
		private System.Windows.Forms.ToolTip toolTip1;
		private System.Windows.Forms.TableLayoutPanel tableLayoutPanel2;
		private System.Windows.Forms.Button button9;
		private System.Windows.Forms.Button _templateButton;
		private System.Windows.Forms.Button _browseButton;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.Panel _topRightPanel;
		private System.Windows.Forms.ToolStrip _toolStrip1;
		private System.Windows.Forms.ToolStripDropDownButton _uiLanguageMenu;
	}
}