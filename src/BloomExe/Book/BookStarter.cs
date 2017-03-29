using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Collection;
using L10NSharp;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;
using SIL.Xml;

namespace Bloom.Book
{
	/// <summary>
	/// Creates the files for a new blank book from a template book
	/// </summary>
	public class BookStarter
	{
		private readonly IFileLocator _fileLocator;
		private readonly BookStorage.Factory _bookStorageFactory;
		private readonly CollectionSettings _collectionSettings;
		private bool _isSourceCollection;

		public delegate BookStarter Factory();//autofac uses this

		public BookStarter(IChangeableFileLocator fileLocator, BookStorage.Factory bookStorageFactory, CollectionSettings collectionSettings)
		{
			_fileLocator = fileLocator;
			_bookStorageFactory = bookStorageFactory;
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

			// We use the "initial name" to make the initial copy, and it gives us something
			//to name the folder and file until such time as the user enters a title in for the book.
			string initialBookName = GetInitialName(sourceBookFolder, parentCollectionPath);
			var newBookFolder = Path.Combine(parentCollectionPath, initialBookName);
			CopyFolder(sourceBookFolder, newBookFolder);
			//if something bad happens from here on out, we need to delete that folder we just made
			try
			{
				var oldNamedFile = Path.Combine(newBookFolder, Path.GetFileName(GetPathToHtmlFile(sourceBookFolder)));
				var newNamedFile = Path.Combine(newBookFolder, initialBookName + ".htm");
				RobustFile.Move(oldNamedFile, newNamedFile);

				//the destination may change here...
				newBookFolder = SetupNewDocumentContents(sourceBookFolder, newBookFolder);

				if(OnNextRunSimulateFailureMakingBook)
					throw new ApplicationException("Simulated failure for unit test");

			}
			catch (Exception)
			{
				SIL.IO.RobustIO.DeleteDirectory(newBookFolder, true);
				throw;
			}
			return newBookFolder;
		}

		private string GetPathToHtmlFile(string folder)
		{
			// BL-4160 don't put an asterisk after the .htm. It is unnecessary as this search pattern
			// already returns both *.htm and *.html, but NOT *.htm.xyz [returns *.html only for Windows]
			// For both, "*.htm?" should work, but it doesn't return *.htm on Linux [Mono4 bug?].
			var candidates = from x in Directory.GetFiles(folder, "*.htm")
							 where !(Path.GetFileName(x).ToLowerInvariant().StartsWith("configuration.htm"))
							 select x;
			if (!candidates.Any())
				candidates = from x in Directory.GetFiles(folder, "*.html")
							 where !(Path.GetFileName(x).ToLowerInvariant().StartsWith("configuration.html"))
							 select x;
			if (candidates.Count() == 1)
				return candidates.First();
			else
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(
					"There should only be a single htm(l) file in each folder ({0}). [not counting configuration.html]", folder);
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

		private string SetupNewDocumentContents(string sourceFolderPath, string initialPath)
		{
			var storage = _bookStorageFactory(initialPath);
			bool usingTemplate = storage.MetaData.IsSuitableForMakingShells;

			var bookData = new BookData(storage.Dom, _collectionSettings, null);
			UpdateEditabilityMetadata(storage);//Path.GetFileName(initialPath).ToLower().Contains("template"));

			// NB: For a new book based on a page template, I think this should remove *everything*,
			// because the rest is in the xmatter.
			// For shells, we'll still have pages.

			//Remove from the new book any div-pages labeled as "extraPage"
			for (var initialPageDivs = storage.Dom.SafeSelectNodes("/html/body/div[contains(@data-page,'extra')]");
				initialPageDivs.Count > 0;
				initialPageDivs = storage.Dom.SafeSelectNodes("/html/body/div[contains(@data-page,'extra')]"))
			{
				initialPageDivs[0].ParentNode.RemoveChild(initialPageDivs[0]);
			}

			XMatterHelper.RemoveExistingXMatter(storage.Dom);

			bookData.RemoveAllForms("ISBN");//ISBN number of the original doesn't apply to derivatives

			var sizeAndOrientation = Layout.FromDomAndChoices(storage.Dom, Layout.A5Portrait, _fileLocator);

			//Note that we do this *before* injecting frontmatter, which is more likely to have a good reason for having English
			//Useful for things like Primers. Note that Lorem Ipsum and prefixing all text with "_" also work.
			//			if ("true"==GetMetaValue(storage.Dom.RawDom, "removeTranslationsWhenMakingNewBook", "false"))
			//			{
			//				ClearAwayAllTranslations(storage.Dom.RawDom);
			//			}

			ProcessXMatterMetaTags(storage);
			if (usingTemplate)
				RemoveLicenseFromShell(storage);

			InjectXMatter(initialPath, storage, sizeAndOrientation);

			SetLineageAndId(storage, sourceFolderPath);

			SetBookTitle(storage, bookData, usingTemplate);

			if(!usingTemplate)
			{
				BookCopyrightAndLicense.SetOriginalCopyrightAndLicense(storage.Dom, bookData, _collectionSettings);
			}

			//Few sources will have this set at all. A template picture dictionary is one place where we might expect it to call for, say, bilingual
			int multilingualLevel = int.Parse(GetMetaValue(storage.Dom.RawDom, "defaultMultilingualLevel", "1"));
			TranslationGroupManager.SetInitialMultilingualSetting(bookData, multilingualLevel, _collectionSettings);

			var sourceDom = XmlHtmlConverter.GetXmlDomFromHtmlFile(sourceFolderPath.CombineForPath(Path.GetFileName(GetPathToHtmlFile(sourceFolderPath))), false);

			//If this is a shell book, make elements to hold the vernacular
			foreach (XmlElement div in storage.Dom.RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]"))
			{
				XmlElement sourceDiv = sourceDom.SelectSingleNode("//div[@id='"+div.GetAttribute("id")+"']") as XmlElement;
				SetupIdAndLineage(sourceDiv, div);
				SetupPage(div, _collectionSettings, null, null);
			}

			ClearAwayDraftText(storage.Dom.RawDom);

			storage.Save();

			//REVIEW this actually undoes the setting of the initial files name:
			//      storage.UpdateBookFileAndFolderName(_librarySettings);
			return storage.FolderPath;
		}

		/// <summary>
		/// If the new book is a shell, it should not have a pre-determined license.
		/// A default will be filled in later.
		/// </summary>
		/// <param name="storage"></param>
		private void RemoveLicenseFromShell(BookStorage storage)
		{
			var dataDiv = storage.Dom.SelectSingleNode("//div[@id='bloomDataDiv']");
			if (dataDiv == null)
				return;
			// There just might be multiple ones; e.g., Vaccinations (though we don't do it for that
			// since it's a shell) has three with no lang, en, and *.
			foreach (var licenseDiv in dataDiv.SelectNodes("./div[@data-book='licenseUrl']").Cast<XmlElement>().ToArray())
			{
				licenseDiv.ParentNode.RemoveChild(licenseDiv);
			}
			foreach (var descDiv in dataDiv.SelectNodes("./div[@data-book='licenseDescription']").Cast<XmlElement>().ToArray())
			{
				descDiv.ParentNode.RemoveChild(descDiv);
			}
			foreach (var notesDiv in dataDiv.SelectNodes("./div[@data-book='licenseNotes']").Cast<XmlElement>().ToArray())
			{
				notesDiv.ParentNode.RemoveChild(notesDiv);
			}
		}

		/// <summary>
		/// TemplateStarter intentionally makes its children (user's custom templates) have a special xmatter.
		/// But books creates with those custom templates should just use whatever xmatter normal books use,
		/// at least until we allow users to choose different ones, or allow template makers to specify which
		/// xmatter children should use.
		/// </summary>
		private static void ProcessXMatterMetaTags(BookStorage storage)
		{
			// Don't copy the parent's xmatter if they specify it
			storage.Dom.RemoveMetaElement("xmatter");

			// But if the parent says what children should use, then use that.
			if(storage.Dom.HasMetaElement("xmatter-for-children"))
			{
				storage.Dom.UpdateMetaElement("xmatter", storage.Dom.GetMetaValue("xmatter-for-children", ""));
			}
			// Children, but not grand-children. So we remove this so the next generation doesn't see it.
			storage.Dom.RemoveMetaElement("xmatter-for-children");
		}

		private void SetLineageAndId(BookStorage storage, string sourceFolderPath)
		{
			string parentId = null;
			string lineage = null;
			if (RobustFile.Exists(Path.Combine(sourceFolderPath, BookInfo.MetaDataFileName)))
			{
				var sourceMetaData = new BookInfo(sourceFolderPath, false);
				parentId = sourceMetaData.Id;
				lineage = sourceMetaData.BookLineage;
			}
			else
			{
				// No parent meta.json, try for legacy embedded metadata in html
				parentId = GetMetaValue(storage.Dom.RawDom, "bloomBookId", "");
				lineage = GetMetaValue(storage.Dom.RawDom, "bloomBookLineage", "");
				if (string.IsNullOrEmpty(lineage))
				{
					lineage = GetMetaValue(storage.Dom.RawDom, "bookLineage", ""); //try the old name for this value
				}
			}

			if (!string.IsNullOrEmpty(lineage))
				lineage += ",";
			if (!string.IsNullOrEmpty(parentId))
			{
				storage.MetaData.BookLineage = lineage + parentId;
			}
			storage.MetaData.Id = Guid.NewGuid().ToString();
			storage.Dom.RemoveMetaElement("bloomBookLineage"); //old metadata
			storage.Dom.RemoveMetaElement("bookLineage"); // even older name
		}

//		private static void ClearAwayAllTranslations(XmlNode element)
//		{
//
//			foreach (XmlNode node in element.ChildNodes)//.SafeSelectNodes(String.Format("//*[@lang='{0}']", _collectionSettings.Language1Iso639Code)))
//            {
//                if (node.NodeType == XmlNodeType.Text)
//                {
//                    node.InnerText = String.Empty;
//                }
//                else
//                {
//					ClearAwayAllTranslations(node);
//                }
//            }
//			//after removing text, we could still be left with the line breaks between them
//			if (element.ChildNodes != null)
//			{
//				var possibleBrNodes = new List<XmlNode>();
//				possibleBrNodes.AddRange(from XmlNode x in element.ChildNodes select x);
//				foreach (XmlNode node in possibleBrNodes)
//				{
//					if (node.NodeType == XmlNodeType.Element && node.Name.ToLower() == "br")
//					{
//						node.ParentNode.RemoveChild(node);
//					}
//				}
//			}
//		}

		/// <summary>
		/// When building on templates, we usually want to have some sample text, but don't let them bleed through to what the user sees
		/// </summary>
		/// <param name="element"></param>
		private static void ClearAwayDraftText(XmlNode element)
		{
			//clear away everything done in language "x"
			var nodesInLangX = new List<XmlNode>();
			nodesInLangX.AddRange(from XmlNode x in element.SafeSelectNodes(String.Format("//*[@lang='x']")) select x);
			foreach (XmlNode node in nodesInLangX)
			{
				node.ParentNode.RemoveChild(node);
			}
		}
		/// <summary>
		/// This clears the description from a page as it comes in.
		/// </summary>
		/// <remarks>
		/// In a normal book,
		/// well there is no place to see the description once it is added. But if
		/// we are building a template, then that description will be shown when
		/// someone uses this template (in the Add Page dialog). The description is
		/// something like "A blank page that allows to create custom items"; once
		/// you modify that page, the description stops being accurate.
		/// Now, I can think of scenarios where you'd want to keep description.
		/// E.g. you have an alphabet chart, you add that to another template where hey, 
		/// it's still an alphabet chart. This is a judgment call, which is worse. 
		/// I'm judging that it's worse to have an out-of-date description than a missing one.
		/// </remarks>
		private static void ClearAwayPageDescription(XmlNode pageDiv)
		{
			//clear away all pageDescription divs except the English one
			var nonEnglishDescriptions = new List<XmlNode>();
			nonEnglishDescriptions.AddRange(from XmlNode x in pageDiv.SafeSelectNodes("//div[contains(@class, 'pageDescription') and @lang != 'en']") select x);
			foreach (var node in nonEnglishDescriptions)
			{
				node.ParentNode.RemoveChild(node);
			}
			// now leave the English Description as empty; serving as a placeholder if we are making a template
			// and want to go into the html and add a description
			var description = pageDiv.SelectSingleNode("//div[contains(@class, 'pageDescription')]");
			if(description != null)
			{
				description.InnerXml = "";
			}
		}

		private void SetBookTitle(BookStorage storage, BookData bookData, bool usingTemplate)
		{
			//This is what we were trying to do: there was a defaultNameForDerivedBooks meta tag in the html
			//which had no language code. It worked fine for English, e.g., naming new English books
			//"My Book" or "My Dictionary" or whatever.
			//But in other cases, it actually hurt because that English name would be hidden down in the new
			//book, where the author wouldn't see it. But some consumer, using English, would see it, and
			//"My Book" is a pretty dumb name for the carefully prepared book to be listed under.
			//
			//Now, if we are making this book from a shell book, we can keep whatever (title,language) pairs it has.
			//Those will be just fine, for example, if we have English as one of our national languages and so get
			// "vaccinations" for free without having to type that in again.
			//
			//But if we are making this from a *template*, then we *don't* want to keep the various ways to say the
			//name of the template. Seeing "Basic Book" as the name of a resulting shell is not helpful.

			//We just don't have a use for this at all anymore: nice idea, doesn't really work:
			storage.Dom.RemoveMetaElement("defaultNameForDerivedBooks");

			// Clear these out let other code set again when there is a real title.
			storage.MetaData.Title = "";
			storage.Dom.Title = "";

			//If we're making a book from a template, remove all the titles in all languages
			if(usingTemplate)
			{
				bookData.RemoveAllForms("bookTitle");
			}
			// If we're making a Template, we really want its title to include Template
			// (in hopes the user will keep it at the end so the pages can be used in Add Page)
			if (storage.MetaData.IsSuitableForMakingShells)
			{
				storage.MetaData.Title = "My Template";
				storage.Dom.Title = "My Template";
				storage.Dom.SetBookSetting("bookTitle", "en", "My Template");
				// Yes, we want the English word Template in the vernacular Title. Ugly, but that's
				// what determines the file name, and that's what determines whether Add Page will
				// include it.
				storage.Dom.SetBookSetting("bookTitle", _collectionSettings.Language1Iso639Code, "My Template");
			}
		}

		private void InjectXMatter(string initialPath, BookStorage storage, Layout sizeAndOrientation)
		{
			//now add in the xmatter from the currently selected xmatter pack
			if (!TestingSoSkipAddingXMatter)
			{
				var data = new DataSet();
				Debug.Assert(!string.IsNullOrEmpty(_collectionSettings.Language1Iso639Code));
				Debug.Assert(!string.IsNullOrEmpty(_collectionSettings.Language2Iso639Code));
				data.WritingSystemAliases.Add("V", _collectionSettings.Language1Iso639Code);
				data.WritingSystemAliases.Add("N1", _collectionSettings.Language2Iso639Code);
				data.WritingSystemAliases.Add("N2", _collectionSettings.Language3Iso639Code);

				var helper = new XMatterHelper(storage.Dom, _collectionSettings.XMatterPackName, _fileLocator);
				helper.FolderPathForCopyingXMatterFiles = storage.FolderPath;
				helper.InjectXMatter(data.WritingSystemAliases, sizeAndOrientation);
				//TranslationGroupManager.PrepareDataBookTranslationGroups(storage.Dom,languages);
			}
		}

		private void RemoveDataDivElement(XmlNode dom, string key)
		{
			var dataDiv = HtmlDom.GetOrCreateDataDiv(dom);
			foreach (XmlNode e in dataDiv.SafeSelectNodes(string.Format("div[@data-book='{0}']", key)))
			{
				dataDiv.RemoveChild(e);
			}
		}

		private void UpdateEditabilityMetadata(BookStorage storage)
		{

			//Here's the logic: If we're in a shell-making library, then it's safe to say that a newly-
			//created book is going to be a shell. Any derivatives will then act as shells.  But it won't
			//prevent us from editing it while in a shell-making collections, since we don't honor this
			//tag in shell-making collections.

			//The problem is, if you make a book in some vernacular library, then share it so that others
			//can use it as a shell, then (as of version 2) Bloom doesn't have a way of realizing that it's
			//being used as a shell. So everything is editable (not a big deal) but you're also locked out
			// of editing the acknowledgments for translated version.

			//It seems to me at the moment (May 2014) that the time to mark something as locked down should
			//be when the they create a book based on a source-with-content book. So the current approach
			//below, of pre-locking it, would go away.

			// JohnT: added the possibility that the source book is 'suitableForMakingTemplates', that is,
			// a template factory like the Template Starter book. In this case we want the resulting book
			// to be a template. Note that the initial state of storage is a copy of the template.
			// (The only way suitableForMakingTemplates currently becomes true is when loaded that way
			// from meta.json, which only happens if someone edited it by hand to be that way.)
			// If we're making a template, the resulting book needs to be suitableForMakingShells
			// and also needs to NOT be RecordedAsLockedDown, because that suppresses options
			// we want in the options tab.
			// If we change this see also Book.SwitchSuitableForMakingShells().
			if (_isSourceCollection && !storage.MetaData.IsSuitableForMakingTemplates)
			{
				storage.Dom.UpdateMetaElement("lockedDownAsShell", "true");
			}

			storage.MetaData.IsSuitableForMakingShells = storage.MetaData.IsSuitableForMakingTemplates;
			// a newly created book is never suitable for making templates, even if its source was.
			storage.MetaData.IsSuitableForMakingTemplates = false;
		}


		public static void SetupPage(XmlElement pageDiv, CollectionSettings collectionSettings, string contentLanguageIso1, string contentLanguageIso2)//, bool inShellMode)
		{
			TranslationGroupManager.PrepareElementsInPageOrDocument(pageDiv, collectionSettings);

			SetLanguageForElementsWithMetaLanguage(pageDiv, collectionSettings);

			// a page might be "extra" as far as the template is concerned, but
			// once a page is inserted into book (which may become a shell), it's
			// just a normal page
			pageDiv.SetAttribute("data-page", pageDiv.GetAttribute("data-page").Replace("extra", "").Trim());
			ClearAwayDraftText(pageDiv);
			ClearAwayPageDescription(pageDiv);
		}

		/// <summary>
		/// In xmatter, text fields are normally labeled with a "meta" language code, like "N1" for first national language.
		/// This method detects those and then looks them up, returning the actual language code in use at the moment.
		/// </summary>
		/// <remarks>This is a little uncomfortable in this class, as this feature is not currently used in any
		/// bloom-translationGroup elements.
		/// </remarks>
		public static void SetLanguageForElementsWithMetaLanguage(XmlNode elementOrDom, CollectionSettings settings)
		{
//			foreach (XmlElement element in elementOrDom.SafeSelectNodes(".//*[@data-metalanguage]"))
//			{
//				string lang = "";
//				string metaLanguage = element.GetStringAttribute("data-metalanguage").Trim();
//				switch (metaLanguage)
//				{
//					case "V":
//						lang = settings.Language1Iso639Code;
//						break;
//					case "N1":
//						lang = settings.Language2Iso639Code;
//						break;
//					case "N2":
//						lang = settings.Language3Iso639Code;
//						break;
//					default:
//						var msg = "Element called for meta language '" + metaLanguage + "', which is unrecognized.";
//						Debug.Fail(msg);
//						Logger.WriteEvent(msg);
//						continue;
//						break;
//				}
//				element.SetAttribute("lang", lang);
//
//				// As an aside: if the field also has a class "bloom-copyFromOtherLanguageIfNecessary", then elsewhere we will copy from the old
//				// national language (or regional, or whatever) to this one if necessary, so as not to lose what they had before.
//
//			}
		}
		public static void SetupIdAndLineage(XmlElement parentPageDiv, XmlElement childPageDiv)
		{
			//NB: this works even if the parent and child are the same, which is the case when making a new book
			//but not when we're adding an individual template page. (Later: Huh?)

			childPageDiv.SetAttribute("id", Guid.NewGuid().ToString());

			if (parentPageDiv != null) //until we get the xmatter also coming in, xmatter pages will have no parentDiv available
			{
				string parentId = parentPageDiv.GetAttribute("id");
				string parentLineage = parentPageDiv.GetOptionalStringAttribute("data-pagelineage", string.Empty);
				childPageDiv.SetAttribute("data-pagelineage", (parentLineage + ";" + parentId).Trim(new char[] {';'}));
			}
		}

		private string GetInitialName(string sourcePath, string parentCollectionPath)
		{
			var name = BookStorage.SanitizeNameForFileSystem(UntitledBookName);
			return BookStorage.GetUniqueFolderName(parentCollectionPath, name);
		}

		public static string UntitledBookName
		{
			get
			{
				return LocalizationManager.GetString("EditTab.NewBookName", "Book",
					"Default file and folder name when you make a new book, but haven't give it a title yet.");
			}
		}

		private static void CopyFolder(string sourcePath, string destinationPath)
		{
			Directory.CreateDirectory(destinationPath);
			foreach (var filePath in Directory.GetFiles(sourcePath))
			{
				//better to not just copy the old thumbnail, as the on in the library may well need to look different
				if (Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant() == "thumbnail")
					continue;
				if (Path.GetFileNameWithoutExtension(filePath).StartsWith(".")) //.guidsForInstaller.xml
					continue;
				var ext = Path.GetExtension(filePath).ToLowerInvariant();
				// We don't need to copy any backups, and we don't want userPrefs because they are likely
				// to include a page number and we want the new book to open at the cover.
				if (new String[] {".jade", ".less", ".md", ".bak", ".userprefs"}.Any(ex => ex == ext))
					continue;
				RobustFile.Copy(filePath, Path.Combine(destinationPath, Path.GetFileName(filePath)));
			}
			foreach (var dirPath in Directory.GetDirectories(sourcePath))
			{
				//any files found under "template" will not be copied. At the moment (Aug 2015), this is only
				//thumbnail svgs, but we could move readme's and such in there
				var directoriesToSkip = new[] {"template", Book.ReadMeImagesFolderName.ToLowerInvariant() };
				if (!directoriesToSkip.Contains(Path.GetFileName(dirPath).ToLowerInvariant()))
				{
					CopyFolder(dirPath, Path.Combine(destinationPath, Path.GetFileName(dirPath)));
				}
			}
		}
	}
}
