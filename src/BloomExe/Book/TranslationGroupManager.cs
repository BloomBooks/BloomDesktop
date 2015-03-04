using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml;
using Bloom.Collection;
using Palaso.Linq;
using Palaso.Xml;

namespace Bloom.Book
{
	/// <summary>
	/// Most editable elements in Bloom are multilingual. They have a wrapping <div> and then inner divs which are visible or not,
	/// depending on various settings. This class manages creating those inner divs, and marking them with classes that turn on
	/// visibility or move the element around the page, depending on the stylesheet in use.
	///
	/// Individual string divs are marked with either bloom-content1, bloom-content2, bloom-content3, or none
	/// Also, page <div/>s are marked with one of these classes: bloom-monolingual, bloom-bilingual, and bloom-trilingual
	///
	///	<div class="bloom-translationGroup">
	///		<div class="bloom-editable" contenteditable="true" lang="en">The Mother said...</div>
	///		<div class="bloom-editable" contenteditable="true" lang="tpi">Mama i tok:</div>
	///		<div class="bloom-editable bloom-content1" contenteditable="true" lang="xyz">abada fakwan</div>
	///	</div>

	/// </summary>
	public class TranslationGroupManager
	{
		/// <summary>
		/// For each group of editable elements in the div which have lang attributes, make a new element
		/// with the lang code of the vernacular.
		/// Also enable/disable editting as warranted (e.g. in shell mode or not)
		/// </summary>
		/// <param name="node"></param>
		public static void PrepareElementsInPageOrDocument(XmlNode node, CollectionSettings collectionSettings)
			//, bool inShellMode)
		{
			PrepareElementsOnPageOneLanguage(node, collectionSettings.Language1Iso639Code);

			//why do this? well, for bilingual/trilingual stuff (e.g., a picture dictionary)
			PrepareElementsOnPageOneLanguage(node, collectionSettings.Language2Iso639Code);

			if (!string.IsNullOrEmpty(collectionSettings.Language3Iso639Code))
			{
				PrepareElementsOnPageOneLanguage(node, collectionSettings.Language3Iso639Code);
			}
		}


		/// <summary>
		/// This is used when a book is first created from a source; without it, if the shell maker left the book as trilingual when working on it,
		/// then everytime someone created a new book based on it, it too would be trilingual.
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
			var national1Iso = settings.Language2Iso639Code;
			var national2Iso = settings.Language3Iso639Code;
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
				Debug.Assert(!String.IsNullOrEmpty(contentLanguageIso2), "shouldn't have a content3 lang with no content2 lang");
				contentLanguages.Add(contentLanguageIso3, "bloom-content3");
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

			foreach (XmlElement group in elementOrDom.SafeSelectNodes(".//*[contains(@class,'bloom-translationGroup')]"))
			{
				var isXMatter =
					@group.SafeSelectNodes("ancestor::div[contains(@class,'bloom-frontMatter') or contains(@class,'bloom-backMatter')]")
						.Count > 0;
				foreach (XmlElement e in @group.SafeSelectNodes(".//textarea | .//div"))
					//nb: we don't necessarily care that a div is editable or not
				{
					var lang = e.GetAttribute("lang");
					HtmlDom.RemoveClassesBeginingWith(e, "bloom-content");
					// they might have been a given content lang before, but not now
					HtmlDom.RemoveRtlDir(e);
					// in case this language has been changed from a Right to Left language to a Left to Right language
					if (isXMatter && lang == national1Iso)
					{
						HtmlDom.AddClass(e, "bloom-contentNational1");
					}
					if (isXMatter && !String.IsNullOrEmpty(national2Iso) && lang == national2Iso)
					{
						HtmlDom.AddClass(e, "bloom-contentNational2");
					}
					foreach (var language in contentLanguages)
					{
						if (lang == language.Key)
						{
							HtmlDom.AddClass(e, language.Value);
							if ((lang == vernacularIso && settings.IsLanguage1Rtl) ||
							    (lang == national1Iso && settings.IsLanguage2Rtl) ||
							    (!String.IsNullOrEmpty(national2Iso) && lang == national2Iso && settings.IsLanguage3Rtl))
							{
								HtmlDom.AddRtlDir(e);
							}
							break; //don't check the other languages
						}
					}
				}
			}
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
			

			foreach(XmlNode node in element.SelectNodes(".//*[(self::p or self::br) and not(contains(@class,'bloom-cloneToOtherLanguages'))]"))
			{
				node.ParentNode.RemoveChild(node);
			}
			// clean up any remaining texts that weren't enclosed
			foreach(XmlNode node in element.SelectNodes(".//*[not(contains(@class,'bloom-cloneToOtherLanguages'))]//text()"))
			{
				node.ParentNode.RemoveChild(node);
			}
		}
	}
}
