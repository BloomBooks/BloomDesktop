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

			// We use the "initial name" to make the intial copy, and it gives us something
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
			var candidates = from x in Directory.GetFiles(folder, "*.htm*")
							 where !(x.ToLowerInvariant().EndsWith("configuration.html"))
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

			//Remove from the new book any div-pages labelled as "extraPage"
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

			InjectXMatter(initialPath, storage, sizeAndOrientation);

			SetLineageAndId(storage, sourceFolderPath);

			SetBookTitle(storage, bookData, usingTemplate);



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

			//REVIEW this actually undoes the setting of the intial files name:
			//      storage.UpdateBookFileAndFolderName(_librarySettings);
			return storage.FolderPath;
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

		private static void SetBookTitle(BookStorage storage, BookData bookData, bool usingTemplate)
		{
			//This is what we were trying to do: there was a defaultNameForDerivedBooks meta tag in the html
			//which had no language code. It worked fine for English, e.g., naming new English books
			//"My Book" or "My Dicionary" or whatever.
			//But in other cases, it actually hurt becuase that English name would be hidden down in the new
			//book, where the author wouldn't see it. But some consumer, using English, would see it, and
			//"My Book" is a pretty dumb name for tha carefully prepared book to be listed under.
			//
			//Now, if we are making this book from a shell book, we can keep whatever (title,language) pairs it has.
			//Those will be just fine, for example, if we have English as one of our national langauges and so get
			// "vaccinations" for free wihtout having to type that in again.
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


				//by default, this comes from the collection, but the book can select one, inlucing "null" to select the factory-supplied empty xmatter
				var xmatterName = storage.Dom.GetMetaValue("xmatter", _collectionSettings.XMatterPackName);

				var helper = new XMatterHelper(storage.Dom, xmatterName, _fileLocator);
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
			// of editing the acknowledgements for translated version.

			//It seems to me at the moment (May 2014) that the time to mark something as locked down should
			//be when the they create a book based on a source-with-content book. So the current approach
			//below, of pre-locking it, would go away.

			if(_isSourceCollection)
			{
				storage.Dom.UpdateMetaElement("lockedDownAsShell", "true");
			}

#if maybe //hard to pin down when a story primer, dictionary, etc. also becomes a new "source for new shells"
			//things like picture dictionaries could be used repeatedly
			//but things from Basic Book are normally not.
			var x = GetMetaValue(storage.Dom, "DerivativesAreSuitableForMakingShells", "false");
#else
			var x = false;
#endif
			storage.MetaData.IsSuitableForMakingShells = x;
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
			foreach (XmlElement element in elementOrDom.SafeSelectNodes(".//*[@data-metalanguage]"))
			{
				string lang = "";
				string metaLanguage = element.GetStringAttribute("data-metalanguage").Trim();
				switch (metaLanguage)
				{
					case "V":
						lang = settings.Language1Iso639Code;
						break;
					case "N1":
						lang = settings.Language2Iso639Code;
						break;
					case "N2":
						lang = settings.Language3Iso639Code;
						break;
					default:
						var msg = "Element called for meta language '" + metaLanguage + "', which is unrecognized.";
						Debug.Fail(msg);
						Logger.WriteEvent(msg);
						continue;
						break;
				}
				element.SetAttribute("lang", lang);

				// As an aside: if the field also has a class "bloom-copyFromOtherLanguageIfNecessary", then elsewhere we will copy from the old
				// national language (or regional, or whatever) to this one if necessary, so as not to lose what they had before.

			}
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
				if (new String[] {".jade", ".less"}.Any(ex => ex == ext))
					continue;
				RobustFile.Copy(filePath, Path.Combine(destinationPath, Path.GetFileName(filePath)));
			}
			foreach (var dirPath in Directory.GetDirectories(sourcePath))
			{
				//any files found under "template" will not be copied. At the moment (Aug 2015), this is only
				//thumbnail svgs, but we could move readme's and such in there
				if (Path.GetFileName(dirPath).ToLowerInvariant() != "template")
				{
					CopyFolder(dirPath, Path.Combine(destinationPath, Path.GetFileName(dirPath)));
				}
			}
		}
	}
}
