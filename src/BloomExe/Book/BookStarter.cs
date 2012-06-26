using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Collection;
using Palaso.Code;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.Text;
using Palaso.Xml;

namespace Bloom.Book
{
	/// <summary>
	/// Creates the files for a new blank book from a template book
	/// </summary>
	public class BookStarter
	{
		private readonly IFileLocator _fileLocator;
		private readonly BookStorage.Factory _bookStorageFactory;
		private LanguageSettings _languageSettings;
		private readonly CollectionSettings _collectionSettings;
		private bool _isSourceCollection;

		public delegate BookStarter Factory();//autofac uses this

		public BookStarter(IChangeableFileLocator fileLocator, BookStorage.Factory bookStorageFactory, LanguageSettings languageSettings, CollectionSettings collectionSettings)
		{
			_fileLocator = fileLocator;
			_bookStorageFactory = bookStorageFactory;
			_languageSettings = languageSettings;
			_collectionSettings = collectionSettings;
			_isSourceCollection = collectionSettings.IsSourceCollection;
		}

		public bool TestingSoSkipAddingXMatter { get; set; }

		/// <summary>
		/// Used in unit testing
		/// </summary>
		public bool OnNextRunSimulateFailureMakingBook;

		/// <summary>
		/// Given a template, make a new book
		/// </summary>
		/// <param name="sourceBookFolder"></param>
		/// <param name="parentCollectionPath"></param>
		/// <returns>path to the new book folder</returns>
		public  string CreateBookOnDiskFromTemplate(string sourceBookFolder, string parentCollectionPath)
		{
			Logger.WriteEvent("BookStarter.CreateBookOnDiskFromTemplate({0}, {1})", sourceBookFolder, parentCollectionPath);

			//TODO: is this meta value at odds with with data-book="bookTitle" somewhere in the book?
			//need to figure out the pro's cons of each approach. Right now, I can't think of why we need the special
			// defaultNameForDerivedBooks, but maybe there is a reason. Maybe it should be for templates, not for shells?

			string initialBookName = GetInitialName(sourceBookFolder, parentCollectionPath);
			var newBookFolder = Path.Combine(parentCollectionPath, initialBookName);
			CopyFolder(sourceBookFolder, newBookFolder);
			//if something bad happens from here on out, we need to delete that folder we just made
			try
			{
				var oldNamedFile = Path.Combine(newBookFolder, Path.GetFileName(GetPathToHtmlFile(sourceBookFolder)));
				var newNamedFile = Path.Combine(newBookFolder, initialBookName + ".htm");
				File.Move(oldNamedFile, newNamedFile);

				//the destination may change here...
				newBookFolder = SetupNewDocumentContents(newBookFolder);

				if(OnNextRunSimulateFailureMakingBook)
					throw new ApplicationException("Simulated failure for unit test");

			}
			catch (Exception)
			{
				Directory.Delete(newBookFolder,true);
				throw;
			}
			return newBookFolder;
		}

		private string GetPathToHtmlFile(string folder)
		{
			var candidates = from x in Directory.GetFiles(folder, "*.htm")
							 where !(x.ToLower().EndsWith("configuration.htm"))
							 select x;
			if (candidates.Count() == 1)
				return candidates.First();
			else
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(
					"There should only be a single htm file in each folder ({0}). [not counting configuration.htm]", folder);
				throw new ApplicationException();
			}

		}

		private string GetMetaValue(XmlDocument Dom, string name, string defaultValue)
		{
			var nameSuggestion = Dom.SafeSelectNodes("//head/meta[@name='" + name + "']");
			if (nameSuggestion.Count > 0)
			{
				return ((XmlElement)nameSuggestion[0]).GetAttribute("content");
			}
			return defaultValue;
		}

		private string SetupNewDocumentContents(string initialPath)
		{
			var storage = _bookStorageFactory(initialPath);
			//SetMetaDataElement(storage, "")

			UpdateEditabilityIndicator(storage);//Path.GetFileName(initialPath).ToLower().Contains("template"));

			//NB: for a new book based on a page template, I think this should remove *everything*, because the rest is in the xmatter
			//	for shells, we'll still have pages.
			//Remove from the new book any div-pages labelled as "extraPage"
			foreach (XmlElement initialPageDiv in storage.Dom.SafeSelectNodes("/html/body/div[contains(@data-page,'extra')]"))
			{
				initialPageDiv.ParentNode.RemoveChild(initialPageDiv);
			}

			XMatterHelper.RemoveExistingXMatter(storage.Dom);

			//remove ISBN number, if the original had one
			RemoveDataDivElement(storage.Dom, "ISBN");

			var sizeAndOrientation = Layout.FromDom(storage.Dom, Layout.A5Portrait);

			//now add in the xmatter from the currently selected xmatter pack
			if (!TestingSoSkipAddingXMatter)
			{
				var data = new DataSet();
				Debug.Assert(!string.IsNullOrEmpty(_collectionSettings.Language1Iso639Code));
				Debug.Assert(!string.IsNullOrEmpty(_collectionSettings.Language2Iso639Code));
				data.WritingSystemCodes.Add("V", _collectionSettings.Language1Iso639Code);
				data.WritingSystemCodes.Add("N1", _collectionSettings.Language2Iso639Code);
				data.WritingSystemCodes.Add("N2", _collectionSettings.Language3Iso639Code);
				var helper = new XMatterHelper(storage.Dom,_collectionSettings.XMatterPackName, _fileLocator);
				helper.FolderPathForCopyingXMatterFiles = storage.FolderPath;
				helper.InjectXMatter(data.WritingSystemCodes, sizeAndOrientation);
			}


			//Few sources will have this set at all. A template picture dictionary is one place where we might expect it to call for, say, bilingual
			int multilingualLevel = int.Parse(GetMetaValue(storage.Dom, "defaultMultilingualLevel", "1"));
			SetInitialMultilingualSetting(storage.Dom, multilingualLevel);


					//If this is a shell book, make elements to hold the vernacular
			foreach (XmlElement div in storage.Dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]"))
			{
				SetupIdAndLineage(div, div);
				SetupPage(div, _collectionSettings, null, null);
			}



	//		SizeAndOrientation.SetLayout(storage.Dom, sizeAndOrientation.ToString());

			storage.Save();

			//REVIEW this actually undoes the setting of the intial files name:
			//      storage.UpdateBookFileAndFolderName(_librarySettings);
			return storage.FolderPath;
		}

		private void RemoveDataDivElement(XmlNode dom, string key)
		{
			var dataDiv = GetOrCreateDataDiv(dom);
			foreach (XmlNode e in dataDiv.SafeSelectNodes(string.Format("div[@data-book='{0}']", key)))
			{
				dataDiv.RemoveChild(e);
			}
		}

		private XmlElement GetOrCreateDataDiv(XmlNode dom)
		{
			var dataDiv = dom.SelectSingleNode("//div[@id='bloomDataDiv']") as XmlElement;
			if (dataDiv == null)
			{
				XmlDocument doc = dom as XmlDocument;
				if (doc == null)
					doc = dom.OwnerDocument;
				dataDiv = doc.CreateElement("div");
				dataDiv.SetAttribute("id", "bloomDataDiv");
				dom.SelectSingleNode("//body").InsertAfter(dataDiv, null);
			}
			return dataDiv;
		}

		private void UpdateEditabilityIndicator(BookStorage storage)
		{

			//Here's the logic: If we're in a shell-making library, then it's safe to say that a newly-
			//created book is going to be a shell. Any derivatives will then act as shells.  But it won't
			//prevent us from editing it while in a shell-making collections, since we don't honor this
			//tag in shell-making collections.
			if(_isSourceCollection)
				BookStorage.UpdateMetaElement(storage.Dom, "lockedDownAsShell", "true");
		}


		public static void SetupPage(XmlElement pageDiv, CollectionSettings collectionSettings, string contentLanguageIso1, string contentLanguageIso2)//, bool inShellMode)
		{
			PrepareElementsInPageOrDocument(pageDiv, collectionSettings);//, inShellMode);

			// a page might be "extra" as far as the template is concerned, but
			// once a page is inserted into book (which may become a shell), it's
			// just a normal page
			pageDiv.SetAttribute("data-page", pageDiv.GetAttribute("data-page").Replace("extra", "").Trim());
	   }


		public static void SetupIdAndLineage(XmlElement parentPageDiv, XmlElement childPageDiv)
		{
			//"data-" is an html5 attribute you can put on any element. We're using that on the page div

			//NB: this works even if the parent and child are the same, which is the case when making a new book
			//but not when we're adding an individual template page.

			string parentId = parentPageDiv.GetAttribute("id");
			childPageDiv.SetAttribute("id", Guid.NewGuid().ToString());

			string parentLineage = parentPageDiv.GetOptionalStringAttribute("data-pageLineage", string.Empty);
			childPageDiv.SetAttribute("data-pageLineage", (parentLineage + ";" + parentId).Trim(new char[] {';'}));
		}


		private static bool ContainsClass(XmlNode element, string className)
		{
			return ((XmlElement) element).GetAttribute("class").Contains(className);
		}

		private static void AddClassIfMissing(XmlElement element, string className)
		{
			string classes = element.GetAttribute("class");
			if (classes.Contains(className))
				return;
			element.SetAttribute("class", (classes + " "+className).Trim());
		}

		/// <summary>
		/// For each group of editable elements in the div which have lang attributes, make a new element
		/// with the lang code of the vernacular.
		/// Also enable/disable editting as warranted (e.g. in shell mode or not)
		/// </summary>
		/// <param name="node"></param>
		public static void PrepareElementsInPageOrDocument(XmlNode node, CollectionSettings collectionSettings)//, bool inShellMode)
		{
			PrepareElementsOnPageOneLanguage(node, collectionSettings.Language1Iso639Code);

			//why do this? well, for bilingual/trilingual stuff (e.g., a picture dictionary)
			BookStarter.PrepareElementsOnPageOneLanguage(node,collectionSettings.Language2Iso639Code);

			//nb: really we need to have a place where we list the bilgual/triligual desires, and that may be book specific
			if(!string.IsNullOrEmpty(collectionSettings.Language3Iso639Code))
			{
				BookStarter.PrepareElementsOnPageOneLanguage(node, collectionSettings.Language3Iso639Code);
			}
		}

		private void SetDataDivElement(XmlNode dom, string key, string value)
		{
			var dataDiv = GetOrCreateDataDiv(dom);
			foreach (XmlNode e in dataDiv.SafeSelectNodes(string.Format("div[@data-book='{0}']", key)))
			{
				dataDiv.RemoveChild(e);
			}

			var d = dataDiv.OwnerDocument.CreateElement("div");
			d.SetAttribute("data-book", key);
			d.InnerXml = value;
			dataDiv.AppendChild(d);
		}


		/// <summary>
		/// This is used when a book is first created from a source; without it, if the shell maker left the book as trilingual when working on it,
		/// then everytime someone created a new book based on it, it too would be trilingual.
		/// </summary>
		/// <param name="pageDivOrDocumentDom"></param>
		/// <param name="oneTwoOrThreeContentLanguages"></param>
		public  void SetInitialMultilingualSetting(XmlDocument documentDom, int oneTwoOrThreeContentLanguages)
		{
			//var multilingualClass =  new string[]{"bloom-monolingual", "bloom-bilingual","bloom-trilingual"}[oneTwoOrThreeContentLanguages-1];

			if(oneTwoOrThreeContentLanguages < 3)
				RemoveDataDivElement(documentDom, "contentLanguage3");
			if (oneTwoOrThreeContentLanguages < 2)
				RemoveDataDivElement(documentDom, "contentLanguage2");

			SetDataDivElement(documentDom, "contentLanguage1", _collectionSettings.Language1Iso639Code);
			if (oneTwoOrThreeContentLanguages > 1)
				SetDataDivElement(documentDom, "contentLanguage2", _collectionSettings.Language2Iso639Code);
			if (oneTwoOrThreeContentLanguages > 2 && !string.IsNullOrEmpty(_collectionSettings.Language3Iso639Code))
				SetDataDivElement(documentDom, "contentLanguage3", _collectionSettings.Language3Iso639Code);

			/* these are fine but not needed

			//Stick a class in the page div telling the stylesheet how many languages we are displaying (only makes sense for content pages, in Jan 2012).
			foreach (
				XmlElement pageDiv in
					documentDom.SafeSelectNodes(
						"//div[contains(@class,'bloom-page') and not(contains(@class,'bloom-frontMatter'))]"))
			{
				RemoveClassesBeginingWith(pageDiv, "bloom-monolingual");
				RemoveClassesBeginingWith(pageDiv, "bloom-bilingual");
				RemoveClassesBeginingWith(pageDiv, "bloom-trilingual");
				AddClassIfMissing(pageDiv, multilingualClass);
			}

			//now, if those old content classes are still around, some other code might try to "fix" our multilingual setting. So let's just clear them out
			foreach (XmlElement group in pageDivOrDocumentDom.SafeSelectNodes(".//*[contains(@class,'bloom-translationGroup')]"))
			{
				foreach (XmlElement e in group.SafeSelectNodes(".//textarea | .//div")) //nb: we don't necessarily care that a div is editable or not
				{
					RemoveClassesBeginingWith(e, "bloom-content");
				}
			}

			 */
		}

		/// <summary>
		/// We stick 'contentLanguage2' and 'contentLanguage3' classes on editable things in bilingual and trilingual books
		/// </summary>
		public static void UpdateContentLanguageClasses(XmlNode pageDivOrDocumentDom, string vernacularIso, string national1Iso, string national2Iso, string contentLanguageIso2, string contentLanguageIso3)
		{
			var multilingualClass = "bloom-monolingual";
			var contentLanguages = new Dictionary<string, string>();
			contentLanguages.Add(vernacularIso, "bloom-content1");

			if (!string.IsNullOrEmpty(contentLanguageIso2) && vernacularIso!= contentLanguageIso2)
			{
				multilingualClass = "bloom-bilingual";
				contentLanguages.Add(contentLanguageIso2, "bloom-content2");
			}
			if (!string.IsNullOrEmpty(contentLanguageIso3) && vernacularIso != contentLanguageIso3 && contentLanguageIso2 != contentLanguageIso3 )
			{
				multilingualClass = "bloom-trilingual";
				Debug.Assert(!string.IsNullOrEmpty(contentLanguageIso2), "shouldn't have a content3 lang with no content2 lang");
				contentLanguages.Add(contentLanguageIso3, "bloom-content3");
			}

			//Stick a class in the page div telling the stylesheet how many languages we are displaying (only makes sense for content pages, in Jan 2012).
			foreach (XmlElement pageDiv in pageDivOrDocumentDom.SafeSelectNodes("//div[contains(@class,'bloom-page') and not(contains(@class,'bloom-frontMatter')) and not(contains(@class,'bloom-backMatter'))]"))
			{
				RemoveClassesBeginingWith(pageDiv, "bloom-monolingual");
				RemoveClassesBeginingWith(pageDiv, "bloom-bilingual");
				RemoveClassesBeginingWith(pageDiv, "bloom-trilingual");
				AddClassIfMissing(pageDiv, multilingualClass);
			}

			foreach (XmlElement group in pageDivOrDocumentDom.SafeSelectNodes(".//*[contains(@class,'bloom-translationGroup')]"))
			{
				var isXMatter = group.SafeSelectNodes("ancestor::div[contains(@class,'bloom-frontMatter') or contains(@class,'bloom-backMatter')]").Count > 0;
				foreach (XmlElement e in group.SafeSelectNodes(".//textarea | .//div")) //nb: we don't necessarily care that a div is editable or not
				{
					var lang = e.GetAttribute("lang");
					RemoveClassesBeginingWith(e, "bloom-content");//they might have been a given content lang before, but not now
					if (isXMatter && lang == national1Iso)
					{
						AddClass(e,"bloom-contentNational1");
					}
					if (isXMatter && !string.IsNullOrEmpty(national2Iso) && lang == national2Iso)
					{
						AddClass(e, "bloom-contentNational2");
					}
					foreach (var language in contentLanguages)
					{
						if(lang == language.Key)
						{
							AddClass(e, language.Value);
							break;//don't check the other languages
						}
					}
				}
			}
		}

		private static void AddClass( XmlElement e,string className)
		{
			e.SetAttribute("class", (e.GetAttribute("class") + " "+className).Trim());
		}

		private static void RemoveClassesBeginingWith(XmlElement xmlElement, string classPrefix)
		{

			var classes = xmlElement.GetAttribute("class");
			var original = classes;

			if (string.IsNullOrEmpty(classes))
				return;
			var parts = classes.SplitTrimmed(' ');

			classes = "";
			foreach (var part in parts)
			{
				if (!part.StartsWith(classPrefix))
					classes += part + " ";
			}
			xmlElement.SetAttribute("class", classes.Trim());

		//	Debug.WriteLine("RemoveClassesBeginingWith    " + xmlElement.InnerText+"     |    "+original + " ---> " + classes);
		}




		private static void PrepareElementsOnPageOneLanguage(XmlNode pageDiv, string isoCode)
		{
			foreach (XmlElement groupElement in pageDiv.SafeSelectNodes("//*[contains(@class,'bloom-translationGroup')]"))
			{
				MakeElementWithLanguageForOneGroup(groupElement, isoCode, "*");
				//remove any elements in teh translationgroup which don't have a lang
				foreach (XmlElement elementWithoutLanguage in groupElement.SafeSelectNodes("textarea[not(@lang)] | div[not(@lang)]"))
				{
					elementWithoutLanguage.ParentNode.RemoveChild(elementWithoutLanguage);
				}
			}


			//any text areas which still don't have a language, set them to the vernacular (this is used for simple templates (non-shell pages))
			foreach (
				XmlElement element in
					pageDiv.SafeSelectNodes(//NB: the jscript will take items with bloom-editable and set the contentEdtable to true.
						"//textarea[not(@lang)] | //*[(contains(@class, 'bloom-editable') or @contentEditable='true'  or @contenteditable='true') and not(@lang)]")
				)
			{
				element.SetAttribute("lang", isoCode);
			}

			foreach (XmlElement e in pageDiv.SafeSelectNodes("//*[starts-with(text(),'{')]"))
			{
				foreach (var node in e.ChildNodes)
				{
					XmlText t = node as XmlText;
					if (t != null && t.Value.StartsWith("{"))
						t.Value = "";
							//otherwise html tidy will through away span's (at least) that are empty, so we never get a chance to fill in the values.
				}
			}
		}

		/// <summary>
		/// For each group (meaning they have a common parent) of editable items, we
		/// need to make sure there are the correct set of copies, with appropriate @lang attributes
		/// </summary>
		private static void MakeElementWithLanguageForOneGroup(XmlElement groupElement, string vernacularCode, string elementTag)
		{
			XmlNodeList editableElementsWithinTheIndicatedParagraph = groupElement.SafeSelectNodes(elementTag);

			if (editableElementsWithinTheIndicatedParagraph.Count == 0)
				return;

			var alreadyInVernacular = from XmlElement x in editableElementsWithinTheIndicatedParagraph
									  where x.GetAttribute("lang") == vernacularCode
									  select x;
			if (alreadyInVernacular.Count() > 0)//don't mess with this set, it already has a vernacular (this will happen when we're editing a shellbook, not just using it to make a vernacular edition)
				return;

			if (groupElement.SafeSelectNodes("ancestor-or-self::*[contains(@class,'bloom-translationGroup')]").Count == 0)
				return;

			XmlElement prototype = editableElementsWithinTheIndicatedParagraph[0] as XmlElement;
			XmlElement vernacularCopy = (XmlElement) prototype.ParentNode.InsertAfter(prototype.Clone(), prototype);
			vernacularCopy.SetAttribute("lang",vernacularCode);
			//if there is an id, get rid of it, because we don't want 2 elements with the same id
			vernacularCopy.RemoveAttribute("id");
			vernacularCopy.InnerText = string.Empty;
		}

		/// <summary>
		/// All textareas which are just the same thing in different languages must by contained within a paragraph.
		/// </summary>
		/// <param name="pageDiv"></param>
		/// <returns></returns>
//		private static IEnumerable<XmlElement> GetEditableGroupsInSinglePageDiv(XmlElement node)
//	    {
////			foreach (XmlElement element in node.SafeSelectNodes("//textarea | //*[(@contentEditable='true' or  @contenteditable='true')]"))
////	        {
////	        	yield return (XmlElement) element.ParentNode;
////	        }
//	    }


		/// <summary>
		/// Get those paragraphs which look like we're supposed to localize them via variables (not via editing)
		/// </summary>
		/// <remarks>maybe the "AndText" part won't be desirable...</remarks>
		/// <param name="pageDiv"></param>
		/// <returns></returns>
		private static IEnumerable<XmlElement> GetParagraphsWithFieldsAndTextInSinglePageDiv(XmlElement pageDiv)
		{
			foreach (XmlElement paragraph in pageDiv.SafeSelectNodes("//p[@data-book or @data-library]"))
			{
				var text = paragraph.InnerText.Trim();
			   if (!string.IsNullOrEmpty(text))
				yield return pageDiv;
			}
		}

		private string GetInitialName(string sourcePath, string parentCollectionPath)
		{

			string name = Path.GetFileName(sourcePath);

			var storage = _bookStorageFactory(sourcePath);
			var nameSuggestion = storage.Dom.SafeSelectNodes("//head/meta[@name='defaultNameForDerivedBooks']");
			if(nameSuggestion.Count>0)
			{
				name = ((XmlElement) nameSuggestion[0]).GetAttribute("content");
			}

			int i = 0;
			string suffix = "";

			while (Directory.Exists(Path.Combine(parentCollectionPath, name+suffix)))
			{
				++i;
				suffix = i.ToString();
			}
			return name+suffix;
		}


		private static void CopyFolder(string sourcePath, string destinationPath)
		{
			Directory.CreateDirectory(destinationPath);
			foreach (var filePath in Directory.GetFiles(sourcePath))
			{
				//better to not just copy the old thumbnail, as the on in the library may well need to look different
				if (Path.GetFileNameWithoutExtension(filePath).ToLower() == "thumbnail")
					continue;
				if (Path.GetFileNameWithoutExtension(filePath).StartsWith(".")) //.guidsForInstaller.xml
					continue;
				File.Copy(filePath, Path.Combine(destinationPath, Path.GetFileName(filePath)));
			}
			foreach (var dirPath in Directory.GetDirectories(sourcePath))
			{
				CopyFolder(dirPath, Path.Combine(destinationPath, Path.GetFileName(dirPath)));
			}
		}
	}
}
