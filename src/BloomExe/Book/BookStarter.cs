using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Reporting;
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
		private readonly LibrarySettings _librarySettings;
		private bool _isShellLibrary;

		public delegate BookStarter Factory();//autofac uses this

		public BookStarter(IFileLocator fileLocator, BookStorage.Factory bookStorageFactory, LanguageSettings languageSettings, LibrarySettings librarySettings)
		{
			_fileLocator = fileLocator;
			_bookStorageFactory = bookStorageFactory;
			_languageSettings = languageSettings;
			_librarySettings = librarySettings;
			_isShellLibrary = librarySettings.IsShellLibrary;
		}

		public bool TestingSoSkipAddingXMatter { get; set; }

		/// <summary>
		/// Given a template, make a new book
		/// </summary>
		/// <param name="sourceTemplateFolder"></param>
		/// <param name="parentCollectionPath"></param>
		/// <returns>path to the new book folder</returns>
		public  string CreateBookOnDiskFromTemplate(string sourceTemplateFolder, string parentCollectionPath)
		{
			Logger.WriteEvent("BookStarter.CreateBookOnDiskFromTemplate({0}, {1})", sourceTemplateFolder, parentCollectionPath);

			//TODO: is this meta value at odds with with data-book="bookTitle" somewhere in the book?
			//need to figure out the pro's cons of each approach. Right now, I can't think of why we need the special
			// defaultNameForDerivedBooks, but maybe there is a reason. Maybe it should be for templates, not for shells?

			string initialBookName = GetInitialName(sourceTemplateFolder, parentCollectionPath);
			var newBookFolder = Path.Combine(parentCollectionPath, initialBookName);
			CopyFolder(sourceTemplateFolder, newBookFolder);
			//if something bad happens from here on out, we need to delete that folder we just made
			try
			{
				var oldNamedFile = Path.Combine(newBookFolder, Path.GetFileName(GetPathToHtmlFile(sourceTemplateFolder)));
				var newNamedFile = Path.Combine(newBookFolder, initialBookName + ".htm");
				File.Move(oldNamedFile, newNamedFile);

				//the destination may change here...
				newBookFolder = SetupDocumentContents(newBookFolder);

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

		private string SetupDocumentContents(string initialPath)
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

			AddXMatter(storage);

			//If this is a shell book, make elements to hold the vernacular
			foreach (XmlElement div in storage.Dom.SafeSelectNodes("//div[contains(@class,'-bloom-page')]"))
			{
				SetupIdAndLineage(div, div);
				SetupPage(div, _languageSettings.VernacularIso639Code);
			}
			storage.Save();

			storage.UpdateBookFileAndFolderName(_languageSettings);
			return storage.FolderPath;
		}

		/// <summary>
		/// the front and back matter (xmatter) comes from a separate html file. We ship a factor one, but orgs can supply their own.
		/// </summary>
		/// <param name="storage"></param>
		private void AddXMatter(BookStorage storage)
		{
			if (TestingSoSkipAddingXMatter)
				return;

			var dom = XmlHtmlConverter.GetXmlDomFromHtmlFile(_fileLocator.LocateFile(_librarySettings.NameOfXMatterTemplate+"-XMatter.htm"));
			XmlNode previousFronMatterPage = null;
			foreach (XmlElement templatePage in dom.SafeSelectNodes("/html/body/div[contains(@data-page,'required')]"))
			{
				var newPageDiv = storage.Dom.ImportNode(templatePage, true) as XmlElement;
				newPageDiv.InnerXml = newPageDiv.InnerXml.Replace("'V'", '"'+_librarySettings.VernacularIso639Code+'"');
				newPageDiv.InnerXml = newPageDiv.InnerXml.Replace("\"V\"", '"' + _librarySettings.VernacularIso639Code + '"');
				newPageDiv.InnerXml = newPageDiv.InnerXml.Replace("'N1'", '"' + _librarySettings.NationalLanguage1Iso639Code + '"');
				newPageDiv.InnerXml = newPageDiv.InnerXml.Replace("\"N1\"", '"' + _librarySettings.NationalLanguage1Iso639Code + '"');
				if(!string.IsNullOrEmpty(_librarySettings.NationalLanguage2Iso639Code))  //otherwise, styleshee will hide it
				{
					newPageDiv.InnerXml = newPageDiv.InnerXml.Replace("'N2'", '"' + _librarySettings.NationalLanguage2Iso639Code + '"');
					newPageDiv.InnerXml = newPageDiv.InnerXml.Replace("\"N2\"", '"' + _librarySettings.NationalLanguage2Iso639Code + '"');
				}
				storage.Dom.SelectSingleNode("//body").InsertAfter(newPageDiv, previousFronMatterPage);
				previousFronMatterPage = newPageDiv;
			}
		}

		private void UpdateEditabilityIndicator(BookStorage storage)
		{
			XmlElement n = storage.Dom.SelectSingleNode("//meta[@name='editability']") as XmlElement;
			if(n==null)
			{
				n = storage.Dom.CreateElement("meta");
				n.SetAttribute("name","editability");
				storage.Dom.SelectSingleNode("//head").AppendChild(n);
			}

			//Here's the logic: If we're in a shell-making library, then it's safe to say that a newly-
			//created book is translationOnly. Any derivatives will then act as shells.  But it won't
			//prevent us from editing it while in a shell-making library, since we don't honor this
			//tag in shell-making libraries.
			if(_isShellLibrary)
				n.SetAttribute("content", "translationOnly");
			else
			{
				n.SetAttribute("content", "open");
			}
		}

		public static void SetupPage(XmlElement pageDiv, string isoCode)
		{
			MakeVernacularElementsForPage(pageDiv, isoCode);

			// a page might be "extra" as far as the template is concerned, but
			// once a page is inserted into book (which may become a shell), it's
			// just a normal page
			pageDiv.SetAttribute("data-page", pageDiv.GetAttribute("data-page").Replace("extra", "").Trim());

			//BookStorage.HideAllTextAreasThatShouldNotShow(pageDiv, isoCode, string.Empty);


			//GatherBracketTemplates(pageDiv);
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

		/// <summary>
		/// For each group of textareas in the div which have lang attributes, make a new text area
		/// with the lang code of the vernacular
		/// </summary>
		/// <param name="pageDiv"></param>
		public static void MakeVernacularElementsForPage(XmlElement pageDiv, string isoCode)
		{
			foreach (var element in GetTextGroupsInSinglePageDiv(pageDiv, isoCode))
			{
				MakeVernacularElementForOneGroup(element, isoCode, "textarea");
			}
			foreach (var element in GetParagraphsWithFieldsAndTextInSinglePageDiv(pageDiv))
			{
				MakeVernacularElementForOneGroup(element, isoCode, "p");
			}
			//any text areas which still don't have a language, set them to the vernacular (this is used for simple templates (non-shell pages))
			foreach (XmlElement textarea in  pageDiv.SafeSelectNodes(string.Format("//textarea[not(@lang)]")))
			{
				textarea.SetAttribute("lang", isoCode);
			}
		}

		private static void MakeVernacularElementForOneGroup(XmlElement groupElement, string isoCode, string elementName)
		{
			//there may be several (english, Tok Pisin, etc.), but we just grab the first one and copy it
			//for the vernacular
			//could not get this to work: var textareas = SafeSelectNodes(pageDiv, string.Format("//textarea[@id='{0}']", groupId));

//            string nonParagraphElementSelector = "/" + elementName;

			/* we aren't fishing for something underneath the paragraph level,
			 * we're actuallylooking for simple paragraphs that are in a
			 * language (e.g. they'd be non-editable areas where we're repeating
			 * the value of some variable you can edit elsewhere)
			 */
//            if (elementName.ToLower() == "p")
//                nonParagraphElementSelector = "";

			//TODO: This is Broken, so when we pass in a single page, it never finds any text areas
//            XmlNodeList editableElementsWithinTheIndicatedParagraph =
//                pageDiv.SafeSelectNodes(string.Format("//div[@id='{0}']//p[@id='{1}']" + nonParagraphElementSelector,
//                                                      pageDiv.GetAttribute("id"), groupId));

			XmlNodeList editableElementsWithinTheIndicatedParagraph = groupElement.SafeSelectNodes(elementName);

			if (editableElementsWithinTheIndicatedParagraph.Count == 0)
				return;

//            if (editableElementsWithinTheIndicatedParagraph.Count == 0)
//            {
//                //hack, which should only bear fruit when we're being called with a single page during template page insertion
//
//                editableElementsWithinTheIndicatedParagraph = pageDiv.SafeSelectNodes(string.Format("//p[@id='{0}']"+nonParagraphElementSelector, groupId));
//			if (editableElementsWithinTheIndicatedParagraph.Count == 0)
//				return;
//
//              }

			var alreadyInVernacular = from XmlElement x in editableElementsWithinTheIndicatedParagraph
									  where x.GetAttribute("lang") == isoCode
									  select x;
			if (alreadyInVernacular.Count() > 0)
				return;
			//don't mess with this set, it already has a vernacular (this will happen when we're editing a shellbook, not just using it to make a vernacular edition)

			if (ContainsClass(editableElementsWithinTheIndicatedParagraph[0], "-bloom-showNational"))
				return;

			XmlElement prototype = editableElementsWithinTheIndicatedParagraph[0] as XmlElement;
			//no... shellbooks should have lang on all, but what would we do for simple templates? //Debug.Assert(prototype.HasAttribute("lang"));
			if (prototype.HasAttribute("lang"))
			{
				if (elementName == "p") // don't leave copies around from the template language
				{
					prototype.SetAttribute("lang", isoCode);
				}
				else // for textareas, we *do* want copies around, because they are used for prompting in shellbooks
				{
					XmlElement vernacularCopy = (XmlElement) prototype.ParentNode.InsertAfter(prototype.Clone(), prototype);
					vernacularCopy.SetAttribute("lang",isoCode);
					//we don't need textarea ids.   //vernacularCopy.SetAttribute("id", Guid.NewGuid().ToString());
					//but we should make sure if there is an id, get rid of it, because we don't want 2 elements with the same id
					vernacularCopy.RemoveAttribute("id");

					vernacularCopy.InnerText = string.Empty;
				}
			}
		}

		/// <summary>
		/// All textareas which are just the same thing in different languages must by contained within a paragraph.
		/// </summary>
		/// <param name="pageDiv"></param>
		/// <returns></returns>
		private static IEnumerable<XmlElement> GetTextGroupsInSinglePageDiv(XmlElement pageDiv, string isoCode)
		{
			foreach (XmlElement textArea in pageDiv.SafeSelectNodes("//textarea"))
			{
				if (textArea.ParentNode.Name.ToLower() != "p")
				{
					//maybe not... if we don't want it to be editable but stay in the national language....
					//Debug.Faile("All textareas need to be wrapped in a paragaraph");
					continue;//ignore it
				}

				yield return (XmlElement) textArea.ParentNode;
			}
		}


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
				File.Copy(filePath, Path.Combine(destinationPath, Path.GetFileName(filePath)));
			}
			foreach (var dirPath in Directory.GetDirectories(sourcePath))
			{
				CopyFolder(dirPath, Path.Combine(destinationPath, Path.GetFileName(dirPath)));
			}
		}
	}
}
