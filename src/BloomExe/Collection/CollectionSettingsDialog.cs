using System;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.TeamCollection;
using Bloom.Utils;
using Bloom.web.controllers;
using Bloom.WebLibraryIntegration;
using L10NSharp;
using SIL.Reporting;

namespace Bloom.Collection
{
    public partial class CollectionSettingsDialog
        : SIL.Windows.Forms.Miscellaneous.FormForUsingPortableClipboard
    {
        public delegate CollectionSettingsDialog Factory(); //autofac uses this

        private readonly CollectionSettings _collectionSettings;
        private readonly QueueRenameOfCollection _queueRenameOfCollection;
        private readonly XMatterPackFinder _xmatterPackFinder;
        private readonly PendingCollectionSettings _pendingSettings;
        private bool _loaded;
        private bool _currentCollectionIsTeamCollection;

        public CollectionSettingsDialog(
            CollectionSettings collectionSettings,
            QueueRenameOfCollection queueRenameOfCollection,
            TeamCollectionManager tcManager,
            XMatterPackFinder xmatterPackFinder
        )
        {
            _collectionSettings = collectionSettings;
            _queueRenameOfCollection = queueRenameOfCollection;
            _xmatterPackFinder = xmatterPackFinder;
            InitializeComponent();

            _language1Name.UseMnemonic = false; // Allow & to be part of the language display names.
            _language2Name.UseMnemonic = false; // This may be unlikely, but can't be ruled out.
            _language3Name.UseMnemonic = false; // See https://issues.bloomlibrary.org/youtrack/issue/BL-9919.

            _pendingSettings = CollectionSettingsApi.BeginEditing(_collectionSettings);
            // The React tabs record their edits through API endpoints, not all of which run on
            // the UI thread, and some of which can arrive before this form has a handle.
            _pendingSettings.RestartRequiredChanged = () =>
            {
                if (IsHandleCreated)
                    Invoke((Action)UpdateDisplay);
            };
            CollectionSettingsApi.ShowScriptSettingsDialog = zeroBasedLanguageNumber =>
            {
                if (FontSettingsLinkClicked(zeroBasedLanguageNumber))
                    ChangeThatRequiresRestart();
            };

            // Currently, ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections)
            // can be false when we're in a team collection, as the user can open a preexisting TC
            // (and then access the TC tab in Collection Settings) without checking/enabling
            // Team Collections under Experimental Features
            _currentCollectionIsTeamCollection =
                tcManager.CurrentCollectionEvenIfDisconnected != null;

            if (
                !ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections)
                && tcManager.CurrentCollectionEvenIfDisconnected == null
            )
            {
                this._tab.Controls.Remove(this._teamCollectionTab);
            }

            if (_collectionSettings.EditingABlorgBook)
            {
                // Don't give the slightest encouragement to making a download-for-edit collection into a team collection.
                _tab.Controls.Remove(this._teamCollectionTab);
            }

            TeamCollectionApi.TheOneInstance.SetCallbackToReopenCollection(() =>
            {
                _pendingSettings.ChangeThatRequiresRestart();
                ReactDialog.CloseCurrentModal(); // close the top Create dialog
                _okButton_Click(null, null); // close this dialog
            });

            UpdateDisplay();

            if (FixingEnterpriseSubscriptionCode)
            {
                _tab.SelectedTab = _subscriptionTab;
            }

            if (tcManager.CurrentCollectionEvenIfDisconnected == null)
            {
                _noRenameTeamCollectionLabel.Visible = false;
            }
            else
            {
                _bloomCollectionName.Enabled = false;
            }
        }

        /// <summary>
        /// AutoUpdate applies only to Windows: see https://silbloom.myjetbrains.com/youtrack/issue/BL-2317.
        /// Also, we are stranding pre-windows 10 people at 5.4.
        /// </summary>
        internal static bool AutoUpdateSupportedOnThisPlatform
        {
            get
            {
                return SIL.PlatformUtilities.Platform.IsWindows
                    && Environment.OSVersion.Version.Major >= 10;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // BL-552, BL-779: a bug in Mono requires us to wait to set Icon until handle created.
            this.Icon = global::Bloom.Properties.Resources.BloomIcon;
        }

        public void SetDesiredTab(string tab)
        {
            if (tab == "subscription")
                _tab.SelectedTab = _subscriptionTab;
        }

        private void UpdateDisplay()
        {
            var lang1UiName = _pendingSettings.Language1.Name;
            var lang2UiName = _pendingSettings.Language2.Name;
            _language1Name.Text = string.Format(
                "{0} ({1})",
                lang1UiName,
                _pendingSettings.Language1.Tag
            );
            _language2Name.Text = string.Format(
                "{0} ({1})",
                lang2UiName,
                _pendingSettings.Language2.Tag
            );
            const string unsetLanguageName = "--";
            if (string.IsNullOrEmpty(_pendingSettings.Language3.Tag))
            {
                _language3Name.Text = unsetLanguageName;
                _removeLanguage3Link.Visible = false;
                _changeLanguage3Link.Text = LocalizationManager.GetString(
                    "CollectionSettingsDialog.LanguageTab.SetThirdLanguageLink",
                    "Set...",
                    "If there is no third or sign language specified, the link changes to this."
                );
            }
            else
            {
                var lang3UiName = _pendingSettings.Language3.Name;
                _language3Name.Text = string.Format(
                    "{0} ({1})",
                    lang3UiName,
                    _pendingSettings.Language3.Tag
                );
                _removeLanguage3Link.Visible = true;
                _changeLanguage3Link.Text = LocalizationManager.GetString(
                    "CollectionSettingsDialog.LanguageTab.ChangeLanguageLink",
                    "Change..."
                );
            }

            if (string.IsNullOrEmpty(_pendingSettings.SignLanguage.Tag))
            {
                _signLanguageName.Text = unsetLanguageName;
                _removeSignLanguageLink.Visible = false;
                _changeSignLanguageLink.Text = LocalizationManager.GetString(
                    "CollectionSettingsDialog.LanguageTab.SetThirdLanguageLink",
                    "Set...",
                    "If there is no third or sign language specified, the link changes to this."
                );
            }
            else
            {
                var signLangUiName = _pendingSettings.SignLanguage.Name;
                _signLanguageName.Text = string.Format(
                    "{0} ({1})",
                    signLangUiName,
                    _pendingSettings.SignLanguage.Tag
                );
                _removeSignLanguageLink.Visible = true;
                _changeSignLanguageLink.Text = LocalizationManager.GetString(
                    "CollectionSettingsDialog.LanguageTab.ChangeLanguageLink",
                    "Change..."
                );
            }

            _restartReminder.Visible = AnyReasonToRestart();
            _okButton.Text = AnyReasonToRestart()
                ? LocalizationManager.GetString(
                    "CollectionSettingsDialog.Restart",
                    "Restart",
                    "If you make certain changes in the settings dialog, the OK button changes to this."
                )
                : LocalizationManager.GetString("Common.OKButton", "&OK");
        }

        private void _language1ChangeLink_LinkClicked(
            object sender,
            LinkLabelLinkClickedEventArgs e
        )
        {
            var potentiallyCustomName = _pendingSettings.Language1.Name;

            void onLanguageChange(LanguageChangeEventArgs args)
            {
                _pendingSettings.Language1.Tag = args.LanguageTag;
                if (args.IsRtl.HasValue)
                    _pendingSettings.Language1.IsRightToLeft = args.IsRtl.Value;
                _pendingSettings.Language1.SetName(args.DesiredName, args.IsCustomName);
                ChangeThatRequiresRestart();
            }
            ChangeLanguage(onLanguageChange, _pendingSettings.Language1.Tag, potentiallyCustomName);
        }

        private void _language2ChangeLink_LinkClicked(
            object sender,
            LinkLabelLinkClickedEventArgs e
        )
        {
            var potentiallyCustomName = _pendingSettings.Language2.Name;
            void onLanguageChange(LanguageChangeEventArgs args)
            {
                _pendingSettings.Language2.Tag = args.LanguageTag;
                if (args.IsRtl.HasValue)
                    _pendingSettings.Language2.IsRightToLeft = args.IsRtl.Value;
                _pendingSettings.Language2.SetName(args.DesiredName, args.IsCustomName);
                ChangeThatRequiresRestart();
            }
            ChangeLanguage(onLanguageChange, _pendingSettings.Language2.Tag, potentiallyCustomName);
        }

        private void _language3ChangeLink_LinkClicked(
            object sender,
            LinkLabelLinkClickedEventArgs e
        )
        {
            var potentiallyCustomName = _pendingSettings.Language3.Name;
            void onLanguageChange(LanguageChangeEventArgs args)
            {
                _pendingSettings.Language3.Tag = args.LanguageTag;
                if (args.IsRtl.HasValue)
                    _pendingSettings.Language3.IsRightToLeft = args.IsRtl.Value;
                _pendingSettings.Language3.SetName(args.DesiredName, args.IsCustomName);
                ChangeThatRequiresRestart();
            }
            ChangeLanguage(onLanguageChange, _pendingSettings.Language3.Tag, potentiallyCustomName);
        }

        private void _removeSecondNationalLanguageButton_LinkClicked(
            object sender,
            LinkLabelLinkClickedEventArgs e
        )
        {
            _pendingSettings.Language3.ChangeTag(string.Empty); // null causes a crash in trying to set it again (BL-5795)
            _pendingSettings.Language3.SetName(string.Empty, false);
            ChangeThatRequiresRestart();
        }

        private void _signLanguageChangeLink_LinkClicked(
            object sender,
            LinkLabelLinkClickedEventArgs e
        )
        {
            var potentiallyCustomName = _pendingSettings.SignLanguage.Name;
            void onLanguageChange(LanguageChangeEventArgs args)
            {
                _pendingSettings.SignLanguage.Tag = args.LanguageTag;
                // Unlike Language1-3 above, args.IsRtl is deliberately ignored: a sign language
                // has no text direction.
                _pendingSettings.SignLanguage.SetName(args.DesiredName, args.IsCustomName);
                ChangeThatRequiresRestart();
            }
            ChangeLanguage(
                onLanguageChange,
                _pendingSettings.SignLanguage.Tag,
                potentiallyCustomName
            );
        }

        private void _removeSignLanguageButton_LinkClicked(
            object sender,
            LinkLabelLinkClickedEventArgs e
        )
        {
            _pendingSettings.SignLanguage.ChangeTag(string.Empty);
            _pendingSettings.SignLanguage.SetName(string.Empty, false);
            ChangeThatRequiresRestart();
        }

        public static void ChangeLanguage(
            Action<LanguageChangeEventArgs> onLanguageChange,
            string languageIdentifier,
            string potentiallyCustomName = null
        )
        {
            // There shouldn't be listeners at this point, but clear just in case any are left over from a previous dialog opening
            CollectionSettingsApi.UnsubscribeAllLanguageChangeListeners();
            EventHandler<LanguageChangeEventArgs> onLanguageChangeListener = null;
            onLanguageChangeListener = delegate(object sender, LanguageChangeEventArgs args)
            {
                onLanguageChange(args);
                CollectionSettingsApi.LanguageChange -= onLanguageChangeListener;
            };
            CollectionSettingsApi.LanguageChange += onLanguageChangeListener;

            using (
                var dlg = new ReactDialog(
                    "languageChooserBundle",
                    new
                    {
                        initialLanguageTag = languageIdentifier,
                        initialCustomName = potentiallyCustomName,
                    }
                )
            )
            {
                var owner = Shell.GetShellOrOtherOpenForm();
                dlg.SetScaledSize(1000, 580);
                dlg.ShowDialog(owner);
            }
        }

        private void _okButton_Click(object sender, EventArgs e)
        {
            Logger.WriteMinorEvent("Settings Dialog OK Clicked");

            _pendingSettings.Country = _countryText.Text;
            _pendingSettings.Province = _provinceText.Text;
            _pendingSettings.District = _districtText.Text;
            _pendingSettings.CollectionName = _bloomCollectionName.Text;

            // Validate before we save the settings
            var errorMessage = CollectionSettingsUpdater.Validate(
                _pendingSettings,
                _currentCollectionIsTeamCollection
            );
            if (errorMessage != null)
            {
                BloomMessageBox.ShowWarning(errorMessage);
                return;
            }

            CollectionSettingsApi.EndEditing();
            CollectionSettingsApi.ShowScriptSettingsDialog = null;

            var restartRequired = CollectionSettingsUpdater.Apply(
                _pendingSettings,
                _collectionSettings,
                _currentCollectionIsTeamCollection,
                _xmatterPackFinder,
                newName => _queueRenameOfCollection.Raise(newName)
            );

            Logger.WriteEvent("Closing Collection Settings Dialog");

            Close();

            DialogResult = restartRequired ? DialogResult.Yes : DialogResult.OK;
        }

        private bool XMatterChangePending
        {
            get { return _pendingSettings.Xmatter != _collectionSettings.XMatterPackName; }
        }

        /// <summary>
        /// Records a change made by this dialog's own controls that needs a restart.
        /// </summary>
        private void ChangeThatRequiresRestart()
        {
            if (!_loaded) //ignore false events that come while setting upt the dialog
                return;

            _pendingSettings.ChangeThatRequiresRestart();
        }

        private bool AnyReasonToRestart()
        {
            return _pendingSettings.RestartRequired || XMatterChangePending;
        }

        /// <summary>
        /// Client that is launching dialog sets this if we are running the dialog for the purpose of
        /// fixing a subscription code. It forces the Enterprise tab and shows the branding that
        /// the code is needed for.
        /// </summary>
        public bool FixingEnterpriseSubscriptionCode;

        private void OnLoad(object sender, EventArgs e)
        {
            _countryText.Text = _collectionSettings.Country;
            _provinceText.Text = _collectionSettings.Province;
            _districtText.Text = _collectionSettings.District;
            _bloomCollectionName.Text = _collectionSettings.CollectionName;
            _loaded = true;
            Logger.WriteEvent("Entered Settings Dialog");
        }

        private void _cancelButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;

            CollectionSettingsApi.CancelEditing();
            CollectionSettingsApi.ShowScriptSettingsDialog = null;
            Close();
        }

        private void _helpButton_Click(object sender, EventArgs e)
        {
            if (_tab.SelectedTab == tabPage1)
                HelpLauncher.Show(this, "Tasks/Basic_tasks/Change_languages.htm");
            else if (_tab.SelectedTab == _bookMakingTab)
                HelpLauncher.Show(
                    this,
                    "Tasks/Basic_tasks/Select_front_matter_or_back_matter_from_a_pack.htm"
                );
            else if (_tab.SelectedTab == tabPage3)
                HelpLauncher.Show(this, "Tasks/Basic_tasks/Enter_project_information.htm");
            else if (_tab.SelectedTab == _subscriptionTab)
                HelpLauncher.Show(this, "Tasks/Basic_tasks/Enter_Subscription_Code.htm");
            else
                HelpLauncher.Show(this, "User_Interface/Dialog_boxes/Settings_dialog_box.htm");
        }

        private void _bloomCollectionName_TextChanged(object sender, EventArgs e)
        {
            if (_bloomCollectionName.Text.Trim() == _collectionSettings.CollectionName)
                return;

            ChangeThatRequiresRestart();
        }

        /// <summary>
        /// Lets the user edit the script settings of one (zero-based) language in the WinForms
        /// ScriptSettingsDialog.
        /// </summary>
        /// <returns>true if the change needs a restart</returns>
        private bool FontSettingsLinkClicked(int zeroBasedLanguageNumber)
        {
            var pendingLanguage = _pendingSettings.Languages[zeroBasedLanguageNumber];
            using (LegacyDpiDialogLauncher.EnterLegacyDpiScope())
            using (var frm = new ScriptSettingsDialog())
            {
                frm.LanguageName = pendingLanguage.Name;
                frm.LanguageRightToLeft = pendingLanguage.IsRightToLeft;
                frm.LanguageLineSpacing = pendingLanguage.LineHeight;
                frm.UIFontSize = pendingLanguage.BaseUIFontSizeInPoints;
                frm.BreakLinesOnlyAtSpaces = pendingLanguage.BreaksLinesOnlyAtSpaces;
                frm.ShowDialog(this);

                // get the changes

                // We usually don't need to restart, just gather the changes up. The caller
                // will save the .bloomCollection file. Later when a book
                // is edited, defaultLangStyles.css will be written out in the book's folder, which is all
                // that is needed for this setting to take effect.
                pendingLanguage.LineHeight = frm.LanguageLineSpacing;
                pendingLanguage.BreaksLinesOnlyAtSpaces = frm.BreakLinesOnlyAtSpaces;
                pendingLanguage.BaseUIFontSizeInPoints = frm.UIFontSize;
                pendingLanguage.IsRightToLeft = frm.LanguageRightToLeft;
                return pendingLanguage.IsRightToLeft
                    != _collectionSettings.AllLanguages[zeroBasedLanguageNumber].IsRightToLeft;
            }
        }

        private void _numberStyleCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            ChangeThatRequiresRestart();
        }
    }
}
