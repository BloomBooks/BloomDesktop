using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Properties;
using DesktopAnalytics;

namespace Bloom
{
	public partial class RegistrationDialog : Form
	{
		private readonly bool _actAsThoughThisIsOptional;

		public RegistrationDialog(bool actAsThoughThisIsOptional)
		{
			_actAsThoughThisIsOptional = actAsThoughThisIsOptional;
			InitializeComponent();

			_cancelButton.Visible = _iAmStuckLabel.Visible = _actAsThoughThisIsOptional;
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
			Close();
		}

		public static bool TimeToAskForInitialOrMoreRegistrationInfo
		{
			get
			{
				return Settings.Default.LaunchCount>2 &&
					(
						string.IsNullOrWhiteSpace(Settings.Default.FirstName) ||
						string.IsNullOrWhiteSpace(Settings.Default.SirName) ||
						string.IsNullOrWhiteSpace(Settings.Default.Organization) ||
						string.IsNullOrWhiteSpace(Settings.Default.Email)
					);
			}
		}

		private void SaveAndSendIfPossible()
		{
			Settings.Default.FirstName = _firstName.Text;
			Settings.Default.SirName = _sirName.Text;
			Settings.Default.Organization = _organization.Text;
			Settings.Default.Email = _email.Text;
			Settings.Default.HowUsing = _howAreYouUsing.Text;

			try
			{
				DesktopAnalytics.Analytics.IdentifyUpdate(GetAnalyticsUserInfo());
			}
			catch (Exception)
			{
				#if DEBUG	//else, it's not polite to complain
								throw;
				#endif
			}

		}

		public static UserInfo GetAnalyticsUserInfo()
		{
			UserInfo userInfo = new UserInfo()
				{
					FirstName = Settings.Default.FirstName,
					LastName = Settings.Default.SirName,
					Email = Settings.Default.Email,
					UILanguageCode = Settings.Default.UserInterfaceLanguage
				};
			userInfo.OtherProperties.Add("Organization", Settings.Default.Organization);
			userInfo.OtherProperties.Add("HowUsing", Settings.Default.HowUsing);
			return userInfo;
		}

		private void RegistrationDialog_Load(object sender, EventArgs e)
		{
			_firstName.Text = Settings.Default.FirstName;
			_sirName.Text = Settings.Default.SirName;
			_organization.Text = Settings.Default.Organization;
			_email.Text = Settings.Default.Email;
			_howAreYouUsing.Text = Settings.Default.HowUsing;
			UpdateDisplay();
			//only need to do this now
			this._email.TextChanged += new System.EventHandler(this.OnTextChanged);
		}

		private void _cancelButton_Click(object sender, EventArgs e)
		{
			Close();
		}
	}
}
