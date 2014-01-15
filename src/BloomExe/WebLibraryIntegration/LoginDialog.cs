using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Properties;
using Palaso.Code;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// this class manages a minimal login process for Bloom.
	/// Todo: Eventually we need to somehow handle password reset and signup.
	/// One option is to just put in a URL to the web site and direct them there if not signed up.
	/// Can't do this until the web site is actually live somewhere.
	/// </summary>
	public partial class LoginDialog : Form
	{
		private BloomParseClient _client;
		public LoginDialog(BloomParseClient client)
		{
			Require.That(client != null);
			_client = client;
			InitializeComponent();
		}

		private void Login(object sender, EventArgs e)
		{
			if (!string.IsNullOrEmpty(_emailBox.Text))
			{
				Settings.Default.WebUserId = _emailBox.Text;
				Settings.Default.WebPassword = _passwordBox.Text; // Review: password is saved in clear text. Is this a security risk? How could we prevent it?
				Settings.Default.Save();
			}
			if (_client.LogIn(_emailBox.Text, _passwordBox.Text))
			{
				DialogResult = DialogResult.OK;
				Close();
			}
			else
			{
				MessageBox.Show(this, "Logon failed", "Password and user ID did not match", MessageBoxButtons.OK,
					MessageBoxIcon.Error);
			}
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
	}
}
