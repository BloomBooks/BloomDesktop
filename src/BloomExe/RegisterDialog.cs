using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Properties;

namespace Bloom
{
	public partial class RegisterDialog : Form
	{
		public RegisterDialog()
		{
			InitializeComponent();
		}

		private void _userIsStuckDetector_Tick(object sender, EventArgs e)
		{
			_iAmStuckLabel.Visible = true;
		}

		private void _iAmStuckLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			SaveAndSendIfPossible();//they might have filled some of it in
			Close();
		}

		private void OnTextChanged(object sender, EventArgs e)
		{
			UpdateDisplay();
		}

		private void UpdateDisplay()
		{
			_okButton.Enabled = !string.IsNullOrWhiteSpace(_firstName.Text) &&
								!string.IsNullOrWhiteSpace(_sirName.Text) &&
								!string.IsNullOrWhiteSpace(_organization.Text) &&
								!string.IsNullOrWhiteSpace(_howAreYouUsing.Text);

			//reset the stuck detection timer
			_userIsStuckDetector.Stop();
			_userIsStuckDetector.Start();
		}

		private void _okButton_Click(object sender, EventArgs e)
		{
			SaveAndSendIfPossible();
		}

		private void SaveAndSendIfPossible()
		{
			Settings.Default.FirstName = _firstName.Text;
			Settings.Default.SirName = _sirName.Text;
			Settings.Default.Organization = _organization.Text;
			Settings.Default.Email = _email.Text;
			Settings.Default.HowUsing = _howAreYouUsing.Text;

			//Segmentio.Analytics.Client.Identify(DesktopAnalytics.Analytics.);
		}
	}
}
