using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml;
using Bloom.SafeXml;
using L10NSharp;
using SIL.Reporting;
using SIL.WritingSystems;
using SIL.Xml;

namespace Bloom.Book
{
    /// <summary>
    /// Most editable elements in Bloom are multilingual. They have a wrapping <div> and then inner divs which are visible or not,
    /// depending on various settings. This class manages creating those inner divs, and marking them with classes that turn on
    /// visibility or move the element around the page, depending on the stylesheet in use.
    ///
    /// Also, page <div/>s are marked with one of these classes: bloom-monolingual, bloom-bilingual, and bloom-trilingual
    ///
    ///	<div class="bloom-translationGroup" data-default-langauges='V, N1'>
    ///		<div class="bloom-editable" contenteditable="true" lang="en">The Mother said...</div>
    ///		<div class="bloom-editable" contenteditable="true" lang="tpi">Mama i tok:</div>
    ///		<div class="bloom-editable contenteditable="true" lang="xyz">abada fakwan</div>
    ///	</div>

    /// </summary>
    public static class TranslationGroupManager
    {
        /// <summary>
        /// For each group of editable elements in the div which have lang attributes  (normally, a .bloom-translationGroup div),
        /// make sure we have child elements with the lang codes we need (most often, a .bloom-editable in the vernacular
        /// is added).
        /// Also enable/disable editing as warranted (e.g. in shell mode or not)
        /// </summary>
        public static void PrepareElementsInPageOrDocument(
            SafeXmlNode pageOrDocumentNode,
            BookData bookData
        )
        {
            GenerateEditableDivsWithPreTranslatedContent(pageOrDocumentNode);

            // Earlier (pre-6.0) versions of Bloom were selective about creating editable divs for
            // languages that were not being used for this field, based on data-default-languages.
            // But the logic for this was already complex in 5.6, and had caused bugs, and the Appearance
            // system made it almost impossible, since the visibility of the L3 bloom-editable
            // can be controlled by various theme files as well as the book-settings dialog
            // (and the logic differs depending on whether we're in legacy theme).
            // Rather than add a lot more complexity, we're going to just create bloom-editables
            // for all the languages in use in the book at all. It is somewhat rare even to have
            // L3, and even when we do, the overhead is small (and invisible stuff is removed from
            // publications).
            foreach (var code in bookData.GetAllBookLanguageCodes(includeMetaData2: true))
                PrepareElementsOnPageOneLanguage(pageOrDocumentNode, code);

            FixGroupStyleSettings(pageOrDocumentNode);
        }

        /// <summary>
        /// Normally, the connection between bloom-translationGroups and the dataDiv is that each bloom-editable child
        /// (which has an @lang) pulls the corresponding string from the dataDiv. This happens in BookData.
        ///
        /// That works except in the case of xmatter which a) start empty and b) only normally get filled with
        /// .bloom-editable's for the current languages. Then, when bloom would normally show a source bubble listing
        /// the string in other languages, well there's nothing to show (the bubble can't pull from dataDiv).
        /// So our solution here is to pre-pack the translationGroup with bloom-editable's for each of the languages
        /// in the data-div.
        /// The original (an possibly only) instance of this is with book titles. See bl-1210.
        /// </summary>
        public static void PrepareDataBookTranslationGroups(
            SafeXmlNode pageOrDocumentNode,
            IEnumerable<string> languageCodes
        )
        {
            //At first, I set out to select all translationGroups that have child .bloomEditables that have data-book attributes
            //however this has implications on other fields, noticeably the acknowledgments. So in order to get this fixed
            //and not open another can of worms, I've reduce the scope of this
            //fix to just the bookTitle, so I'm going with findOnlyBookTitleFields for now
            //var findAllDataBookFields = "descendant-or-self::*[contains(@class,'bloom-translationGroup') and descendant::div[@data-book and contains(@class,'bloom-editable')]]";
            // 7 years later, we have a bug due to limiting this operation to only bookTitle. For the moment, I'm only expanding
            // the search to the one value ("smallCoverCredits") causing a problem.  See BL-11869.
            var findRestrictedFields =
                "descendant-or-self::*[contains(@class,'bloom-translationGroup') and descendant::div[(@data-book='bookTitle' or @data-book='smallCoverCredits') and contains(@class,'bloom-editable')]]";
            foreach (
                SafeXmlElement groupElement in pageOrDocumentNode.SafeSelectNodes(
                    findRestrictedFields
                )
            )
            {
                foreach (var lang in languageCodes)
                {
                    MakeElementWithLanguageForOneGroup(groupElement, lang);
                }
            }
        }

        /// <summary>
        /// Returns the sequence of bloom-editable divs that would normally be created as the content of an empty bloom-translationGroup,
        /// given the current collection and book settings, with the classes that would normally be set on them prior to editing to make the right ones visible etc.
        /// </summary>
        public static string GetDefaultTranslationGroupContent(Book currentBook)
        {
            // Borrow the book's XMLDocument so that we can create elements to work with.
            // We don't add them to the document, so it is never changed.
            var ownerDocument = currentBook.RawDom.OwnerDocument;
            if (ownerDocument == null) // the OwnerDocument child can be null if currentBook.RawDom is a document itself
            {
                if (currentBook.RawDom is SafeXmlDocument)
                    ownerDocument = currentBook.RawDom;
                else
                {
                    // Unexpected. Since the result here will be appended to the image container, just return an empty string in the failure case.
                    // Note, in 6/2024, I'm just reinstating code which was recently removed. I'm not claiming this is the best way to handle this.
                    return "";
                }
            }

            // We want to use the usual routines that insert the required bloom-editables and classes into existing translation groups.
            // To make this work we make a temporary translation group
            // Since one of the routines expects the TG to be a descendant of the element passed to it, we make another layer of temporary wrapper around the translation group as well
            var containerElement = ownerDocument.CreateElement("div");
            containerElement.SetAttribute("class", "bloom-translationGroup");

            var wrapper = ownerDocument.CreateElement("div");
            wrapper.AppendChild(containerElement);

            PrepareElementsInPageOrDocument(containerElement, currentBook.BookData);

            UpdateContentLanguageClasses(
                wrapper,
                currentBook.BookData,
                currentBook.BookInfo.AppearanceSettings,
                currentBook.Language1Tag,
                currentBook.Language2Tag,
                currentBook.Language3Tag
            );

            return containerElement.InnerXml;
        }

        private static void GenerateEditableDivsWithPreTranslatedContent(SafeXmlNode elementOrDom)
        {
            var ownerDoc = elementOrDom.OwnerDocument;
            foreach (
                SafeXmlElement editableDiv in elementOrDom.SafeSelectNodes(
                    ".//*[contains(@class,'bloom-editable') and @data-generate-translations and @data-i18n]"
                )
            )
            {
                var englishText = editableDiv.InnerText;
                var l10nId = editableDiv.GetAttribute("data-i18n");
                if (String.IsNullOrWhiteSpace(l10nId))
                    continue;

                foreach (var uiLanguage in LocalizationManager.GetAvailableLocalizedLanguages())
                {
                    var translation = LocalizationManager.GetDynamicStringOrEnglish(
                        "Bloom",
                        l10nId,
                        englishText,
                        null,
                        uiLanguage
                    );
                    if (translation == englishText)
                        continue;
                    var newEditableDiv = ownerDoc.CreateElement("div");
                    newEditableDiv.SetAttribute("class", "bloom-editable");
                    newEditableDiv.SetAttribute("lang", IetfLanguageTag.GetGeneralCode(uiLanguage));
                    newEditableDiv.InnerText = translation;
                    editableDiv.ParentNode.AppendChild(newEditableDiv);
                }

                editableDiv.RemoveAttribute("data-generate-translations");
                editableDiv.RemoveAttribute("data-i18n");
            }
        }

        /// <summary>
        /// This is used when a book is first created from a source; without it, if the shell maker left the book as trilingual when working on it,
        /// then every time someone created a new book based on it, it too would be trilingual.
        /// </summary>
        /// <remarks>
        /// This method explicitly used the CollectionSettings languages in creating a new book.
        /// </remarks>
        public static void SetInitialMultilingualSetting(
            BookData bookData,
            int oneTwoOrThreeContentLanguages
        )
        {
            //var multilingualClass =  new string[]{"bloom-monolingual", "bloom-bilingual","bloom-trilingual"}[oneTwoOrThreeContentLanguages-1];

            if (oneTwoOrThreeContentLanguages < 3)
                bookData.RemoveAllForms("contentLanguage3");
            if (oneTwoOrThreeContentLanguages < 2)
                bookData.RemoveAllForms("contentLanguage2");

            var language1 = bookData.CollectionSettings.Language1;
            bookData.Set("contentLanguage1", XmlString.FromUnencoded(language1.Tag), false);
            bookData.Set(
                "contentLanguage1Rtl",
                XmlString.FromUnencoded(language1.IsRightToLeft.ToString()),
                false
            );
            if (oneTwoOrThreeContentLanguages > 1)
            {
                var language2 = bookData.CollectionSettings.Language2;
                bookData.Set("contentLanguage2", XmlString.FromUnencoded(language2.Tag), false);
                bookData.Set(
                    "contentLanguage2Rtl",
                    XmlString.FromUnencoded(language2.IsRightToLeft.ToString()),
                    false
                );
            }
            var language3 = bookData.CollectionSettings.Language3;
            if (oneTwoOrThreeContentLanguages > 2 && !String.IsNullOrEmpty(language3.Tag))
            {
                bookData.Set("contentLanguage3", XmlString.FromUnencoded(language3.Tag), false);
                bookData.Set(
                    "contentLanguage3Rtl",
                    XmlString.FromUnencoded(language3.IsRightToLeft.ToString()),
                    false
                );
            }
        }

        /// <summary>
        /// We stick various classes on editable things to control order and visibility
        /// </summary>
        public static void UpdateContentLanguageClasses(
            SafeXmlNode elementOrDom,
            BookData bookData,
            AppearanceSettings settings,
            string language1Tag,
            string language2Tag,
            string language3Tag
        )
        {
            var multilingualClass = "bloom-monolingual";
            var contentLanguages = new Dictionary<string, string>();
            contentLanguages.Add(language1Tag, "bloom-content1");

            if (!String.IsNullOrEmpty(language2Tag) && language1Tag != language2Tag)
            {
                multilingualClass = "bloom-bilingual";
                contentLanguages.Add(language2Tag, "bloom-content2");
            }
            if (
                !String.IsNullOrEmpty(language3Tag)
                && language1Tag != language3Tag
                && language2Tag != language3Tag
            )
            {
                multilingualClass = "bloom-trilingual";
                contentLanguages.Add(language3Tag, "bloom-content3");
                Debug.Assert(
                    !String.IsNullOrEmpty(language2Tag),
                    "shouldn't have a content3 lang with no content2 lang"
                );
            }

            //Stick a class in the page div telling the stylesheet how many languages we are displaying (only makes sense for content pages, in Jan 2012).
            foreach (
                SafeXmlElement pageDiv in elementOrDom.SafeSelectNodes(
                    "descendant-or-self::div[contains(@class,'bloom-page') and not(contains(@class,'bloom-frontMatter')) and not(contains(@class,'bloom-backMatter'))]"
                )
            )
            {
                HtmlDom.RemoveClassesBeginningWith(pageDiv, "bloom-monolingual");
                HtmlDom.RemoveClassesBeginningWith(pageDiv, "bloom-bilingual");
                HtmlDom.RemoveClassesBeginningWith(pageDiv, "bloom-trilingual");
                pageDiv.AddClass(multilingualClass);
            }

            // This is the "code" part of the visibility system: https://goo.gl/EgnSJo
            foreach (SafeXmlElement group in GetTranslationGroups(elementOrDom))
            {
                // This controls visibility for fields not yet controlled by the Appearance system
                // (or in legacy mode).
                var defLangsAttr = group.GetAttribute("data-default-languages");
                var dataDefaultLanguages = defLangsAttr.Split(
                    new char[] { ',', ' ' },
                    StringSplitOptions.RemoveEmptyEntries
                );

                //nb: we don't necessarily care that a div is editable or not
                UpdateGeneratedClassesOnChildren(
                    group.SafeSelectNodes(".//textarea | .//div"),
                    bookData,
                    settings,
                    language2Tag,
                    language3Tag,
                    contentLanguages,
                    dataDefaultLanguages
                );
            }

            // Also correct bloom-contentX fields in the bloomDataDiv, which are not listed under translation groups
            foreach (
                SafeXmlElement coverImageDescription in elementOrDom.SafeSelectNodes(
                    ".//*[@id='bloomDataDiv']/*[@data-book='coverImageDescription']"
                )
            )
            {
                string[] dataDefaultLanguages = new string[] { " auto" }; // bloomDataDiv contents don't have dataDefaultLanguages on them, so just go with "auto"
                //nb: we don't necessarily care that a div is editable or not
                UpdateGeneratedClassesOnChildren(
                    coverImageDescription.SafeSelectNodes(".//textarea | .//div"),
                    bookData,
                    settings,
                    language2Tag,
                    language3Tag,
                    contentLanguages,
                    dataDefaultLanguages
                );
            }
        }

        public static bool IsPageAffectedByLanguageMenu(SafeXmlElement page, bool legacy)
        {
            var groups = GetTranslationGroups(page);
            foreach (var g in groups)
            {
                var defLangs = g.GetAttribute("data-default-languages");

                // missing attribute or empty is treated as "auto", and this menu definitely affects those.
                if (string.IsNullOrEmpty(defLangs) || defLangs.StartsWith("auto"))
                    return true;

                var visVariable = g.GetAttribute("data-visibility-variable");
                // If the group is controlled by the Appearance system, it's not affected by the language menu.
                if (!String.IsNullOrEmpty(visVariable) && !legacy)
                    continue;

                // The menu can change the primary Vernacular language, so it's applicable to any block that shows that.
                if (defLangs.Split(',').Contains("V"))
                    return true;
            }
            return false;
        }

        private static void UpdateGeneratedClassesOnChildren(
            SafeXmlNode[] editables,
            BookData bookData,
            AppearanceSettings settings,
            string language2Tag,
            string language3Tag,
            Dictionary<string, string> contentLanguages,
            string[] dataDefaultLanguages
        )
        {
            // Must do this first, content class generation depends on ALL elements in the group
            // having the visibility class set.
            foreach (SafeXmlElement e in editables)
            {
                UpdateVisibilityClassOnElement(
                    e,
                    contentLanguages,
                    bookData,
                    settings,
                    language2Tag,
                    language3Tag,
                    dataDefaultLanguages
                );
            }

            foreach (SafeXmlElement e in editables)
            {
                UpdateContentLanguageClassesOnElement(
                    e,
                    contentLanguages,
                    bookData,
                    settings,
                    language2Tag,
                    language3Tag,
                    dataDefaultLanguages
                );
            }
        }

        public static SafeXmlElement[] GetTranslationGroups(
            SafeXmlNode elementOrDom,
            bool omitBoxHeaders = true
        )
        {
            var groups = elementOrDom
                .SafeSelectNodes(".//div[contains(@class, 'bloom-translationGroup')]")
                .Cast<SafeXmlElement>();
            if (omitBoxHeaders)
            {
                groups = groups.Where(g => !g.GetAttribute("class").Contains("box-header-off"));
            }
            return groups.ToArray();
        }

        private static void UpdateVisibilityClassOnElement(
            SafeXmlElement editable,
            Dictionary<string, string> contentLanguages,
            BookData bookData,
            AppearanceSettings settings,
            string contentLanguageTag2,
            string contentLanguageTag3,
            string[] dataDefaultLanguages
        )
        {
            HtmlDom.RemoveClassesBeginningWith(editable, "bloom-visibility-code");
            var lang = editable.GetAttribute("lang");
            if (
                ShouldShowEditable(
                    editable,
                    settings,
                    lang,
                    dataDefaultLanguages,
                    contentLanguageTag2,
                    contentLanguageTag3,
                    bookData
                )
            )
            {
                // Managing the presence of this class is how we control visibility of the various language blocks.
                // It would be more elegant in some ways in the appearance system to generate a CSS variable
                // like --cover-title-L2-show and then have basePage.css apply that where appropriate, but
                // it becomes almost impossible for C# code to know what elements are actually visible, and that
                // is important in all kinds of ways. For example, we only export visible stuff (or sometimes stuff
                // the user could make visible) to most publications. Source bubbles only show what is NOT
                // already visible in the page content. And so on. So we decided to stick with a variable that
                // controls visibility in a straightforward, unconditional way, which means C# code can just look
                // to see whether the class is there or not.
                editable.AddClass("bloom-visibility-code-on");
            }
        }

        private static void UpdateContentLanguageClassesOnElement(
            SafeXmlElement editable,
            Dictionary<string, string> contentLanguages,
            BookData bookData,
            AppearanceSettings settings,
            string contentLanguageTag2,
            string contentLanguageTag3,
            string[] dataDefaultLanguages
        )
        {
            HtmlDom.RemoveClassesBeginningWith(editable, "bloom-content");
            var lang = editable.GetAttribute("lang");

            //These bloom-content* classes are used by some stylesheet rules, primarily to boost the font-size of some languages.
            //Enhance: this is too complex; the semantics of these overlap with each other and with bloom-visibility-code-on, and with data-language-order.
            //It would be better to have non-overlapping things; 1 for order, 1 for visibility, one for the lang's role in this collection.
            string orderClass;
            if (contentLanguages.TryGetValue(lang, out orderClass))
            {
                editable.AddClass(orderClass); //bloom-content1, bloom-content2, bloom-content3
            }

            //Enhance: it's even more likely that we can get rid of these by replacing them with bloom-content2, bloom-content3
            if (lang == bookData.MetadataLanguage1Tag)
            {
                editable.AddClass("bloom-contentNational1");
            }

            // It's not clear that this class should be applied to blocks where lang == bookData.Language3Tag.
            // I (JohnT) added lang == bookData.MetadataLanguage2Tag while dealing with BL-10893
            // but am reluctant to remove the old code as something might depend on it. I believe it is (nearly?)
            // always true that if we have Language3Tag at all, it will be equal to MetadataLanguage2Tag,
            // so at least for now it probably makes no difference. In our next major reworking of language codes,
            // hopefully we can make this distinction clearer and remove Language3Tag here.
            if (lang == bookData.Language3Tag || lang == bookData.MetadataLanguage2Tag)
            {
                editable.AddClass("bloom-contentNational2");
            }

            // At the time of this writing (6.0) this class only affects cover page titles.
            AddThemeVisibleOrderClass(
                editable,
                settings.UsingLegacy,
                lang,
                bookData.CollectionSettings.Language1Tag,
                bookData.CollectionSettings.Language2Tag,
                bookData.CollectionSettings.Language3Tag
            );

            UpdateRightToLeftSetting(bookData, editable, lang);
        }

        static bool IsVisible(SafeXmlElement editable)
        {
            return editable.HasAttribute("class")
                && editable.GetAttribute("class").Contains("bloom-visibility-code-on");
        }

        /// <summary>
        /// If this field's visibility is controlled by the appearance system (introduced in 6.0, though as of 6.0 only applying
        /// to the cover title), add a class indicating its order within the collection languages that are visible for this field.
        /// Unlike in legacy theme (or pre-6.0), the order, like the visibility choice, does not depend on what languages are
        /// chosen to appear in content pages.
        /// Must be called after setting bloom-visibility-code-on for all appropriate fields in the same group.
        /// </summary>
        /// <remarks>We could obtain lang from the element, but the one real caller already has it, and passing
        /// it separately makes testing easier.</remarks>
        public static void AddThemeVisibleOrderClass(
            SafeXmlElement editable,
            bool legacy,
            string lang,
            string l1,
            string l2,
            string l3
        )
        {
            if (legacy)
                return; // not in theme mode
            var visibilityVariable = (editable.ParentNode as SafeXmlElement).GetAttribute(
                "data-visibility-variable"
            );
            if (string.IsNullOrEmpty(visibilityVariable))
                return; // visibility of this element is not controlled by the Appearance system
            if (!IsVisible(editable))
                return; // the element is not visible at all, don't mark it.
            if (lang == l1)
            {
                // L1 comes first, so if it's visible, it gets class 1.
                editable.AddClass("bloom-contentFirst");
                return;
            }
            var visibleSiblingLangs = (editable.ParentNode).ChildNodes
                .Where(ed => ed is SafeXmlElement)
                .Cast<SafeXmlElement>()
                .Where(
                    ed =>
                        ed != editable
                        && ed.GetAttribute("class").Contains("bloom-visibility-code-on")
                )
                .Select(e => e.GetAttribute("lang"))
                .ToList();
            if (lang == l2)
            {
                if (visibleSiblingLangs.Contains(l1))
                    editable.AddClass("bloom-contentSecond");
                else
                    editable.AddClass("bloom-contentFirst");
                return;
            }
            if (lang == l3)
            {
                if (visibleSiblingLangs.Contains(l1) && visibleSiblingLangs.Contains(l2))
                    editable.AddClass("bloom-contentThird");
                else if (visibleSiblingLangs.Contains(l1) || visibleSiblingLangs.Contains(l2))
                    editable.AddClass("bloom-contentSecond");
                else
                    editable.AddClass("bloom-contentFirst");
            }
        }

        /// <summary>
        /// The master routine that determines whether a given editable block should be visible.
        /// Most of the arguments are unchanged from the list needed by the pre-existing ShouldNormallyShowEditable.
        /// I think some of them are probably redundant and could be recomputed from the element and the bookData,
        /// but it may be slightly more efficient to do so just once for the group.
        /// </summary>
        static bool ShouldShowEditable(
            SafeXmlElement e,
            AppearanceSettings settings,
            string lang,
            string[] dataDefaultLanguages,
            string contentLanguageTag2,
            string contentLanguageTag3, // these are affected by the multilingual settings for this book
            BookData bookData
        )
        {
            if (settings.UsingLegacy)
            {
                // ignore appearance settings in legacy mode
                return ShouldNormallyShowEditable(
                    lang,
                    dataDefaultLanguages,
                    contentLanguageTag2,
                    contentLanguageTag3,
                    bookData
                );
            }

            var visibilityVariable = (e.ParentNode as SafeXmlElement).GetAttribute(
                "data-visibility-variable"
            );
            if (string.IsNullOrEmpty(visibilityVariable))
            {
                // visibility of this element is not controlled by the Appearance system
                return ShouldNormallyShowEditable(
                    lang,
                    dataDefaultLanguages,
                    contentLanguageTag2,
                    contentLanguageTag3,
                    bookData
                );
            }

            string whichLang;
            if (lang == bookData.CollectionSettings.Language1Tag)
            {
                whichLang = "L1";
            }
            // It's surprising that e.g. conver-title-L2-show controls whether we show MetadataLanguage1Tag
            // rather than Language2Tag. But we haven't fully surfaced the distinction in the UI. A typical
            // user thinks of the book as having the three (or two, or one) languages defined in the
            // collection, and that's what MetadataLanguage{1,2}Tag give. Language2Tag is only defined
            // if the book has two or more languages "turned on" for content pages, and is not even
            // necessarily the second language in the collection. So, for now, we'll keep using the
            // MetadataLanguage1Tag and MetadataLanguage2Tag values here. This is consistent with
            // the typical use of N1 and N2 (rather than L2 and L3) in data-default-languages.
            else if (lang == bookData.CollectionSettings.Language2Tag)
            {
                whichLang = "L2";
            }
            else if (lang == bookData.CollectionSettings.Language3Tag)
            {
                whichLang = "L3";
            }
            else
            {
                return false;
            }

            var varName = visibilityVariable.Replace("LN", whichLang);
            // If the variable is defined, that confirms that the appearance system is controlling
            // the visibility of this element, so simply return the result it gives.
            if (settings.TryGetBooleanPropertyValue(varName, out bool result))
                return result;
            // If we don't have a variable controlling this language (e.g., cover-title-L1-show
            // does not exist), use the default behavior.
            return ShouldNormallyShowEditable(
                lang,
                dataDefaultLanguages,
                contentLanguageTag2,
                contentLanguageTag3,
                bookData
            );
        }

        private static void UpdateRightToLeftSetting(
            BookData bookData,
            SafeXmlElement e,
            string lang
        )
        {
            HtmlDom.RemoveRtlDir(e);
            if (
                (lang == bookData.Language1Tag && bookData.Language1.IsRightToLeft)
                || (lang == bookData.Language2Tag && bookData.Language2.IsRightToLeft)
                || (lang == bookData.Language3Tag && bookData.Language3.IsRightToLeft)
                || (
                    lang == bookData.MetadataLanguage1Tag
                    && bookData.MetadataLanguage1.IsRightToLeft
                )
            )
            {
                HtmlDom.AddRtlDir(e);
            }
        }

        /// <summary>
        /// Here, "normally" means unless the field's visibility is now controlled by the Appearance system.
        /// </summary>
        internal static bool ShouldNormallyShowEditable(
            string lang,
            string[] dataDefaultLanguages,
            string contentLanguageTag2,
            string contentLanguageTag3, // these are effected by the multilingual settings for this book
            BookData bookData
        ) // use to get the collection's current N1 and N2 in xmatter or other template pages that specify default languages
        {
            if (string.IsNullOrEmpty(lang))
                return false; // if by any bizarre chance we have a block with an empty language code, we don't want to show it!
            // Note: There is code in bloom-player that is modeled after this code.
            //       If this function changes, you should check in bloom-player's bloom-player-core.tsx file, function shouldNormallyShowEditable().
            //       It may benefit from being updated too.
            if (
                dataDefaultLanguages == null
                || dataDefaultLanguages.Length == 0
                || String.IsNullOrWhiteSpace(dataDefaultLanguages[0])
                || dataDefaultLanguages[0].Equals(
                    "auto",
                    StringComparison.InvariantCultureIgnoreCase
                )
            )
            {
                return lang == bookData.Language1.Tag
                    || lang == contentLanguageTag2
                    || lang == contentLanguageTag3;
            }
            else
            {
                // Note there are (perhaps unfortunately) two different labeling systems, but they have a more-or-less 1-to-1 correspondence:
                // The V/N1/N2 system feels natural in vernacular book contexts
                // The L1/L2/L3 system is more natural in source book contexts.
                // But, the new model makes L2 and L3 mean the second and third checked languages, while N1 and N2 are the
                // second and third metadata languages, currently locked to the collection L2 and L3.
                // V and L1 both mean the first checked language.
                // Changes here should result in consistent ones in Book.IsLanguageWanted and BookData.GatherDataItemsFromXElement
                // and RuntimeInformationInjector.AddUIDictionaryToDom and I18ApiHandleI18nRequest
                return (lang == bookData.Language1.Tag && dataDefaultLanguages.Contains("V"))
                    || (lang == bookData.Language1.Tag && dataDefaultLanguages.Contains("L1"))
                    || (
                        lang == bookData.MetadataLanguage1Tag && dataDefaultLanguages.Contains("N1")
                    )
                    || (lang == bookData.Language2Tag && dataDefaultLanguages.Contains("L2"))
                    || (
                        lang == bookData.MetadataLanguage2Tag && dataDefaultLanguages.Contains("N2")
                    )
                    || (lang == bookData.Language3Tag && dataDefaultLanguages.Contains("L3"))
                    || dataDefaultLanguages.Contains(lang); // a literal language id, e.g. "en" (used by template starter)
            }
        }

        private static int GetOrderOfThisLanguageInTheTextBlock(
            string editableTag,
            string vernacularTag,
            string contentLanguageTag2,
            string contentLanguageTag3
        )
        {
            if (editableTag == vernacularTag)
                return 1;
            if (editableTag == contentLanguageTag2)
                return 2;
            if (editableTag == contentLanguageTag3)
                return 3;
            return -1;
        }

        private static void PrepareElementsOnPageOneLanguage(SafeXmlNode pageDiv, string langTag)
        {
            foreach (
                SafeXmlElement groupElement in pageDiv.SafeSelectNodes(
                    "descendant-or-self::*[contains(@class,'bloom-translationGroup')]"
                )
            )
            {
                MakeElementWithLanguageForOneGroup(groupElement, langTag);

                //remove any elements in the translationgroup which don't have a lang (but ignore any label elements, which we're using for annotating groups)
                foreach (
                    SafeXmlElement elementWithoutLanguage in groupElement.SafeSelectNodes(
                        "textarea[not(@lang)] | div[not(@lang) and not(self::label)]"
                    )
                )
                {
                    elementWithoutLanguage.ParentNode.RemoveChild(elementWithoutLanguage);
                }
            }

            //any editable areas which still don't have a language, set them to the vernacular (this is used for simple templates (non-shell pages))
            foreach (
                SafeXmlElement element in pageDiv.SafeSelectNodes( //NB: the jscript will take items with bloom-editable and set the contentEdtable to true.
                    "descendant-or-self::textarea[not(@lang)] | descendant-or-self::*[(contains(@class, 'bloom-editable') or @contentEditable='true'  or @contenteditable='true') and not(@lang)]"
                )
            )
            {
                element.SetAttribute("lang", langTag);
            }

            foreach (
                SafeXmlElement e in pageDiv.SafeSelectNodes(
                    "descendant-or-self::*[starts-with(text(),'{')]"
                )
            )
            {
                foreach (var node in e.ChildNodes)
                {
                    var t = node as SafeXmlText;
                    if (t != null && t.Value.StartsWith("{"))
                        t.Value = "";
                    //otherwise html tidy will throw away spans (at least) that are empty, so we never get a chance to fill in the values.
                }
            }
        }

        /// <summary>
        /// If the group element contains more than one child div in the given language, remove or
        /// merge the duplicates.
        /// </summary>
        /// <remarks>
        /// We've had at least one user end up with duplicate vernacular divs in a shell book.  (She
        /// was using Bloom 4.3 to translate a shell book created with Bloom 3.7.)  I haven't been
        /// able to reproduce the effect or isolate the cause, but this fixes things.
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-6923.
        /// </remarks>
        internal static void FixDuplicateLanguageDivs(SafeXmlElement groupElement, string langTag)
        {
            var list = groupElement.SafeSelectNodes("./div[@lang='" + langTag + "']");
            if (list.Length > 1)
            {
                var count = list.Length;
                foreach (var div in list)
                {
                    var innerText = div.InnerText.Trim();
                    if (String.IsNullOrEmpty(innerText))
                    {
                        Logger.WriteEvent(
                            $"An empty duplicate div for {langTag} has been removed from a translation group."
                        );
                        groupElement.RemoveChild(div);
                        --count;
                        if (count == 1)
                            break;
                    }
                }
                if (count > 1)
                {
                    Logger.WriteEvent(
                        $"Duplicate divs for {langTag} have been merged in a translation group."
                    );
                    list = groupElement.SafeSelectNodes("./div[@lang='" + langTag + "']");
                    var first = list[0];
                    for (int i = 1; i < list.Length; ++i)
                    {
                        var newline = groupElement.OwnerDocument.CreateTextNode(
                            Environment.NewLine
                        );
                        first.AppendChild(newline);
                        foreach (SafeXmlNode node in list[i].ChildNodes)
                            first.AppendChild(node);
                        groupElement.RemoveChild(list[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Shift any translationGroup level style setting to child editable divs that lack a style.
        /// This is motivated by the fact HTML/CSS underlining cannot be turned off in child nodes.
        /// So if underlining is enabled for Normal, everything everywhere would be underlined
        /// regardless of style or immediate character formatting.  If a style sets underlining
        /// on, then immediate character formatting cannot turn it off anywhere in a text box that
        /// uses that style.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-6282.
        /// </summary>
        private static void FixGroupStyleSettings(SafeXmlNode pageDiv)
        {
            foreach (
                SafeXmlElement groupElement in pageDiv.SafeSelectNodes(
                    "descendant-or-self::*[contains(@class,'bloom-translationGroup')]"
                )
            )
            {
                var groupStyle = HtmlDom.GetStyle(groupElement);
                if (String.IsNullOrEmpty(groupStyle))
                    continue;
                // Copy the group's style setting to any child div with the bloom-editable class that lacks one.
                // Then remove the group style if it does have any child divs with the bloom-editable class.
                bool hasInternalEditableDiv = false;
                foreach (
                    SafeXmlElement element in groupElement.SafeSelectNodes(
                        "child::div[contains(@class, 'bloom-editable')]"
                    )
                )
                {
                    var divStyle = HtmlDom.GetStyle(element);
                    if (String.IsNullOrEmpty(divStyle))
                        element.AddClass(groupStyle);
                    hasInternalEditableDiv = true;
                }
                if (hasInternalEditableDiv)
                    groupElement.RemoveClass(groupStyle);
            }
        }

        /// <summary>
        /// For each group (meaning they have a common parent) of editable items, we
        /// need to make sure there are the correct set of copies, with appropriate @lang attributes
        /// </summary>
        public static SafeXmlElement MakeElementWithLanguageForOneGroup(
            SafeXmlElement groupElement,
            string langTag
        )
        {
            if (groupElement.GetAttribute("class").Contains("STOP"))
            {
                Console.Write("stop");
            }

            // If we don't have the relevant language code in this collection, don't make a block for it!
            if (string.IsNullOrEmpty(langTag))
                return null;

            var editableChildrenOfTheGroup = groupElement.SafeSelectNodes(
                "*[self::textarea or contains(@class,'bloom-editable')]"
            );

            var elementsAlreadyInThisLanguage =
                from SafeXmlElement x in editableChildrenOfTheGroup
                where x.GetAttribute("lang") == langTag
                select x;
            if (elementsAlreadyInThisLanguage.Any())
                //don't mess with this set, it already has a vernacular (this will happen when we're editing a shellbook, not just using it to make a vernacular edition)
                return elementsAlreadyInThisLanguage.First();

            if (
                groupElement
                    .SafeSelectNodes(
                        "ancestor-or-self::*[contains(@class,'bloom-translationGroup')]"
                    )
                    .Length == 0
            )
                return null;

            SafeXmlElement prototype = null;
            if (editableChildrenOfTheGroup.Length > 0)
                prototype = editableChildrenOfTheGroup[0] as SafeXmlElement;
            SafeXmlElement newElementInThisLanguage;
            if (prototype == null) //this was an empty translation-group (unusual, but we can cope)
            {
                newElementInThisLanguage = groupElement.OwnerDocument.CreateElement("div");
                newElementInThisLanguage.SetAttribute("class", "bloom-editable normal-style");
                newElementInThisLanguage.SetAttribute("contenteditable", "true");
                if (groupElement.HasAttribute("data-placeholder"))
                {
                    newElementInThisLanguage.SetAttribute(
                        "data-placeholder",
                        groupElement.GetAttribute("data-placeholder")
                    );
                }
                groupElement.AppendChild(newElementInThisLanguage);
            }
            else //this is the normal situation, where we're just copying the first element
            {
                //what we want to do is copy everything in the element, except that which is specific to a language.
                //so classes on the element, non-text children (like images), etc. should be copied
                newElementInThisLanguage = (SafeXmlElement)
                    prototype.ParentNode.InsertAfter(prototype.Clone(), prototype);
                //if there is an id, get rid of it, because we don't want 2 elements with the same id
                newElementInThisLanguage.RemoveAttribute("id");

                // Since we change the ID, the corresponding mp3 will change, which means the duration is no longer valid
                newElementInThisLanguage.RemoveAttribute("data-duration");

                // No need to copy over the audio-sentence markup
                // Various code expects elements with class audio-sentence to have an ID.
                // Both will be added when and if  we do audio recording (in whole-text-box mode) on the new div.
                // Until then it makes things more consistent if we make sure elements without ids
                // don't have this class.
                // Also, if audio recording markup is done using one audio-sentence span per sentence, we won't copy it.  (Because we strip all out the text underneath this node)
                // So, it's more consistent to treat all scenarios the same way (don't copy the audio-sentence markup)
                newElementInThisLanguage.RemoveClass("audio-sentence");

                // Nor any need to copy over other audio markup
                // We want to clear up all the audio markup so it's not left in an inconsistent state
                // where the Talking Book JS code thinks it's been initialized already, but actually all the audio-sentence markup has been stripped out :(
                // See BL-8215
                newElementInThisLanguage.RemoveAttribute("data-audiorecordingmode");
                newElementInThisLanguage.RemoveAttribute("data-audiorecordingendtimes");
                newElementInThisLanguage.RemoveClass("bloom-postAudioSplit");

                //OK, now any text in there will belong to the prototype language, so remove it, while retaining everything else
                StripOutText(newElementInThisLanguage);
            }
            newElementInThisLanguage.SetAttribute("lang", langTag);
            return newElementInThisLanguage;
        }

        /// <summary>
        /// Remove nodes that are either pure text or exist only to contain text, including BR and P
        /// Elements with a "bloom-cloneToOtherLanguages" class are preserved
        /// </summary>
        /// <param name="element"></param>
        private static void StripOutText(SafeXmlNode element)
        {
            var listToRemove = new List<SafeXmlNode>();
            foreach (
                var node in element.SafeSelectNodes(
                    "descendant-or-self::*[(self::p or self::br or self::u or self::b or self::i) and not(contains(@class,'bloom-cloneToOtherLanguages'))]"
                )
            )
            {
                listToRemove.Add(node);
            }
            // clean up any remaining texts that weren't enclosed
            foreach (
                var node in element.SafeSelectNodes(
                    "descendant-or-self::*[not(contains(@class,'bloom-cloneToOtherLanguages'))]/text()"
                )
            )
            {
                listToRemove.Add(node);
            }
            RemoveXmlChildren(listToRemove);
        }

        private static void RemoveXmlChildren(List<SafeXmlNode> removalList)
        {
            foreach (var node in removalList)
            {
                if (node.ParentNode != null)
                    node.ParentNode.RemoveChild(node);
            }
        }

        /// <summary>
        /// Sort the list of translation groups into the order their audio should be spoken,
        /// which is also the order used for Spreadsheet import/export. Ones that have tabindex are
        /// sorted by that. Ones that don't sort after ones that do, in the order
        /// they occur in the input list (that is, the sort is stable, typically preserving
        /// document order if that is how the input list was generated).
        /// </summary>
        /// <param name="groups"></param>
        /// <returns></returns>
        public static List<SafeXmlElement> SortTranslationGroups(IEnumerable<SafeXmlElement> groups)
        {
            // This is better than making the list and then using List's Sort method,
            // because it is guaranteed to be a stable sort, keeping things with the same
            // (or no) tabindex in the original, typically document, order.
            return groups.OrderBy(GetTabIndex).ToList();
        }

        private static int GetTabIndex(SafeXmlElement x)
        {
            if (Int32.TryParse(x.GetAttribute("tabindex"), out int val))
                return val;
            return Int32.MaxValue;
        }

        /// <summary>
        /// bloom-translationGroup elements on the page in audio-reading order.
        /// </summary>
        public static List<SafeXmlElement> SortedGroupsOnPage(
            SafeXmlElement page,
            bool omitBoxHeaders = false
        )
        {
            return TranslationGroupManager.SortTranslationGroups(
                GetTranslationGroups(page, omitBoxHeaders)
            );
        }
    }
}
