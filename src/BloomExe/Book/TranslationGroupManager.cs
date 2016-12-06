﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Xml;
using Bloom.Collection;
using SIL.Extensions;
using SIL.Linq;
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
	public class TranslationGroupManager
	{
		/// <summary>
		/// For each group of editable elements in the div which have lang attributes  (normally, a .bloom-translationGroup div),
		/// make a new element with the lang code of the vernacular (normally, a .bloom-editable).
		/// Also enable/disable editing as warranted (e.g. in shell mode or not)
		/// </summary>
		public static void PrepareElementsInPageOrDocument(XmlNode pageOrDocumentNode, CollectionSettings collectionSettings)
		{
			PrepareElementsOnPageOneLanguage(pageOrDocumentNode, collectionSettings.Language1Iso639Code);
			PrepareElementsOnPageOneLanguage(pageOrDocumentNode, collectionSettings.Language2Iso639Code);

			if (!string.IsNullOrEmpty(collectionSettings.Language3Iso639Code))
			{
				PrepareElementsOnPageOneLanguage(pageOrDocumentNode, collectionSettings.Language3Iso639Code);
			}
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
		public static void PrepareDataBookTranslationGroups(XmlNode pageOrDocumentNode, IEnumerable<string> languageCodes)
		{
			//At first, I set out to select all translationGroups that have child .bloomEditables that have data-book attributes
			//however this has implications on other fields, noticeably the acknowledgments. So in order to get this fixed
			//and not open another can of worms, I've reduce the scope of this
			//fix to just the bookTitle, so I'm going with findOnlyBookTitleFields for now
			var findAllDataBookFields = "descendant-or-self::*[contains(@class,'bloom-translationGroup') and descendant::div[@data-book and contains(@class,'bloom-editable')]]";
			var findOnlyBookTitleFields = "descendant-or-self::*[contains(@class,'bloom-translationGroup') and descendant::div[@data-book='bookTitle' and contains(@class,'bloom-editable')]]";
			foreach (XmlElement groupElement in
					pageOrDocumentNode.SafeSelectNodes(findOnlyBookTitleFields))
			{
				foreach (var lang in languageCodes)
				{
					MakeElementWithLanguageForOneGroup(groupElement, lang);
				}
			}
		}


		/// <summary>
		/// This is used when a book is first created from a source; without it, if the shell maker left the book as trilingual when working on it,
		/// then every time someone created a new book based on it, it too would be trilingual.
		/// </summary>
		public static void SetInitialMultilingualSetting(BookData bookData, int oneTwoOrThreeContentLanguages,
			CollectionSettings collectionSettings)
		{
			//var multilingualClass =  new string[]{"bloom-monolingual", "bloom-bilingual","bloom-trilingual"}[oneTwoOrThreeContentLanguages-1];

			if (oneTwoOrThreeContentLanguages < 3)
				bookData.RemoveAllForms("contentLanguage3");
			if (oneTwoOrThreeContentLanguages < 2)
				bookData.RemoveAllForms("contentLanguage2");

			bookData.Set("contentLanguage1", collectionSettings.Language1Iso639Code, false);
			bookData.Set("contentLanguage1Rtl", collectionSettings.IsLanguage1Rtl.ToString(), false);
			if (oneTwoOrThreeContentLanguages > 1)
			{
				bookData.Set("contentLanguage2", collectionSettings.Language2Iso639Code, false);
				bookData.Set("contentLanguage2Rtl", collectionSettings.IsLanguage2Rtl.ToString(), false);
			}
			if (oneTwoOrThreeContentLanguages > 2 && !string.IsNullOrEmpty(collectionSettings.Language3Iso639Code))
			{
				bookData.Set("contentLanguage3", collectionSettings.Language3Iso639Code, false);
				bookData.Set("contentLanguage3Rtl", collectionSettings.IsLanguage3Rtl.ToString(), false);
			}
		}


		/// <summary>
		/// We stick 'contentLanguage2' and 'contentLanguage3' classes on editable things in bilingual and trilingual books
		/// </summary>
		public static void UpdateContentLanguageClasses(XmlNode elementOrDom, CollectionSettings settings,
			string vernacularIso, string contentLanguageIso2, string contentLanguageIso3)
		{
			var multilingualClass = "bloom-monolingual";
			var contentLanguages = new Dictionary<string, string>();
			contentLanguages.Add(vernacularIso, "bloom-content1");

			if (!String.IsNullOrEmpty(contentLanguageIso2) && vernacularIso != contentLanguageIso2)
			{
				multilingualClass = "bloom-bilingual";
				contentLanguages.Add(contentLanguageIso2, "bloom-content2");
			}
			if (!String.IsNullOrEmpty(contentLanguageIso3) && vernacularIso != contentLanguageIso3 &&
				contentLanguageIso2 != contentLanguageIso3)
			{
				multilingualClass = "bloom-trilingual";
				contentLanguages.Add(contentLanguageIso3, "bloom-content3");
				Debug.Assert(!String.IsNullOrEmpty(contentLanguageIso2), "shouldn't have a content3 lang with no content2 lang");
			}

			//Stick a class in the page div telling the stylesheet how many languages we are displaying (only makes sense for content pages, in Jan 2012).
			foreach (
				XmlElement pageDiv in
					elementOrDom.SafeSelectNodes(
						"descendant-or-self::div[contains(@class,'bloom-page') and not(contains(@class,'bloom-frontMatter')) and not(contains(@class,'bloom-backMatter'))]")
				)
			{
				HtmlDom.RemoveClassesBeginingWith(pageDiv, "bloom-monolingual");
				HtmlDom.RemoveClassesBeginingWith(pageDiv, "bloom-bilingual");
				HtmlDom.RemoveClassesBeginingWith(pageDiv, "bloom-trilingual");
				HtmlDom.AddClassIfMissing(pageDiv, multilingualClass);
			}


			// This is the "code" part of the visibility system: https://goo.gl/EgnSJo
			foreach (XmlElement group in elementOrDom.SafeSelectNodes(".//*[contains(@class,'bloom-translationGroup')]"))
			{
				var dataDefaultLanguages = HtmlDom.GetAttributeValue(group, "data-default-languages").Split(new char[] { ',', ' ' }, 
					StringSplitOptions.RemoveEmptyEntries);

				//nb: we don't necessarily care that a div is editable or not
				foreach (XmlElement e in @group.SafeSelectNodes(".//textarea | .//div"))
				{
					HtmlDom.RemoveClassesBeginingWith(e, "bloom-content");
					var lang = e.GetAttribute("lang");

					//These bloom-content* classes are used by some stylesheet rules, primarily to boost the font-size of some languages.
					//Enhance: this is too complex; the semantics of these overlap with each other and with bloom-visibility-code-on, and with data-language-order.
					//It would be better to have non-overlapping things; 1 for order, 1 for visibility, one for the lang's role in this collection.
					string orderClass;
					if (contentLanguages.TryGetValue(lang, out orderClass))
					{
						HtmlDom.AddClass(e, orderClass); //bloom-content1, bloom-content2, bloom-content3
					}

					//Enhance: it's even more likely that we can get rid of these by replacing them with bloom-content2, bloom-content3
					if (lang == settings.Language2Iso639Code)
					{
						HtmlDom.AddClass(e, "bloom-contentNational1");
					}
					if (lang == settings.Language3Iso639Code)
					{
						HtmlDom.AddClass(e, "bloom-contentNational2");
					}

					HtmlDom.RemoveClassesBeginingWith(e, "bloom-visibility-code");
					if (ShouldNormallyShowEditable(lang, dataDefaultLanguages, contentLanguageIso2, contentLanguageIso3, settings))
					{
						HtmlDom.AddClass(e, "bloom-visibility-code-on");
					}

					UpdateRightToLeftSetting(settings, e, lang);
				}
			}
		}

		private static void UpdateRightToLeftSetting(CollectionSettings settings, XmlElement e, string lang)
		{
			HtmlDom.RemoveRtlDir(e);
			if((lang == settings.Language1Iso639Code && settings.IsLanguage1Rtl) ||
			   (lang == settings.Language2Iso639Code && settings.IsLanguage2Rtl) ||
			   (lang == settings.Language3Iso639Code && settings.IsLanguage3Rtl))
			{
				HtmlDom.AddRtlDir(e);
			}
		}

		/// <summary>
		/// Here, "normally" means unless the user overrides via a .bloom-visibility-user-on/off
		/// </summary>
		internal static bool ShouldNormallyShowEditable(string lang, string[] dataDefaultLanguages, 
			string contentLanguageIso2, string contentLanguageIso3, // these are effected by the multilingual settings for this book
			CollectionSettings settings) // use to get the collection's current N1 and N2 in xmatter or other template pages that specify default languages
		{
			if (dataDefaultLanguages == null || dataDefaultLanguages.Length == 0 
				|| string.IsNullOrWhiteSpace(dataDefaultLanguages[0])
				|| dataDefaultLanguages[0].Equals("auto",StringComparison.InvariantCultureIgnoreCase))
			{
					return lang == settings.Language1Iso639Code || lang == contentLanguageIso2 || lang == contentLanguageIso3;
			}
			else
			{
				return (lang == settings.Language1Iso639Code && dataDefaultLanguages.Contains("V")) ||
				   (lang == settings.Language2Iso639Code && dataDefaultLanguages.Contains("N1")) ||
				   (lang == settings.Language3Iso639Code && dataDefaultLanguages.Contains("N2"));
			}
		}

		private static int GetOrderOfThisLanguageInTheTextBlock(string editableIso, string vernacularIso, string contentLanguageIso2, string contentLanguageIso3)
		{
			if (editableIso == vernacularIso)
				return 1;
			if (editableIso == contentLanguageIso2)
				return 2;
			if (editableIso == contentLanguageIso3)
				return 3;
			return -1;
		}



		private static void PrepareElementsOnPageOneLanguage(XmlNode pageDiv, string isoCode)
		{
			foreach (
				XmlElement groupElement in
					pageDiv.SafeSelectNodes("descendant-or-self::*[contains(@class,'bloom-translationGroup')]"))
			{
				MakeElementWithLanguageForOneGroup(groupElement, isoCode);

				//remove any elements in the translationgroup which don't have a lang (but ignore any label elements, which we're using for annotating groups)
				foreach (
					XmlElement elementWithoutLanguage in
						groupElement.SafeSelectNodes("textarea[not(@lang)] | div[not(@lang) and not(self::label)]"))
				{
					elementWithoutLanguage.ParentNode.RemoveChild(elementWithoutLanguage);
				}
			}

			//any editable areas which still don't have a language, set them to the vernacular (this is used for simple templates (non-shell pages))
			foreach (
				XmlElement element in
					pageDiv.SafeSelectNodes( //NB: the jscript will take items with bloom-editable and set the contentEdtable to true.
						"descendant-or-self::textarea[not(@lang)] | descendant-or-self::*[(contains(@class, 'bloom-editable') or @contentEditable='true'  or @contenteditable='true') and not(@lang)]")
				)
			{
				element.SetAttribute("lang", isoCode);
			}

			foreach (XmlElement e in pageDiv.SafeSelectNodes("descendant-or-self::*[starts-with(text(),'{')]"))
			{
				foreach (var node in e.ChildNodes)
				{
					XmlText t = node as XmlText;
					if (t != null && t.Value.StartsWith("{"))
						t.Value = "";
					//otherwise html tidy will throw away spans (at least) that are empty, so we never get a chance to fill in the values.
				}
			}
		}

		/// <summary>
		/// For each group (meaning they have a common parent) of editable items, we
		/// need to make sure there are the correct set of copies, with appropriate @lang attributes
		/// </summary>
		private static void MakeElementWithLanguageForOneGroup(XmlElement groupElement, string isoCode)
		{
			if (groupElement.GetAttribute("class").Contains("STOP"))
			{
				Console.Write("stop");
			}
			XmlNodeList editableChildrenOfTheGroup =
				groupElement.SafeSelectNodes("*[self::textarea or contains(@class,'bloom-editable')]");

			var elementsAlreadyInThisLanguage = from XmlElement x in editableChildrenOfTheGroup
												where x.GetAttribute("lang") == isoCode
												select x;
			if (elementsAlreadyInThisLanguage.Any())
				//don't mess with this set, it already has a vernacular (this will happen when we're editing a shellbook, not just using it to make a vernacular edition)
				return;

			if (groupElement.SafeSelectNodes("ancestor-or-self::*[contains(@class,'bloom-translationGroup')]").Count == 0)
				return;

			var prototype = editableChildrenOfTheGroup[0] as XmlElement;
			XmlElement newElementInThisLanguage;
			if (prototype == null) //this was an empty translation-group (unusual, but we can cope)
			{
				newElementInThisLanguage = groupElement.OwnerDocument.CreateElement("div");
				newElementInThisLanguage.SetAttribute("class", "bloom-editable");
				newElementInThisLanguage.SetAttribute("contenteditable", "true");
				if (groupElement.HasAttribute("data-placeholder"))
				{
					newElementInThisLanguage.SetAttribute("data-placeholder", groupElement.GetAttribute("data-placeholder"));
				}
				groupElement.AppendChild(newElementInThisLanguage);
			}
			else //this is the normal situation, where we're just copying the first element
			{
				//what we want to do is copy everything in the element, except that which is specific to a language. 
				//so classes on the element, non-text children (like images), etc. should be copied
				newElementInThisLanguage = (XmlElement) prototype.ParentNode.InsertAfter(prototype.Clone(), prototype);
				//if there is an id, get rid of it, because we don't want 2 elements with the same id
				newElementInThisLanguage.RemoveAttribute("id");
				//OK, now any text in there will belong to the prototype language, so remove it, while retaining everything else
				StripOutText(newElementInThisLanguage);
			}
			newElementInThisLanguage.SetAttribute("lang", isoCode);		
		}

		/// <summary>
		/// Remove nodes that are either pure text or exist only to contain text, including BR and P
		/// Elements with a "bloom-cloneToOtherLanguages" class are preserved
		/// </summary>
		/// <param name="element"></param>
		private static void StripOutText(XmlNode element)
		{
			var listToRemove = new List<XmlNode>();
			foreach (XmlNode node in element.SelectNodes("descendant-or-self::*[(self::p or self::br or self::u or self::b or self::i) and not(contains(@class,'bloom-cloneToOtherLanguages'))]"))
			{
				listToRemove.Add(node);
			}
			// clean up any remaining texts that weren't enclosed
			foreach (XmlNode node in element.SelectNodes("descendant-or-self::*[not(contains(@class,'bloom-cloneToOtherLanguages'))]/text()"))
			{
				listToRemove.Add(node);
			}
			RemoveXmlChildren(listToRemove);
		}

		private static void RemoveXmlChildren(List<XmlNode> removalList)
		{
			foreach (var node in removalList)
			{
				if(node.ParentNode != null)
					node.ParentNode.RemoveChild(node);
			}
		}
	}
}
