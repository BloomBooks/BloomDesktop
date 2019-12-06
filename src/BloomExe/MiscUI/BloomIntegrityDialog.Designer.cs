namespace Bloom.MiscUI
{
	partial class BloomIntegrityDialog
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
			if(disposing && (components != null))
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(BloomIntegrityDialog));
			this._reportButton = new System.Windows.Forms.Button();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this.htmlTextBox1 = new Bloom.MiscUI.HtmlTextBox();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// _reportButton
			// 
			this._reportButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._reportButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._reportButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._reportButton, "BloomIntegrityDialog.ReportProblem");
			this._reportButton.Location = new System.Drawing.Point(371, 539);
			this._reportButton.Name = "_reportButton";
			this._reportButton.Size = new System.Drawing.Size(135, 23);
			this._reportButton.TabIndex = 0;
			this._reportButton.Text = "Close";
			this._reportButton.UseVisualStyleBackColor = true;
			this._reportButton.Click += new System.EventHandler(this._reportButton_Click);
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "BloomIntegrityDialog";
			// 
			// markDownTextBox1
			// 
			this.htmlTextBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._L10NSharpExtender.SetLocalizableToolTip(this.htmlTextBox1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.htmlTextBox1, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.htmlTextBox1, L10NSharp.LocalizationPriority.NotLocalizable);
			this.htmlTextBox1.Location = new System.Drawing.Point(12, 12);
			this.htmlTextBox1.HtmlText = "Need to set the property \"MarkDownText\"";
			this.htmlTextBox1.Name = "markDownTextBox1";
			this.htmlTextBox1.Size = new System.Drawing.Size(494, 511);
			this.htmlTextBox1.TabIndex = 1;
			// 
			// BloomIntegrityDialog
			// 
			this.AcceptButton = this._reportButton;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(518, 574);
			this.Controls.Add(this.htmlTextBox1);
			this.Controls.Add(this._reportButton);
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "BloomIntegrity.WindowTitle");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "BloomIntegrityDialog";
			this.Text = "Bloom Problem";
			this.TopMost = true;
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.Button _reportButton;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private MiscUI.HtmlTextBox htmlTextBox1;
	}
}