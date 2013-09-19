using System;
using System.Windows.Forms;
using Bloom.Properties;
using DesktopAnalytics;
using L10NSharp;

namespace Bloom.Registration
{
	public partial class RegistrationDialog : Form
	{
		private readonly bool _registrationIsOptional;
		private bool _hadEmailAlready;

		public RegistrationDialog(bool registrationIsOptional)
		{
			_registrationIsOptional = registrationIsOptional;
			InitializeComponent();

			_hadEmailAlready = !string.IsNullOrWhiteSpace(Settings.Default.Registration.Email);

			_cancelButton.Visible = _registrationIsOptional;

			Text = LocalizationManager.GetString("RegisterDialog.WindowTitle", string.Format(Text,Application.ProductName), "Place a {0} where the name of the program goes.");
			_headingLabel.Text = LocalizationManager.GetString("RegisterDialog.Heading", string.Format(_headingLabel.Text,Application.ProductName), "Place a {0} where the name of the program goes.");
			_howUsingLabel.Text = LocalizationManager.GetString("RegisterDialog.HowAreYouUsing", string.Format(_howUsingLabel.Text, Application.ProductName), "Place a {0} where the name of the program goes.");
		}

		private void _userIsStuckDetector_Tick(object sender, EventArgs e)
		{
			_iAmStuckLabel.Visible = true;
		}

		private void OnIAmStuckLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
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
				return Settings.Default.Registration.LaunchCount > 2 &&
					(
						string.IsNullOrWhiteSpace(Settings.Default.Registration.FirstName) ||
						string.IsNullOrWhiteSpace(Settings.Default.Registration.SirName) ||
						string.IsNullOrWhiteSpace(Settings.Default.Registration.Organization) ||
						string.IsNullOrWhiteSpace(Settings.Default.Registration.Email)
					);
			}
		}

		private void SaveAndSendIfPossible()
		{
			Settings.Default.Registration.FirstName = _firstName.Text;
			Settings.Default.Registration.SirName = _sirName.Text;
			Settings.Default.Registration.Organization = _organization.Text;
			Settings.Default.Registration.Email = _email.Text;
			Settings.Default.Registration.HowUsing = _howAreYouUsing.Text;

			try
			{
				DesktopAnalytics.Analytics.IdentifyUpdate(GetAnalyticsUserInfo());

				if (!_hadEmailAlready && !string.IsNullOrWhiteSpace(Settings.Default.Registration.Email))
				{
					DesktopAnalytics.Analytics.Track("Register");
				}

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
					FirstName = Settings.Default.Registration.FirstName,
					LastName = Settings.Default.Registration.SirName,
					Email = Settings.Default.Registration.Email,
					UILanguageCode = Settings.Default.UserInterfaceLanguage
				};
			userInfo.OtherProperties.Add("Organization", Settings.Default.Registration.Organization);
			userInfo.OtherProperties.Add("HowUsing", Settings.Default.Registration.HowUsing);
			return userInfo;
		}

		private void RegistrationDialog_Load(object sender, EventArgs e)
		{
			_firstName.Text = Settings.Default.Registration.FirstName;
			_sirName.Text = Settings.Default.Registration.SirName;
			_organization.Text = Settings.Default.Registration.Organization;
			_email.Text = Settings.Default.Registration.Email;
			_howAreYouUsing.Text = Settings.Default.Registration.HowUsing;
			UpdateDisplay();
			//only need to do this now
			_email.TextChanged += new System.EventHandler(this.OnTextChanged);
		}

		private void _cancelButton_Click(object sender, EventArgs e)
		{
			Close();
		}
	}
}
