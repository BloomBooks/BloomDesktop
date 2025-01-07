using System;
using System.IO;
using System.Linq;
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

        private dynamic _settings;

        public string Organization
        {
            get { return _settings.Organization; }
            set { _settings.Organization = value; }
        }
        public string HowUsing
        {
            get { return _settings.HowUsing; }
            set { _settings.HowUsing = value; }
        }
        public string FirstName
        {
            get { return _settings.FirstName; }
            set { _settings.FirstName = value; }
        }
        public string Surname
        {
            get { return _settings.Surname; }
            set { _settings.Surname = value; }
        }
        public string Email
        {
            get { return _settings.Email; }
            set { _settings.Email = value; }
        }
        public int LaunchCount
        {
            get { return (int)_settings.LaunchCount; }
            set { _settings.LaunchCount = value; }
        }

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
            {
                CreateEmptySettings();
            }
            else
            {
                _settings = DynamicJson.Parse(rawData);
                if (!doesPropertyExist("LaunchCount"))
                {
                    _settings.LaunchCount = -1;
                }
                if (!doesPropertyExist("HowUsing"))
                {
                    _settings.HowUsing = "";
                }
                if (!doesPropertyExist("Organization"))
                {
                    _settings.Organization = "";
                }
                if (!doesPropertyExist("Surname"))
                {
                    _settings.Surname = "";
                }
                if (!doesPropertyExist("FirstName"))
                {
                    _settings.FirstName = "";
                }
                if (!doesPropertyExist("Email"))
                {
                    _settings.Email = "";
                }
            }
        }

        private bool doesPropertyExist(string property)
        {
            return ((Type)_settings.GetType())
                .GetProperties()
                .Where(p => p.Name.Equals(property))
                .Any();
        }

        public void Save()
        {
            var json = Convert.ToString(_settings);
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
            if (_settings == null)
            {
                CreateEmptySettings();
            }
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

        private void CreateEmptySettings()
        {
            _settings = new DynamicJson();
            _settings.Email = "";
            _settings.FirstName = "";
            _settings.Surname = "";
            _settings.Organization = "";
            _settings.HowUsing = "";
            _settings.LaunchCount = -1;
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
