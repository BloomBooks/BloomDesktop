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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(OpenCreateCloneControl));
			this._debounceListIndexChangedEvent = new System.Windows.Forms.Timer(this.components);
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			this.tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
			this._browseButton = new System.Windows.Forms.Button();
			this.button6 = new System.Windows.Forms.Button();
			this.button7 = new System.Windows.Forms.Button();
			this.button8 = new System.Windows.Forms.Button();
			this.button9 = new System.Windows.Forms.Button();
			this._templateButton = new System.Windows.Forms.Button();
			this.label1 = new System.Windows.Forms.Label();
			this._readMoreLink = new System.Windows.Forms.LinkLabel();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this.tableLayoutPanel2.SuspendLayout();
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
			this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 100F));
			this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.tableLayoutPanel2.Controls.Add(this._browseButton, 0, 5);
			this.tableLayoutPanel2.Controls.Add(this.button9, 0, 0);
			this.tableLayoutPanel2.Controls.Add(this._templateButton, 0, 1);
			this.tableLayoutPanel2.Controls.Add(this.label1, 2, 0);
			this.tableLayoutPanel2.Controls.Add(this.button8, 2, 4);
			this.tableLayoutPanel2.Controls.Add(this.button7, 2, 3);
			this.tableLayoutPanel2.Controls.Add(this.button6, 2, 2);
			this.tableLayoutPanel2.Controls.Add(this._readMoreLink, 2, 5);
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
			// button6
			//
			this.button6.FlatAppearance.BorderSize = 0;
			this.button6.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.button6.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.button6.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.button6.Image = global::Bloom.Properties.Resources.cloneFromUsb;
			this.button6.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this._L10NSharpExtender.SetLocalizableToolTip(this.button6, null);
			this._L10NSharpExtender.SetLocalizationComment(this.button6, null);
			this._L10NSharpExtender.SetLocalizingId(this.button6, "OpenCreateNewCollectionsDialog.CopyFromUsbDrive");
			this.button6.Location = new System.Drawing.Point(363, 108);
			this.button6.Name = "button6";
			this.button6.Size = new System.Drawing.Size(404, 39);
			this.button6.TabIndex = 19;
			this.button6.Tag = "sendreceive";
			this.button6.Text = "Copy from USB Drive";
			this.button6.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
			this.button6.UseVisualStyleBackColor = true;
;
			//
			// button7
			//
			this.button7.FlatAppearance.BorderSize = 0;
			this.button7.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.button7.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.button7.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.button7.Image = ((System.Drawing.Image)(resources.GetObject("button7.Image")));
			this.button7.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this._L10NSharpExtender.SetLocalizableToolTip(this.button7, null);
			this._L10NSharpExtender.SetLocalizationComment(this.button7, null);
			this._L10NSharpExtender.SetLocalizingId(this.button7, "OpenCreateNewCollectionsDialog.CopyFromInternet");
			this.button7.Location = new System.Drawing.Point(363, 153);
			this.button7.Name = "button7";
			this.button7.Size = new System.Drawing.Size(405, 36);
			this.button7.TabIndex = 20;
			this.button7.Tag = "sendreceive";
			this.button7.Text = "Copy from Internet";
			this.button7.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
			this.button7.UseVisualStyleBackColor = true;

			//
			// button8
			//
			this.button8.FlatAppearance.BorderSize = 0;
			this.button8.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.button8.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.button8.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.button8.Image = global::Bloom.Properties.Resources.cloneFromChorusHub;
			this.button8.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this._L10NSharpExtender.SetLocalizableToolTip(this.button8, null);
			this._L10NSharpExtender.SetLocalizationComment(this.button8, null);
			this._L10NSharpExtender.SetLocalizingId(this.button8, "OpenCreateNewCollectionsDialog.CopyFromChorusHub");
			this.button8.Location = new System.Drawing.Point(363, 198);
			this.button8.Name = "button8";
			this.button8.Size = new System.Drawing.Size(391, 36);
			this.button8.TabIndex = 21;
			this.button8.Tag = "sendreceive";
			this.button8.Text = "Copy From Chorus Hub on Local Network";
			this.button8.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
			this.button8.UseVisualStyleBackColor = true;

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
			// label1
			//
			this.label1.AutoSize = true;
			this.label1.Font = new System.Drawing.Font("Segoe UI", 9.75F);
			this.label1.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(77)))), ((int)(((byte)(77)))), ((int)(((byte)(77)))));
			this._L10NSharpExtender.SetLocalizableToolTip(this.label1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label1, null);
			this._L10NSharpExtender.SetLocalizingId(this.label1, "OpenCreateNewCollectionsDialog.TextAboutGetUsingChorus");
			this.label1.Location = new System.Drawing.Point(363, 10);
			this.label1.Margin = new System.Windows.Forms.Padding(3, 10, 3, 0);
			this.label1.Name = "label1";
			this.tableLayoutPanel2.SetRowSpan(this.label1, 2);
			this.label1.Size = new System.Drawing.Size(425, 51);
			this.label1.TabIndex = 24;
			this.label1.Tag = "sendreceive";
			this.label1.Text = "Has someone else used Send/Receive to share a collection with you?\r\nUse one of th" +
	"ese red buttons to copy their collection to your computer.\r\nLater, use Send/Rece" +
	"ive to share your work back with them.";
			//
			// _readMoreLink
			//
			this._readMoreLink.AutoSize = true;
			this._readMoreLink.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._readMoreLink, null);
			this._L10NSharpExtender.SetLocalizationComment(this._readMoreLink, "This opens the Chorus Help to learn more about send/receive.");
			this._L10NSharpExtender.SetLocalizingId(this._readMoreLink, "OpenCreateNewCollectionsDialog.ReadMoreLink");
			this._readMoreLink.Location = new System.Drawing.Point(363, 265);
			this._readMoreLink.Margin = new System.Windows.Forms.Padding(3, 15, 3, 0);
			this._readMoreLink.Name = "_readMoreLink";
			this._readMoreLink.Size = new System.Drawing.Size(63, 13);
			this._readMoreLink.TabIndex = 30;
			this._readMoreLink.TabStop = true;
			this._readMoreLink.Tag = "sendreceive";
			this._readMoreLink.Text = "Read More";
			this._readMoreLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._readMoreLabel_Click);
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
			this.tableLayoutPanel2.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);

		}

#endregion

		private System.Windows.Forms.Timer _debounceListIndexChangedEvent;
		private System.Windows.Forms.ToolTip toolTip1;
		private System.Windows.Forms.TableLayoutPanel tableLayoutPanel2;
		private System.Windows.Forms.Button button6;
		private System.Windows.Forms.Button button7;
		private System.Windows.Forms.Button button8;
		private System.Windows.Forms.Button button9;
		private System.Windows.Forms.Button _templateButton;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Button _browseButton;
		private System.Windows.Forms.LinkLabel _readMoreLink;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;

	}
}