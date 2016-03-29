﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Xml;
using Bloom.Collection;
using Bloom.Properties;
using Bloom.ToPalaso;
using L10NSharp;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Windows.Forms.WritingSystems;
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

		public static void AddUIDictionaryToDom(HtmlDom pageDom, CollectionSettings collectionSettings)
		{
			CheckDynamicStrings();

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

			// Hard-coded localizations for 2.0
			AddHtmlUiStrings(d);

			// Do this last, on the off-chance that the page contains a localizable string that matches
			// a language code.
			AddLanguagesUsedInPage(pageDom.RawDom, d);

			dictionaryScriptElement.InnerText = String.Format("function GetInlineDictionary() {{ return {0};}}", JsonConvert.SerializeObject(d));

			// add i18n initialization script to the page
			//AddLocalizationTriggerToDom(pageDom);

			pageDom.Head.InsertAfter(dictionaryScriptElement, pageDom.Head.LastChild);

			_collectDynamicStrings = false;
		}

		/// <summary>
		/// Add to the dictionary which maps original to Localized strings an entry for any language code that doesn't already
		/// have one. We have localizations for a few major languages that map e.g. de->German/Deutsch/etc, so they are functioning
		/// not just to localize but to expand from a language code to an actual name. For any other languages where we don't
		/// have localization information, we'd like to at least expand the cryptic code into a name. This method does that.
		/// </summary>
		/// <param name="xmlDocument"></param>
		/// <param name="mapOriginalToLocalized"></param>
		internal static void AddLanguagesUsedInPage(XmlDocument xmlDocument, Dictionary<string, string> mapOriginalToLocalized)
		{
			var langs = xmlDocument.SafeSelectNodes("//*[@lang]").Cast<XmlElement>()
				.Select(e => e.Attributes["lang"].Value)
				.Distinct()
				.Where(lang => !mapOriginalToLocalized.ContainsKey(lang))
				.ToList();
			if (langs.Any())
			{
				// We don't have a localization for these languages, but we can at least try to give them a name
				var lookup = new LanguageLookupModel(); // < 1ms
				foreach (var lang in langs) // may include things like empty string, z, *, but this is harmless as they are not language codes.
				{
					string match;
					if (lookup.GetBestLanguageName(lang, out match)) // some better name found
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

		private static void MakePageLabelLocalizable(HtmlDom singlePageHtmlDom, Dictionary<string, string> d)
		{
			foreach (XmlElement element in singlePageHtmlDom.RawDom.SelectNodes("//*[contains(@class, 'pageLabel')]"))
			{
				if (!element.HasAttribute("data-i18n"))
				{
					var englishLabel = element.InnerText;
					var key = "TemplateBooks.PageLabel." + englishLabel;
					AddTranslationToDictionaryUsingEnglishAsKey(d, key, englishLabel);

					element.SetAttribute("data-i18n", key);
				}
			}
		}

		private static void AddSomeCommonNationalLanguages(Dictionary<string, string> d)
		{
			SafelyAddLanguage(d, "en", "English");
			SafelyAddLanguage(d, "ha", "Hausa");
			SafelyAddLanguage(d, "hi", "हिन्दी");//hindi
			SafelyAddLanguage(d, "es", "español");
			SafelyAddLanguage(d, "fr", "français");
			SafelyAddLanguage(d, "pt", "português");
			SafelyAddLanguage(d, "swa", "Kiswahili");
			SafelyAddLanguage(d, "th", "ภาษาไทย"); //thai
			SafelyAddLanguage(d, "tpi", "Tok Pisin");
			SafelyAddLanguage(d, "id", "Bahasa Indonesia");
			SafelyAddLanguage(d, "ar","العربية/عربي‎");//arabic
			//    return { "en": "English", "vernacularLang": "en", "{V}": "English", "{N1}": "English", "{N2}": "", "ar": "العربية/عربي‎","id": "Bahasa Indonesia", 
			//"ha": "Hausa", "hi": "हिन्दी", "es": "español", "fr": "français", "pt": "português", "swa": "Swahili", "th": "ภาษาไทย", "tpi": "Tok Pisin", "TemplateBooks.PageLabel.Front Cover": "Front Cover", "*You may use this space for author/illustrator, or anything else.": "*You may use this space for author/illustrator, or anything else.", "Click to choose topic": "Click to choose topic", "BookEditor.FontSizeTip": "Changes the text size for all boxes carrying the style '{0}' and language '{1}'.\\nCurrent size is {2}pt.", "FrontMatter.Factory.Book title in {lang}": "Book title in {lang}", "FrontMatter.Factory.Click to choose topic": "Click to choose topic", "FrontMatter.Factory.International Standard Book Number. Leave blank if you don't have one of these.": "International Standard Book Number. Leave blank if you don't have one of these.", "FrontMatter.Factory.Acknowledgments for translated version, in {lang}": "Acknowledgments for translated version, in {lang}", "FrontMatter.Factory.Use this to acknowledge any funding agencies.": "Use this to acknowledge any funding agencies.", "BackMatter.Factory.If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover.": "If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover.", "BackMatter.Factory.If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover.": "If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover." };

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

			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.FontSizeTip",
				"Changes the text size for all boxes carrying the style '{0}' and language '{1}'.\nCurrent size is {2}pt.");
			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.FrontMatter.BookTitlePrompt",
				"Book title in {lang}");
			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.FrontMatter.AuthorIllustratorPrompt",
				"You may use this space for author/illustrator, or anything else.");
			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.FrontMatter.OriginalContributorsPrompt",
				"The contributions made by writers, illustrators, editors, etc., in {lang}");
			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.FrontMatter.TranslatedAcknowledgmentsPrompt",
				"Acknowledgments for translated version, in {lang}");
			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.FrontMatter.NameofTranslatorPrompt",
				"Name of Translator, in {lang}");
			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.FrontMatter.FundingAgenciesPrompt",
				"Use this to acknowledge any funding agencies.");
			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.FrontMatter.CopyrightPrompt",
				"Click to Edit Copyright & License");
			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.FrontMatter.OriginalAcknowledgmentsPrompt",
				"Original (or Shell) Acknowledgments in {lang}");
			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.FrontMatter.TopicPrompt",
				"Click to choose topic");
			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.FrontMatter.ISBNPrompt",
				"International Standard Book Number. Leave blank if you don't have one of these.");

			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.FrontMatter.BigBook.Contributions",
				"When you are making an original book, use this box to record contributions made by writers, illustrators, editors, etc.");
			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.FrontMatter.BigBook.Translator",
				"When you make a book from a shell, use this box to tell who did the translation.");
			
			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.BackMatter.InsideBackCoverTextPrompt",
				"If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover.");
			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.BackMatter.OutsideBackCoverTextPrompt",
				"If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover.");
			// Used in Traditional Front matter
			AddTranslationToDictionaryUsingEnglishAsKey(d, "EditTab.FrontMatter.InsideFrontCoverTextPrompt",
				"If you need somewhere to put more information about the book, you can use this page, which is the inside of the front cover.");

			AddTranslationToDictionaryUsingKey(d, "EditTab.Image.PasteImage", "Paste Image");
			AddTranslationToDictionaryUsingKey(d, "EditTab.Image.ChangeImage", "Change Image");
			AddTranslationToDictionaryUsingKey(d, "EditTab.Image.EditMetadata",
				"Edit Image Credits, Copyright, & License");
			AddTranslationToDictionaryUsingKey(d, "EditTab.Image.CopyImage", "Copy Image");
			AddTranslationToDictionaryUsingKey(d, "EditTab.Image.CutImage", "Cut Image");

			// tool tips for style editor
			AddTranslationToDictionaryUsingKey(d, "BookEditor.FontSizeTip",
				"Changes the text size for all boxes carrying the style '{0}' and language '{1}'.\nCurrent size is {2}pt.");
			//No longer used. See BL-799 AddTranslationToDictionaryUsingKey(d, "EditTab.FormatDialogTip", "Adjust formatting for style");
			AddTranslationToDictionaryUsingKey(d, "EditTab.FormatDialog.WordSpacingNormal", "Normal");
			AddTranslationToDictionaryUsingKey(d, "EditTab.FormatDialog.WordSpacingWide", "Wide");
			AddTranslationToDictionaryUsingKey(d, "EditTab.FormatDialog.WordSpacingExtraWide", "Extra Wide");
			AddTranslationToDictionaryUsingKey(d, "EditTab.FormatDialog.FontFaceToolTip", "Change the font face");
			AddTranslationToDictionaryUsingKey(d, "EditTab.FormatDialog.FontSizeToolTip", "Change the font size");
			AddTranslationToDictionaryUsingKey(d, "EditTab.FormatDialog.LineSpacingToolTip", "Change the spacing between lines of text");
			AddTranslationToDictionaryUsingKey(d, "EditTab.FormatDialog.WordSpacingToolTip", "Change the spacing between words");
			AddTranslationToDictionaryUsingKey(d, "EditTab.FormatDialog.BorderToolTip", "Change the border and background");

			// "No Topic" localization for Topic Chooser
			AddTranslationToDictionaryUsingKey(d, "Topics.NoTopic", "No Topic");
		}

		private static void AddTranslationToDictionaryUsingKey(Dictionary<string, string> dictionary, string key, string defaultText)
		{
			var translation = _collectDynamicStrings
				? LocalizationManager.GetDynamicString("Bloom", key, defaultText)
				: LocalizationManager.GetString(key, defaultText);

			if (!dictionary.ContainsKey(key))
			{
				dictionary.Add(key, translation);
			}
		}

		private static void AddTranslationToDictionaryUsingEnglishAsKey(Dictionary<string, string> dictionary, string key, string defaultText)
		{
			var translation = _collectDynamicStrings
				? LocalizationManager.GetDynamicString("Bloom", key, defaultText)
				: LocalizationManager.GetString(key, defaultText);

			//We have to match on some key. Ideally, we'd match on something "key-ish", like BookEditor.FrontMatter.BookTitlePrompt
			//But that would require changes to all the templates to have that key somehow, in adition to or in place of the current English
			//So for now, we're just keeping the real key on the c#/tmx side of things, and letting the javascript work by matching our defaultText to the English text in the html
			var keyUsedInTheJavascriptDictionary = defaultText;
			if (!dictionary.ContainsKey(keyUsedInTheJavascriptDictionary))
			{
				dictionary.Add(keyUsedInTheJavascriptDictionary, WebUtility.HtmlEncode(translation));
			}
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
			CheckDynamicStrings();

			XmlElement existingElement = pageDom.RawDom.SelectSingleNode("//script[@id='ui-settings']") as XmlElement;

			XmlElement element = pageDom.RawDom.CreateElement("script");
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

			// BL-2357 To aid in smart ordering of source languages in source bubble
			if (!String.IsNullOrEmpty(collectionSettings.Language2Iso639Code))
			{
				d.Add("currentCollectionLanguage2", collectionSettings.Language2Iso639Code);
			}
			if (!String.IsNullOrEmpty(collectionSettings.Language3Iso639Code))
			{
				d.Add("currentCollectionLanguage3", collectionSettings.Language3Iso639Code);
			}

			d.Add("browserRoot", FileLocator.GetDirectoryDistributedWithApplication(BloomFileLocator.BrowserRoot).ToLocalhost());

	
			element.InnerText = String.Format("function GetSettings() {{ return {0};}}", JsonConvert.SerializeObject(d));

			var head = pageDom.RawDom.SelectSingleNode("//head");
			if (existingElement != null)
				head.ReplaceChild(element, existingElement);
			else
				head.InsertAfter(element, head.LastChild);

			_collectDynamicStrings = false;
		}
	}
}
