using System;
using System.Collections.Generic;
using System.Diagnostics;
using Bloom.Book;
using Bloom.Properties;
using Bloom.SubscriptionAndFeatures;
using Bloom.web.controllers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIL.Reporting;

namespace Bloom.Collection
{
    /// <summary>
    /// Applies the values the React Collection Settings dialog posts. Most of them write
    /// themselves into the collection as they are read (see CollectionSettingsValues); this class
    /// holds the rules that need more than one value: the languages, the xmatter check, and the
    /// subscription, bookshelf and administrator rules ported from the WinForms OK handler.
    /// </summary>
    public static class CollectionSettingsUpdater
    {
        /// <summary>
        /// Writes the posted values into the collection and saves it.
        /// </summary>
        /// <param name="queueRenameOfCollection">called with the new (sanitized) folder name when
        /// the user renamed the collection</param>
        public static void Apply(
            JObject postedValues,
            CollectionSettings settings,
            bool currentCollectionIsTeamCollection,
            XMatterPackFinder xmatterPackFinder,
            Action<string> queueRenameOfCollection
        )
        {
            Logger.WriteEvent("React Collection Settings dialog: saving");
            // This writes the posted values straight into the open collection (and queues any
            // rename) before anything is saved, so if a save below fails, the in-memory settings
            // are left partly changed. We accept that, as the WinForms OK handler always has: the
            // failure is reported, and the collection is read from disk again when it reopens.
            using (var reader = postedValues.CreateReader())
            {
                JsonSerializer
                    .Create(CollectionSettingsApi.kCamelCaseSettings)
                    .Populate(
                        reader,
                        new CollectionSettingsValues(settings, queueRenameOfCollection)
                    );
            }

            // A pack that is no longer available, or one the branding overrides, is replaced.
            settings.XMatterPackName = xmatterPackFinder.GetValidXmatter(
                settings.GetXMatterPackNameSpecifiedByBrandingOrNull(),
                settings.XMatterPackName
            );

            Settings.Default.Save(); // AutoUpdate is a user-level setting

            // The dialog does not post the administrators, the subscription or the bookshelf
            // yet (their tab cards will), so these rules run with "unchanged". That still matters:
            // it is what keeps an expired subscription's bookshelf in the file (BL-15056).
            ApplyAdministrators(null, settings, currentCollectionIsTeamCollection);
            var clearDefaultBookshelfAfterSaving = ApplySubscriptionAndBookshelf(
                null,
                null,
                settings,
                currentCollectionIsTeamCollection
            );
            settings.Save();

            if (clearDefaultBookshelfAfterSaving)
                settings.DefaultBookshelf = "";
        }

        /// <summary>
        /// Writes the administrators of a Team Collection.
        /// </summary>
        /// <param name="newAdministrators">as the user typed them (already validated by the
        /// client), or null if unchanged</param>
        internal static void ApplyAdministrators(
            string newAdministrators,
            CollectionSettings settings,
            bool currentCollectionIsTeamCollection
        )
        {
            if (currentCollectionIsTeamCollection && newAdministrators != null)
                settings.ModifyAdministrators(newAdministrators);
        }

        /// <summary>
        /// Applies a new subscription and bookshelf, with the rules that tie the two together.
        /// Ported from the WinForms OK handler, where a null pending subscription means the user
        /// did not change it.
        /// </summary>
        /// <param name="newSubscription">the subscription the user entered, or null if unchanged</param>
        /// <param name="newBookshelf">the bookshelf the user chose, or null if unchanged</param>
        /// <returns>true if DefaultBookshelf must be cleared again once the collection is saved
        /// (the expired subscription's bookshelf was put back only so that it gets saved)</returns>
        internal static bool ApplySubscriptionAndBookshelf(
            Subscription newSubscription,
            string newBookshelf,
            CollectionSettings settings,
            bool currentCollectionIsTeamCollection
        )
        {
            var pendingSubscription = newSubscription;
            var pendingDefaultBookshelf = newBookshelf ?? settings.DefaultBookshelf;

            Subscription originalSubscription = null;
            if (pendingSubscription != null)
            {
                if (
                    pendingSubscription.Tier == SubscriptionTier.Pro
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
                    pendingSubscription = null; // not allowed; dialog already explained this
                else
                {
                    originalSubscription = settings.Subscription;
                    settings.Subscription = pendingSubscription;

                    // This comparison is always false: settings.Subscription was just assigned from
                    // pendingSubscription. So the bookshelf is never cleared here. Kept as it was; BL-16904.
                    if (pendingSubscription.Descriptor != settings.Subscription.Descriptor)
                    {
                        // The user has entered a different subscription code than what was previously saved.
                        // We need to clear out the Bookshelf, since the new branding may not have the same bookshelf as the old one.
                        // (We don't know if it does or not, so we have to assume it doesn't.)
                        pendingDefaultBookshelf = string.Empty;
                    }
                }
            }

            settings.DefaultBookshelf = pendingDefaultBookshelf;
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
            // dialog.  We signal to do this by returning true. (BL-15056)
            var clearDefaultBookshelfAfterSaving = false;
            if (
                String.IsNullOrEmpty(pendingDefaultBookshelf)
                && !string.IsNullOrEmpty(settings.ExpiredBookshelf)
            )
            {
                if (pendingSubscription == null)
                {
                    // The subscription has not changed, it's still the same one that expired.
                    // We need to keep remembering the former bookshelf.
                    settings.DefaultBookshelf = settings.ExpiredBookshelf;
                    clearDefaultBookshelfAfterSaving = true;
                }
                else if (
                    IsPendingSubscriptionSameAsOriginalSubscription(
                        originalSubscription,
                        pendingSubscription
                    )
                )
                {
                    // The subscription has changed, but the new one is for the same subscription, presumably renewed.
                    // We restore the bookshelf that was set when the subscription expired.
                    settings.DefaultBookshelf = settings.ExpiredBookshelf;
                    if (pendingSubscription.IsExpired())
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
            return clearDefaultBookshelfAfterSaving;
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
                pendingSubscription.Descriptor.StartsWith(
                    originalSubscription.Descriptor + "-",
                    StringComparison.Ordinal
                )
                || originalSubscription.Descriptor.StartsWith(
                    pendingSubscription.Descriptor + "-",
                    StringComparison.Ordinal
                )
            )
            {
                // This may be the case when the user has entered a new subscription code that is for
                // the same subscription, but it is incorrectly entered.  An incorrectly entered
                // subscription may have been persisted already.
                return true;
            }
            return false;
        }

        /// <summary>
        /// Applies the posted languages to the collection. A null third language or sign
        /// language means the collection has none.
        /// </summary>
        internal static void ApplyLanguages(LanguagesValues posted, CollectionSettings settings)
        {
            var pendingLanguages = new[]
            {
                settings.Language1.Clone(),
                settings.Language2.Clone(),
                settings.Language3.Clone(),
            };
            var pendingFonts = new string[3];
            var postedLanguages = new[] { posted.Language1, posted.Language2, posted.Language3 };
            for (var i = 0; i < 3; i++)
                pendingFonts[i] = CopyLanguage(postedLanguages[i], pendingLanguages[i]);

            //no point in letting them have the Nat lang 2 be the same as 1
            if (pendingLanguages[1].Tag == pendingLanguages[2].Tag)
            {
                pendingLanguages[2].ChangeTag(String.Empty);
                pendingLanguages[2].SetName(String.Empty, false);
            }

            UpdateLanguageSettings(settings.AllLanguages, pendingLanguages, pendingFonts);

            settings.SignLanguage.ChangeTag(posted.SignLanguage?.Tag ?? String.Empty);
            if (posted.SignLanguage != null)
                settings.SignLanguage.SetName(
                    posted.SignLanguage.Name,
                    posted.SignLanguage.IsCustomName
                );
        }

        /// <summary>
        /// Copies one posted language onto a copy of the collection's, and returns its font. A
        /// null language (only the third can be) clears it.
        /// </summary>
        private static string CopyLanguage(LanguageValues posted, WritingSystem pending)
        {
            if (posted == null)
            {
                pending.ChangeTag(String.Empty);
                pending.SetName(String.Empty, false);
                return "";
            }
            // Setting the tag also sets a default name, so set the name we were given afterwards.
            pending.ChangeTag(posted.Tag);
            pending.SetName(posted.Name, posted.IsCustomName);
            pending.IsRightToLeft = posted.IsRightToLeft;
            pending.LineHeight = posted.LineHeight;
            pending.BreaksLinesOnlyAtSpaces = posted.BreaksLinesOnlyAtSpaces;
            pending.BaseUIFontSizeInPoints = posted.BaseUIFontSizeInPoints;
            return posted.FontName;
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
