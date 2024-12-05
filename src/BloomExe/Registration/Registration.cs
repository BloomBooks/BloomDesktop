using System;
using System.IO;
using System.Text;
using Bloom.Api;
using SIL.IO;

namespace Bloom.Registration
{
    public class Registration
    {
        private static readonly Registration defaultInstance = new Registration();
        public static Registration Default => defaultInstance;

        string _settingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SIL",
            "Bloom",
            "Registration.json"
        );

        public string Organization;
        public string HowUsing;
        public string FirstName;
        public string Surname;
        public string Email;
        public int LaunchCount = -1;

        public Registration()
        {
            var folder = Path.GetDirectoryName(_settingsFile);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            if (RobustFile.Exists(_settingsFile))
            {
                Load();
                if (IncompleteData())
                {
                    // Fill in any missing data from the old settings file.
                    MigrateFromOldSettingsFile();
                }
            }
            else
            {
                // If the old settings file exists, we need to migrate it.
                MigrateFromOldSettingsFile();
            }
        }

        ~Registration()
        {
            Save(); // just to be on the safe side
        }

        private void Load()
        {
            var rawData = RobustFile.ReadAllText(_settingsFile, Encoding.UTF8);
            if (string.IsNullOrEmpty(rawData))
                return;
            var settings = DynamicJson.Parse(rawData);
            if (settings.FirstName != null)
                FirstName = settings.FirstName;
            if (settings.Surname != null)
                Surname = settings.Surname;
            if (settings.Email != null)
                Email = settings.Email;
            if (settings.Organization != null)
                Organization = settings.Organization;
            if (settings.HowUsing != null)
                HowUsing = settings.HowUsing;
            if (settings.LaunchCount != null)
                LaunchCount = (int)settings.LaunchCount;
        }

        public void Save()
        {
            var settings = new
            {
                FirstName,
                Surname,
                Email,
                Organization,
                HowUsing,
                LaunchCount
            };
            var json = DynamicJson.Serialize(settings);
            RobustFile.WriteAllText(_settingsFile, json, Encoding.UTF8);
        }

        private void MigrateFromOldSettingsFile()
        {
            if (SIL.Windows.Forms.Registration.Registration.Default.NeedUpgrade)
            {
                SIL.Windows.Forms.Registration.Registration.Default.Upgrade();
                SIL.Windows.Forms.Registration.Registration.Default.NeedUpgrade = false;
                SIL.Windows.Forms.Registration.Registration.Default.Save();
            }
            var dirty = false;
            var email = SIL.Windows.Forms.Registration.Registration.Default.Email;
            if (string.IsNullOrEmpty(Email) && !string.IsNullOrEmpty(email))
            {
                Email = email;
                dirty = true;
            }
            var firstName = SIL.Windows.Forms.Registration.Registration.Default.FirstName;
            if (string.IsNullOrEmpty(FirstName) && !string.IsNullOrEmpty(firstName))
            {
                FirstName = firstName;
                dirty = true;
            }
            var surName = SIL.Windows.Forms.Registration.Registration.Default.Surname;
            if (string.IsNullOrEmpty(Surname) && !string.IsNullOrEmpty(surName))
            {
                Surname = surName;
                dirty = true;
            }
            var organization = SIL.Windows.Forms.Registration.Registration.Default.Organization;
            if (string.IsNullOrEmpty(Organization) && !string.IsNullOrEmpty(organization))
            {
                Organization = organization;
                dirty = true;
            }
            var howUsing = SIL.Windows.Forms.Registration.Registration.Default.HowUsing;
            if (string.IsNullOrEmpty(HowUsing) && !string.IsNullOrEmpty(howUsing))
            {
                HowUsing = howUsing;
                dirty = true;
            }
            var launchCount = SIL.Windows.Forms.Registration.Registration.Default.LaunchCount;
            if (LaunchCount == -1 && launchCount >= 0)
            {
                LaunchCount = launchCount;
                dirty = true;
            }
            if (dirty)
                Save();
        }

        private bool IncompleteData()
        {
            return (
                string.IsNullOrEmpty(FirstName)
                || string.IsNullOrEmpty(Surname)
                || string.IsNullOrEmpty(Email)
                || string.IsNullOrEmpty(Organization)
                || string.IsNullOrEmpty(HowUsing)
                || LaunchCount == -1
            );
        }

        internal void EnsureLoaded()
        {
            // This is a bit of a hack, but it ensures that the registration data is loaded
            // since the constructor must have been called.
        }
    }
}
