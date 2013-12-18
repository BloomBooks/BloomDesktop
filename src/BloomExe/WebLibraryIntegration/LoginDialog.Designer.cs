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
			this.label1 = new System.Windows.Forms.Label();
			this._emailBox = new System.Windows.Forms.TextBox();
			this.label2 = new System.Windows.Forms.Label();
			this._passwordBox = new System.Windows.Forms.TextBox();
			this._loginButton = new System.Windows.Forms.Button();
			this.SuspendLayout();
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(29, 9);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(73, 13);
			this.label1.TabIndex = 0;
			this.label1.Text = "Email Address";
			// 
			// _emailBox
			// 
			this._emailBox.Location = new System.Drawing.Point(32, 25);
			this._emailBox.Name = "_emailBox";
			this._emailBox.Size = new System.Drawing.Size(233, 20);
			this._emailBox.TabIndex = 1;
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(29, 74);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(53, 13);
			this.label2.TabIndex = 2;
			this.label2.Text = "Password";
			// 
			// _passwordBox
			// 
			this._passwordBox.Location = new System.Drawing.Point(32, 90);
			this._passwordBox.Name = "_passwordBox";
			this._passwordBox.Size = new System.Drawing.Size(233, 20);
			this._passwordBox.TabIndex = 3;
			// 
			// _loginButton
			// 
			this._loginButton.Location = new System.Drawing.Point(37, 134);
			this._loginButton.Name = "_loginButton";
			this._loginButton.Size = new System.Drawing.Size(75, 23);
			this._loginButton.TabIndex = 4;
			this._loginButton.Text = "Login";
			this._loginButton.UseVisualStyleBackColor = true;
			this._loginButton.Click += new System.EventHandler(this.Login);
			// 
			// LoginDialog
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(284, 262);
			this.Controls.Add(this._loginButton);
			this.Controls.Add(this._passwordBox);
			this.Controls.Add(this.label2);
			this.Controls.Add(this._emailBox);
			this.Controls.Add(this.label1);
			this.Name = "LoginDialog";
			this.Text = "Login to Bloom Web service";
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.TextBox _emailBox;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.TextBox _passwordBox;
		private System.Windows.Forms.Button _loginButton;
	}
}