using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
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

			XMatterHelper.RemoveExistingXMatter(storage.Dom);

			//now add in the xmatter from the currently selected xmatter pack
			if (!TestingSoSkipAddingXMatter)
			{
				var data = new DataSet();
				Debug.Assert(!string.IsNullOrEmpty(_librarySettings.VernacularIso639Code));
				Debug.Assert(!string.IsNullOrEmpty(_librarySettings.NationalLanguage1Iso639Code));
				data.WritingSystemCodes.Add("V", _librarySettings.VernacularIso639Code);
				data.WritingSystemCodes.Add("N1", _librarySettings.NationalLanguage1Iso639Code);
				data.WritingSystemCodes.Add("N2", _librarySettings.NationalLanguage2Iso639Code);
				var helper = new XMatterHelper(storage.Dom,_librarySettings.XMatterPackName, _fileLocator);
				helper.FolderPathForCopyingXMatterFiles = storage.FolderPath;
				helper.InjectXMatter( data);
			}

			//If this is a shell book, make elements to hold the vernacular
			foreach (XmlElement div in storage.Dom.SafeSelectNodes("//div[contains(@class,'-bloom-page')]"))
			{
				SetupIdAndLineage(div, div);
				SetupPage(div, _librarySettings);
			}
			storage.Save();

			storage.UpdateBookFileAndFolderName(_librarySettings);
			return storage.FolderPath;
		}



		private void UpdateEditabilityIndicator(BookStorage storage)
		{

			//Here's the logic: If we're in a shell-making library, then it's safe to say that a newly-
			//created book is translationOnly. Any derivatives will then act as shells.  But it won't
			//prevent us from editing it while in a shell-making library, since we don't honor this
			//tag in shell-making libraries.
			if(_isShellLibrary)
				BookStorage.UpdateMetaElement(storage.Dom, "editability", "translationOnly");

			//otherwise, stick with whatever it came in with.  All shells will come in with translationOnly,
			//all templates will come in with 'open'.
//			else
//			{
//				n.SetAttribute("content", "open");
//			}
		}


		public static void SetupPage(XmlElement pageDiv, LibrarySettings librarySettings)//, bool inShellMode)
		{
			PrepareElementsOnPage(pageDiv, librarySettings);//, inShellMode);

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

		/// <summary>
		/// For each group of editable elements in the div which have lang attributes, make a new element
		/// with the lang code of the vernacular.
		/// Also enable/disable editting as warranted (e.g. in shell mode or not)
		/// </summary>
		/// <param name="pageDiv"></param>
		public static void PrepareElementsOnPage(XmlElement pageDiv, LibrarySettings librarySettings)//, bool inShellMode)
		{
			PrepareElementsOnPageOneLanguage(pageDiv, librarySettings.VernacularIso639Code);

			//why do this? well, for bilingual/trilingual stuff (e.g., a picture dictionary)
			BookStarter.PrepareElementsOnPageOneLanguage(pageDiv,librarySettings.NationalLanguage1Iso639Code);

			//nb: really we need to have a place where we list the bilgual/triligual desires, and that may be book specific
			if(!string.IsNullOrEmpty(librarySettings.NationalLanguage2Iso639Code))
			{
				BookStarter.PrepareElementsOnPageOneLanguage(pageDiv, librarySettings.NationalLanguage2Iso639Code);
			}
		}

		private static void PrepareElementsOnPageOneLanguage(XmlElement pageDiv, string isoCode)
		{
			foreach (var element in GetEditableGroupsInSinglePageDiv(pageDiv))
			{
				MakeVernacularElementForOneGroup(element, isoCode, "textarea");
				MakeVernacularElementForOneGroup(element, isoCode, "*[@contentEditable='true' or @contenteditable='true']");
			}
			foreach (var element in GetParagraphsWithFieldsAndTextInSinglePageDiv(pageDiv))
			{
				MakeVernacularElementForOneGroup(element, isoCode, "p");
			}
			//any text areas which still don't have a language, set them to the vernacular (this is used for simple templates (non-shell pages))
			foreach (
				XmlElement element in
					pageDiv.SafeSelectNodes(
						string.Format("//textarea[not(@lang)] | //*[(@contentEditable='true'  or @contenteditable='true') and not(@lang)]"))
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
		private static void MakeVernacularElementForOneGroup(XmlElement groupElement, string vernacularCode, string elementTag)
		{
			XmlNodeList editableElementsWithinTheIndicatedParagraph = groupElement.SafeSelectNodes(elementTag);

			if (editableElementsWithinTheIndicatedParagraph.Count == 0)
				return;

			var alreadyInVernacular = from XmlElement x in editableElementsWithinTheIndicatedParagraph
									  where x.GetAttribute("lang") == vernacularCode
									  select x;
			if (alreadyInVernacular.Count() > 0)//don't mess with this set, it already has a vernacular (this will happen when we're editing a shellbook, not just using it to make a vernacular edition)
				return;


			if (groupElement.SafeSelectNodes("ancestor-or-self::*[contains(@class,'-bloom-translationGroup')]").Count == 0)
				return;

			XmlElement prototype = editableElementsWithinTheIndicatedParagraph[0] as XmlElement;

			//REVIEW... shellbooks should have lang on all, but what would we do for simple templates? //Debug.Assert(prototype.HasAttribute("lang"));

			if (prototype.HasAttribute("lang"))
			{
				if (elementTag == "p") // don't leave copies around from the template language
				{
					prototype.SetAttribute("lang", vernacularCode);
				}
				else // for textareas, we *do* want copies around, because they are used for prompting in shellbooks
				{
					XmlElement vernacularCopy = (XmlElement) prototype.ParentNode.InsertAfter(prototype.Clone(), prototype);
					vernacularCopy.SetAttribute("lang",vernacularCode);
					//if there is an id, get rid of it, because we don't want 2 elements with the same id
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
		private static IEnumerable<XmlElement> GetEditableGroupsInSinglePageDiv(XmlElement pageDiv)
		{
			foreach (XmlElement element in pageDiv.SafeSelectNodes("//textarea | //*[(@contentEditable='true' or  @contenteditable='true')]"))
			{
				yield return (XmlElement) element.ParentNode;
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
