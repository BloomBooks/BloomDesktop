using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Collection;
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
				newBookFolder = SetupNewDocumentContents(sourceBookFolder, newBookFolder);

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

		private string SetupNewDocumentContents(string sourceFolderPath, string initialPath)
		{
			var storage = _bookStorageFactory(initialPath);
			var bookData = new BookData(storage.Dom, _collectionSettings, null);
			UpdateEditabilityMetadata(storage);//Path.GetFileName(initialPath).ToLower().Contains("template"));

			//NB: for a new book based on a page template, I think this should remove *everything*, because the rest is in the xmatter
			//	for shells, we'll still have pages.
			//Remove from the new book any div-pages labelled as "extraPage"
			foreach (XmlElement initialPageDiv in storage.Dom.SafeSelectNodes("/html/body/div[contains(@data-page,'extra')]"))
			{
				initialPageDiv.ParentNode.RemoveChild(initialPageDiv);
			}

			XMatterHelper.RemoveExistingXMatter(storage.Dom);

			bookData.RemoveAllForms("ISBN");//ISBN number of the original doesn't apply to derivatives

			var sizeAndOrientation = Layout.FromDom(storage.Dom, Layout.A5Portrait);

			//Note that we do this *before* injecting frontmatter, which is more likely to have a good reason for having English
			//Useful for things like Primers. Note that Lorem Ipsum and prefixing all text with "_" also work.
//			if ("true"==GetMetaValue(storage.Dom.RawDom, "removeTranslationsWhenMakingNewBook", "false"))
//			{
//				ClearAwayAllTranslations(storage.Dom.RawDom);
//			}

			InjectXMatter(initialPath, storage, sizeAndOrientation);

			SetLineageAndId(storage);

			SetBookTitle(storage, bookData);



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

		private void SetLineageAndId(BookStorage storage)
		{
			var parentId = GetMetaValue(storage.Dom.RawDom, "bloomBookId", "");

			var lineage = GetMetaValue(storage.Dom.RawDom, "bloomBookLineage", "");
			if (string.IsNullOrEmpty(lineage))
			{
				lineage = GetMetaValue(storage.Dom.RawDom, "bookLineage", ""); //try the old name for this value
			}
			if (!string.IsNullOrEmpty(lineage))
				lineage += ",";
			if (!string.IsNullOrEmpty(parentId))
			{
				storage.Dom.UpdateMetaElement("bloomBookLineage", lineage + parentId);
			}
			storage.Dom.UpdateMetaElement("bloomBookId",Guid.NewGuid().ToString());
			storage.Dom.RemoveMetaElement("bookLineage");//old name
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

		private static void SetBookTitle(BookStorage storage, BookData bookData)
		{
//NB: no multi-lingual name suggestion ability yet

			//otherwise, the case where there is no defaultNameForDerivedBooks, we just want to use the names
			//that the shell used, e.g. "Vaccinations".
			//We don't have to do anything special to get that.
			string kdefaultName = null;
			var nameSuggestion = storage.Dom.GetMetaValue("defaultNameForDerivedBooks", kdefaultName);
//	        var nameSuggestion = storage.Dom.SafeSelectNodes("//head/meta[@name='defaultNameForDerivedBooks']");

			if(nameSuggestion!=null)
				bookData.Set("bookTitle",nameSuggestion,"en");
			storage.Dom.RemoveMetaElement("defaultNameForDerivedBooks");

//	        //var name = "New Book"; //shouldn't rarel show up, because it will be overriden by the meta tag
//	        if (nameSuggestion.Count > 0)
//	        {
//	            var metaTag = (XmlElement) nameSuggestion[0];
//	            var name = metaTag.GetAttribute("content");
//	            bookData.SetDataDivBookVariable("bookTitle", name, "en");
//	            metaTag.ParentNode.RemoveChild(metaTag);
//	        }
//	        else
//	        {
//
//	        }
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
			}
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

		private void UpdateEditabilityMetadata(BookStorage storage)
		{

			//Here's the logic: If we're in a shell-making library, then it's safe to say that a newly-
			//created book is going to be a shell. Any derivatives will then act as shells.  But it won't
			//prevent us from editing it while in a shell-making collections, since we don't honor this
			//tag in shell-making collections.
			if(_isSourceCollection)
			{
				storage.Dom.UpdateMetaElement("lockedDownAsShell", "true");
			}

#if maybe //hard to pin down when a story primer, dictionary, etc. also becomes a new "source for new shells"
			//things like picture dictionaries could be used repeatedly
			//but things from Basic Book are normally not.
			var x = GetMetaValue(storage.Dom, "DerivativesAreSuitableForMakingShells", "false");
#else
			var x = "false";
#endif
			storage.Dom.UpdateMetaElement("SuitableForMakingShells", x);
		}


		public static void SetupPage(XmlElement pageDiv, CollectionSettings collectionSettings, string contentLanguageIso1, string contentLanguageIso2)//, bool inShellMode)
		{
			TranslationGroupManager.PrepareElementsInPageOrDocument(pageDiv, collectionSettings);

			// a page might be "extra" as far as the template is concerned, but
			// once a page is inserted into book (which may become a shell), it's
			// just a normal page
			pageDiv.SetAttribute("data-page", pageDiv.GetAttribute("data-page").Replace("extra", "").Trim());
			ClearAwayDraftText(pageDiv);
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
			var storage = _bookStorageFactory(sourcePath);
			var name = storage.Dom.GetMetaValue("defaultNameForDerivedBooks",  Path.GetFileName(sourcePath));
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
