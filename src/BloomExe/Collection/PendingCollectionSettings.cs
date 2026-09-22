using System;
using Bloom.Properties;
using Bloom.SubscriptionAndFeatures;

namespace Bloom.Collection
{
    /// <summary>
    /// The edits a user has made in a Collection Settings dialog but has not applied yet.
    /// Both the WinForms dialog and the React one record their edits here, and
    /// CollectionSettingsUpdater applies them when the user clicks OK.
    /// </summary>
    public class PendingCollectionSettings
    {
        public WritingSystem Language1;
        public WritingSystem Language2;
        public WritingSystem Language3;
        public WritingSystem SignLanguage;

        // Ugly I know, but we need to be able to access these by an index number sometimes.
        public readonly WritingSystem[] Languages = new WritingSystem[3];

        public readonly string[] FontSelections = new[] { "", "", "" };

        public string NumberingStyle;
        public bool ShowQrCode;
        public string BadgeQrCodeCaption;
        public string Xmatter;
        public string Administrators;
        public string DefaultBookshelf;
        public bool AutomaticallyUpdate;
        public bool ShowExperimentalBookSources;
        public bool AllowTeamCollection;
        public string Country;
        public string Province;
        public string District;

        /// <summary>
        /// The collection name exactly as the user typed it; the rename sanitizes it.
        /// </summary>
        public string CollectionName;

        /// <summary>
        /// The subscription the user entered, when its code differs from the collection's own;
        /// null while the user has not changed it. Several rules (notably the expired-bookshelf
        /// reconciliation of BL-15056) turn on whether the user changed the subscription at all,
        /// rather than on its value.
        /// </summary>
        public Subscription Subscription;

        public bool RestartRequired { get; private set; }

        /// <summary>
        /// Called whenever a change is recorded that needs a restart, so a dialog showing a
        /// restart reminder can update it. Not every API endpoint that records a pending change
        /// runs on the UI thread, so a WinForms listener has to marshal.
        /// </summary>
        public Action RestartRequiredChanged;

        /// <summary>
        /// Starts a session whose values are those the collection currently has.
        /// </summary>
        public PendingCollectionSettings(CollectionSettings settings)
        {
            Language1 = settings.Language1.Clone();
            Language2 = settings.Language2.Clone();
            Language3 = settings.Language3.Clone();
            SignLanguage = settings.SignLanguage.Clone();
            Languages[0] = Language1;
            Languages[1] = Language2;
            Languages[2] = Language3;

            FontSelections[0] = settings.AllLanguages[0].FontName;
            FontSelections[1] = settings.AllLanguages[1].FontName;
            var have3rdLanguage = settings.AllLanguages[2] != null;
            FontSelections[2] = have3rdLanguage ? settings.AllLanguages[2].FontName : "";

            NumberingStyle = settings.PageNumberStyle;
            ShowQrCode = settings.ShowBlorgLanguageQrCode;
            BadgeQrCodeCaption = settings.BadgeQrCodeLabelLocalized;
            Xmatter = settings.XMatterPackName;
            Administrators = settings.AdministratorsDisplayString;
            // A collection whose settings file never carried these leaves them null.
            Country = settings.Country ?? "";
            Province = settings.Province ?? "";
            District = settings.District ?? "";
            CollectionName = settings.CollectionName;

            ShowExperimentalBookSources = ExperimentalFeatures.IsFeatureEnabled(
                ExperimentalFeatures.kExperimentalSourceBooks
            );
            AllowTeamCollection = ExperimentalFeatures.IsFeatureEnabled(
                ExperimentalFeatures.kTeamCollections
            );
            AutomaticallyUpdate =
                CollectionSettingsDialog.AutoUpdateSupportedOnThisPlatform
                && Settings.Default.AutoUpdate;

            // Without this, DefaultBookshelf stays null unless the user changes it.
            // The result is the bookshelf selection gets cleared when other collection settings are saved. See BL-10093.
            DefaultBookshelf = settings.DefaultBookshelf;
        }

        /// <summary>
        /// Records that the user has changed something Bloom can only act on after a restart.
        /// </summary>
        public void ChangeThatRequiresRestart()
        {
            RestartRequired = true;
            RestartRequiredChanged?.Invoke();
        }
    }
}
