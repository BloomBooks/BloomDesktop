using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Windows.Forms;
using Bloom.Properties;
using DesktopAnalytics;
using L10NSharp;

namespace PalasoUIWinforms.Registration
{
	public partial class RegistrationDialog : Form
	{
		private readonly bool _registrationIsOptional;
		private bool _hadEmailAlready;
		private static bool _haveRegisteredLaunch;

		public RegistrationDialog(bool registrationIsOptional)
		{
			InitializeComponent();

			if (ReallyDesignMode)
				return;

			_registrationIsOptional = registrationIsOptional;
			_hadEmailAlready = !string.IsNullOrWhiteSpace(Palaso.UI.WindowsForms.Registration.Registration.Default.Email);

			_cancelButton.Visible = _registrationIsOptional;

			Text = LocalizationManager.GetString("RegisterDialog.WindowTitle", string.Format(Text,Application.ProductName), "Place a {0} where the name of the program goes.");
			_headingLabel.Text = LocalizationManager.GetString("RegisterDialog.Heading", string.Format(_headingLabel.Text,Application.ProductName), "Place a {0} where the name of the program goes.");
			_howUsingLabel.Text = LocalizationManager.GetString("RegisterDialog.HowAreYouUsing", string.Format(_howUsingLabel.Text, Application.ProductName), "Place a {0} where the name of the program goes.");
		}

		protected bool ReallyDesignMode
		{
			get
			{
				return (base.DesignMode || GetService(typeof(IDesignerHost)) != null) ||
					(LicenseManager.UsageMode == LicenseUsageMode.Designtime);
			}
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
								!string.IsNullOrWhiteSpace(_surname.Text) &&
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

		public static bool ShouldWeShowRegistrationDialog()
		{
			if (!_haveRegisteredLaunch)//in case the client app calls this more then once during a single run (like Bloom does when opening a different collection)
			{
				_haveRegisteredLaunch=true;

				if (Palaso.UI.WindowsForms.Registration.Registration.Default.NeedUpgrade)
				{
					//see http://stackoverflow.com/questions/3498561/net-applicationsettingsbase-should-i-call-upgrade-every-time-i-load
					Palaso.UI.WindowsForms.Registration.Registration.Default.Upgrade();
					Palaso.UI.WindowsForms.Registration.Registration.Default.NeedUpgrade = false;
					Palaso.UI.WindowsForms.Registration.Registration.Default.Save();
				}

				Palaso.UI.WindowsForms.Registration.Registration.Default.LaunchCount++;
				Palaso.UI.WindowsForms.Registration.Registration.Default.Save();
			}

			return Palaso.UI.WindowsForms.Registration.Registration.Default.LaunchCount > 2 &&
				   (
					   string.IsNullOrWhiteSpace(Palaso.UI.WindowsForms.Registration.Registration.Default.FirstName) ||
					   string.IsNullOrWhiteSpace(Palaso.UI.WindowsForms.Registration.Registration.Default.Surname) ||
					   string.IsNullOrWhiteSpace(Palaso.UI.WindowsForms.Registration.Registration.Default.Organization) ||
					   string.IsNullOrWhiteSpace(Palaso.UI.WindowsForms.Registration.Registration.Default.Email)
				   );
		}

		private void SaveAndSendIfPossible()
		{
			Palaso.UI.WindowsForms.Registration.Registration.Default.FirstName = _firstName.Text;
			Palaso.UI.WindowsForms.Registration.Registration.Default.Surname = _surname.Text;
			Palaso.UI.WindowsForms.Registration.Registration.Default.Organization = _organization.Text;
			Palaso.UI.WindowsForms.Registration.Registration.Default.Email = _email.Text;
			Palaso.UI.WindowsForms.Registration.Registration.Default.HowUsing = _howAreYouUsing.Text;
			Palaso.UI.WindowsForms.Registration.Registration.Default.Save();
			try
			{
				DesktopAnalytics.Analytics.IdentifyUpdate(GetAnalyticsUserInfo());

				if (!_hadEmailAlready && !string.IsNullOrWhiteSpace(Palaso.UI.WindowsForms.Registration.Registration.Default.Email))
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
					FirstName = Palaso.UI.WindowsForms.Registration.Registration.Default.FirstName,
					LastName = Palaso.UI.WindowsForms.Registration.Registration.Default.Surname,
					Email = Palaso.UI.WindowsForms.Registration.Registration.Default.Email,
					UILanguageCode = Settings.Default.UserInterfaceLanguage
				};
			userInfo.OtherProperties.Add("Organization", Palaso.UI.WindowsForms.Registration.Registration.Default.Organization);
			userInfo.OtherProperties.Add("HowUsing", Palaso.UI.WindowsForms.Registration.Registration.Default.HowUsing);
			return userInfo;
		}

		private void RegistrationDialog_Load(object sender, EventArgs e)
		{
			_firstName.Text = Palaso.UI.WindowsForms.Registration.Registration.Default.FirstName;
			_surname.Text = Palaso.UI.WindowsForms.Registration.Registration.Default.Surname;
			_organization.Text = Palaso.UI.WindowsForms.Registration.Registration.Default.Organization;
			_email.Text = Palaso.UI.WindowsForms.Registration.Registration.Default.Email;
			_howAreYouUsing.Text = Palaso.UI.WindowsForms.Registration.Registration.Default.HowUsing;
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
