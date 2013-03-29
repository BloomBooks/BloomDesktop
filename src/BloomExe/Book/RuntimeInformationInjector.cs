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
			XmlElement dictionaryScriptElement = pageDom.RawDom.SelectSingleNode("//script[@id='ui-dictionary']") as XmlElement;
			if (dictionaryScriptElement != null)
				dictionaryScriptElement.ParentNode.RemoveChild(dictionaryScriptElement);

			dictionaryScriptElement = pageDom.RawDom.CreateElement("script");
			dictionaryScriptElement.SetAttribute("type", "text/javascript");
			dictionaryScriptElement.SetAttribute("id", "ui-dictionary");
			var d = new Dictionary<string, string>();

			d.Add(collectionSettings.Language1Iso639Code, collectionSettings.Language1Name);
			if (!String.IsNullOrEmpty(collectionSettings.Language2Iso639Code) && !d.ContainsKey(collectionSettings.Language2Iso639Code))
				d.Add(collectionSettings.Language2Iso639Code, collectionSettings.GetLanguage2Name(collectionSettings.Language2Iso639Code));
			if (!String.IsNullOrEmpty(collectionSettings.Language3Iso639Code) && !d.ContainsKey(collectionSettings.Language3Iso639Code))
				d.Add(collectionSettings.Language3Iso639Code, collectionSettings.GetLanguage3Name(collectionSettings.Language3Iso639Code));

			d.Add("vernacularLang", collectionSettings.Language1Iso639Code);//use for making the vernacular the first tab
			d.Add("{V}", collectionSettings.Language1Name);
			d.Add("{N1}", collectionSettings.GetLanguage2Name(collectionSettings.Language2Iso639Code));
			d.Add("{N2}", collectionSettings.GetLanguage3Name(collectionSettings.Language3Iso639Code));

			AddLocalizedHintContentsToDictionary(pageDom, d, collectionSettings);

			dictionaryScriptElement.InnerText = String.Format("function GetDictionary() {{ return {0};}}", JsonConvert.SerializeObject(d));

			pageDom.Head.InsertAfter(dictionaryScriptElement, null);
		}

		private static void AddLocalizedHintContentsToDictionary(HtmlDom singlePageHtmlDom, Dictionary<string, string> dictionary, CollectionSettings collectionSettings)
		{
			string idPrefix = "";
			var pageElement = singlePageHtmlDom.RawDom.SelectSingleNode("//div") as XmlElement;
			if (XMatterHelper.IsFrontMatterPage(pageElement))
			{
				idPrefix = "FrontMatter." + collectionSettings.XMatterPackName + ".";
			}
			else if (XMatterHelper.IsBackMatterPage(pageElement))
			{
				idPrefix = "BackMatter." + collectionSettings.XMatterPackName + ".";
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

			d.Add("urlOfUIFiles", "file:///" + fileLocator.LocateDirectory("ui", "ui files directory"));
			if (!String.IsNullOrEmpty(Settings.Default.LastSourceLanguageViewed))
			{
				d.Add("defaultSourceLanguage", Settings.Default.LastSourceLanguageViewed);
			}

			d.Add("languageForNewTextBoxes", collectionSettings.Language1Iso639Code);

			d.Add("bloomProgramFolder", Directory.GetParent(FileLocator.GetDirectoryDistributedWithApplication("root")).FullName);

			var topics = new[] { "Agriculture", "Animal Stories", "Business", "Culture", "Community Living", "Dictionary", "Environment", "Fiction", "Health", "How To", "Math", "Non Fiction", "Spiritual", "Personal Development", "Primer", "Science", "Tradition" };
			var builder = new StringBuilder();
			builder.Append("[");
			foreach (var topic in topics)
			{
				var localized = LocalizationManager.GetDynamicString("Bloom", "Topics." + topic, topic, "shows in the topics chooser in the edit tab");
				builder.Append("\""+localized+"\", ");
			}
			builder.Append("]");
			d.Add("topics", builder.ToString().Replace(", ]","]"));
//            d.Add("topics", "['Agriculture', 'Animal Stories', 'Business', 'Culture', 'Community Living', 'Dictionary', 'Environment', 'Fiction', 'Health', 'How To', 'Math', 'Non Fiction', 'Spiritual', 'Personal Development', 'Primer', 'Science', 'Tradition']".Replace("'", "\\\""));

			element.InnerText = String.Format("function GetSettings() {{ return {0};}}", JsonConvert.SerializeObject(d));

			pageDom.RawDom.SelectSingleNode("//head").InsertAfter(element, null);
		}
	}
}
