namespace Bloom.NewCollection
{
	partial class ProjectStorageControl
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
			this._collectionNameControl = new System.Windows.Forms.TextBox();
			this._nameCollectionLabel = new System.Windows.Forms.Label();
			this.betterLabel1 = new Palaso.UI.WindowsForms.Widgets.BetterLabel();
			this.htmlLabel1 = new Bloom.HtmlLabel();
			this.SuspendLayout();
			// 
			// _textLibraryName
			// 
			this._collectionNameControl.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._collectionNameControl.Location = new System.Drawing.Point(3, 29);
			this._collectionNameControl.Name = "_collectionNameControl";
			this._collectionNameControl.Size = new System.Drawing.Size(229, 23);
			this._collectionNameControl.TabIndex = 10;
			this._collectionNameControl.TextChanged += new System.EventHandler(this._textLibraryName_TextChanged);
			// 
			// _nameCollectionLabel
			// 
			this._nameCollectionLabel.AutoSize = true;
			this._nameCollectionLabel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._nameCollectionLabel.Location = new System.Drawing.Point(0, 5);
			this._nameCollectionLabel.Name = "_nameCollectionLabel";
			this._nameCollectionLabel.Size = new System.Drawing.Size(232, 15);
			this._nameCollectionLabel.TabIndex = 11;
			this._nameCollectionLabel.Text = "What would you like to call this collection?";
			// 
			// betterLabel1
			// 
			this.betterLabel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.betterLabel1.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this.betterLabel1.Font = new System.Drawing.Font("Segoe UI", 9F);
			this.betterLabel1.ForeColor = System.Drawing.Color.DimGray;
			this.betterLabel1.Location = new System.Drawing.Point(3, 58);
			this.betterLabel1.Multiline = true;
			this.betterLabel1.Name = "betterLabel1";
			this.betterLabel1.ReadOnly = true;
			this.betterLabel1.Size = new System.Drawing.Size(311, 41);
			this.betterLabel1.TabIndex = 13;
			this.betterLabel1.TabStop = false;
			this.betterLabel1.Text = "Examples: \"Health Books\", \"PNG Animal Stories\"";
			// 
			// htmlLabel1
			// 
			this.htmlLabel1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.htmlLabel1.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.htmlLabel1.HTML = null;
			this.htmlLabel1.Location = new System.Drawing.Point(-7, 97);
			this.htmlLabel1.Margin = new System.Windows.Forms.Padding(0);
			this.htmlLabel1.Name = "htmlLabel1";
			this.htmlLabel1.Size = new System.Drawing.Size(331, 168);
			this.htmlLabel1.TabIndex = 14;
			// 
			// ProjectStorageControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.htmlLabel1);
			this.Controls.Add(this.betterLabel1);
			this.Controls.Add(this._collectionNameControl);
			this.Controls.Add(this._nameCollectionLabel);
			this.Name = "ProjectStorageControl";
			this.Size = new System.Drawing.Size(329, 352);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		protected System.Windows.Forms.TextBox _collectionNameControl;
		protected System.Windows.Forms.Label _nameCollectionLabel;
		private Palaso.UI.WindowsForms.Widgets.BetterLabel betterLabel1;
		private HtmlLabel htmlLabel1;
	}
}
