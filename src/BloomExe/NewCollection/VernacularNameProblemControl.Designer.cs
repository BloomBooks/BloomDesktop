namespace Bloom.NewCollection
{
	partial class VernacularNameProblemControl
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
			this._textLibraryName = new System.Windows.Forms.TextBox();
			this._nameCollectionLabel = new System.Windows.Forms.Label();
			this._pathLabel = new Palaso.UI.WindowsForms.Widgets.BetterLabel();
			this.SuspendLayout();
			// 
			// _textLibraryName
			// 
			this._textLibraryName.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._textLibraryName.Location = new System.Drawing.Point(3, 29);
			this._textLibraryName.Name = "_textLibraryName";
			this._textLibraryName.Size = new System.Drawing.Size(229, 23);
			this._textLibraryName.TabIndex = 10;
			this._textLibraryName.TextChanged += new System.EventHandler(this._textLibraryName_TextChanged);
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
			// _pathLabel
			// 
			this._pathLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._pathLabel.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this._pathLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
			this._pathLabel.Location = new System.Drawing.Point(3, 75);
			this._pathLabel.Multiline = true;
			this._pathLabel.Name = "_pathLabel";
			this._pathLabel.ReadOnly = true;
			this._pathLabel.Size = new System.Drawing.Size(311, 75);
			this._pathLabel.TabIndex = 12;
			this._pathLabel.TabStop = false;
			this._pathLabel.Text = "prompt";
			// 
			// VernacularNameProblemControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this._pathLabel);
			this.Controls.Add(this._textLibraryName);
			this.Controls.Add(this._nameCollectionLabel);
			this.Name = "VernacularNameProblemControl";
			this.Size = new System.Drawing.Size(329, 352);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		protected System.Windows.Forms.TextBox _textLibraryName;
		protected System.Windows.Forms.Label _nameCollectionLabel;
		private Palaso.UI.WindowsForms.Widgets.BetterLabel _pathLabel;
	}
}
