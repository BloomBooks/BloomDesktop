namespace Bloom.Publish
{
    partial class PleaseWait
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
			this._creatingThePdfDocumentLabel = new System.Windows.Forms.Label();
			this.localizationExtender1 = new Localization.UI.LocalizationExtender(this.components);
			((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).BeginInit();
			this.SuspendLayout();
			// 
			// _creatingThePdfDocumentLabel
			// 
			this._creatingThePdfDocumentLabel.AutoSize = true;
			this._creatingThePdfDocumentLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.localizationExtender1.SetLocalizableToolTip(this._creatingThePdfDocumentLabel, null);
			this.localizationExtender1.SetLocalizationComment(this._creatingThePdfDocumentLabel, null);
			this.localizationExtender1.SetLocalizingId(this._creatingThePdfDocumentLabel, "PleaseWait.label1");
			this._creatingThePdfDocumentLabel.Location = new System.Drawing.Point(34, 22);
			this._creatingThePdfDocumentLabel.Name = "_creatingThePdfDocumentLabel";
			this._creatingThePdfDocumentLabel.Size = new System.Drawing.Size(219, 20);
			this._creatingThePdfDocumentLabel.TabIndex = 0;
			this._creatingThePdfDocumentLabel.Text = "Creating the PDF document...";
			this._creatingThePdfDocumentLabel.UseWaitCursor = true;
			// 
			// localizationExtender1
			// 
			this.localizationExtender1.LocalizationManagerId = "Bloom";
			// 
			// PleaseWait
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(290, 75);
			this.ControlBox = false;
			this.Controls.Add(this._creatingThePdfDocumentLabel);
			this.localizationExtender1.SetLocalizableToolTip(this, null);
			this.localizationExtender1.SetLocalizationComment(this, null);
			this.localizationExtender1.SetLocalizingId(this, "PleaseWait.WindowTitle");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "PleaseWait";
			this.ShowIcon = false;
			this.ShowInTaskbar = false;
			this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Please Wait...";
			this.UseWaitCursor = true;
			((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label _creatingThePdfDocumentLabel;
		private Localization.UI.LocalizationExtender localizationExtender1;
    }
}