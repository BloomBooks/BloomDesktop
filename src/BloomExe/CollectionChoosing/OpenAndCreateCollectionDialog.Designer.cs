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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(OpenAndCreateCollectionDialog));
            this._openAndCreateControl = new Bloom.CollectionChoosing.OpenCreateCloneControl();
            this.SuspendLayout();
            // 
            // _openAndCreateControl
            // 
            this._openAndCreateControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._openAndCreateControl.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this._openAndCreateControl.BackColor = System.Drawing.Color.White;
            this._openAndCreateControl.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._openAndCreateControl.Location = new System.Drawing.Point(12, 12);
            this._openAndCreateControl.Name = "_openAndCreateControl";
            this._openAndCreateControl.Size = new System.Drawing.Size(796, 348);
            this._openAndCreateControl.TabIndex = 0;
            // 
            // OpenAndCreateCollectionDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(820, 383);
            this.Controls.Add(this._openAndCreateControl);
#if !__MonoCS__
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
#endif
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OpenAndCreateCollectionDialog";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Open/Create Collections";
            this.ResumeLayout(false);

		}

		#endregion

        private OpenCreateCloneControl _openAndCreateControl;

	}
}