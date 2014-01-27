using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Bloom.Properties;
using L10NSharp;
using Palaso.Code;

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
		public LoginDialog(BloomParseClient client)
		{
			Require.That(client != null);
			_client = client;
			InitializeComponent();
			_showPasswordCheckBox.Checked = Settings.Default.WebShowPassword;
			oldText = this.Text;
			oldLogin = _loginButton.Text;
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
			if (_client.LogIn(_emailBox.Text, _passwordBox.Text))
			{
				DialogResult = DialogResult.OK;
				Close();
			}
			else
			{
				MessageBox.Show(this, LocalizationManager.GetString("Login.PasswordMismatch", "Password and user ID did not match"),
					LocalizationManager.GetString("Login.LoginFailed", "Login failed"),
					MessageBoxButtons.OK,
					MessageBoxIcon.Error);
			}
		}

		private void DoSignUp()
		{
			if (_client.UserExists(_emailBox.Text))
			{
				if (
					MessageBox.Show(this,
						LocalizationManager.GetString("Login.AlreadyHaveAccount", "We already have an account with this address.  Would you like to login?"),
						LocalizationManager.GetString("Login.AccountExists", "Account Exists"),
						MessageBoxButtons.YesNo)
						== DialogResult.Yes)
				{
					RestoreToLogin();
					return;
				}
			}
			_client.CreateUser(_emailBox.Text, _passwordBox.Text);
			if (_client.LogIn(_emailBox.Text, _passwordBox.Text))
				Close();
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
			this.Text = LocalizationManager.GetString("Login.Signup", "Sign up for Bloom Library.");
			_loginButton.Text = LocalizationManager.GetString("Login.Signup", "Sign up");
			_doingSignup = true;
		}

		private void RestoreToLogin()
		{
			_doingSignup = false;
			this.Text = oldText;
			_loginButton.Text = oldLogin;
			_forgotLabel.Visible = true;
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
				if (_client.UserExists(_emailBox.Text))
				{
					var msg = string.Format(
						LocalizationManager.GetString("Login.SendingResetPassword",
							"We are sending an email to {0} with instructions for how to reset your password"), _emailBox.Text);
					MessageBox.Show(this, msg, LocalizationManager.GetString("Login.ResetPassword", "Resetting Password"));
					_client.SendResetPassword(_emailBox.Text);
				}
				else
				{
					if (MessageBox.Show(this, LocalizationManager.GetString("Login.NoRecordOfUser",
						"We don't have a user on record with that email. Would you like to sign up?"),
						LocalizationManager.GetString("Login.UnknownUser", "Unknown user"),
						MessageBoxButtons.YesNo)
						== DialogResult.Yes)
					{
						SwitchToSignUp();
					}
				}
			}
			else
			{
				var msg = LocalizationManager.GetString("Login.PleaseProvideEmail", "Please enter a valid email address. We will send an email to this address so you can reset your password.");
				MessageBox.Show(this, msg, LocalizationManager.GetString("Login.Need Email", "Email Needed"));
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
	}
}
