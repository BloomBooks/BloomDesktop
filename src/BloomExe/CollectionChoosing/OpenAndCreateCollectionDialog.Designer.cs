namespace Bloom.CollectionChoosing
{
	partial class OpenAndCreateCollectionDialog
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
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(OpenAndCreateCollectionDialog));
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._openAndCreateControl = new Bloom.CollectionChoosing.OpenCreateCloneControl();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "OpenCreateNewCollectionsDialog";
			// 
			// _openAndCreateControl
			// 
			this._openAndCreateControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._openAndCreateControl.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this._openAndCreateControl.BackColor = System.Drawing.Color.White;
            this._openAndCreateControl.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._L10NSharpExtender.SetLocalizableToolTip(this._openAndCreateControl, null);
            this._L10NSharpExtender.SetLocalizationComment(this._openAndCreateControl, null);
            this._L10NSharpExtender.SetLocalizingId(this._openAndCreateControl, "OpenCreateNewCollectionsDialog.OpenAndCreateCollectionDialog.OpenCreateCloneContr" +
        "ol");
            this._openAndCreateControl.Location = new System.Drawing.Point(12, 12);
            this._openAndCreateControl.Name = "_openAndCreateControl";
            this._openAndCreateControl.Size = new System.Drawing.Size(796, 348);
            this._openAndCreateControl.TabIndex = 0;
            // 
            // OpenAndCreateCollectionDialog
            // 
			this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(820, 383);
            this.Controls.Add(this._openAndCreateControl);
            this._L10NSharpExtender.SetLocalizableToolTip(this, null);
            this._L10NSharpExtender.SetLocalizationComment(this, null);
            this._L10NSharpExtender.SetLocalizingId(this, "OpenCreateNewCollectionsDialog.OpenAndCreateWindowTitle");
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OpenAndCreateCollectionDialog";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Open/Create Collections";
			this.DragDrop += new System.Windows.Forms.DragEventHandler(this.OpenAndCreateCollectionDialog_DragDrop);
			this.DragEnter += new System.Windows.Forms.DragEventHandler(this.OpenAndCreateCollectionDialog_DragEnter);
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
            this.ResumeLayout(false);

		}

#endregion

        private OpenCreateCloneControl _openAndCreateControl;
        private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;

	}
}
