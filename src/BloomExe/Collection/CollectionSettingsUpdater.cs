using System;
using System.Collections.Generic;
using System.Diagnostics;
using Bloom.Book;
using Bloom.Properties;
using Bloom.SubscriptionAndFeatures;
using L10NSharp;
using SIL.Extensions;

namespace Bloom.Collection
{
    /// <summary>
    /// Validates and applies the edits gathered in a PendingCollectionSettings. Both the WinForms
    /// Collection Settings dialog and the React one go through here, so that whichever the user
    /// used, the collection is saved in exactly the same way.
    /// </summary>
    public static class CollectionSettingsUpdater
    {
        /// <summary>
        /// Checks the pending settings the way the OK button does before it commits anything.
        /// </summary>
        /// <returns>a localized message to show the user, or null when there is nothing wrong</returns>
        public static string Validate(
            PendingCollectionSettings pending,
            bool currentCollectionIsTeamCollection
        )
        {
            if (
                currentCollectionIsTeamCollection
                && !CollectionSettings.ValidateAdministrators(pending.Administrators)
            )
            {
                // The user has entered invalid email address(es)
                return LocalizationManager.GetString(
                    "TeamCollection.InvalidAdminEmails",
                    "Please enter one or more valid administrator email addresses, separated by commas or spaces."
                );
            }
            return null;
        }

        /// <summary>
        /// Writes the pending settings into the collection and saves it. Call Validate first; this
        /// assumes the values are good.
        /// </summary>
        /// <param name="queueRenameOfCollection">called with the new (sanitized) folder name when
        /// the user renamed the collection</param>
        /// <returns>true if Bloom has to restart for the changes to take effect</returns>
        public static bool Apply(
            PendingCollectionSettings pending,
            CollectionSettings settings,
            bool currentCollectionIsTeamCollection,
            XMatterPackFinder xmatterPackFinder,
            Action<string> queueRenameOfCollection
        )
        {
            if (currentCollectionIsTeamCollection)
                settings.ModifyAdministrators(pending.Administrators);

            Settings.Default.AutoUpdate =
                pending.AutomaticallyUpdate
                && CollectionSettingsDialog.AutoUpdateSupportedOnThisPlatform;
            Settings.Default.Save();
            ExperimentalFeatures.SetValue(
                ExperimentalFeatures.kExperimentalSourceBooks,
                pending.ShowExperimentalBookSources
            );
            UpdateTeamCollectionAllowed(pending);

            settings.Country = pending.Country.Trim();
            settings.Province = pending.Province.Trim();
            settings.District = pending.District.Trim();

            settings.PageNumberStyle = pending.NumberingStyle; // non-localized key
            settings.ShowBlorgLanguageQrCode = pending.ShowQrCode;
            if (pending.BadgeQrCodeCaption != settings.BadgeQrCodeLabelLocalized)
            {
                // Update the BadgeQrCodeLabel value only if the user has actually changed it.
                // The default value displayed for BadgeQrCodeLabel is based on the current UI language,
                // so if the user changes the UI language and then opens the Collection Settings dialog,
                // we don't want to have the default BadgeQrCodeLabel frozen to the original UI language.
                settings.BadgeQrCodeLabel = pending.BadgeQrCodeCaption;
            }

            Subscription originalSubscription = null;
            if (pending.Subscription != null)
            {
                if (
                    pending.Subscription.Tier == SubscriptionTier.Pro
                    && currentCollectionIsTeamCollection
                )
                    // Pro tier is not allowed for team collections. It's a matter of policy that
                    // Pro tier does not support the TC feature, but it's conceivable that someone wants
                    // to do what is possible in a disconnected TC using Pro features. However, it
                    // generates a mass of confusing corner cases, such as
                    // - If we sync the collection settings with the Pro subscription to the repo,
                    //   that encourages sharing a Pro subscription, which we don't want (and current code
                    //   may not do the sync, if neither the old nor the new sub allow it).
                    // - if we don't copy it to the repo, the change won't stick: the restart will sync
                    //   whatever's in the repo to overwrite our collection settings with the old sub.
                    // As we tried to think what special cases we might make to overcome those basic problems,
                    // we just kept finding more and more corner cases. We decided to just not allow it.
                    pending.Subscription = null; // not allowed; dialog already explained this
                else
                {
                    originalSubscription = settings.Subscription;
                    settings.Subscription = pending.Subscription;

                    // This comparison is always false: settings.Subscription was just assigned from
                    // pending.Subscription. So the bookshelf is never cleared here. Kept as it was; BL-16904.
                    if (pending.Subscription.Descriptor != settings.Subscription.Descriptor)
                    {
                        // The user has entered a different subscription code than what was previously saved.
                        // We need to clear out the Bookshelf, since the new branding may not have the same bookshelf as the old one.
                        // (We don't know if it does or not, so we have to assume it doesn't.)
                        pending.DefaultBookshelf = string.Empty;
                    }
                }
            }

            string xmatterKeyForcedByBranding =
                settings.GetXMatterPackNameSpecifiedByBrandingOrNull();
            pending.Xmatter = xmatterPackFinder.GetValidXmatter(
                xmatterKeyForcedByBranding,
                pending.Xmatter
            );
            var xmatterChanged = pending.Xmatter != settings.XMatterPackName;
            settings.XMatterPackName = pending.Xmatter;

            //no point in letting them have the Nat lang 2 be the same as 1
            if (pending.Language2.Tag == pending.Language3.Tag)
            {
                pending.Language3.ChangeTag(String.Empty);
                pending.Language3.SetName(String.Empty, false);
            }

            UpdateLanguageSettings(
                settings.AllLanguages,
                pending.Languages,
                pending.FontSelections
            );

            settings.SignLanguage.ChangeTag(pending.SignLanguage.Tag);
            if (!String.IsNullOrEmpty(pending.SignLanguage.Tag))
                settings.SignLanguage.SetName(
                    pending.SignLanguage.Name,
                    pending.SignLanguage.IsCustomName
                );

            if (pending.CollectionName.Trim() != settings.CollectionName)
            {
                queueRenameOfCollection(pending.CollectionName.SanitizeFilename('-'));
            }

            settings.DefaultBookshelf = pending.DefaultBookshelf;
            // If the user has not changed the subscription code (signaled by a null pending Subscription
            // object) and has not changed the bookshelf (signaled by an empty pending string), we want
            // to keep the bookshelf that was set when a subscription expired.  We also want to keep the
            // bookshelf if the user changes the subscription code to one that is for the same subscription,
            // but has been renewed even if they misenter it. (BL-15056)
            //
            // settings.ExpiredBookshelf is not written to the .bloomCollection file, but is
            // set from settings.DefaultBookshelf when the file is read if the subscription
            // has expired.  (In which case, settings.DefaultBookshelf is cleared.)  This is
            // so that if the user has not changed the bookshelf or the subscription, we can restore the
            // bookshelf when the user gets the subscription renewed.  But we don't want to start showing
            // the user the expired bookshelf while the subscription is still expired since they can't
            // make use of it until they renew the subscription.  So if we've written the expired value to
            // the file, we want to clear it from memory to restore the status quo coming into the
            // dialog.  We signal to do this by setting clearDefaultBookshelfAfterSaving. (BL-15056)
            var clearDefaultBookshelfAfterSaving = false;
            if (
                String.IsNullOrEmpty(pending.DefaultBookshelf)
                && !string.IsNullOrEmpty(settings.ExpiredBookshelf)
            )
            {
                if (pending.Subscription == null)
                {
                    // The subscription has not changed, it's still the same one that expired.
                    // We need to keep remembering the former bookshelf.
                    settings.DefaultBookshelf = settings.ExpiredBookshelf;
                    clearDefaultBookshelfAfterSaving = true;
                }
                else if (
                    IsPendingSubscriptionSameAsOriginalSubscription(
                        originalSubscription,
                        pending.Subscription
                    )
                )
                {
                    // The subscription has changed, but the new one is for the same subscription, presumably renewed.
                    // We restore the bookshelf that was set when the subscription expired.
                    settings.DefaultBookshelf = settings.ExpiredBookshelf;
                    if (pending.Subscription.IsExpired())
                    {
                        // This could be partially entered as well as expired.
                        clearDefaultBookshelfAfterSaving = true;
                    }
                    else
                    {
                        // If the new subscription is not expired, we can clear the expired bookshelf.
                        settings.ExpiredBookshelf = string.Empty;
                    }
                }
                else
                {
                    // The subscription has changed, and the new one is for a different subscription.
                    // The user has essentially told us to forget the expired bookshelf.
                    settings.ExpiredBookshelf = string.Empty;
                }
            }
            settings.Save();

            if (clearDefaultBookshelfAfterSaving)
                settings.DefaultBookshelf = "";

            return pending.RestartRequired || xmatterChanged;
        }

        /// <summary>
        /// Turning the Team Collections feature on or off needs a restart, and the feature is a
        /// user-level setting rather than part of the collection.
        /// </summary>
        private static void UpdateTeamCollectionAllowed(PendingCollectionSettings pending)
        {
            var wasTeamCollectionsEnabled = ExperimentalFeatures.IsFeatureEnabled(
                ExperimentalFeatures.kTeamCollections
            );

            ExperimentalFeatures.SetValue(
                ExperimentalFeatures.kTeamCollections,
                pending.AllowTeamCollection
            );

            if (wasTeamCollectionsEnabled != pending.AllowTeamCollection)
                pending.ChangeThatRequiresRestart();
        }

        /// <summary>
        /// Check whether the pending subscription is the same as the original subscription, or
        /// possibly a renewed version of the same subscription.
        /// </summary>
        /// <remarks>
        /// Call this only if pendingSubscription is not null.
        /// </remarks>
        private static bool IsPendingSubscriptionSameAsOriginalSubscription(
            Subscription originalSubscription,
            Subscription pendingSubscription
        )
        {
            if (originalSubscription == null)
                return false; // no subscription, so can't be the same
            if (originalSubscription.Descriptor == pendingSubscription.Descriptor)
                return true;
            if (
                pendingSubscription.Descriptor.StartsWith(originalSubscription.Descriptor + "-")
                || originalSubscription.Descriptor.StartsWith(pendingSubscription.Descriptor + "-")
            )
            {
                // This may be the case when the user has entered a new subscription code that is for
                // the same subscription, but it is incorrectly entered.  An incorrectly entered
                // subscription may have been persisted already.
                return true;
            }
            return false;
        }

        // internal and static to facilitate unit testing
        internal static void UpdateLanguageSettings(
            List<WritingSystem> languages,
            WritingSystem[] pendingLanguages,
            string[] pendingFonts
        )
        {
            Debug.Assert(languages.Count >= 3);
            Debug.Assert(pendingLanguages.Length == 3);
            Debug.Assert(pendingFonts.Length == 3);

            // Provide some useful abbreviations for the first 3 languages.
            // (This method is static so that it can be tested without creating a dialog.)
            var Language1 = languages[0];
            var Language2 = languages[1];
            var Language3 = languages.Count > 2 ? languages[2] : null;
            var PendingLanguage1 = pendingLanguages[0];
            var PendingLanguage2 = pendingLanguages[1];
            var PendingLanguage3 = pendingLanguages[2];

            // NOTE: if one of the first 3 languages is replaced, we need to add it to
            // the list after the first 3.  If one of the first 3 languages was already
            // in the list, we need to remove it from its old position.

            // Copy the old Language1 if it's not in the first 3 languages.
            if (
                Language1.Tag != PendingLanguage1.Tag
                && Language1.Tag != PendingLanguage2.Tag
                && Language1.Tag != PendingLanguage3.Tag
            )
            {
                languages.Add(Language1.Clone()); // need a fresh copy
            }
            // Copy the old Language2 if it's not in the first 3 languages, and is not
            // the same as the old Language1.
            if (
                Language2.Tag != PendingLanguage1.Tag
                && Language2.Tag != PendingLanguage2.Tag
                && Language2.Tag != PendingLanguage3.Tag
                && Language2.Tag != Language1.Tag
            )
            {
                languages.Add(Language2.Clone());
            }
            // Copy the old Language3 if it exists and is not in the first 3 languages, and
            // is not the same as either the old Language1 or the old Language2.
            if (
                !String.IsNullOrEmpty(Language3.Tag)
                && Language3.Tag != PendingLanguage1.Tag
                && Language3.Tag != PendingLanguage2.Tag
                && Language3.Tag != PendingLanguage3.Tag
                && Language3.Tag != Language1.Tag
                && Language3.Tag != Language2.Tag
            )
            {
                languages.Add(Language3.Clone());
            }
            // Remove the languages that are now in the first 3 languages from later in the list
            // if they were in the list after the first 3 languages.
            for (int i = languages.Count - 1; i >= 3; i--)
            {
                if (languages[i].Tag == PendingLanguage1.Tag)
                {
                    languages.RemoveAt(i);
                }
                else if (languages[i].Tag == PendingLanguage2.Tag)
                {
                    languages.RemoveAt(i);
                }
                else if (languages[i].Tag == PendingLanguage3.Tag)
                {
                    languages.RemoveAt(i);
                }
            }
            // Update the values in the first three languages.
            for (int i = 0; i < 3; i++)
            {
                if (languages[i] == null)
                    continue;
                languages[i].FontName = pendingFonts[i];
                languages[i].IsRightToLeft = pendingLanguages[i].IsRightToLeft;
                languages[i].LineHeight = pendingLanguages[i].LineHeight;
                languages[i].BaseUIFontSizeInPoints = pendingLanguages[i].BaseUIFontSizeInPoints;
                languages[i].BreaksLinesOnlyAtSpaces = pendingLanguages[i].BreaksLinesOnlyAtSpaces;
            }

            Language1.ChangeTag(PendingLanguage1.Tag);
            Language1.SetName(PendingLanguage1.Name, PendingLanguage1.IsCustomName);
            Language2.ChangeTag(PendingLanguage2.Tag);
            if (!String.IsNullOrEmpty(PendingLanguage2.Tag))
                Language2.SetName(PendingLanguage2.Name, PendingLanguage2.IsCustomName);
            Language3.ChangeTag(PendingLanguage3.Tag);
            if (!String.IsNullOrEmpty(PendingLanguage3.Tag))
                Language3.SetName(PendingLanguage3.Name, PendingLanguage3.IsCustomName);
        }
    }
}
