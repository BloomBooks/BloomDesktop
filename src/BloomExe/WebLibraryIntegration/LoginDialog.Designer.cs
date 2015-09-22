﻿namespace Bloom.WebLibraryIntegration
{
	partial class LoginDialog
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
			this.label1 = new System.Windows.Forms.Label();
			this._emailBox = new System.Windows.Forms.TextBox();
			this.label2 = new System.Windows.Forms.Label();
			this._passwordBox = new System.Windows.Forms.TextBox();
			this._loginButton = new System.Windows.Forms.Button();
			this._cancelButton = new System.Windows.Forms.Button();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._forgotLabel = new System.Windows.Forms.LinkLabel();
			this._borderLabel = new System.Windows.Forms.Label();
			this._showPasswordCheckBox = new System.Windows.Forms.CheckBox();
			this._termsOfUseCheckBox = new System.Windows.Forms.CheckBox();
			this._showTermsOfUse = new System.Windows.Forms.LinkLabel();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label1, null);
			this._L10NSharpExtender.SetLocalizingId(this.label1, "LoginDialog.Email");
			this.label1.Location = new System.Drawing.Point(15, 15);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(73, 13);
			this.label1.TabIndex = 0;
			this.label1.Text = "Email Address";
			// 
			// _emailBox
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._emailBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this._emailBox, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._emailBox, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._emailBox, "LoginDialog.LoginDialog._emailBox");
			this._emailBox.Location = new System.Drawing.Point(18, 31);
			this._emailBox.Name = "_emailBox";
			this._emailBox.Size = new System.Drawing.Size(247, 20);
			this._emailBox.TabIndex = 1;
			this._emailBox.TextChanged += new System.EventHandler(this._emailBox_TextChanged);
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label2, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label2, null);
			this._L10NSharpExtender.SetLocalizingId(this.label2, "LoginDialog.Password");
			this.label2.Location = new System.Drawing.Point(15, 58);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(53, 13);
			this.label2.TabIndex = 2;
			this.label2.Text = "Password";
			// 
			// _passwordBox
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._passwordBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this._passwordBox, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._passwordBox, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._passwordBox, "LoginDialog.LoginDialog._passwordBox");
			this._passwordBox.Location = new System.Drawing.Point(18, 74);
			this._passwordBox.Name = "_passwordBox";
			this._passwordBox.Size = new System.Drawing.Size(247, 20);
			this._passwordBox.TabIndex = 3;
			this._passwordBox.TextChanged += new System.EventHandler(this._passwordBox_TextChanged);
			// 
			// _loginButton
			// 
			this._loginButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._loginButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._loginButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._loginButton, "LoginDialog.LoginButton");
			this._loginButton.Location = new System.Drawing.Point(109, 148);
			this._loginButton.Name = "_loginButton";
			this._loginButton.Size = new System.Drawing.Size(75, 23);
			this._loginButton.TabIndex = 6;
			this._loginButton.Text = "&Login";
			this._loginButton.UseVisualStyleBackColor = true;
			this._loginButton.Click += new System.EventHandler(this.Login);
			// 
			// _cancelButton
			// 
			this._cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this._L10NSharpExtender.SetLocalizableToolTip(this._cancelButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._cancelButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._cancelButton, "Common.CancelButton");
			this._cancelButton.Location = new System.Drawing.Point(190, 148);
			this._cancelButton.Name = "_cancelButton";
			this._cancelButton.Size = new System.Drawing.Size(75, 23);
			this._cancelButton.TabIndex = 7;
			this._cancelButton.Text = "&Cancel";
			this._cancelButton.UseVisualStyleBackColor = true;
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "LoginDialog";
			// 
			// _forgotLabel
			// 
			this._forgotLabel.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._forgotLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._forgotLabel, null);
			this._L10NSharpExtender.SetLocalizingId(this._forgotLabel, "LoginDialog.ForgotPassword");
			this._forgotLabel.Location = new System.Drawing.Point(180, 106);
			this._forgotLabel.Name = "_forgotLabel";
			this._forgotLabel.Size = new System.Drawing.Size(86, 13);
			this._forgotLabel.TabIndex = 5;
			this._forgotLabel.TabStop = true;
			this._forgotLabel.Text = "Forgot Password";
			this._forgotLabel.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._forgotLabel_LinkClicked);
			// 
			// _borderLabel
			// 
			this._borderLabel.BackColor = System.Drawing.Color.Red;
			this._L10NSharpExtender.SetLocalizableToolTip(this._borderLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._borderLabel, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._borderLabel, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._borderLabel, "LoginDialog.label3");
			this._borderLabel.Location = new System.Drawing.Point(17, 30);
			this._borderLabel.Name = "_borderLabel";
			this._borderLabel.Size = new System.Drawing.Size(249, 22);
			this._borderLabel.TabIndex = 7;
			// 
			// _showPasswordCheckBox
			// 
			this._showPasswordCheckBox.AutoSize = true;
			this._showPasswordCheckBox.Checked = true;
			this._showPasswordCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
			this._L10NSharpExtender.SetLocalizableToolTip(this._showPasswordCheckBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this._showPasswordCheckBox, null);
			this._L10NSharpExtender.SetLocalizingId(this._showPasswordCheckBox, "LoginDialog.showPassword");
			this._showPasswordCheckBox.Location = new System.Drawing.Point(20, 105);
			this._showPasswordCheckBox.Name = "_showPasswordCheckBox";
			this._showPasswordCheckBox.Size = new System.Drawing.Size(102, 17);
			this._showPasswordCheckBox.TabIndex = 4;
			this._showPasswordCheckBox.Text = "&Show Password";
			this._showPasswordCheckBox.UseVisualStyleBackColor = true;
			this._showPasswordCheckBox.CheckedChanged += new System.EventHandler(this._showPasswordCheckBox_CheckedChanged);
			// 
			// _termsOfUseCheckBox
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._termsOfUseCheckBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this._termsOfUseCheckBox, null);
			this._L10NSharpExtender.SetLocalizingId(this._termsOfUseCheckBox, "LoginDialog.AgreeToTerms");
			this._termsOfUseCheckBox.Location = new System.Drawing.Point(22, 185);
			this._termsOfUseCheckBox.Name = "_termsOfUseCheckBox";
			this._termsOfUseCheckBox.Size = new System.Drawing.Size(244, 23);
			this._termsOfUseCheckBox.TabIndex = 9;
			this._termsOfUseCheckBox.Text = "I agree to the Bloom Library\'s Terms of Use";
			// 
			// _showTermsOfUse
			// 
			this._showTermsOfUse.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._showTermsOfUse, null);
			this._L10NSharpExtender.SetLocalizationComment(this._showTermsOfUse, null);
			this._L10NSharpExtender.SetLocalizingId(this._showTermsOfUse, "LoginDialog.ShowTerms");
			this._showTermsOfUse.Location = new System.Drawing.Point(165, 208);
			this._showTermsOfUse.Name = "_showTermsOfUse";
			this._showTermsOfUse.Size = new System.Drawing.Size(100, 13);
			this._showTermsOfUse.TabIndex = 10;
			this._showTermsOfUse.TabStop = true;
			this._showTermsOfUse.Text = "Show Terms of Use";
			this._showTermsOfUse.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._showTermsOfUse_LinkClicked);
			// 
			// LoginDialog
			// 
			this.AcceptButton = this._loginButton;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this._cancelButton;
			this.ClientSize = new System.Drawing.Size(284, 236);
			this.Controls.Add(this._showTermsOfUse);
			this.Controls.Add(this._termsOfUseCheckBox);
			this.Controls.Add(this._showPasswordCheckBox);
			this.Controls.Add(this._forgotLabel);
			this.Controls.Add(this._cancelButton);
			this.Controls.Add(this._loginButton);
			this.Controls.Add(this._passwordBox);
			this.Controls.Add(this.label2);
			this.Controls.Add(this._emailBox);
			this.Controls.Add(this.label1);
			this.Controls.Add(this._borderLabel);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "LoginDialog.WindowTitle");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "LoginDialog";
			this.Text = "Log in to BloomLibrary.org";
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.TextBox _emailBox;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.TextBox _passwordBox;
		private System.Windows.Forms.Button _loginButton;
		private System.Windows.Forms.Button _cancelButton;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.LinkLabel _forgotLabel;
		private System.Windows.Forms.Label _borderLabel;
		private System.Windows.Forms.CheckBox _showPasswordCheckBox;
		private System.Windows.Forms.CheckBox _termsOfUseCheckBox;
		private System.Windows.Forms.LinkLabel _showTermsOfUse;
	}
}