namespace Bloom.CollectionTab
{
    partial class BloomLibraryLinkVerification
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
            this._continueToLinkButton = new System.Windows.Forms.Button();
            this._cancelButton = new System.Windows.Forms.Button();
            this._message = new System.Windows.Forms.Label();
            this._infoIcon = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)(this._infoIcon)).BeginInit();
            this.SuspendLayout();
            // 
            // _continueToLinkButton
            // 
            this._continueToLinkButton.AccessibleName = "continueToLinkButton";
            this._continueToLinkButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this._continueToLinkButton.Location = new System.Drawing.Point(376, 196);
            this._continueToLinkButton.Margin = new System.Windows.Forms.Padding(5, 4, 5, 4);
            this._continueToLinkButton.Name = "_continueToLinkButton";
            this._continueToLinkButton.Size = new System.Drawing.Size(235, 55);
            this._continueToLinkButton.TabIndex = 1;
            this._continueToLinkButton.Text = "Go to BloomLibrary.org";
            this._continueToLinkButton.UseVisualStyleBackColor = true;
            // 
            // _cancelButton
            // 
            this._cancelButton.AccessibleName = "cancelButton";
            this._cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this._cancelButton.Location = new System.Drawing.Point(636, 196);
            this._cancelButton.Margin = new System.Windows.Forms.Padding(5, 4, 5, 4);
            this._cancelButton.Name = "_cancelButton";
            this._cancelButton.Size = new System.Drawing.Size(120, 55);
            this._cancelButton.TabIndex = 0;
            this._cancelButton.Text = "&Cancel";
            this._cancelButton.UseVisualStyleBackColor = true;
            // 
            // _message
            // 
            this._message.Location = new System.Drawing.Point(104, 48);
            this._message.Name = "_message";
            this._message.Size = new System.Drawing.Size(652, 116);
            this._message.TabIndex = 2;
            this._message.Text = "Some localizable message here.";
            // 
            // _infoIcon
            // 
            this._infoIcon.Location = new System.Drawing.Point(33, 33);
            this._infoIcon.Name = "_infoIcon";
            this._infoIcon.Size = new System.Drawing.Size(74, 73);
            this._infoIcon.TabIndex = 3;
            this._infoIcon.TabStop = false;
            // 
            // BloomLibraryLinkVerification
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this._cancelButton;
            this.ClientSize = new System.Drawing.Size(775, 270);
            this.Controls.Add(this._infoIcon);
            this.Controls.Add(this._message);
            this.Controls.Add(this._cancelButton);
            this.Controls.Add(this._continueToLinkButton);
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Margin = new System.Windows.Forms.Padding(5, 4, 5, 4);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "BloomLibraryLinkVerification";
            this.ShowIcon = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Source Collection";
            this.TopMost = true;
            ((System.ComponentModel.ISupportInitialize)(this._infoIcon)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button _continueToLinkButton;
        private System.Windows.Forms.Button _cancelButton;
        private System.Windows.Forms.Label _message;
        private System.Windows.Forms.PictureBox _infoIcon;
    }
}