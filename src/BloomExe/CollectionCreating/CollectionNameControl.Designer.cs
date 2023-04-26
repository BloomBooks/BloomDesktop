namespace Bloom.CollectionCreating
{
	partial class CollectionNameControl
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
            this._collectionNameControl = new System.Windows.Forms.TextBox();
            this._exampleText = new SIL.Windows.Forms.Widgets.BetterLabel();
            this._collectionInfoLabel = new SIL.Windows.Forms.Widgets.BetterLabel();
            this._nameCollectionLabel = new System.Windows.Forms.Label();
            this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
            this.SuspendLayout();
            // 
            // _collectionNameControl
            // 
            this._collectionNameControl.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._collectionNameControl.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._L10NSharpExtender.SetLocalizableToolTip(this._collectionNameControl, null);
            this._L10NSharpExtender.SetLocalizationComment(this._collectionNameControl, null);
            this._L10NSharpExtender.SetLocalizationPriority(this._collectionNameControl, L10NSharp.LocalizationPriority.NotLocalizable);
            this._L10NSharpExtender.SetLocalizingId(this._collectionNameControl, "NewCollectionWizard.CollectionNamePage.CollectionName");
            this._collectionNameControl.Location = new System.Drawing.Point(3, 29);
            this._collectionNameControl.Name = "_collectionNameControl";
            this._collectionNameControl.Size = new System.Drawing.Size(229, 29);
            this._collectionNameControl.TabIndex = 10;
            this._collectionNameControl.TextChanged += new System.EventHandler(this._textCollectionName_TextChanged);
            // 
            // _exampleText
            // 
            this._exampleText.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._exampleText.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this._exampleText.Enabled = false;
            this._exampleText.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._exampleText.ForeColor = System.Drawing.Color.DimGray;
            this._exampleText.IsTextSelectable = false;
            this._L10NSharpExtender.SetLocalizableToolTip(this._exampleText, null);
            this._L10NSharpExtender.SetLocalizationComment(this._exampleText, null);
            this._L10NSharpExtender.SetLocalizingId(this._exampleText, "NewCollectionWizard.CollectionNamePage.ExampleText");
            this._exampleText.Location = new System.Drawing.Point(3, 64);
            this._exampleText.Multiline = true;
            this._exampleText.Name = "_exampleText";
            this._exampleText.ReadOnly = true;
            this._exampleText.Size = new System.Drawing.Size(311, 15);
            this._exampleText.TabIndex = 13;
            this._exampleText.TabStop = false;
            this._exampleText.Text = "Examples: \"Health Books\", \"PNG Animal Stories\"";
            // 
            // _collectionInfoLabel
            // 
            this._collectionInfoLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._collectionInfoLabel.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this._collectionInfoLabel.Enabled = false;
            this._collectionInfoLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._collectionInfoLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            this._collectionInfoLabel.IsTextSelectable = false;
            this._L10NSharpExtender.SetLocalizableToolTip(this._collectionInfoLabel, null);
            this._L10NSharpExtender.SetLocalizationComment(this._collectionInfoLabel, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._collectionNameControl, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._collectionInfoLabel, "NewCollectionWizard.CollectionNamePage.CollectionNameControl.HtmlLabel");
            this._collectionInfoLabel.Location = new System.Drawing.Point(3, 108);
            this._collectionInfoLabel.Margin = new System.Windows.Forms.Padding(0);
            this._collectionInfoLabel.Multiline = true;
            this._collectionInfoLabel.Name = "_collectionInfoLabel";
            this._collectionInfoLabel.ReadOnly = true;
            this._collectionInfoLabel.Size = new System.Drawing.Size(321, 0);
            this._collectionInfoLabel.TabIndex = 14;
            this._collectionInfoLabel.TabStop = false;
            // 
            // _nameCollectionLabel
            // 
            this._nameCollectionLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._nameCollectionLabel.Enabled = false;
            this._nameCollectionLabel.Font = new System.Drawing.Font("Segoe UI", 12F);
            this._L10NSharpExtender.SetLocalizableToolTip(this._nameCollectionLabel, null);
            this._L10NSharpExtender.SetLocalizationComment(this._nameCollectionLabel, null);
            this._L10NSharpExtender.SetLocalizingId(this._nameCollectionLabel, "NewCollectionWizard.CollectionNamePage.NameCollectionLabel");
            this._nameCollectionLabel.Location = new System.Drawing.Point(3, 0);
            this._nameCollectionLabel.Name = "_nameCollectionLabel";
            this._nameCollectionLabel.Size = new System.Drawing.Size(321, 23);
            this._nameCollectionLabel.TabIndex = 15;
            this._nameCollectionLabel.Text = "What would you like to call this collection?";
            // 
            // _L10NSharpExtender
            // 
            this._L10NSharpExtender.LocalizationManagerId = "Bloom";
            this._L10NSharpExtender.PrefixForNewItems = "NewCollectionWizard.CollectionNamePage";
            // 
            // CollectionNameControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this._nameCollectionLabel);
            this.Controls.Add(this._collectionInfoLabel);
            this.Controls.Add(this._exampleText);
            this.Controls.Add(this._collectionNameControl);
            this._L10NSharpExtender.SetLocalizableToolTip(this, null);
            this._L10NSharpExtender.SetLocalizationComment(this, null);
            this._L10NSharpExtender.SetLocalizingId(this, "NewCollectionWizard.CollectionNamePage.CollectionNameControl.CollectionNameContro" +
        "l");
            this.Name = "CollectionNameControl";
            this.Size = new System.Drawing.Size(329, 352);
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion

		protected System.Windows.Forms.TextBox _collectionNameControl;
		private SIL.Windows.Forms.Widgets.BetterLabel _exampleText;
		private SIL.Windows.Forms.Widgets.BetterLabel _collectionInfoLabel;
		private System.Windows.Forms.Label _nameCollectionLabel;
        private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
	}
}
