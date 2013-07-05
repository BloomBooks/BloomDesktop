namespace Bloom.CollectionTab
{
	partial class InDesignXmlInformationDialog
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(InDesignXmlInformationDialog));
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this.button1 = new System.Windows.Forms.Button();
			this.htmlLabel1 = new Bloom.HtmlLabel();
			this.dontShowThisAgainButton1 = new Bloom.MiscUI.DontShowThisAgainButton();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "InDesignXmlInformationDialog";
			// 
			// button1
			// 
			this.button1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._L10NSharpExtender.SetLocalizableToolTip(this.button1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.button1, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.button1, L10NSharp.LocalizationPriority.High);
			this._L10NSharpExtender.SetLocalizingId(this.button1, "Common.OKButton");
			this.button1.Location = new System.Drawing.Point(268, 201);
			this.button1.Name = "button1";
			this.button1.Size = new System.Drawing.Size(75, 23);
			this.button1.TabIndex = 2;
			this.button1.Text = "&OK";
			this.button1.UseVisualStyleBackColor = true;
			this.button1.Click += new System.EventHandler(this.button1_Click);
			// 
			// htmlLabel1
			// 
			this.htmlLabel1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.htmlLabel1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.htmlLabel1.BackColor = System.Drawing.Color.Transparent;
			this.htmlLabel1.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.htmlLabel1.HTML = resources.GetString("htmlLabel1.HTML");
			this._L10NSharpExtender.SetLocalizableToolTip(this.htmlLabel1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.htmlLabel1, null);
			this._L10NSharpExtender.SetLocalizingId(this.htmlLabel1, resources.GetString("htmlLabel1.LocalizingId"));
			this.htmlLabel1.Location = new System.Drawing.Point(28, 22);
			this.htmlLabel1.Margin = new System.Windows.Forms.Padding(0);
			this.htmlLabel1.Name = "htmlLabel1";
			this.htmlLabel1.Size = new System.Drawing.Size(315, 157);
			this.htmlLabel1.TabIndex = 1;
			// 
			// dontShowThisAgainButton1
			// 
			this.dontShowThisAgainButton1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.dontShowThisAgainButton1.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this.dontShowThisAgainButton1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.dontShowThisAgainButton1, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.dontShowThisAgainButton1, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this.dontShowThisAgainButton1, "InDesignXmlInformationDialog.dontShowThisAgainButton");
			this.dontShowThisAgainButton1.Location = new System.Drawing.Point(28, 207);
			this.dontShowThisAgainButton1.Name = "dontShowThisAgainButton1";
			this.dontShowThisAgainButton1.Size = new System.Drawing.Size(134, 17);
			this.dontShowThisAgainButton1.TabIndex = 0;
			this.dontShowThisAgainButton1.Text = "Don\'t Show This Again";
			this.dontShowThisAgainButton1.UseVisualStyleBackColor = true;
			// 
			// InDesignXmlInformationDialog
			// 
			this.AcceptButton = this.button1;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(376, 246);
			this.ControlBox = false;
			this.Controls.Add(this.button1);
			this.Controls.Add(this.htmlLabel1);
			this.Controls.Add(this.dontShowThisAgainButton1);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "InDesignXmlInformationDialog.WindowTitle");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "InDesignXmlInformationDialog";
			this.Text = "InDesign XML Information";
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private MiscUI.DontShowThisAgainButton dontShowThisAgainButton1;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private HtmlLabel htmlLabel1;
		private System.Windows.Forms.Button button1;
	}
}