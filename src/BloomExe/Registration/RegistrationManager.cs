using System;
using System.Windows.Forms;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.Utils;
using DesktopAnalytics;

namespace Bloom.Registration
{
    public static class RegistrationManager
    {
        private static bool _haveRegisteredLaunch;

        /// <summary>
        /// Creates the Registration form in a React Dialog and blocks the caller from continuing until this Dialog is closed
        /// </summary>
        public static void ShowRegistrationDialog(Form projectWindow = null)
        {
            using (
                var registrationDialog = new ReactDialog(
                    "registrationDialogBundle",
                    new { },
                    "Registration Dialog"
                )
            )
            {
                registrationDialog.Width = 400 + 50; // width DialogMiddle + WinWrapper size
                registrationDialog.Height = 340 + 63 + 53 + 12; // height DialogMiddle + DialogTitle, Buttons, & WinWrapper size

                // either we have the ProjectWindow, or the dialog is the only open window, so we need it in the Taskbar
                if (projectWindow != null)
                {
                    registrationDialog.ShowInTaskbar = false;
                    registrationDialog.ShowDialog(projectWindow);
                }
                else
                {
                    registrationDialog.ShowInTaskbar = true;
                    registrationDialog.ShowDialog();
                }
            }
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

        public static void SaveAndSendIfPossible(
            string firstName,
            string surname,
            string email,
            string organization,
            string howAreYouUsing,
            bool hadEmailAlready
        )
        {
            Registration.Default.FirstName = firstName;
            Registration.Default.Surname = surname;
            Registration.Default.Email =
                email == "" ? ""
                : MiscUtils.IsValidEmail(email) ? email.Trim()
                : "";
            Registration.Default.Organization = organization;
            Registration.Default.HowUsing = howAreYouUsing;
            Registration.Default.Save();
            try
            {
                DesktopAnalytics.Analytics.IdentifyUpdate(GetAnalyticsUserInfo());

                if (!hadEmailAlready && !string.IsNullOrWhiteSpace(Registration.Default.Email))
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
                UILanguageCode = Settings.Default.UserInterfaceLanguage,
            };
            userInfo.OtherProperties.Add("Organization", Registration.Default.Organization);
            userInfo.OtherProperties.Add("HowUsing", Registration.Default.HowUsing);
            return userInfo;
        }

        /// <summary>
        /// Returns true if registration has email address (after prompting the user if needed); false otherwise
        /// </summary>
        public static bool PromptForRegistrationIfNeeded()
        {
            if (Program.RunningUnitTests)
                return true;

            if (!string.IsNullOrWhiteSpace(Registration.Default.Email))
                return true;

            // Used when Joining a Team Collection without an email

            // It is good to have this dialog with a task bar since no other window is giving a task bar,
            // so if someone brings another window to the foreground, it will be easier to get this back
            ShowRegistrationDialog();

            return !string.IsNullOrWhiteSpace(Registration.Default.Email);
        }
    }
}
