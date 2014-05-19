#if !__MonoCS__
namespace Bloom.Publish
{
	partial class AdobeReaderControl
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
			this._problemLabel = new Palaso.UI.WindowsForms.Widgets.BetterLabel();
			this._problemPicture = new System.Windows.Forms.PictureBox();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			((System.ComponentModel.ISupportInitialize)(this._problemPicture)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// _problemLabel
			// 
			this._problemLabel.BackColor = System.Drawing.Color.White;
			this._problemLabel.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this._problemLabel.Enabled = false;
			this._problemLabel.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._problemLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._problemLabel, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._problemLabel, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._problemLabel, "PublishTab.AdobeReaderProblemControl._adobeProblemLabel");
			this._problemLabel.Location = new System.Drawing.Point(13, 150);
			this._problemLabel.Multiline = true;
			this._problemLabel.Name = "_problemLabel";
			this._problemLabel.ReadOnly = true;
			this._problemLabel.Size = new System.Drawing.Size(480, 240);
			this._problemLabel.TabIndex = 18;
			this._problemLabel.TabStop = false;
			this._problemLabel.Text = "<set at runtime>";
			this._problemLabel.Visible = false;
			// 
			// _problemPicture
			// 
			this._problemPicture.Image = global::Bloom.Properties.Resources.sad_large;
			this._L10NSharpExtender.SetLocalizableToolTip(this._problemPicture, null);
			this._L10NSharpExtender.SetLocalizationComment(this._problemPicture, null);
			this._L10NSharpExtender.SetLocalizingId(this._problemPicture, "AdobeReaderControl._adobeProblemPicture");
			this._problemPicture.Location = new System.Drawing.Point(189, 3);
			this._problemPicture.Name = "_problemPicture";
			this._problemPicture.Size = new System.Drawing.Size(128, 132);
			this._problemPicture.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
			this._problemPicture.TabIndex = 17;
			this._problemPicture.TabStop = false;
			this._problemPicture.Visible = false;
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			// 
			// AdobeReaderControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.White;
			this.Controls.Add(this._problemLabel);
			this.Controls.Add(this._problemPicture);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "AdobeReaderProblemControl.AdobeReaderProblemControl");
			this.Name = "AdobeReaderControl";
			this.Size = new System.Drawing.Size(509, 264);
			((System.ComponentModel.ISupportInitialize)(this._problemPicture)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private Palaso.UI.WindowsForms.Widgets.BetterLabel _problemLabel;
		private System.Windows.Forms.PictureBox _problemPicture;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
	}
}
#endif
