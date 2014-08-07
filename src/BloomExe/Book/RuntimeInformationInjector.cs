using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Bloom.Collection;
using Bloom.Properties;
using L10NSharp;
using Newtonsoft.Json;
using Palaso.IO;

namespace Bloom.Book
{
    /// <summary>
    /// stick in a json with various string values/translations we want to make available to the javascript
    /// </summary>
    public class RuntimeInformationInjector
    {
        public static void AddUIDictionaryToDom(HtmlDom pageDom, CollectionSettings collectionSettings)
        {
			// add i18n initialization script to the page
            XmlElement i18nScriptElement = pageDom.RawDom.SelectSingleNode("//script[@id='ui-i18n']") as XmlElement;
            if (i18nScriptElement != null)
                i18nScriptElement.ParentNode.RemoveChild(i18nScriptElement);

            i18nScriptElement = pageDom.RawDom.CreateElement("script");
            i18nScriptElement.SetAttribute("type", "text/javascript");
            i18nScriptElement.SetAttribute("id", "ui-i18n");
            i18nScriptElement.InnerText = "$(document).ready(function() { $('body').find('*[data-i18n]').localize(); });";
            pageDom.Head.InsertAfter(i18nScriptElement, pageDom.Head.LastChild);

            // add dictionary script to the page
            XmlElement dictionaryScriptElement = pageDom.RawDom.SelectSingleNode("//script[@id='ui-dictionary']") as XmlElement;
            if (dictionaryScriptElement != null)
                dictionaryScriptElement.ParentNode.RemoveChild(dictionaryScriptElement);

            dictionaryScriptElement = pageDom.RawDom.CreateElement("script");
            dictionaryScriptElement.SetAttribute("type", "text/javascript");
            dictionaryScriptElement.SetAttribute("id", "ui-dictionary");
            var d = new Dictionary<string, string>();

            d.Add(collectionSettings.Language1Iso639Code, collectionSettings.Language1Name);
            if (!String.IsNullOrEmpty(collectionSettings.Language2Iso639Code))
                SafelyAddLanguage(d, collectionSettings.Language2Iso639Code,
                    collectionSettings.GetLanguage2Name(collectionSettings.Language2Iso639Code));
            if (!String.IsNullOrEmpty(collectionSettings.Language3Iso639Code))
                SafelyAddLanguage(d, collectionSettings.Language3Iso639Code,
                    collectionSettings.GetLanguage3Name(collectionSettings.Language3Iso639Code));

            SafelyAddLanguage(d, "vernacularLang", collectionSettings.Language1Iso639Code);//use for making the vernacular the first tab
            SafelyAddLanguage(d, "{V}", collectionSettings.Language1Name);
            SafelyAddLanguage(d, "{N1}", collectionSettings.GetLanguage2Name(collectionSettings.Language2Iso639Code));
            SafelyAddLanguage(d, "{N2}", collectionSettings.GetLanguage3Name(collectionSettings.Language3Iso639Code));

            // TODO: Eventually we need to look through all .bloom-translationGroup elements on the current page to determine
            // whether there is text in a language not yet added to the dictionary.
            // For now, we just add a few we know we need
            AddSomeCommonNationalLanguages(d);

            MakePageLabelLocalizable(pageDom, d);

            AddLocalizedHintContentsToDictionary(pageDom, d, collectionSettings);

            // Hard-coded localizations for 2.0
            AddHtmlUiStrings(d);

            dictionaryScriptElement.InnerText = String.Format("function GetDictionary() {{ return {0};}}", JsonConvert.SerializeObject(d));

			pageDom.Head.InsertAfter(dictionaryScriptElement, pageDom.Head.LastChild);
        }

        private static void MakePageLabelLocalizable(HtmlDom singlePageHtmlDom, Dictionary<string, string> d)
        {
            var pageElement = singlePageHtmlDom.RawDom.SelectSingleNode("//div") as XmlElement;
            foreach (XmlElement element in singlePageHtmlDom.RawDom.SelectNodes("//*[contains(@class, 'pageLabel')]"))
            {
                // Hard-coded localizations for 2.0
                var key = "EditTab.ThumbnailCaptions." + element.InnerText;
                AddTranslationToDictionary(d, key, element.InnerText);

                if (!element.HasAttribute("data-i18n"))
                    element.SetAttribute("data-i18n", key);
            }

        }

        private static void AddSomeCommonNationalLanguages(Dictionary<string, string> d)
        {
            SafelyAddLanguage(d, "ha", "Hausa");
            SafelyAddLanguage(d, "hi", "Hindi");
            SafelyAddLanguage(d, "es", "Spanish");
            SafelyAddLanguage(d, "fr", "French");
            SafelyAddLanguage(d, "pt", "Portuguese");
            SafelyAddLanguage(d, "swa", "Swahili");
            SafelyAddLanguage(d, "th", "Thai");
            SafelyAddLanguage(d, "tpi", "Tok Pisin");
        }

        private static void SafelyAddLanguage(Dictionary<string, string> d, string key, string name)
        {
            if (!d.ContainsKey(key))
                d.Add(key, name);
        }

        private static void AddLocalizedHintContentsToDictionary(HtmlDom singlePageHtmlDom, Dictionary<string, string> dictionary, CollectionSettings collectionSettings)
        {
			var nameOfXMatterPack = singlePageHtmlDom.GetMetaValue("xMatter", collectionSettings.XMatterPackName);
			

            string idPrefix = "";
            var pageElement = singlePageHtmlDom.RawDom.SelectSingleNode("//div") as XmlElement;
            if (XMatterHelper.IsFrontMatterPage(pageElement))
            {
				idPrefix = "FrontMatter." + nameOfXMatterPack + ".";
            }
            else if (XMatterHelper.IsBackMatterPage(pageElement))
            {
				idPrefix = "BackMatter." + nameOfXMatterPack + ".";
            }
            foreach (XmlElement element in singlePageHtmlDom.RawDom.SelectNodes("//*[@data-hint]"))
            {
                //why aren't we just doing: element.SetAttribute("data-hint", translation);  instead of bothering to write out a dictionary?
                //because (especially since we're currently just assuming it is in english), we would later save it with the translation, and then next time try to translate that, and poplute the 
                //list of strings that we tell people to translate
                var key = element.GetAttribute("data-hint");
                if (!dictionary.ContainsKey(key))
                {
                    string translation;
                    var id = idPrefix + key;
                    if (key.Contains("{lang}"))
                    {
                        translation = LocalizationManager.GetDynamicString("Bloom", id, key, "Put {lang} in your translation, so it can be replaced by the language name.");
                    }
                    else
                    {
                        translation = LocalizationManager.GetDynamicString("Bloom", id, key);
                    }
                    dictionary.Add(key, translation);
                }
            }
        }

        /// <summary>
        /// For Bloom 2.0 this list is hard-coded
        /// </summary>
        /// <param name="d"></param>
        private static void AddHtmlUiStrings(Dictionary<string, string> d)
        {
            AddTranslationToDictionary(d, "BookEditor.FontSizeTip", "Changes the text size for all boxes carrying the style '{0}' and language '{1}'.\nCurrent size is {2}pt.");
            AddTranslationToDictionary(d, "FrontMatter.Factory.Book title in {lang}", "Book title in {lang}");
            AddTranslationToDictionary(d, "FrontMatter.Factory.Click to choose topic", "Click to choose topic");
            AddTranslationToDictionary(d, "FrontMatter.Factory.International Standard Book Number. Leave blank if you don't have one of these.", "International Standard Book Number. Leave blank if you don't have one of these.");
            AddTranslationToDictionary(d, "FrontMatter.Factory.Acknowledgments for translated version, in {lang}", "Acknowledgments for translated version, in {lang}");
            AddTranslationToDictionary(d, "FrontMatter.Factory.Use this to acknowledge any funding agencies.", "Use this to acknowledge any funding agencies.");
            AddTranslationToDictionary(d, "BackMatter.Factory.If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover.", "If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover.");
            AddTranslationToDictionary(d, "BackMatter.Factory.If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover.", "If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover.");
        }

        private static void AddTranslationToDictionary(Dictionary<string, string> d, string key, string defaultText)  {

            var translation = LocalizationManager.GetDynamicString("Bloom", key, defaultText);
            if (!d.ContainsKey(key)) d.Add(key, translation);
        }

        /// <summary>
        /// keeps track of the most recent set of topics we injected, mapping the localization back to the original.
        /// </summary>
        public static Dictionary<string, string> TopicReversal; 

        /// <summary>
        /// stick in a json with various settings we want to make available to the javascript
        /// </summary>
        public static void AddUISettingsToDom(HtmlDom pageDom, CollectionSettings collectionSettings, IFileLocator fileLocator)
        {
            XmlElement element = pageDom.RawDom.SelectSingleNode("//script[@id='ui-settings']") as XmlElement;
            if (element != null)
                element.ParentNode.RemoveChild(element);

            element = pageDom.RawDom.CreateElement("script");
            element.SetAttribute("type", "text/javascript");
            element.SetAttribute("id", "ui-settings");
            var d = new Dictionary<string, string>();

            //d.Add("urlOfUIFiles", "file:///" + fileLocator.LocateDirectory("ui", "ui files directory"));
            if (!String.IsNullOrEmpty(Settings.Default.LastSourceLanguageViewed))
            {
                d.Add("defaultSourceLanguage", Settings.Default.LastSourceLanguageViewed);
            }

            d.Add("languageForNewTextBoxes", collectionSettings.Language1Iso639Code);
            d.Add("isSourceCollection", collectionSettings.IsSourceCollection.ToString());

            d.Add("bloomBrowserUIFolder", FileLocator.GetDirectoryDistributedWithApplication("BloomBrowserUI"));

            var topics = new[] { "Agriculture", "Animal Stories", "Business", "Culture", "Community Living", "Dictionary", "Environment", "Fiction", "Health", "How To", "Math", "Non Fiction", "Spiritual", "Personal Development", "Primer", "Science", "Traditional Story" };
            var builder = new StringBuilder();
            builder.Append("[");
            TopicReversal = new Dictionary<string, string>();
            foreach (var topic in topics)
            {
                var localized = LocalizationManager.GetDynamicString("Bloom", "Topics." + topic, topic, "shows in the topics chooser in the edit tab");
                TopicReversal[localized] = topic;
                builder.Append("\""+localized+"\", ");
            }
            builder.Append("]");
            d.Add("topics", builder.ToString().Replace(", ]","]"));
//            d.Add("topics", "['Agriculture', 'Animal Stories', 'Business', 'Culture', 'Community Living', 'Dictionary', 'Environment', 'Fiction', 'Health', 'How To', 'Math', 'Non Fiction', 'Spiritual', 'Personal Development', 'Primer', 'Science', 'Tradition']".Replace("'", "\\\""));

            element.InnerText = String.Format("function GetSettings() {{ return {0};}}", JsonConvert.SerializeObject(d));

            var head = pageDom.RawDom.SelectSingleNode("//head");
            head.InsertAfter(element, head.LastChild);
        }
    }
}
