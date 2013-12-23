namespace Bloom.WebLibraryIntegration
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
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label1, null);
			this._L10NSharpExtender.SetLocalizingId(this.label1, "LoginDialog.Email");
			this.label1.Location = new System.Drawing.Point(29, 9);
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
			this._emailBox.Location = new System.Drawing.Point(32, 25);
			this._emailBox.Name = "_emailBox";
			this._emailBox.Size = new System.Drawing.Size(233, 20);
			this._emailBox.TabIndex = 1;
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label2, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label2, null);
			this._L10NSharpExtender.SetLocalizingId(this.label2, "LoginDialog.Password");
			this.label2.Location = new System.Drawing.Point(29, 74);
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
			this._passwordBox.Location = new System.Drawing.Point(32, 90);
			this._passwordBox.Name = "_passwordBox";
			this._passwordBox.PasswordChar = '*';
			this._passwordBox.Size = new System.Drawing.Size(233, 20);
			this._passwordBox.TabIndex = 3;
			// 
			// _loginButton
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._loginButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._loginButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._loginButton, "LoginDialog.LoginButton");
			this._loginButton.Location = new System.Drawing.Point(32, 133);
			this._loginButton.Name = "_loginButton";
			this._loginButton.Size = new System.Drawing.Size(75, 23);
			this._loginButton.TabIndex = 4;
			this._loginButton.Text = "Login";
			this._loginButton.UseVisualStyleBackColor = true;
			this._loginButton.Click += new System.EventHandler(this.Login);
			// 
			// _cancelButton
			// 
			this._cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this._L10NSharpExtender.SetLocalizableToolTip(this._cancelButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._cancelButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._cancelButton, "Common.CancelButton");
			this._cancelButton.Location = new System.Drawing.Point(141, 133);
			this._cancelButton.Name = "_cancelButton";
			this._cancelButton.Size = new System.Drawing.Size(75, 23);
			this._cancelButton.TabIndex = 5;
			this._cancelButton.Text = "Cancel";
			this._cancelButton.UseVisualStyleBackColor = true;
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "LoginDialog";
			// 
			// LoginDialog
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(284, 183);
			this.Controls.Add(this._cancelButton);
			this.Controls.Add(this._loginButton);
			this.Controls.Add(this._passwordBox);
			this.Controls.Add(this.label2);
			this.Controls.Add(this._emailBox);
			this.Controls.Add(this.label1);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "LoginDialog.WindowTitle");
			this.Name = "LoginDialog";
			this.Text = "Login to Bloom Web service";
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
	}
}