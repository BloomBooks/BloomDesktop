using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Bloom.ImageProcessing;
using Bloom.SafeXml;
using L10NSharp;
using SIL.Core.ClearShare;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using SIL.Text;
using SIL.Windows.Forms.ClearShare;

namespace Bloom.Book
{
    /// <summary>
    /// Reads and writes the aspects of the book related to copyright, license, license logo, etc.
    /// That involves three duties:
    /// 1) Serializing/Deserializing a libpalaso.ClearShare.Metadata to/from the bloomDataDiv of the html
    /// 2) Propagating that information into template fields found in the pages of the book (normally just the credits page)
    /// 3) Placing the correct license image into the folder
    /// </summary>
    public class BookCopyrightAndLicense
    {
        /// <summary>
        /// Create a Clearshare.Metadata object by reading values out of the dom's bloomDataDiv
        /// </summary>
        public static Metadata GetMetadata(HtmlDom dom, BookData bookData)
        {
            if (ShouldSetToDefaultCopyrightAndLicense(dom))
            {
                return GetMetadataWithDefaultCopyrightAndLicense();
            }
            return CreateMetadata(
                dom.GetBookSetting("copyright"),
                GetLicenseUrl(dom),
                dom.GetBookSetting("licenseNotes"),
                bookData
            );
        }

        public static Metadata GetOriginalMetadata(HtmlDom dom, BookData bookData)
        {
            return CreateMetadata(
                dom.GetBookSetting("originalCopyright"),
                dom.GetBookSetting("originalLicenseUrl").GetExactAlternative("*"),
                dom.GetBookSetting("originalLicenseNotes"),
                bookData
            );
        }

        public static Metadata CreateMetadata(
            MultiTextBase copyright,
            string licenseUrl,
            MultiTextBase licenseNotes,
            BookData bookData
        )
        {
            var metadata = new Metadata();
            if (!copyright.Empty)
            {
                metadata.CopyrightNotice = GetBestMultiTextBaseValue(copyright, bookData);
            }

            if (string.IsNullOrWhiteSpace(licenseUrl))
            {
                //NB: we are mapping "RightsStatement" (which comes from XMP-dc:Rights) to "LicenseNotes" in the html.
                //custom licenses live in this field, so if we have notes (and no URL) it is a custom one.
                if (!licenseNotes.Empty)
                {
                    metadata.License = new CustomLicense
                    {
                        RightsStatement = GetBestMultiTextBaseValue(licenseNotes, bookData),
                    };
                }
                else
                {
                    // The only remaining current option is a NullLicense
                    metadata.License = new NullLicense(); //"contact the copyright owner
                }
            }
            else // there is a licenseUrl, which means it is a CC license
            {
                try
                {
                    metadata.License = CreativeCommonsLicense.FromLicenseUrl(licenseUrl);
                }
                catch (IndexOutOfRangeException)
                {
                    // Need to handle urls which do not end with the version number.
                    // Simply set it to the default version.
                    if (!licenseUrl.EndsWith("/"))
                        licenseUrl += "/";
                    licenseUrl += CreativeCommonsLicenseInfo.kDefaultVersion;
                    metadata.License = CreativeCommonsLicense.FromLicenseUrl(licenseUrl);
                }
                catch (Exception e)
                {
                    throw new ApplicationException(
                        "Bloom had trouble parsing this license url: '"
                            + licenseUrl
                            + "'. (ref BL-4108)",
                        e
                    );
                }
                //are there notes that go along with that?
                if (!licenseNotes.Empty)
                {
                    metadata.License.RightsStatement = GetBestMultiTextBaseValue(
                        licenseNotes,
                        bookData
                    );
                }
            }
            return metadata;
        }

        private static string GetBestMultiTextBaseValue(
            MultiTextBase multiTextBase,
            BookData bookData
        )
        {
            string alternative = multiTextBase.GetFirstAlternative();

            if (bookData != null)
            {
                var langs = new List<string>();
                langs.AddRange(bookData.GetAllBookLanguageCodes());
                langs.Add("*");
                langs.Add("en");
                var bestAltString = multiTextBase.GetBestAlternativeString(langs);
                if (!string.IsNullOrEmpty(bestAltString))
                    alternative = bestAltString;
            }

            return DecodeAlternative(alternative);
        }

        private static string DecodeAlternative(string alternative)
        {
            return HtmlDom.ConvertHtmlBreaksToNewLines(WebUtility.HtmlDecode(alternative));
        }

        public static string GetLicenseUrl(HtmlDom dom)
        {
            return dom.GetBookSetting("licenseUrl").GetBestAlternativeString(new[] { "*", "en" });
        }

        private static Metadata GetMetadataWithDefaultCopyrightAndLicense()
        {
            var metadata = new Metadata();
            Logger.WriteEvent(
                "For BL-3166 Investigation: GetMetadata() setting to default license"
            );
            metadata.License = new CreativeCommonsLicense(
                true,
                true,
                CreativeCommonsLicenseInfo.DerivativeRules.Derivatives
            );
            return metadata;
        }

        /// <summary>
        /// Call this when we have a new set of metadata to use. It
        /// 1) sets the bloomDataDiv with the data,
        /// 2) causes any template fields in the book to get the new values
        /// 3) updates the license image on disk
        /// </summary>
        public static void SetMetadata(
            Metadata metadata,
            HtmlDom dom,
            string bookFolderPath,
            BookData bookData,
            bool useOriginalCopyright,
            bool userEditsOriginalCopyrightNotice = false
        )
        {
            dom.SetBookSetting(
                "copyright",
                "*",
                ConvertNewLinesToHtmlBreaks(metadata.CopyrightNotice)
            );
            dom.SetBookSetting("licenseUrl", "*", metadata.License.Url);
            // This is for backwards compatibility. The book may have  licenseUrl in 'en' created by an earlier version of Bloom.
            // For backwards compatibility, GetMetaData will read that if it doesn't find a '*' license first. So now that we're
            // setting a licenseUrl for '*', we must make sure the 'en' one is gone, because if we're setting a non-CC license,
            // the new URL will be empty and the '*' one will go away, possibly exposing the 'en' one to be used by mistake.
            // See BL-3166.
            dom.SetBookSetting("licenseUrl", "en", null);
            string languageUsedForDescription;

            //This part is unfortunate... the license description, which is always localized, doesn't belong in the datadiv; it
            //could instead just be generated when we update the page. However, for backwards compatibility (prior to 3.6),
            //we localize it and place it in the datadiv.
            dom.RemoveBookSetting("licenseDescription");
            var langPriorities = bookData.GetLanguagePrioritiesForLocalizedTextOnPage();
            var description = metadata.License.GetDescription(
                langPriorities,
                out languageUsedForDescription
            );

            // We have removed this check because we now throw in L10nSharp itself if it is used
            // before being initialized. I'm leaving the code in case we need to return to it. See BL-13266.
            //
            // CustomLicense returns "und" for the description language unless the description is empty.
            // For an empty description, it returns the localized form of the boilerplate text
            // "For permission to reuse, contact the copyright holder." with the appropriate language tag.
            //if (!(languageUsedForDescription == "und" && metadata.License is CustomLicenseInfo))
            //{
            //    LocalizationHelper.CheckForMissingLocalization(
            //        langPriorities.ToList(),
            //        languageUsedForDescription,
            //        "Palaso.xlf"
            //    );
            //}

            dom.SetBookSetting(
                "licenseDescription",
                languageUsedForDescription,
                ConvertNewLinesToHtmlBreaks(description)
            );

            // Book may have old licenseNotes, typically in 'en'. This can certainly show up again if licenseNotes in '*' is removed,
            // and maybe anyway. Safest to remove it altogether if we are setting it using the new scheme.
            dom.RemoveBookSetting("licenseNotes");
            dom.SetBookSetting(
                "licenseNotes",
                "*",
                ConvertNewLinesToHtmlBreaks(metadata.License.RightsStatement)
            );

            // we could do away with licenseImage in the bloomDataDiv, since the name is always the same, but we keep it for backward compatibility
            if (metadata.License is CreativeCommonsLicenseInfo)
            {
                dom.SetBookSetting("licenseImage", "*", "license.png");
            }
            else
            {
                //CC licenses are the only ones we know how to show an image for
                dom.RemoveBookSetting("licenseImage");
            }

            UpdateDomFromDataDiv(
                dom,
                bookFolderPath,
                bookData,
                useOriginalCopyright,
                userEditsOriginalCopyrightNotice
            );
        }

        private static string ConvertNewLinesToHtmlBreaks(string s)
        {
            return string.IsNullOrEmpty(s) ? s : s.Replace("\r", "").Replace("\n", "<br/>");
        }

        /// <summary>
        /// Propagating the copyright and license information in the bloomDataDiv to template fields
        /// found in the pages of the book (normally just the credits page).
        /// </summary>
        /// <remarks>This is "internal" just as a convention, that it is accessible for testing purposes only</remarks>
        internal static void UpdateDomFromDataDiv(
            HtmlDom dom,
            string bookFolderPath,
            BookData bookData,
            bool useOriginalCopyright,
            bool userEditsOriginalCopyrightNotice = false
        )
        {
            CopyItemToFieldsInPages(dom, "copyright");
            CopyItemToFieldsInPages(dom, "licenseUrl");
            CopyItemToFieldsInPages(
                dom,
                "licenseDescription",
                languagePreferences: bookData
                    .GetLanguagePrioritiesForLocalizedTextOnPage()
                    .ToArray()
            );
            CopyItemToFieldsInPages(dom, "licenseNotes");
            CopyItemToFieldsInPages(dom, "licenseImage", valueAttribute: "src");
            // The sentence about the original book. Bloom generates it, unless the user has
            // taken it over, in which case their wording is in the data div. Either way the
            // book's own copy of the page holds it locked; the editable form exists only in the
            // copy of the page sent to the editor, and only for one rendering, so that leaving
            // the page or refreshing it locks the sentence again.
            string originalCopyrightNotice;
            if (useOriginalCopyright)
            {
                // The book's own copyright is the original one, so saying it again here would
                // print it twice. This holds for the user's own wording as well as Bloom's;
                // theirs stays in the data div and comes back if they turn the option off.
                // See https://issues.bloomlibrary.org/youtrack/issue/BL-7381.
                originalCopyrightNotice = null;
            }
            else if (userEditsOriginalCopyrightNotice)
            {
                originalCopyrightNotice = bookData
                    .GetVariableOrNull(kOriginalCopyrightAndLicense, "*")
                    ?.Xml;
            }
            else
            {
                originalCopyrightNotice = GetOriginalCopyrightAndLicenseNotice(bookData, dom);
            }
            ShowOriginalCopyrightNoticeLocked(
                dom,
                originalCopyrightNotice,
                userEditsOriginalCopyrightNotice
            );

            if (!String.IsNullOrEmpty(bookFolderPath)) //unit tests may not be interested in checking this part
                UpdateBookLicenseIcon(GetMetadata(dom, bookData), bookFolderPath);
        }

        internal const string kOriginalCopyrightAndLicense = "originalCopyrightAndLicense";

        // The English here must exactly match what RuntimeInformationInjector registers for this
        // key; that dictionary is keyed by the English, not by the l10n id.
        internal const string kOriginalCopyrightNoticeHint = "Original copyright & license";

        // Likewise keyed by the English. The open padlock gets no tooltip: by then the user has
        // just clicked the closed one and the field is waiting for them.
        internal const string kUnlockOriginalCopyrightNoticeTooltip = "Unlock to edit";

        /// <summary>
        /// The place on the credits page where the sentence about the original book goes, in
        /// whichever of its two shapes it is currently in: the plain div Bloom writes the
        /// generated sentence into, or the translation group it becomes while the user is
        /// editing it. The data div holds the user's wording under the same key, so it is
        /// excluded here.
        /// </summary>
        private static IEnumerable<SafeXmlElement> GetOriginalCopyrightNoticeSpots(
            SafeXmlNode pageOrDom
        )
        {
            return pageOrDom
                .SafeSelectNodes(
                    ".//*[@data-derived='"
                        + kOriginalCopyrightAndLicense
                        + "']"
                        + " | .//*[div[@data-book='"
                        + kOriginalCopyrightAndLicense
                        + "']][not(ancestor-or-self::div[@id='bloomDataDiv'])]"
                )
                .OfType<SafeXmlElement>();
        }

        /// <summary>
        /// Show the sentence about the original book as text the user cannot type in, which is
        /// how it looks whenever they are not actively editing it, with the hint bubble that
        /// offers to hand it over. An empty sentence means the line is not there at all.
        /// </summary>
        /// <param name="noticeIsMarkup">True when the sentence is the user's own, which comes
        /// out of the data div as markup and goes back in as it is. The sentence Bloom generates
        /// instead goes through the filter that lets only a few tags through.</param>
        private static void ShowOriginalCopyrightNoticeLocked(
            HtmlDom dom,
            string notice,
            bool noticeIsMarkup
        )
        {
            foreach (var spot in GetOriginalCopyrightNoticeSpots(dom.RawDom))
                LockOriginalCopyrightNoticeSpot(spot, notice, noticeIsMarkup);
        }

        /// <summary>
        /// Put the sentence about the original book back under Bloom's control on a page the user
        /// has been editing it on, keeping the words they typed. The page is on its way either to
        /// the book's own copy or to a fresh look at it, and neither should hold the editable
        /// shape: that is what makes leaving the page or refreshing it lock the sentence again.
        /// </summary>
        internal static void LockOriginalCopyrightNotice(SafeXmlNode page)
        {
            foreach (var spot in GetOriginalCopyrightNoticeSpots(page))
            {
                if (!spot.HasClass("bloom-translationGroup"))
                    continue; // already locked
                // The value the user sees and edits is the one with no language of its own; the
                // others are the empty ones Bloom makes for every language in the book.
                var editable =
                    spot.SelectSingleNode(
                        "div[@data-book='" + kOriginalCopyrightAndLicense + "' and @lang='*']"
                    ) as SafeXmlElement;
                LockOriginalCopyrightNoticeSpot(spot, editable?.InnerXml ?? "", true);
            }
        }

        /// <summary>
        /// Show the sentence about the original book as text the user cannot type in, which is
        /// how it looks whenever they are not actively editing it, with the hint bubble that
        /// offers to hand it over. An empty sentence means the line is not there at all.
        /// </summary>
        /// <param name="noticeIsMarkup">True when the sentence is the user's own, which is kept
        /// as markup and goes back onto the page as it is. The sentence Bloom generates instead
        /// goes through the filter that lets only a few tags through.</param>
        private static void LockOriginalCopyrightNoticeSpot(
            SafeXmlElement spot,
            string notice,
            bool noticeIsMarkup
        )
        {
            // Undo the editable shape, in case the user was editing the sentence.
            spot.RemoveClass("bloom-translationGroup");
            spot.RemoveAttribute("data-default-languages");
            spot.AddClass("Credits-Page-style");
            spot.SetAttribute("data-derived", kOriginalCopyrightAndLicense);

            if (string.IsNullOrEmpty(notice))
            {
                spot.RemoveAttribute("lang");
                spot.InnerText = "";
            }
            else
            {
                if (noticeIsMarkup)
                    spot.InnerXml = notice;
                else
                    HtmlDom.SetElementFromUserStringSafely(spot, notice);
                spot.SetAttribute("lang", "*");
            }
            SetOriginalCopyrightNoticeHint(spot, !string.IsNullOrEmpty(notice));
        }

        /// <summary>
        /// Turn the sentence about the original book into an ordinary editable field, so the user
        /// can reword it. Call this only on the copy of the page being sent to the editor: the
        /// book's own copy stays locked, which is what makes the unlock last for one look at the
        /// page rather than for good.
        /// The shape mirrors the ISBN field: a translation group whose only default language is
        /// "*", so there is a single value rather than one per language. We do it here rather
        /// than in the xmatter templates so that every xmatter gets it without being edited.
        /// </summary>
        internal static void MakeOriginalCopyrightNoticeEditable(HtmlDom pageDom)
        {
            foreach (var spot in GetOriginalCopyrightNoticeSpots(pageDom.RawDom))
            {
                if (spot.HasClass("bloom-translationGroup"))
                    continue; // already editable
                if (string.IsNullOrWhiteSpace(spot.InnerText))
                    continue; // no sentence here to hand over
                var notice = spot.InnerXml;

                spot.RemoveAttribute("data-derived");
                // The generated sentence carried lang="*" on this element; the language now
                // belongs to the editable child instead.
                spot.RemoveAttribute("lang");
                spot.InnerXml = "";
                // A style class belongs on the editable, not on the group that now wraps it.
                spot.RemoveClass("Credits-Page-style");
                spot.AddClass("bloom-translationGroup");
                spot.SetAttribute("data-default-languages", "*");

                var editable = spot.AppendChild("div");
                editable.SetAttribute(
                    "class",
                    "bloom-editable Credits-Page-style bloom-visibility-code-on"
                );
                editable.SetAttribute("data-book", kOriginalCopyrightAndLicense);
                editable.SetAttribute("lang", "*");
                editable.InnerXml = notice;
                // The bubble stays on the group rather than moving to the editable: anything on
                // the editable is harvested into the data div along with the text.
                SetOriginalCopyrightNoticeHint(spot, true, unlocked: true);
                // The user clicked to get here, so put their cursor in it. The editing code
                // takes this off again once it has done so.
                editable.SetAttribute("data-bloom-focus-when-shown", "true");
            }
        }

        /// <summary>
        /// Give the sentence a hint bubble offering to hand the text over to the user, or take
        /// that bubble away when there is no sentence to offer.
        /// We do this here rather than in the xmatter templates so that the bubble exists exactly
        /// when the sentence does, and so that every xmatter gets it without being edited.
        /// </summary>
        private static void SetOriginalCopyrightNoticeHint(
            SafeXmlElement target,
            bool wantHint,
            bool unlocked = false
        )
        {
            target.RemoveAttribute("data-hint");
            target.RemoveAttribute("data-link-icon");
            target.RemoveAttribute("data-link-icon-tooltip");
            target.RemoveAttribute("data-link-target");
            if (!wantHint)
                return;
            target.SetAttribute("data-hint", kOriginalCopyrightNoticeHint);
            // A closed padlock opens the text for editing; the open one closes it again.
            target.SetAttribute("data-link-icon", unlocked ? "unlock" : "lock");
            target.SetAttribute(
                "data-link-target",
                unlocked ? "RelockOriginalCredits()" : "UnlockOriginalCredits()"
            );
            if (!unlocked)
                target.SetAttribute(
                    "data-link-icon-tooltip",
                    kUnlockOriginalCopyrightNoticeTooltip
                );
        }

        /// <summary>
        /// Hand the generated original copyright and license sentence over to the user: put the
        /// wording Bloom is currently showing into the data div, which is where the editable
        /// field that replaces it reads its text from.
        /// Call this before setting BookInfo.MetaData.UserEditsOriginalCopyrightNotice, while
        /// Bloom is still generating the sentence.
        /// </summary>
        internal static void SeedUserEditableOriginalCopyrightNotice(HtmlDom dom, BookData bookData)
        {
            var notice = FlattenOriginalTitleCitation(
                GetOriginalCopyrightAndLicenseNotice(bookData, dom) ?? ""
            );
            // Wrap it in a paragraph, because that is the shape the editing code keeps text in.
            // Handed a bare run of text and markup, it wraps only the text nodes, which would
            // strand the italicized title on a line of its own.
            if (!string.IsNullOrEmpty(notice))
                notice = "<p>" + notice + "</p>";
            bookData.Set(kOriginalCopyrightAndLicense, XmlString.FromXml(notice), "*");
        }

        /// <summary>
        /// The generated sentence names the original title in a &lt;cite data-book="originalTitle"&gt;,
        /// which Bloom keeps in step with the book's originalTitle setting and which the user edits
        /// through a dialog. Once the sentence is the user's own text, it is just words they can
        /// type over, so the citation becomes plain italics. Leaving the data-book attribute there
        /// would also nest one data-book field inside another.
        /// </summary>
        private static string FlattenOriginalTitleCitation(string noticeHtml)
        {
            return Regex.Replace(
                noticeHtml,
                @"<cite\b[^>]*>(.*?)</cite>",
                "<em>$1</em>",
                RegexOptions.Singleline
            );
        }

        private static void CopyItemToFieldsInPages(
            HtmlDom dom,
            string key,
            string valueAttribute = null,
            string[] languagePreferences = null
        )
        {
            if (languagePreferences == null)
                languagePreferences = new[] { "*", "en" };

            MultiTextBase source = dom.GetBookSetting(key);

            if (key == "copyright")
            {
                // For CC0, we store the "copyright", but don't display it in the text of the book.
                var licenseUrl = dom.GetBookSetting("licenseUrl").GetExactAlternative("*");
                if (licenseUrl == CreativeCommonsLicenseInfo.CC0Url)
                    source = new MultiTextBase();
            }

            foreach (
                SafeXmlElement target in dom.SafeSelectNodes("//*[@data-derived='" + key + "']")
            )
            {
                //just put value into the text of the element
                if (string.IsNullOrEmpty(valueAttribute))
                {
                    //clear out what's there now
                    target.RemoveAttribute("lang");
                    target.InnerText = "";

                    var form = source.GetBestAlternative(languagePreferences);
                    if (form != null && !string.IsNullOrWhiteSpace(form.Form))
                    {
                        // HtmlDom.GetBookSetting(key) returns the result of SafeXmlNode.InnerXml which will be Html encoded (&amp; &lt; etc).
                        // HtmlDom.SetElementFromUserStringSafely() calls SafeXmlNode.InnerXml, which Html encodes if necessary.
                        // So we need to decode here to prevent double encoding.  See http://issues.bloomlibrary.org/youtrack/issue/BL-4585.
                        // Note that HtmlDom.SetElementFromUserStringSafely() handles embedded <br/> elements, but makes no
                        // effort to handle p or div elements.
                        var decoded = System.Web.HttpUtility.HtmlDecode(form.Form);
                        HtmlDom.SetElementFromUserStringSafely(target, decoded);
                        target.SetAttribute("lang", form.WritingSystemId); //this allows us to set the font to suit the language
                    }
                }
                else //Put the value into an attribute. The license image goes through this path.
                {
                    target.SetAttribute(
                        valueAttribute,
                        source.GetBestAlternativeString(languagePreferences)
                    );
                    if (source.Empty)
                    {
                        //if the license image is empty, make sure we don't have some alternative text
                        //about the image being missing or slow to load
                        target.SetAttribute("alt", "");
                        //over in javascript land, @alt will get set appropriately when the image url is not empty.
                    }
                }
            }
        }

        /// <summary>
        /// Get the license from the metadata and save it.
        /// </summary>
        private static void UpdateBookLicenseIcon(Metadata metadata, string bookFolderPath)
        {
            var licenseImage = (metadata.License as ILicenseWithImage)?.GetImage();
            var imagePath = bookFolderPath.CombineForPath("license.png");
            // Don't try to overwrite the license image for a template book.  (See BL-3284.)
            if (
                RobustFile.Exists(imagePath)
                && BloomFileLocator.IsInstalledFileOrDirectory(imagePath)
            )
                return;
            ImageUtils.SaveOrDeletePngImageToPath(licenseImage, imagePath);
        }

        public static void RemoveLicense(BookStorage storage)
        {
            storage.Dom.RemoveBookSetting("licenseUrl");
            storage.Dom.RemoveBookSetting("licenseDescription");
            storage.Dom.RemoveBookSetting("licenseNotes");
        }

        public static IEnumerable<string> SettingsToCheckForDefaultCopyright =>
            new[] { "copyright", "licenseUrl", "licenseNotes" };

        private static bool ShouldSetToDefaultCopyrightAndLicense(HtmlDom dom)
        {
            //Enhance: this logic is perhaps overly restrictive?
            foreach (var setting in SettingsToCheckForDefaultCopyright)
            {
                if (!dom.GetBookSetting(setting).Empty)
                    return false;
            }
            return true;
        }

        public static void LogMetdata(HtmlDom dom)
        {
            Logger.WriteEvent("LicenseUrl: " + dom.GetBookSetting("licenseUrl"));
            Logger.WriteEvent("LicenseNotes: " + dom.GetBookSetting("licenseNotes"));
            Logger.WriteEvent("");
        }

        public static bool IsDerivative(Metadata originalMetadata)
        {
            // Checking for a license which is not a NullLicense is not sufficient because that indicates the user has selected
            // "Contact the copyright holder..." for the license. But in order to do so, he must have entered a copyright.
            return !String.IsNullOrEmpty(originalMetadata.CopyrightNotice)
                || !(originalMetadata.License is NullLicense);
        }

        internal static string GetOriginalCopyrightAndLicenseNotice(BookData bookData, HtmlDom dom)
        {
            var originalMetadata = GetOriginalMetadata(dom, bookData);

            // As of BL-7898, we are using the existence of an original copyright/license to determine if we are working with a derivative.
            if (!IsDerivative(originalMetadata))
                return null;

            // The originalTitle strategy used here is not ideal. We would prefer to have a placeholder specifically for it
            // in both EditTab.FrontMatter.OriginalCopyrightSentence and EditTab.FrontMatter.OriginalHadNoCopyrightSentence.
            // But we don't want to require a new set of translations if we can avoid it.
            var encodedTitle = dom.GetBookSetting("originalTitle")?.GetExactAlternative("*");
            var originalTitle = HttpUtility.HtmlDecode(encodedTitle);

            var titleCitation =
                "<cite data-book=\"originalTitle\""
                + (string.IsNullOrEmpty(originalTitle) ? " class=\"missingOriginalTitle\">" : ">")
                + originalTitle
                + "</cite>";

            var languagePriorityIdsNotLang1 = bookData.GetLanguagePrioritiesForLocalizedTextOnPage(
                false
            );
            var originalLicenseSentence = GetOriginalLicenseSentence(
                languagePriorityIdsNotLang1,
                originalMetadata.License,
                out string licenseOnly
            );

            var rawCopyright = originalMetadata.CopyrightNotice;
            // If we have all the pieces available, we want to use this one.
            // At the very least it's easier to localize into the format the language wants to use.
            var fullFormatString = LocalizationManager.GetString(
                "EditTab.FrontMatter.FullOriginalCopyrightLicenseSentence",
                "This book is an adaptation of the original, {0}, {1}. Licensed under {2}.",
                "On the Credits page of a book being translated, Bloom shows the original copyright. {0} is original title, {1} is original copyright, and {2} is license information.",
                languagePriorityIdsNotLang1,
                out string langUsed
            );
            // The last condition here (langUsed ==...) is meant to detect if the string has been translated
            // into the current language or not.
            if (
                !string.IsNullOrEmpty(originalTitle)
                && !string.IsNullOrEmpty(rawCopyright)
                && !string.IsNullOrEmpty(licenseOnly)
                && langUsed == languagePriorityIdsNotLang1.First()
            )
            {
                return string.Format(fullFormatString, titleCitation, rawCopyright, licenseOnly);
            }
            var copyrightNotice = GetOriginalCopyrightSentence(
                    languagePriorityIdsNotLang1,
                    rawCopyright,
                    titleCitation
                )
                .Trim();

            return (copyrightNotice + " " + originalLicenseSentence).Trim();
        }

        private static string GetOriginalCopyrightSentence(
            IEnumerable<string> languagePriorityIds,
            string rawOriginalCopyright,
            string titleCitation
        )
        {
            if (string.IsNullOrWhiteSpace(rawOriginalCopyright))
            {
                var noCopyrightSentence = LocalizationManager.GetString(
                    "EditTab.FrontMatter.OriginalHadNoCopyrightSentence",
                    "This book is an adaptation of the original without a copyright notice.",
                    "On the Credits page of a book being translated, Bloom shows this if the original book did not have a copyright notice.",
                    languagePriorityIds,
                    out _
                );

                noCopyrightSentence =
                    noCopyrightSentence.Substring(0, noCopyrightSentence.Length - 1)
                    + ", "
                    + titleCitation
                    + ".";

                return noCopyrightSentence;
            }
            var originalCopyrightSentence = LocalizationManager.GetString(
                "EditTab.FrontMatter.OriginalCopyrightSentence",
                "This book is an adaptation of the original, {0}.",
                "On the Credits page of a book being translated, Bloom shows the original copyright. Put {0} in the translation where the copyright notice should go. For example in English, 'This book is an adaptation of the original, {0}.' comes out like 'This book is an adaptation of the original, Copyright 2011 SIL'.",
                languagePriorityIds,
                out _
            );
            return string.Format(
                originalCopyrightSentence,
                titleCitation + ", " + rawOriginalCopyright
            );
        }

        // This will USUALLY return something like "Licensed under {some license}.", but corner cases include empty string and
        // whatever the user puts in as CustomLicense text.
        // The out var is for use in the full format string only (FullOriginalCopyrightLicenseSentence).
        public static string GetOriginalLicenseSentence(
            IEnumerable<string> languagePriorityIds,
            LicenseInfo licenseInfo,
            out string licenseOnly
        )
        {
            licenseOnly = licenseInfo.GetMinimalFormForCredits(languagePriorityIds, out _);
            if (licenseInfo is CustomLicenseInfo)
            {
                // I can imagine being more fancy... something like "Licensed under custom license:", and get localizations
                // for that... but sheesh, these are even now very rare in Bloom-land and should become more rare.
                // So for now, let's just print the custom license contents.
                return licenseOnly;
            }
            licenseOnly = licenseOnly.TrimEnd('.'); // in case we had notes which also had a period.
            var licenseSentenceTemplate = LocalizationManager.GetString(
                "EditTab.FrontMatter.OriginalLicenseSentence",
                "Licensed under {0}.",
                "On the Credits page of a book being translated, Bloom puts texts like 'Licensed under CC-BY', so that we have a record of what the license was for the original book. Put {0} in the translation, where the license should go in the sentence.",
                languagePriorityIds,
                out _
            );
            return string.IsNullOrWhiteSpace(licenseOnly)
                ? ""
                : string.Format(licenseSentenceTemplate, licenseOnly);
        }
    }
}
