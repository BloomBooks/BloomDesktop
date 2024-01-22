using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Bloom.Properties;
using L10NSharp;
using Newtonsoft.Json;
using SIL.IO;
using SIL.WritingSystems;
using SIL.Xml;

namespace Bloom.Book
{
    /// <summary>
    /// stick in a json with various string values/translations we want to make available to the javascript
    /// </summary>
    public class RuntimeInformationInjector
    {
        // Collecting dynamic strings is slow, it only applies to English, and we only need to do it one time.
        private static bool _collectDynamicStrings;
        private static bool _foundEnglish;

        public static void AddUIDictionaryToDom(
            HtmlDom pageDom,
            BookData bookData,
            BookInfo bookInfo
        )
        {
            CheckDynamicStrings();

            // add dictionary script to the page
            XmlElement dictionaryScriptElement =
                pageDom.RawDom.SelectSingleNode("//script[@id='ui-dictionary']") as XmlElement;
            if (dictionaryScriptElement != null)
                dictionaryScriptElement.ParentNode.RemoveChild(dictionaryScriptElement);

            dictionaryScriptElement = pageDom.RawDom.CreateElement("script");
            dictionaryScriptElement.SetAttribute("type", "text/javascript");
            dictionaryScriptElement.SetAttribute("id", "ui-dictionary");
            var d = CreateDictionaryFromBookLanguages(bookData, bookInfo);

            // TODO: Eventually we need to look through all .bloom-translationGroup elements on the current page to determine
            // whether there is text in a language not yet added to the dictionary.
            // For now, we just add a few we know we need
            AddSomeCommonNationalLanguages(d);

            // Hard-coded localizations for 2.0
            AddHtmlUiStrings(d);

            // label elements with explicit localization keys.
            // (legacy labels are handled using the hard-coded localizations above, but new ones can have
            // explicit keys. We insert these here to keep the pattern of how BloomHintBubbles.getHintContent()
            // works and avoid having to rewrite it as async code.
            AddLabelTranslations(pageDom.Body, d);

            // Do this last, on the off-chance that the page contains a localizable string that matches
            // a language code.
            AddLanguagesUsedInPage(pageDom.RawDom, d);

            dictionaryScriptElement.InnerText = String.Format(
                "function GetInlineDictionary() {{ return {0};}}",
                JsonConvert.SerializeObject(d)
            );

            // add i18n initialization script to the page
            //AddLocalizationTriggerToDom(pageDom);

            pageDom.Head.InsertAfter(dictionaryScriptElement, pageDom.Head.LastChild);

            _collectDynamicStrings = false;
        }

        private static Dictionary<string, string> CreateDictionaryFromBookLanguages(
            BookData bookData,
            BookInfo bookInfo
        )
        {
            var d = new Dictionary<string, string>();
            // This method grabs the display names from meta.json's 'language-display-names' field which will be the
            // names the user expects to see as hints in text boxes because these are the names entered in the collection.
            // Any that get added here won't be overwritten by 'SafelyAddLanguage()' calls in the foreach below.
            // (See BL-12064.)
            PullInCollectionLanguagesDisplayNames(d, bookInfo);
            foreach (var lang in bookData.GetAllBookLanguages())
                SafelyAddLanguage(d, lang.Tag, lang.GetNameInLanguage(lang.Tag));
            SafelyAddLanguage(d, "vernacularLang", bookData.Language1.Tag); //use for making the vernacular the first tab
            SafelyAddLanguage(d, "{V}", bookData.Language1.Name);
            SafelyAddLanguage(
                d,
                "{N1}",
                bookData.MetadataLanguage1.GetNameInLanguage(bookData.MetadataLanguage1Tag)
            );
            if (!string.IsNullOrEmpty(bookData.Language3Tag))
                SafelyAddLanguage(
                    d,
                    "{N2}",
                    bookData.MetadataLanguage2.GetNameInLanguage(bookData.MetadataLanguage2Tag)
                );
            return d;
        }

        internal static void PullInCollectionLanguagesDisplayNames(
            Dictionary<string, string> d,
            BookInfo bookInfo
        )
        {
            var displayNames = bookInfo.MetaData.DisplayNames;
            if (displayNames == null)
                return;
            foreach (var kvp in displayNames)
            {
                SafelyAddLanguage(d, kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// Add to the dictionary which maps original to Localized strings an entry for any language code that doesn't already
        /// have one. We have localizations for a few major languages that map e.g. de->German/Deutsch/etc, so they are functioning
        /// not just to localize but to expand from a language code to an actual name. For any other languages where we don't
        /// have localization information, we'd like to at least expand the cryptic code into a name. This method does that.
        /// </summary>
        /// <param name="xmlDocument"></param>
        /// <param name="mapOriginalToLocalized"></param>
        internal static void AddLanguagesUsedInPage(
            XmlDocument xmlDocument,
            Dictionary<string, string> mapOriginalToLocalized
        )
        {
            var langs = xmlDocument
                .SafeSelectNodes("//*[@lang]")
                .Cast<XmlElement>()
                .Select(e => e.Attributes["lang"].Value)
                .Distinct()
                .Where(lang => !mapOriginalToLocalized.ContainsKey(lang))
                .ToList();
            if (langs.Any())
            {
                // We don't have a localization for these languages, but we can at least try to give them a name
                foreach (var lang in langs) // may include things like empty string, z, *, but this is harmless as they are not language codes.
                {
                    if (IetfLanguageTag.GetBestLanguageName(lang, out string match)) // some better name found
                        mapOriginalToLocalized[lang] = match;
                }
            }
        }

        private static void CheckDynamicStrings()
        {
            // if the ui language changes, check for English
            if (!_foundEnglish && (LocalizationManager.UILanguageId == "en"))
            {
                _foundEnglish = true;

                // if the current language is English, check the dynamic strings once
                _collectDynamicStrings = true;
            }
        }

        /// <summary>
        /// Adds a script to the page that triggers i18n after the page is fully loaded.
        /// </summary>
        /// <param name="pageDom"></param>
        //		private static void AddLocalizationTriggerToDom(HtmlDom pageDom)
        //		{
        //			XmlElement i18nScriptElement = pageDom.RawDom.SelectSingleNode("//script[@id='ui-i18n']") as XmlElement;
        //			if (i18nScriptElement != null)
        //				i18nScriptElement.ParentNode.RemoveChild(i18nScriptElement);
        //
        //			i18nScriptElement = pageDom.RawDom.CreateElement("script");
        //			i18nScriptElement.SetAttribute("type", "text/javascript");
        //			i18nScriptElement.SetAttribute("id", "ui-i18n");
        //
        //			// Explanation of the JavaScript:
        //			//   $(document).ready(function() {...}) tells the browser to run the code inside the braces after the document has completed loading.
        //			//   $('body') is a jQuery function that selects the contents of the body tag.
        //			//   .find('*[data-i18n]') instructs jQuery to return a collection of all elements inside the body tag that have a "data-i18n" attribute.
        //			//   .localize() runs the jQuery.fn.localize() method, which loops through the above collection of elements and attempts to localize the text.
        //			i18nScriptElement.InnerText = "$(document).ready(function() { $('body').find('*[data-i18n]').localize(); });";
        //
        //			pageDom.Head.InsertAfter(i18nScriptElement, pageDom.Head.LastChild);
        //		}

        private static void AddSomeCommonNationalLanguages(Dictionary<string, string> d)
        {
            SafelyAddLanguage(d, "en", "English");
            SafelyAddLanguage(d, "ha", "Hausa");
            SafelyAddLanguage(d, "hi", "हिन्दी"); //hindi
            SafelyAddLanguage(d, "es", "español");
            SafelyAddLanguage(d, "fr", "français");
            SafelyAddLanguage(d, "pt", "português");
            SafelyAddLanguage(d, "swa", "Kiswahili");
            SafelyAddLanguage(d, "th", "ภาษาไทย"); //thai
            SafelyAddLanguage(d, "tpi", "Tok Pisin");
            SafelyAddLanguage(d, "id", "Bahasa Indonesia");
            SafelyAddLanguage(d, "ar", "العربية/عربي‎"); //arabic
            SafelyAddLanguage(d, "zh-CN", "中文（简体）"); // chinese, simplified
            //    return { "en": "English", "vernacularLang": "en", "{V}": "English", "{N1}": "English", "{N2}": "", "ar": "العربية/عربي‎","id": "Bahasa Indonesia",
            //"ha": "Hausa", "hi": "हिन्दी", "es": "español", "fr": "français", "pt": "português", "swa": "Swahili", "th": "ภาษาไทย", "tpi": "Tok Pisin", "TemplateBooks.PageLabel.Front Cover": "Front Cover", "*You may use this space for author/illustrator, or anything else.": "*You may use this space for author/illustrator, or anything else.", "Click to choose topic": "Click to choose topic", "EditTab.FormatDialog.FontSizeTip": "Changes the text size for all boxes carrying the style '{0}' and language '{1}'.\\nCurrent size is {2}pt.", "FrontMatter.Factory.Book title in {lang}": "Book title in {lang}", "FrontMatter.Factory.Click to choose topic": "Click to choose topic", "FrontMatter.Factory.International Standard Book Number. Leave blank if you don't have one of these.": "International Standard Book Number. Leave blank if you don't have one of these.", "FrontMatter.Factory.Acknowledgments for translated version, in {lang}": "Acknowledgments for translated version, in {lang}", "FrontMatter.Factory.Use this to acknowledge any funding agencies.": "Use this to acknowledge any funding agencies.", "BackMatter.Factory.If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover.": "If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover.", "BackMatter.Factory.If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover.": "If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover." };
        }

        private static void SafelyAddLanguage(Dictionary<string, string> d, string key, string name)
        {
            if (!d.ContainsKey(key))
                d.Add(key, name);
        }

        /// <summary>
        /// For Bloom 2.0 this list is hard-coded
        /// </summary>
        /// <param name="d"></param>
        private static void AddHtmlUiStrings(Dictionary<string, string> d)
        {
            // ATTENTION: Currently, the english here must exactly match whats in the html.
            // See comment in AddTranslationToDictionaryUsingEnglishAsKey

            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.BookTitlePrompt",
                "Book title in {lang}"
            );
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.AuthorIllustratorPrompt",
                "You may use this space for author/illustrator, or anything else."
            );
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.OriginalContributorsPrompt",
                "The contributions made by writers, illustrators, editors, etc., in {lang}"
            );
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.CreditTranslator",
                "Acknowledgments for this version, in {lang}. For example, give credit to the translator for this version."
            );
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.PasteImageCreditsLink",
                "Paste Image Credits"
            );
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.TranslatedAcknowledgmentsPrompt",
                "Acknowledgments for translated version, in {lang}"
            );
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.NameofTranslatorPrompt",
                "Name of Translator, in {lang}"
            );
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.FundingAgenciesPrompt",
                "Use this to acknowledge any funding agencies."
            );
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.CopyrightPrompt",
                "Click to Edit Copyright & License"
            ); // pug files use & everywhere (BL-4120)
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.OriginalAcknowledgmentsPrompt",
                "Original (or Shell) Acknowledgments in {lang}"
            );
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.TopicPrompt",
                "Click to choose topic"
            );
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.ISBNPrompt",
                "International Standard Book Number. Leave blank if you don't have one of these."
            );
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.SimpleLineDrawings",
                "Simple line drawings look best. Instead of using this page, you can also make your own thumbnail.png file and set it to Read-only so Bloom doesn't write over it."
            );

            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.BigBook.Contributions",
                "When you are making an original book, use this box to record contributions made by writers, illustrators, editors, etc."
            );
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.BigBook.Translator",
                "When you make a book from a shell, use this box to tell who did the translation."
            );

            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.BackMatter.InsideBackCoverTextPrompt",
                "If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover."
            );
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.BackMatter.OutsideBackCoverTextPrompt",
                "If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover."
            );
            // Used in Traditional Front matter
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.FrontMatter.InsideFrontCoverTextPrompt",
                "If you need somewhere to put more information about the book, you can use this page, which is the inside of the front cover."
            );

            // Used in Big Book instructions page.  (BL-4115)
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "EditTab.Instructions.DeleteAllowed",
                "Feel free to modify or delete this page."
            );
            // Inserted by XMatterHelper as needed (BL-4116)
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "TemplateBooks.PageLabel.Flyleaf",
                "Flyleaf"
            );
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "TemplateBooks.PageDescription.Flyleaf",
                "This page was automatically inserted because the following page is marked as part of a two page spread."
            );

            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "TemplateBooks.Quiz.HintBubble.Answer",
                "Put a possible answer here. Check it if it is correct."
            );
            AddTranslationToDictionaryUsingEnglishAsKey(
                d,
                "TemplateBooks.Quiz.HintBubble.Question",
                "Put a comprehension question here"
            );

            AddTranslationToDictionaryUsingKey(d, "EditTab.Image.PasteImage", "Paste Image");
            AddTranslationToDictionaryUsingKey(d, "EditTab.Image.ChangeImage", "Change Image");
            AddTranslationToDictionaryUsingKey(
                d,
                "EditTab.Image.EditMetadata",
                "Edit Image Credits, Copyright, & License"
            );
            AddTranslationToDictionaryUsingKey(d, "EditTab.Image.CopyImage", "Copy Image");
            AddTranslationToDictionaryUsingKey(d, "EditTab.Image.CutImage", "Cut Image");

            // tool tips for style editor
            AddTranslationToDictionaryUsingKey(
                d,
                "EditTab.FormatDialog.FontSizeTip",
                "Changes the text size for all boxes carrying the style '{0}' and language '{1}'.\nCurrent size is {2}pt."
            );
            //No longer used. See BL-799 AddTranslationToDictionaryUsingKey(d, "EditTab.FormatDialogTip", "Adjust formatting for style");
            AddTranslationToDictionaryUsingKey(
                d,
                "EditTab.FormatDialog.WordSpacingNormal",
                "Normal"
            );
            AddTranslationToDictionaryUsingKey(d, "EditTab.FormatDialog.WordSpacingWide", "Wide");
            AddTranslationToDictionaryUsingKey(
                d,
                "EditTab.FormatDialog.WordSpacingExtraWide",
                "Extra Wide"
            );
            AddTranslationToDictionaryUsingKey(
                d,
                "EditTab.FormatDialog.FontFaceToolTip",
                "Change the font face"
            );
            AddTranslationToDictionaryUsingKey(
                d,
                "EditTab.FormatDialog.FontSizeToolTip",
                "Change the font size"
            );
            AddTranslationToDictionaryUsingKey(
                d,
                "EditTab.FormatDialog.LineSpacingToolTip",
                "Change the spacing between lines of text"
            );
            AddTranslationToDictionaryUsingKey(
                d,
                "EditTab.FormatDialog.WordSpacingToolTip",
                "Change the spacing between words"
            );
            AddTranslationToDictionaryUsingKey(
                d,
                "EditTab.FormatDialog.BorderToolTip",
                "Change the border and background"
            );

            // "No Topic" localization for Topic Chooser
            AddTranslationToDictionaryUsingKey(d, "Topics.NoTopic", "No Topic");
        }

        private static void AddTranslationToDictionaryUsingKey(
            Dictionary<string, string> dictionary,
            string key,
            string defaultText
        )
        {
            var translation = _collectDynamicStrings
                ? LocalizationManager.GetDynamicString("Bloom", key, defaultText)
                : LocalizationManager.GetString(key, defaultText);

            if (!dictionary.ContainsKey(key))
            {
                dictionary.Add(key, translation);
            }
        }

        private static void AddLabelTranslations(
            XmlElement root,
            Dictionary<string, string> dictionary
        )
        {
            var labels = root.SafeSelectNodes("//label[@data-i18n]");
            foreach (XmlElement label in labels)
            {
                var key = label.GetAttribute("data-i18n");
                // In similar code in AddTranslationToDictionaryUsingEnglishAsKey, we call GetDynamicString if
                // _collectDynamicStrings is true (which it always is in this method). But I think
                // use of GetDynamicString is obsolete and it's better not to add another use of it,
                // so I'm just using the ordinary GetString here.
                var translation = LocalizationManager.GetString(key, label.InnerText);
                dictionary[key] = translation;
            }
        }

        private static void AddTranslationToDictionaryUsingEnglishAsKey(
            Dictionary<string, string> dictionary,
            string key,
            string defaultText
        )
        {
            var translation = _collectDynamicStrings
                ? LocalizationManager.GetDynamicString("Bloom", key, defaultText)
                : LocalizationManager.GetString(key, defaultText);

            //We have to match on some key. Ideally, we'd match on something "key-ish", like BookEditor.FrontMatter.BookTitlePrompt
            //But that would require changes to all the templates to have that key somehow, in addition to or in place of the current English
            //So for now, we're just keeping the real key on the c#/xlf side of things, and letting the javascript work by matching our defaultText to the English text in the html
            var keyUsedInTheJavascriptDictionary = defaultText;
            if (!dictionary.ContainsKey(keyUsedInTheJavascriptDictionary))
            {
                // Do NOT HtmlEncode these strings!.  See https://issues.bloomlibrary.org/youtrack/issue/BL-5469.
                dictionary.Add(keyUsedInTheJavascriptDictionary, translation);
            }
        }

        /// <summary>
        /// keeps track of the most recent set of topics we injected, mapping the localization back to the original.
        /// </summary>
        public static Dictionary<string, string> TopicReversal;

        /// <summary>
        /// stick in a json with various settings we want to make available to the javascript
        /// </summary>
        public static void AddUISettingsToDom(
            HtmlDom pageDom,
            BookData bookData,
            IFileLocator fileLocator
        )
        {
            CheckDynamicStrings();

            XmlElement existingElement =
                pageDom.RawDom.SelectSingleNode("//script[@id='ui-settings']") as XmlElement;

            XmlElement element = pageDom.RawDom.CreateElement("script");
            element.SetAttribute("type", "text/javascript");
            element.SetAttribute("id", "ui-settings");
            var d = new Dictionary<string, string>();

            //d.Add("urlOfUIFiles", "file:///" + fileLocator.LocateDirectory("ui", "ui files directory"));
            if (!String.IsNullOrEmpty(Settings.Default.LastSourceLanguageViewed))
            {
                d.Add("defaultSourceLanguage", Settings.Default.LastSourceLanguageViewed);
            }

            d.Add("languageForNewTextBoxes", bookData.Language1.Tag);

            // BL-2357 To aid in smart ordering of source languages in source bubble
            if (!String.IsNullOrEmpty(bookData.Language2Tag))
            {
                d.Add("currentCollectionLanguage2", bookData.Language2Tag);
            }
            if (!String.IsNullOrEmpty(bookData.Language3Tag))
            {
                d.Add("currentCollectionLanguage3", bookData.Language3Tag);
            }

            d.Add(
                "browserRoot",
                FileLocationUtilities
                    .GetDirectoryDistributedWithApplication(BloomFileLocator.BrowserRoot)
                    .ToLocalhost()
            );

            element.InnerText = String.Format(
                "function GetSettings() {{ return {0};}}",
                JsonConvert.SerializeObject(d)
            );

            var head = pageDom.RawDom.SelectSingleNode("//head");
            if (existingElement != null)
                head.ReplaceChild(element, existingElement);
            else
                head.InsertAfter(element, head.LastChild);

            _collectDynamicStrings = false;
        }
    }
}
