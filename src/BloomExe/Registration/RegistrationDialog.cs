using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Windows.Forms;
using Bloom.Properties;
using Bloom.Utils;
using DesktopAnalytics;

namespace Bloom.Registration
{
    public partial class RegistrationDialog
        : SIL.Windows.Forms.Miscellaneous.FormForUsingPortableClipboard
    {
        private static bool _haveRegisteredLaunch;

        protected string AdditionalText
        {
            set => _additionalTextLabel.Text = value;
        }
        protected bool IsEmailRequired { get; set; }

        private readonly bool _hadEmailAlready;

        public RegistrationDialog(bool registrationIsOptional, bool mayChangeEmail)
        {
            InitializeComponent();

            if (ReallyDesignMode)
                return;

            _hadEmailAlready = !string.IsNullOrWhiteSpace(Registration.Default.Email);

            _cancelButton.Visible = registrationIsOptional;

            Text = string.Format(Text, Application.ProductName);
            _headingLabel.Text = string.Format(_headingLabel.Text, Application.ProductName);
            _howUsingLabel.Text = string.Format(_howUsingLabel.Text, Application.ProductName);
            _additionalTextLabel.Text = null;

            _email.Enabled = mayChangeEmail;
            if (!mayChangeEmail)
            {
                // This is a very minimal indication and not yet localizable...
                // but a long translation will badly mess up the dialog layout, whereas, this
                // message fits. At least it gives a clue why the control is disabled.
                _emailLabel.Text = "Check in to change email";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            _additionalTextLabel = null;
        }

        protected bool ReallyDesignMode =>
            (DesignMode || GetService(typeof(IDesignerHost)) != null)
            || (LicenseManager.UsageMode == LicenseUsageMode.Designtime);

        private void _userIsStuckDetector_Tick(object sender, EventArgs e)
        {
            _iAmStuckLabel.Visible = true;
        }

        private void OnIAmStuckLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            SaveAndSendIfPossible(); //they might have filled some of it in
            Close();
        }

        private void OnTextChanged(object sender, EventArgs e)
        {
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            var emailAddressProvided = !string.IsNullOrWhiteSpace(_email.Text);

            _okButton.Enabled =
                !string.IsNullOrWhiteSpace(_firstName.Text)
                && !string.IsNullOrWhiteSpace(_surname.Text)
                && !string.IsNullOrWhiteSpace(_organization.Text)
                && !string.IsNullOrWhiteSpace(_howAreYouUsing.Text)
                && (!IsEmailRequired || emailAddressProvided)
                && (!emailAddressProvided || MiscUtils.IsValidEmail(_email.Text));

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
            // Allow registration information to be copied from version to version even if
            // the user has set the FEEDBACK environment variable.  If the user does register, or
            // use an email address in a feedback form, we want to preserve that information!
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-7956.
            //there is no point registering if we are are developer/tester
            string feedbackSetting = Environment.GetEnvironmentVariable("FEEDBACK");
            if (
                !string.IsNullOrEmpty(feedbackSetting)
                && feedbackSetting.ToLowerInvariant() != "yes"
                && feedbackSetting.ToLowerInvariant() != "true"
            )
                return false;

            if (!_haveRegisteredLaunch) //in case the client app calls this more then once during a single run (like Bloom does when opening a different collection)
            {
                _haveRegisteredLaunch = true;
                Registration.Default.LaunchCount++;
                Registration.Default.Save();
            }

            return Registration.Default.LaunchCount > 2
                && (
                    string.IsNullOrWhiteSpace(Registration.Default.FirstName)
                    || string.IsNullOrWhiteSpace(Registration.Default.Surname)
                    || string.IsNullOrWhiteSpace(Registration.Default.Organization)
                    || string.IsNullOrWhiteSpace(Registration.Default.Email)
                );
        }

        private void SaveAndSendIfPossible()
        {
            Registration.Default.FirstName = _firstName.Text;
            Registration.Default.Surname = _surname.Text;
            Registration.Default.Organization = _organization.Text;
            Registration.Default.Email =
                _email.Text == null
                    ? null
                    : MiscUtils.IsValidEmail(_email.Text)
                        ? _email.Text.Trim()
                        : null;
            Registration.Default.HowUsing = _howAreYouUsing.Text;
            Registration.Default.Save();
            try
            {
                DesktopAnalytics.Analytics.IdentifyUpdate(GetAnalyticsUserInfo());

                if (!_hadEmailAlready && !string.IsNullOrWhiteSpace(Registration.Default.Email))
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
            UserInfo userInfo = new UserInfo
            {
                FirstName = Registration.Default.FirstName,
                LastName = Registration.Default.Surname,
                Email = Registration.Default.Email,
                UILanguageCode = Settings.Default.UserInterfaceLanguage
            };
            userInfo.OtherProperties.Add("Organization", Registration.Default.Organization);
            userInfo.OtherProperties.Add("HowUsing", Registration.Default.HowUsing);
            return userInfo;
        }

        private void RegistrationDialog_Load(object sender, EventArgs e)
        {
            _firstName.Text = Registration.Default.FirstName;
            _surname.Text = Registration.Default.Surname;
            _organization.Text = Registration.Default.Organization;
            _email.Text = Registration.Default.Email;
            _howAreYouUsing.Text = Registration.Default.HowUsing;
            UpdateDisplay();
            //only need to do this now
            _email.TextChanged += OnTextChanged;
        }

        private void _cancelButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // BL-832: a bug in Mono requires us to wait to set Icon until handle created.
            this.Icon = global::Bloom.Properties.Resources.BloomIcon;
        }

        /// <summary>
        /// Returns true if registration has email address (after prompting the user if needed); false otherwise
        /// </summary>
        /// <param name="message">An optional message which appears below the heading</param>
        public static bool RequireRegistrationEmail(string message = null)
        {
            if (Program.RunningUnitTests)
                return true;

            if (!string.IsNullOrWhiteSpace(Registration.Default.Email))
                return true;

            // We're only doing this because current registration does NOT have email, so changing it must be OK.
            using (
                var registrationDialog = new RegistrationDialog(false, true)
                {
                    AdditionalText = message,
                    IsEmailRequired = true
                }
            )
                registrationDialog.ShowDialog();

            return !string.IsNullOrWhiteSpace(Registration.Default.Email);
        }
    }
}
