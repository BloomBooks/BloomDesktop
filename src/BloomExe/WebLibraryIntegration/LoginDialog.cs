﻿using System;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Bloom.Properties;
using Bloom.Publish.BloomLibrary;
using L10NSharp;
using SIL.Code;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// this class manages a the login process for Bloom, including signing up for new accounts.
	/// (This is a bit clumsy, but makes it easy to switch to login if the user enters a known address,
	/// or switch to signup if the user tries to log in with an unknown email.
	/// It also saves passing another object around.)
	/// Much of this logic unfortunately is or will be duplicated in the web site itself.
	/// We may at some point consider embedding a part of the web site itself in a Gecko window instead for login;
	/// but in the current state of my (JohnT's) knowledge, it was easier to do a plain C# version.
	/// </summary>
	public partial class LoginDialog : Form
	{
		private BloomParseClient _client;
		private int _originalHeight;
		public LoginDialog(BloomParseClient client)
		{
			Require.That(client != null);
			_client = client;
			InitializeComponent();
			_showPasswordCheckBox.Checked = Settings.Default.WebShowPassword;
			oldText = this.Text;
			oldLogin = _loginButton.Text;
			_originalHeight = Height;
			ShowTermsOfUse(false);
		}

		private void Login(object sender, EventArgs e)
		{
			if (!string.IsNullOrEmpty(_emailBox.Text))
			{
				Settings.Default.WebUserId = _emailBox.Text;
				Settings.Default.WebPassword = _passwordBox.Text; // Review: password is saved in clear text. Is this a security risk? How could we prevent it?
				Settings.Default.Save();
			}
			if (_doingSignup)
			{
				DoSignUp();
				return;
			}
			bool logIn;
			try
			{
				logIn = _client.LogIn(_emailBox.Text, _passwordBox.Text);
			}
			catch (Exception)
			{
				MessageBox.Show(this, LocalizationManager.GetString("PublishTab.Upload.Login.LoginConnectFailed", "Bloom could not connect to the server to verify your login. Please check your network connection."),
					LoginFailureString,
					MessageBoxButtons.OK,
					MessageBoxIcon.Error);
				return;
			}
			if (logIn)
			{
				DialogResult = DialogResult.OK;
				Close();
			}
			else
			{
				MessageBox.Show(this, LocalizationManager.GetString("PublishTab.Upload.Login.PasswordMismatch", "Password and user ID did not match"),
					LoginFailureString,
					MessageBoxButtons.OK,
					MessageBoxIcon.Error);
			}
		}


		private static string LoginFailureString
		{
			get
			{
				return LocalizationManager.GetString("PublishTab.Upload.Login.LoginFailed", "Login failed");
			}
		}
		private static string LoginOrSignupConnectionFailedString
		{
			get
			{
				return LocalizationManager.GetString("PublishTab.Upload.Login.LoginOrSignupConnectionFailed",
					"Bloom could not connect to the server to complete your login or signup. This could be a problem with your internet connection, our server, or some equipment in between.");
			}
		}

		private void DoSignUp()
		{
			if (!_termsOfUseCheckBox.Checked)
			{
				MessageBox.Show(this,
					LocalizationManager.GetString("PublishTab.Upload.Login.MustAgreeTerms",
						"In order to sign up for a BloomLibrary.org account, you must check the box indicating that you agree to the BloomLibrary Terms of Use."),
					LocalizationManager.GetString("PublishTab.Upload.Login.PleaseAgreeTerms", "Please agree to terms of use"),
					MessageBoxButtons.OK);
				return;
			}
			bool userExists;
			try
			{
				userExists = _client.UserExists(_emailBox.Text);
			}
			catch (Exception e)
			{
				NonFatalProblem.Report(ModalIf.None, PassiveIf.All, LoginFailureString, LoginOrSignupConnectionFailedString, e);
				return;
			}
			if (userExists)
			{
				if (
					MessageBox.Show(this,
						LocalizationManager.GetString("PublishTab.Upload.Login.AlreadyHaveAccount", "We cannot sign you up with that address, because we already have an account with that address.  Would you like to log in instead?"),
						LocalizationManager.GetString("PublishTab.Upload.Login.AccountAlreadyExists", "Account Already Exists"),
						MessageBoxButtons.YesNo)
						== DialogResult.Yes)
				{
					RestoreToLogin();
					return;
				}
			}
			try
			{
				_client.CreateUser(_emailBox.Text, _passwordBox.Text);
				if (_client.LogIn(_emailBox.Text, _passwordBox.Text))
					Close();

			}
			catch (Exception e)
			{
				NonFatalProblem.Report(ModalIf.None, PassiveIf.All, LoginFailureString, LoginOrSignupConnectionFailedString, e);
			}
		}

		private bool _doingSignup;
		string oldText;
		private string oldLogin;


		/// <summary>
		/// Adapt the dialog to use it for signup and run it.
		/// Return true if successfully signed up (or possibly logged on with an existing account).
		/// </summary>
		/// <returns></returns>
		public bool SignUp(IWin32Window owner)
		{
			Settings.Default.WebUserId = _emailBox.Text = "";
			Settings.Default.WebPassword = _passwordBox.Text = "";
			SwitchToSignUp();
			if (ShowDialog(owner) == DialogResult.Cancel)
				return false;
			return true;
		}

		private void SwitchToSignUp()
		{
			_forgotLabel.Visible = false;
			this.Text = LocalizationManager.GetString("PublishTab.Upload.Login.Signup", "Sign up for Bloom Library.");
			_loginButton.Text = LocalizationManager.GetString("PublishTab.Upload.Login.Signup", "Sign up");
			_doingSignup = true;
			ShowTermsOfUse(true);
		}

		private void RestoreToLogin()
		{
			_doingSignup = false;
			this.Text = oldText;
			_loginButton.Text = oldLogin;
			_forgotLabel.Visible = true;
			ShowTermsOfUse(false);
		}

		private void ShowTermsOfUse(bool show)
		{
			Height = show ? _originalHeight : _termsOfUseCheckBox.Top - 2 + (Height - ClientRectangle.Height);
			_termsOfUseCheckBox.Visible = show;
			_showTermsOfUse.Visible = show;
		}

		protected override void OnClosed(EventArgs e)
		{
			RestoreToLogin();
			base.OnClosed(e);
		}

		/// <summary>
		/// This may be called by clients to log us in using the saved settings, if any.
		/// </summary>
		/// <returns></returns>
		public bool LogIn()
		{
			if (string.IsNullOrEmpty(Settings.Default.WebUserId))
				return false;
			return _client.LogIn(Settings.Default.WebUserId, Settings.Default.WebPassword);
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			_emailBox.Text = Settings.Default.WebUserId;
			_passwordBox.Text = Settings.Default.WebPassword;
			UpdateDisplay();
		}

		protected override void OnShown(EventArgs e)
		{
			_emailBox.Select();
			base.OnShown(e);
		}

		private void _forgotLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			if (HaveGoodEmail())
			{
				try
				{

				if (_client.UserExists(_emailBox.Text))
				{
					var msg = string.Format(
						LocalizationManager.GetString("PublishTab.Upload.Login.SendingResetPassword",
							"We are sending an email to {0} with instructions for how to reset your password."), _emailBox.Text);
					MessageBox.Show(this, msg, LocalizationManager.GetString("PublishTab.Upload.Login.ResetPassword", "Resetting Password"));
					_client.SendResetPassword(_emailBox.Text);
				}
				else
				{
					if (MessageBox.Show(this, LocalizationManager.GetString("PublishTab.Upload.Login.NoRecordOfUser",
						"We don't have a user on record with that email. Would you like to sign up?"),
						LocalizationManager.GetString("PublishTab.Upload.Login.UnknownUser", "Unknown user"),
						MessageBoxButtons.YesNo)
						== DialogResult.Yes)
					{
						SwitchToSignUp();
					}
				}
				}
				catch (Exception)
				{
					MessageBox.Show(this, LocalizationManager.GetString("PublishTab.Upload.Login.ResetConnectFailed", "Bloom could not connect to the server to reset your password. Please check your network connection."),
						LocalizationManager.GetString("PublishTab.Upload.Login.ResetFailed", "Reset Password failed"),
						MessageBoxButtons.OK,
						MessageBoxIcon.Error);
				}
			}
			else
			{
				var msg = LocalizationManager.GetString("PublishTab.Upload.Login.PleaseProvideEmail", "Please enter a valid email address. We will send an email to this address so you can reset your password.");
				MessageBox.Show(this, msg, LocalizationManager.GetString("PublishTab.Upload.Login.Need Email", "Email Needed"));
			}
		}

		public static bool IsValidEmail(string email)
		{
			// source: http://thedailywtf.com/Articles/Validating_Email_Addresses.aspx
			Regex rx = new Regex(
			@"^[-!#$%&'*+/0-9=?A-Z^_a-z{|}~](\.?[-!#$%&'*+/0-9=?A-Z^_a-z{|}~])*@[a-zA-Z](-?[a-zA-Z0-9])*(\.[a-zA-Z](-?[a-zA-Z0-9])*)+$");
			return rx.IsMatch(email);
		}

		private bool HaveGoodEmail()
		{
			return IsValidEmail(_emailBox.Text);
		}

		private void _emailBox_TextChanged(object sender, EventArgs e)
		{
			UpdateDisplay();
		}

		private void UpdateDisplay()
		{
			_borderLabel.BackColor = HaveGoodEmail() ? this.BackColor : Color.Red;
			_loginButton.Enabled = HaveGoodEmail() && !string.IsNullOrWhiteSpace(_passwordBox.Text);
		}

		private void _passwordBox_TextChanged(object sender, EventArgs e)
		{
			UpdateDisplay();
		}

		private void _showPasswordCheckBox_CheckedChanged(object sender, EventArgs e)
		{
			Settings.Default.WebShowPassword = _showPasswordCheckBox.Checked;
			Settings.Default.Save();
			_passwordBox.UseSystemPasswordChar = !_showPasswordCheckBox.Checked;
		}

		private void _showTermsOfUse_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			Process.Start(BloomLibraryPublishControl.BloomLibraryUrlPrefix + "/terms");
		}
	}
}
