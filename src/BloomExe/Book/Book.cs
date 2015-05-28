﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.ImageProcessing;
using Bloom.Properties;
using Bloom.Publish;
using L10NSharp;
using MarkdownSharp;
using Palaso.Code;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Progress;
using Palaso.Reporting;
using Palaso.Text;
using Palaso.UI.WindowsForms.ClearShare;
using Palaso.Xml;
using Image = System.Drawing.Image;

namespace Bloom.Book
{
	public class Book
	{
		public delegate Book Factory(BookInfo info, IBookStorage storage);//autofac uses this
		public static Color[] CoverColors = new Color[] { Color.FromArgb(228, 140, 132), Color.FromArgb(176, 222, 228), Color.FromArgb(152, 208, 185), Color.FromArgb(194, 166, 191) };


		//We only randomize the initial value for each run. Without it, we were making a lot 
		// more red books than any other color, because the 
		//first book for a given run would always be red, and people are unlikely to make more
		//than one book per session.
		private static int _coverColorIndex=new Random().Next(CoverColors.Length-1);

		private readonly ITemplateFinder _templateFinder;
		private readonly CollectionSettings _collectionSettings;

		private readonly HtmlThumbNailer _thumbnailProvider;
		private readonly PageSelection _pageSelection;
		private readonly PageListChangedEvent _pageListChangedEvent;
		private readonly BookRefreshEvent _bookRefreshEvent;
		private readonly IBookStorage _storage;
		private List<IPage> _pagesCache;
		internal const string kIdOfBasicBook = "056B6F11-4A6C-4942-B2BC-8861E62B03B3";

		public event EventHandler ContentsChanged;

		private IProgress _log = new StringBuilderProgress();
		private bool _haveCheckedForErrorsAtLeastOnce;
		private readonly BookData _bookData;

		//for moq'ing only
		public Book()
		{
#if __MonoCS__
			// TODO: Fixme - refactor ErrorBook not to use this constructor.
			// Removing libtidy package from system is an easy way to get an ErrorBook.
			throw new ApplicationException("This should only be used for test code. ErrorBook uses it and causes uninitalized Books.");
#endif
		}

		public Book(BookInfo info, IBookStorage storage, ITemplateFinder templateFinder,
		   CollectionSettings collectionSettings, HtmlThumbNailer thumbnailProvider,
			PageSelection pageSelection,
			PageListChangedEvent pageListChangedEvent,
			BookRefreshEvent bookRefreshEvent)
		{
			BookInfo = info;

			Guard.AgainstNull(storage,"storage");

			// This allows the _storage to
			storage.MetaData = info;

			_storage = storage;

			//this is a hack to keep these two in sync (in one direction)
			_storage.FolderPathChanged +=(x,y)=>BookInfo.FolderPath = _storage.FolderPath;

			_templateFinder = templateFinder;

			_collectionSettings = collectionSettings;

			_thumbnailProvider = thumbnailProvider;
			_pageSelection = pageSelection;
			_pageListChangedEvent = pageListChangedEvent;
			_bookRefreshEvent = bookRefreshEvent;
			_bookData = new BookData(OurHtmlDom,
					_collectionSettings, UpdateImageMetadataAttributes);

			InjectStringListingActiveLanguagesOfBook(); 

			if (IsEditable && !HasFatalError)
			{
				_bookData.SynchronizeDataItemsThroughoutDOM();

				WriteLanguageDisplayStyleSheet(); //NB: if you try to do this on a file that's in program files, access will be denied
				OurHtmlDom.AddStyleSheet(@"languageDisplay.css");
			}
			// If it doesn't already have a cover color give it one.
			if (OurHtmlDom.SafeSelectNodes("//head/style/text()[contains(., 'coverColor')]").Count == 0)
			{
				InitCoverColor();
				if (info.IsEditable)
				{
					// make that cover color permanent!
					// We don't use simply Save() because that does some extra work we don't need here
					// and it causes at least one unit test to fail.
					_storage.Save();
				}
			}
			FixBookIdAndLineageIfNeeded();
			_storage.Dom.RemoveExtraContentTypesMetas();
			Guard.Against(OurHtmlDom.RawDom.InnerXml=="","Bloom could not parse the xhtml of this document");
		}

		public static Color NextBookColor()
		{
			return CoverColors[_coverColorIndex++ % CoverColors.Length];
		}

		public CollectionSettings CollectionSettings { get { return _collectionSettings; }}

		public void InvokeContentsChanged(EventArgs e)
		{
			EventHandler handler = ContentsChanged;
			if (handler != null) handler(this, e);
		}

		public enum BookType { Unknown, Template, Shell, Publication }

		/// <summary>
		/// If we have to just show title in one language, which should it be?
		/// Note, this isn't going to be the best for choosing a filename, which we are more likely to want in a national language
		/// </summary>
		public string TitleBestForUserDisplay
		{
			get
			{
				var title = _bookData.GetMultiTextVariableOrEmpty("bookTitle");
				var display = title.GetExactAlternative(_collectionSettings.Language1Iso639Code);

				if (string.IsNullOrEmpty(display))
				{
					//the SIL-LEAD project, SHRP (around 2012-2016) had books that just had an English name, before we changed Bloom
					//to not show English names. But the order was also critical. So we want those old books to go ahead and use their
					//English names.
					var englishTitle = title.GetExactAlternative("en").ToLowerInvariant();
					var SHRPMatches = new string[] {"p1", "p2", "p3", "p4", "SHRP"};
					var couldBeOldStyleUgandaSHRPBook = SHRPMatches.Any(m => englishTitle.Contains(m.ToLowerInvariant()));

					//if this book is one of the ones we're editing in our collection, it really
					//needs a title in our main language, it would be confusing to show a title from some other langauge
					if(!couldBeOldStyleUgandaSHRPBook && (IsEditable || title.Empty))
					{
						display = LocalizationManager.GetString("CollectionTab.TitleMissing", "Title Missing",
							"Shown as the thumbnail caption when the book doesn't have a title");
					}
					//but if this book is just in our list of sources, well then let's look through the names
					//and try to get one that is likely to be helpful
					else
					{
						var orderedPreferences = new List<string>();
						orderedPreferences.Add(LocalizationManager.UILanguageId);

						//already checked for this, previsouly. orderedPreferences.Add(_collectionSettings.Language1Iso639Code);
						if (_collectionSettings.Language2Iso639Code != null)
							orderedPreferences.Add(_collectionSettings.Language2Iso639Code);
						if (_collectionSettings.Language3Iso639Code != null)
							orderedPreferences.Add(_collectionSettings.Language3Iso639Code);

						orderedPreferences.Add("en");
						orderedPreferences.Add("fr");
						orderedPreferences.Add("es");
						display = title.GetBestAlternativeString(orderedPreferences);
						if (string.IsNullOrWhiteSpace(display))
						{
							display = title.GetFirstAlternative();
							Debug.Assert(!string.IsNullOrEmpty(display), "by our logic, this shouldn't possible");
						}
					}
				}
				// Handle both Windows and Linux line endings in case a file copied between the two
				// ends up with the wrong one.
				display = display.Replace("<br />", " ").Replace("\r\n"," ").Replace("\n", " ").Replace("  "," ");
				display = RemoveXmlMarkup(display).Trim();
				return display;
			}
		}

		public static string RemoveXmlMarkup(string input)
		{
			try
			{
				var doc = new XmlDocument();
				doc.PreserveWhitespace = true;
				doc.LoadXml("<div>" + input + "</div>");
				return doc.DocumentElement.InnerText;
			}
			catch (XmlException)
			{
				return input; // If we can't parse for some reason, return the original string
			}
		}

		/// <summary>
		/// we could get the title from the <title/> element, the name of the html, or the name of the folder...
		/// </summary>
		public virtual string Title
		{
			get
			{
				Debug.Assert(BookInfo.FolderPath == _storage.FolderPath);

				if (Type == BookType.Publication)
				{
					//REVIEW: evaluate and explain when we would choose the value in the html over the name of the folder.
					//1 advantage of the folder is that if you have multiple copies, the folder tells you which one you are looking at
					var s = OurHtmlDom.Title;
					if(string.IsNullOrEmpty(s))
						return Path.GetFileName(_storage.FolderPath);
					return s;
				}
				else //for templates and such, we can already just use the folder name
				{
					return Path.GetFileName(_storage.FolderPath);
				}
			}
		}

		public string PrettyPrintLanguage(string code)
		{
			return _bookData.PrettyPrintLanguage(code);
		}

		public virtual void GetThumbNailOfBookCoverAsync(HtmlThumbNailer.ThumbnailOptions thumbnailOptions, Action<Image> callback, Action<Exception> errorCallback)
		{
			try
			{
				if (HasFatalError) //NB: we might not know yet... we don't fully load every book just to show its thumbnail
				{
					callback(Resources.Error70x70);
				}
				Image thumb;
				if (_storage.TryGetPremadeThumbnail(thumbnailOptions.FileName, out thumb))
				{
					callback(thumb);
					return;
				}

				var dom = GetPreviewXmlDocumentForFirstPage();
				if (dom == null)
				{
					callback(Resources.Error70x70);
					return;
				}
				string folderForCachingThumbnail;

				folderForCachingThumbnail = _storage.FolderPath;
				_thumbnailProvider.GetThumbnailAsync(folderForCachingThumbnail, _storage.Key, dom, thumbnailOptions, callback, errorCallback);
			}
			catch (Exception err)
			{
				callback(Resources.Error70x70);
				Debug.Fail(err.Message);
			}
		}

		public virtual HtmlDom GetEditableHtmlDomForPage(IPage page)
		{
			if (_log.ErrorEncountered)
			{
				return GetErrorDom();
			}

			var pageDom = GetHtmlDomWithJustOnePage(page);
			pageDom.RemoveModeStyleSheets();
			pageDom.AddStyleSheet("basePage.css");
			pageDom.AddStyleSheet("editMode.css");
			if (LockedDown)
			{
				pageDom.AddStyleSheet("editTranslationMode.css");
			}
			else
			{
				pageDom.AddStyleSheet("editOriginalMode.css");
			}

			AddCreationTypeAttribute(pageDom);

			pageDom.AddStyleSheet("editPaneGlobal.css");
			pageDom.SortStyleSheetLinks();
			AddJavaScriptForEditing(pageDom);
			RuntimeInformationInjector.AddUIDictionaryToDom(pageDom, _collectionSettings);
			RuntimeInformationInjector.AddUISettingsToDom(pageDom, _collectionSettings, _storage.GetFileLocator());
			UpdateMultilingualSettings(pageDom);
			return pageDom;
		}

		private void AddJavaScriptForEditing(HtmlDom dom)
		{
			// BL-117, PH: With the newer xulrunner, javascript code with parenthesis in the URL is not working correctly.
			dom.AddJavascriptFile("bookEdit/js/bloomBootstrap.js".ToLocalhost());

			if (BookInfo.ReaderToolsAvailable)
				dom.AddJavascriptFile("bookEdit/js/readerToolsBootstrap.js".ToLocalhost());
		}


		private void UpdateMultilingualSettings(HtmlDom dom)
		{
			TranslationGroupManager.UpdateContentLanguageClasses(dom.RawDom, _collectionSettings, _collectionSettings.Language1Iso639Code, _bookData.MultilingualContentLanguage2,
													 _bookData.MultilingualContentLanguage3);

			BookStarter.SetLanguageForElementsWithMetaLanguage(dom.RawDom, _collectionSettings);
		}

		private HtmlDom GetHtmlDomWithJustOnePage(IPage page)
		{
			var headXml = _storage.Dom.SelectSingleNodeHonoringDefaultNS("/html/head").OuterXml;
			var dom = new HtmlDom(@"<html>" + headXml + "<body></body></html>");
			dom = _storage.MakeDomRelocatable(dom, _log);
			var body = dom.RawDom.SelectSingleNodeHonoringDefaultNS("//body");
			var divNodeForThisPage = page.GetDivNodeForThisPage();
			if(divNodeForThisPage==null)
			{
				throw new ApplicationException(String.Format("The requested page {0} from book {1} isn't in this book {2}.", page.Id,
															 page.Book.FolderPath, FolderPath));
			}
			var pageDom = dom.RawDom.ImportNode(divNodeForThisPage, true);
			body.AppendChild(pageDom);

//                BookStorage.HideAllTextAreasThatShouldNotShow(dom, iso639CodeToLeaveVisible, Page.GetPageSelectorXPath(dom));

			return dom;
		}

		public HtmlDom GetHtmlDomReadyToAddPages(HtmlDom inputDom)
		{
			var headNode = _storage.Dom.SelectSingleNodeHonoringDefaultNS("/html/head");
			var inputHead = inputDom.SelectSingleNodeHonoringDefaultNS("/html/head");
			var insertBefore = inputHead.FirstChild;  // Enhance: handle case where there is no existing child
			foreach (XmlNode child in headNode.ChildNodes)
			{
				inputHead.InsertBefore(inputDom.RawDom.ImportNode(child, true), insertBefore);
			}

			// This version somehow leaves the head in the wrong (empty) namespace and nothing works.
			//var importNode = inputDom.RawDom.ImportNode(headNode, true);
			//foreach (XmlNode child in inputHead.ChildNodes)
			//	importNode.AppendChild(child);
			//inputHead.ParentNode.ReplaceChild(importNode, inputHead);
			return _storage.MakeDomRelocatable(inputDom, _log);
		}

		public HtmlDom GetPreviewXmlDocumentForPage(IPage page)
		{
			if(_log.ErrorEncountered)
			{
				return GetErrorDom();
			}
			var pageDom = GetHtmlDomWithJustOnePage(page);
			pageDom.AddStyleSheet(_storage.GetFileLocator().LocateFileWithThrow(@"basePage.css"));
			pageDom.AddStyleSheet(_storage.GetFileLocator().LocateFileWithThrow(@"previewMode.css"));
			pageDom.AddStyleSheet(_storage.GetFileLocator().LocateFileWithThrow(@"origami.css"));

			pageDom.SortStyleSheetLinks();

			AddPreviewJScript(pageDom);//review: this is just for thumbnails... should we be having the javascript run?
			return pageDom;
		}

		public HtmlDom GetPreviewXmlDocumentForFirstPage()
		{
			if (_log.ErrorEncountered)
			{
				return null;
			}

			var bookDom = GetBookDomWithStyleSheets("previewMode.css","thumbnail.css");

			HideEverythingButFirstPageAndRemoveScripts(bookDom.RawDom);
			return bookDom;
		}

		private static void HideEverythingButFirstPageAndRemoveScripts(XmlDocument bookDom)
		{
			bool onFirst = true;
			foreach (XmlElement node in bookDom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]"))
			{
				if (!onFirst)
				{
					node.SetAttribute("style", "", "display:none");
				}
				onFirst =false;
			}
			//Without casting to array, Mono considers this manipulating the enumerable list
			foreach (var node in bookDom.SafeSelectNodes("//script").Cast<XmlNode>().ToArray())
			{
				//TODO: this removes image scaling, which is ok so long as it's already scaled with width/height attributes
				node.ParentNode.RemoveChild(node);
			}
		}

		private static void DeletePages(XmlDocument bookDom, Func<XmlElement, bool> pageSelectingPredicate)
		{
			// Seems safest to make a list so we're not modifying the document while iterating through it.
			var pagesToDelete = new List<XmlElement>();
			foreach (XmlElement node in bookDom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]"))
			{
				if (pageSelectingPredicate(node))
				{
					pagesToDelete.Add(node);
				}
			}
			foreach (var node in pagesToDelete)
			{
				// An earlier version of this method just set the visibility of the pages we don't want
				// in this printout to display:none, like this:
				//node.SetAttribute("style", "", "display:none");
				// However, this runs up against a defect in Gecko PDF generation: apparently when
				// all the content after the last page in a paginated document is display:none, Gecko
				// puts in an extra blank page. We suspect something like code that detects that
				// the current page is finished and the document is not finished and starts a new page,
				// which turns out not to be needed. The extra blank page can mess up booklet generation
				// and cause an extra sheet of paper to be used (leaving a wasted four blank pages at
				// the end). See BL-705.
				node.ParentNode.RemoveChild(node);
			}
		}

		internal IFileLocator GetFileLocator()
		{
			return _storage.GetFileLocator();
		}

		private HtmlDom GetBookDomWithStyleSheets(params string[] cssFileNames)
		{
			var dom = _storage.GetRelocatableCopyOfDom(_log);
			dom.RemoveModeStyleSheets();
			foreach (var cssFileName in cssFileNames)
			{
				dom.AddStyleSheet(cssFileName);
			}
			dom.SortStyleSheetLinks();

			return dom;
		}

		public virtual string StoragePageFolder { get { return _storage.FolderPath; } }

		private string GetGenericBrokenBookRecommendationString()
		{
			return LocalizationManager.GetString("Errors.BrokenBook",
				"Bloom had a problem showing this book. This doesn't mean your work is lost, but it does mean that something is out of date, is missing, or has gone wrong. Consider using the 'Report a Problem' command under the 'Help' menu.");
		}
		
		private HtmlDom GetPageListingErrorsWithBook(string contents)
		{
			var builder = new StringBuilder();
			builder.Append("<html><body style='font-family:arial,sans'>");

			builder.AppendLine("<p>" + GetGenericBrokenBookRecommendationString() + "</p>");

			builder.AppendFormat("<p>" + _storage.ErrorMessages + "</p>");
			
			foreach (var line in contents.Split(new []{'\n'}))
			{
				builder.AppendFormat("<li>{0}</li>\n", WebUtility.HtmlEncode(line));
			}
			builder.Append("</body></html>");
			return new HtmlDom(builder.ToString());
		}

		private HtmlDom GetErrorDom()
		{
			var builder = new StringBuilder();
			builder.Append("<html><body style='font-family:arial,sans'>");
			
			builder.AppendLine(GetGenericBrokenBookRecommendationString());

			builder.AppendFormat("<p>" + _storage.ErrorMessages + "</p>");

			builder.Append(((StringBuilderProgress)_log).Text);

			builder.Append("</body></html>");
			return new HtmlDom(builder.ToString());
		}


		public virtual bool CanDelete
		{
			get { return IsEditable; }
		}

		public bool CanPublish
		{
			get
			{
				if (!BookInfo.IsEditable)
					return false;
				GetErrorsIfNotCheckedBefore();
				return !HasFatalError;
			}
		}

		/// <summary>
		/// In the Bloom app, only one collection at a time is editable; that's the library they opened. All the other collections of templates, shells, etc., are not editable.
		/// </summary>
		public bool IsEditable {
			get
			{
				if (!BookInfo.IsEditable)
					return false;
				GetErrorsIfNotCheckedBefore();
				return !HasFatalError;
			}
		}



		public IPage FirstPage
		{
			get { return GetPages().First(); }
		}

		public Book FindTemplateBook()
		{
				Guard.AgainstNull(_templateFinder, "_templateFinder");
				if(Type!=BookType.Publication)
					return null;
				string templateKey = OurHtmlDom.GetMetaValue("pageTemplateSource", "");

				Book book=null;
				if (!String.IsNullOrEmpty(templateKey))
				{
					if (templateKey.ToLowerInvariant() == "basicbook")//catch this pre-beta spelling with no space
						templateKey = "Basic Book";
					book = _templateFinder.FindTemplateBook(templateKey);
					if(book==null)
					{
						ErrorReport.NotifyUserOfProblem("Bloom could not find the source of template pages named {0} (as in {0}.htm).\r\nThis comes from the <meta name='pageTemplateSource' content='{0}'/>.\r\nCheck that name matches the html exactly.",templateKey);
					}
				}
				if(book==null)
				{
					//re-use the pages in the document itself. This is useful when building
					//a new, complicated shell, which often have repeating pages but
					//don't make sense as a new kind of template.
					return this;
				}
				return book;
		}

		public BookType TypeOverrideForUnitTests;

		/// <summary>
		/// once in our lifetime, we want to do any migrations needed for this version of bloom
		/// </summary>
		private bool _haveDoneUpdate = false;

		public BookType Type
		{
			get
			{
				if(TypeOverrideForUnitTests != BookType.Unknown)
					return TypeOverrideForUnitTests;

				return IsEditable ? BookType.Publication : BookType.Template; //TODO there are other types...should there be some way they can they happen?
			}
		}

		public HtmlDom OurHtmlDom
		{
			get { return _storage.Dom;}
		}

		public virtual XmlDocument RawDom
		{
			get {return  OurHtmlDom.RawDom; }
		}

		public virtual string FolderPath
		{
			get { return _storage.FolderPath; }
		}

		public virtual HtmlDom GetPreviewHtmlFileForWholeBook()
		{
			//we may already know we have an error (we might not discover until later)
			if (HasFatalError)
			{
				return GetErrorDom();
			}
			if (!_storage.GetLooksOk())
			{
				return GetPageListingErrorsWithBook(_storage.GetValidateErrors());
			}
			var previewDom= GetBookDomWithStyleSheets("previewMode.css", "origami.css");
			AddCreationTypeAttribute(previewDom);

			//We may have just run into an error for the first time
			if (HasFatalError)
			{
				return GetErrorDom();
			}

			BringBookUpToDate(previewDom, new NullProgress());

			// this is normally the vernacular, but when we're previewing a shell, well it won't have anything for the vernacular
			var primaryLanguage = _collectionSettings.Language1Iso639Code;
			if (IsShellOrTemplate) //TODO: this won't be enough, if our national language isn't, say, English, and the shell just doesn't have our national language. But it might have some other language we understand.
				primaryLanguage = _collectionSettings.Language2Iso639Code;

			TranslationGroupManager.UpdateContentLanguageClasses(previewDom.RawDom, _collectionSettings, primaryLanguage, _bookData.MultilingualContentLanguage2, _bookData.MultilingualContentLanguage3);

			AddPreviewJScript(previewDom);
			previewDom.AddPublishClassToBody();
			return previewDom;
		}

		private void AddCreationTypeAttribute(HtmlDom htmlDom)
		{
			htmlDom.AddCreationType(LockedDown ? "translation" : "original");
		}

		public void BringBookUpToDate(IProgress progress)
		{
			_pagesCache = null;
			BringBookUpToDate(OurHtmlDom, progress);
			if (Type == Book.BookType.Publication)
			{
				ImageUpdater.UpdateAllHtmlDataAttributesForAllImgElements(FolderPath, OurHtmlDom, progress);
				UpdatePageFromFactoryTemplates(OurHtmlDom, progress);
				//ImageUpdater.CompressImages(FolderPath, progress);
				ImageUtils.RemoveTransparencyOfImagesInFolder(FolderPath, progress);
				Save();
			}

			if (SHRP_TeachersGuideExtension.ExtensionIsApplicable(this))
			{
				SHRP_TeachersGuideExtension.UpdateBook(OurHtmlDom, _collectionSettings.Language1Iso639Code);
			}

			Save();
			if (_bookRefreshEvent != null)
			{
				_bookRefreshEvent.Raise(this);
			}
		}

		/// <summary>
		/// As the bloom format evolves, including structure and classes and other attributes, this
		/// makes changes to old books. It needs to be very fast, because currently we dont' have
		/// a real way to detect the need for migration. So we do it all the time.
		/// 
		/// Yes, we have format version number, but, for example, one overhaul of the common xmatter
		/// html introduced a new class, "frontCover". Hardly enough to justify bumping the version number
		/// and making older Blooms unable to read new books. But because this is run, the xmatter will be
		/// migrated to the new template.
		/// </summary>
		/// <param name="bookDOM"></param>
		/// <param name="progress"></param>
		private void BringBookUpToDate(HtmlDom bookDOM /* may be a 'preview' version*/, IProgress progress)
		{
			progress.WriteStatus("Updating Front/Back Matter...");
			BringXmatterHtmlUpToDate(bookDOM);

			progress.WriteStatus("Gathering Data...");
			TranslationGroupManager.PrepareElementsInPageOrDocument(bookDOM.RawDom, _collectionSettings);
			progress.WriteStatus("Updating Data...");

			InjectStringListingActiveLanguagesOfBook();

			//hack
			if(bookDOM == OurHtmlDom)//we already have a data for this
			{
				_bookData.SynchronizeDataItemsThroughoutDOM();

				// I think we should only mess with tags if we are updating the book for real.
				var oldTagsPath = Path.Combine(_storage.FolderPath, "tags.txt");
				if (File.Exists(oldTagsPath))
				{
					ConvertTagsToMetaData(oldTagsPath, BookInfo);
					File.Delete(oldTagsPath);
				}
				// get any license info into the json
				var metadata = GetLicenseMetadata();
				UpdateLicenseMetdata(metadata);
			}
			else //used for making a preview dom
			{
				var bd = new BookData(bookDOM, _collectionSettings, UpdateImageMetadataAttributes);
				bd.SynchronizeDataItemsThroughoutDOM();
			}

			bookDOM.RemoveMetaElement("bloomBookLineage", () => BookInfo.BookLineage, val => BookInfo.BookLineage = val);
			bookDOM.RemoveMetaElement("bookLineage", () => BookInfo.BookLineage, val => BookInfo.BookLineage = val);
			// BookInfo will always have an ID, the constructor makes one even if there is no json file.
			// To allow migration, pretend it has no ID if there is not yet a meta.json.
			bookDOM.RemoveMetaElement("bloomBookId", () => (File.Exists(BookInfo.MetaDataPath) ? BookInfo.Id : null), val => BookInfo.Id = val);

			// Title should be replicated in json
			//if (!string.IsNullOrWhiteSpace(Title)) // check just in case we somehow have more useful info in json.
			//    bookDOM.Title = Title;
			// Bit of a kludge, but there's no way to tell whether a boolean is already set in the JSON, so we fake that it is not,
			// thus ensuring that if something is in the metadata we use it.
			// If there is nothing there the default of true will survive.
			bookDOM.RemoveMetaElement("SuitableForMakingVernacularBooks", () => null, val => BookInfo.IsSuitableForVernacularLibrary = val == "yes" || val == "definitely");

			UpdateTextsNewlyChangedToRequiresParagraph(bookDOM);

			//we've removed and possible added pages, so our page cache is invalid
			_pagesCache = null;
		}

		private void BringXmatterHtmlUpToDate(HtmlDom bookDOM)
		{
			//by default, this comes from the collection, but the book can select one, including "null" to select the factory-supplied empty xmatter
			var nameOfXMatterPack = OurHtmlDom.GetMetaValue("xMatter", _collectionSettings.XMatterPackName);
			var helper = new XMatterHelper(bookDOM, nameOfXMatterPack, _storage.GetFileLocator());
			//note, we determine this before removing xmatter to fix the situation where there is *only* xmatter, no content, so if
			//we wait until we've removed the xmatter, we no how no way of knowing what size/orientation they had before the update.
			Layout layout = Layout.FromDom(bookDOM, Layout.A5Portrait);
			XMatterHelper.RemoveExistingXMatter(bookDOM);
			layout = Layout.FromDom(bookDOM, layout);
				//this says, if you can't figure out the page size, use the one we got before we removed the xmatter
			helper.InjectXMatter(_bookData.GetWritingSystemCodes(), layout);
		}


		// Around May 2014 we added a class, .bloom-requireParagraphs, backed by javascript that makes geckofx
		// emit <p>s instead of <br>s (which you can't style and don't leave a space in wkhtmltopdf).
		// If there is existing text after we added this, it needs code to do the conversion. There
		// is already javascript for this, but by having it here allows us to update an entire collection in one commmand.
		// Note, this doesn't yet do as much as the javascript version, which also can be triggered by a border-top-style
		// of "dashed", so that books shipped without this class can still be converted over.
		public void UpdateTextsNewlyChangedToRequiresParagraph(HtmlDom bookDom)
		{
			var texts = OurHtmlDom.SafeSelectNodes("//*[contains(@class,'bloom-requiresParagraphs')]/div[contains(@class,'bloom-editable') and br]");
			foreach (XmlElement text in texts)
			{
				string s = "";
				foreach (var chunk in text.InnerXml.Split(new string[] { "<br />", "<br/>"}, StringSplitOptions.None))
				{
					if (chunk.Trim().Length > 0)
						s += "<p>" + chunk + "</p>";
				}
				text.InnerXml = s;
			}
		}



		internal static void ConvertTagsToMetaData(string oldTagsPath, BookInfo bookMetaData)
		{
			var oldTags = File.ReadAllText(oldTagsPath);
			bookMetaData.IsFolio = oldTags.Contains("folio");
			bookMetaData.IsExperimental = oldTags.Contains("experimental");
		}

		private void FixBookIdAndLineageIfNeeded()
		{
			HtmlDom bookDOM = _storage.Dom;
//at version 0.9.71, we introduced this book lineage for real. At that point almost all books were from Basic book,
			//so let's get further evidence by looking at the page source and then fix the lineage
			// However, if we have json lineage, it is normal not to have it in HTML metadata.
			if (string.IsNullOrEmpty(BookInfo.BookLineage) && bookDOM.GetMetaValue("bloomBookLineage", "") == "")
				if (bookDOM.GetMetaValue("pageTemplateSource", "") == "Basic Book")
				{
					bookDOM.UpdateMetaElement("bloomBookLineage", kIdOfBasicBook);
				}

			//there were a number of books in version 0.9 that just copied the id of the basic book from which they were created
			if (bookDOM.GetMetaValue("bloomBookId", "") == kIdOfBasicBook)
			{
				if (bookDOM.GetMetaValue("title", "") != "Basic Book")
				{
					bookDOM.UpdateMetaElement("bloomBookId", Guid.NewGuid().ToString());
				}
			}
		}

		/// <summary>
		/// THe bloomBookId meta value
		/// </summary>
		public string ID { get { return _storage.Dom.GetMetaValue("bloomBookId", ""); } }

		private void UpdateImageMetadataAttributes(XmlElement imgNode)
		{
			ImageUpdater.UpdateImgMetdataAttributesToMatchImage(FolderPath, imgNode, new NullProgress());
		}

		private void UpdatePageFromFactoryTemplates(HtmlDom bookDom, IProgress progress)
		{
			var originalLayout = Layout.FromDom(bookDom, Layout.A5Portrait);

			var templatePath = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates", "Basic Book");

			var templateDom = XmlHtmlConverter.GetXmlDomFromHtmlFile(templatePath.CombineForPath("Basic Book.htm"), false);

			progress.WriteStatus("Updating pages that were based on Basic Book...");
			foreach (XmlElement templatePageDiv in templateDom.SafeSelectNodes("//body/div"))
			{
				if (templatePageDiv.GetOptionalStringAttribute("class", "").Contains("customPage"))
					continue; // we sure don't want to revert this page to its blank custom state

				var templateId = templatePageDiv.GetStringAttribute("id");
				if (string.IsNullOrEmpty(templateId))
					continue;

				var templatePageClasses = templatePageDiv.GetAttribute("class");
				//note, lineage is a series of guids separated by a semicolon
				foreach (XmlElement pageDiv in bookDom.SafeSelectNodes("//body/div[contains(@data-pagelineage, '" + templateId + "')]"))
				{
					pageDiv.SetAttribute("class", templatePageClasses);

					//now for all the editable elements within the page
					int count = 0;
					foreach (XmlElement templateElement in templatePageDiv.SafeSelectNodes("div/div"))
					{
						UpdateDivInsidePage(count, templateElement, pageDiv, progress);
						++count;
					}
				}
			}

			//custom layout gets messed up when we copy classes over from, for example, Basic Book
			SetLayout(originalLayout);

			//Likewise, the multilingual settings (e.g. bloom-bilingual) get messed up, so restore those
			UpdateMultilingualSettings(bookDom);
		}

		private static void UpdateDivInsidePage(int zeroBasedCount, XmlElement templateElement, XmlElement targetPage, IProgress progress)
		{
			XmlElement targetElement = targetPage.SelectSingleNode("div/div[" + (zeroBasedCount + 1).ToString(CultureInfo.InvariantCulture) + "]") as XmlElement;
			if (targetElement == null)
			{
				progress.WriteError("Book had less than the expected number of divs on page " + targetPage.GetAttribute("id") +
									", so it cannot be completely updated.");
				return;
			}
			targetElement.SetAttribute("class", templateElement.GetAttribute("class"));
		}

		public bool IsShellOrTemplate
		{
			get
			{
				//hack. Eventually we might be able to lock books so that you can't edit them.
				return !IsEditable;
			}
		}

		public bool HasSourceTranslations
		{
			get
			{
				//is there a textarea with something other than the vernacular, which has a containing element marked as a translation group?
				var x = OurHtmlDom.SafeSelectNodes(String.Format("//*[contains(@class,'bloom-translationGroup')]//textarea[@lang and @lang!='{0}']", _collectionSettings.Language1Iso639Code));
				return x.Count > 0;
			}

		}

		/*
		 *					Basic Book		Shellbook		Calendar		Picture Dictionary		Picture Dictionary Premade
		 *	Change Images		y				n				y					y					y
		 *	UseSrcForTmpPgs		y				n				n					y					y
		 *	remove pages		y				n				n					y					y
		 *	change orig creds	y				n				n					y					no?
		 *	change license		y				n				y					y					no?
		 */

		/*
		 *  The current design: for all these settings, put them in meta, except, override them all with the "LockDownForShell" setting, which can be specified in a meta tag.
		 *  The default for all permissions is 'true', so don't specify them in a document unless you want to withhold the permission.
		 *  See UseSourceForTemplatePages for one the exception.
		 */

		/// <summary>
		/// This one is a bit different becuase we just imply that it's false unless at least one pageTemplateSource is specified.
		/// </summary>
		public bool UseSourceForTemplatePages
		{
			get
			{
				if (LockedDown)
					return false;

				var node = OurHtmlDom.SafeSelectNodes(String.Format("//meta[@name='pageTemplateSource']"));
				return node.Count > 0;
			}
		}

		/// <summary>
		/// Don't allow (or at least don't encourage) changing the images
		/// </summary>
		/// <remarks>In April 2012, we don't yet have an example of a book which would explicitly
		/// restrict changing images. Shells do, of course, but do so by virtue of their lockedDownAsShell being set to 'true'.</remarks>
		public bool CanChangeImages
		{
			get
			{
				if (LockedDown)
					return false;

				var node = OurHtmlDom.SafeSelectNodes(String.Format("//meta[@name='canChangeImages' and @content='false']"));
				return node.Count == 0;
			}
		}

		/// <summary>
		/// This is useful if you are allowing people to make major changes, but want to insist that derivatives carry the same license
		/// </summary>
		public bool CanChangeLicense
		{
			get
			{
				if (LockedDown)
					return false;

				var node = OurHtmlDom.SafeSelectNodes(String.Format("//meta[@name='canChangeLicense' and @content='false']"));
				return node.Count == 0;
			}
		}


		/// <summary>
		/// This is useful if you are allowing people to make major changes, but want to preserve acknowledments, for example, for the jscript programmers on Wall Calendar, or
		/// the person who put together a starter picture-dictionary.
		/// </summary>
		public bool CanChangeOriginalAcknowledgments
		{
			get
			{
				if (LockedDown)
					return false;

				var node = OurHtmlDom.SafeSelectNodes(String.Format("//meta[@name='canChangeOriginalAcknowledgments' and @content='false']"));
				return node.Count == 0;
			}
		}

		/// <summary>
		/// A book is lockedDown if it says it is AND we're not in a shell-making library
		/// </summary>
		public bool LockedDown
		{
			get
			{
				if(_collectionSettings.IsSourceCollection) //nothing is locked if we're in a shell-making library
					return false;
				return RecordedAsLockedDown;
			}
		}

		/// <summary>
		/// This is how the book's LockedDown state will be reported in a vernacular collection.
		/// </summary>
		public bool RecordedAsLockedDown
		{
			get { return IsLockedDown(OurHtmlDom); }
			set
			{
				RecordAsLockedDown(OurHtmlDom, value);
			}
		}

		public static bool IsLockedDown(HtmlDom dom)
		{
			var node = dom.SafeSelectNodes(String.Format("//meta[@name='lockedDownAsShell' and @content='true']"));
			return node.Count > 0;
		}

		public static void RecordAsLockedDown(HtmlDom dom, bool locked)
		{
			if (locked)
			{
				dom.UpdateMetaElement("lockedDownAsShell", "true");
			}
			else
			{
				dom.RemoveMetaElement("lockedDownAsShell");
			}
		}

//		/// <summary>
//        /// Is this a shell we're translating? And if so, is this a shell-making project?
//        /// </summary>
//        public bool LockedExceptForTranslation
//        {
//            get
//            {
//            	return !_librarySettings.IsSourceCollection &&
//            	       RawDom.SafeSelectNodes("//meta[@name='editability' and @content='translationOnly']").Count > 0;
//            }
//        }

		public string CategoryForUsageReporting
		{
			get
			{
				if (_collectionSettings.IsSourceCollection)
				{
					return "ShellEditing";
				}
				else if (LockedDown)
				{
					return "ShellTranslating";
				}
				else
				{
					return "CustomVernacularBook";
				}
			}
		}



		public virtual bool HasFatalError
		{
			get { return _log.ErrorEncountered || !string.IsNullOrEmpty(_storage.ErrorMessages); }
		}


		public string ThumbnailPath
		{
			get { return Path.Combine(FolderPath, "thumbnail.png"); }
		}

		public virtual bool CanUpdate
		{
			get { return IsEditable && !HasFatalError; }
		}
		public virtual bool CanExport
		{
			get { return IsEditable && !HasFatalError; }
		}

		/// <summary>
		/// In a vernacular library, we want to hide books that are meant only for people making shells
		/// </summary>
		public bool IsSuitableForVernacularLibrary
		{
			get { return BookInfo.IsSuitableForVernacularLibrary; }
		}


		//discontinuing this for now becuase we need to know whether to show the book when all we have is a bookinfo, not access to the
		//dom like this requires. We'll just hard code the names of the experimental things.
//        public bool IsExperimental
//        {
//            get
//            {
//                string metaValue = OurHtmlDom.GetMetaValue("experimental", "false");
//                return metaValue == "true" || metaValue == "yes";
//            }
//        }

		/// <summary>
		/// In a shell-making library, we want to hide books that are just shells, so rarely make sense as a starting point for more shells
		/// </summary>
		public bool IsSuitableForMakingShells
		{
			get
			{
				return BookInfo.IsSuitableForMakingShells;
			}
		}

		/// <summary>
		/// A "Folio" document is one that acts as a wrapper for a number of other books
		/// </summary>
		public bool IsFolio
		{
			get
			{
				string metaValue = OurHtmlDom.GetMetaValue("folio",  OurHtmlDom.GetMetaValue("Folio", "no"));
				return metaValue == "yes" || metaValue == "true";
			}
		}

		/// <summary>
		/// For bilingual or trilingual books, this is the second language to show, after the vernacular
		/// </summary>
		public string MultilingualContentLanguage2
		{
			get { return _bookData.MultilingualContentLanguage2; }
		}

		/// <summary>
		/// For trilingual books, this is the third language to show
		/// </summary>
		public string MultilingualContentLanguage3
		{
			get { return _bookData.MultilingualContentLanguage3; }
		}

		public BookInfo BookInfo { get; private set; }

		public int NextStyleNumber
		{
			get { return _bookData.StyleNumberSequence; }
		}

		public void SetMultilingualContentLanguages(string language2Code, string language3Code)
		{
			_bookData.SetMultilingualContentLanguages(language2Code, language3Code);
			InjectStringListingActiveLanguagesOfBook();
			_bookData.UpdateDomFromDataset();
		}

		/// <summary>
		/// Bloom books can have up to 3 languages active at any time. This method pushes in a string
		/// listing then, separated by commas. It is then usable on the front page, title page, etc.
		/// </summary>
		private void InjectStringListingActiveLanguagesOfBook()
		{
			string codeOfNationalLanguage = _collectionSettings.Language2Iso639Code;
			var languagesOfBook = _collectionSettings.GetLanguage1Name(codeOfNationalLanguage);

			if (MultilingualContentLanguage2 != null)
			{
				languagesOfBook += ", " + _collectionSettings.GetLanguageName(MultilingualContentLanguage2, codeOfNationalLanguage);
			}
			if (MultilingualContentLanguage3 != null)
			{
				languagesOfBook += ", " + _collectionSettings.GetLanguageName(MultilingualContentLanguage3, codeOfNationalLanguage);
			}

			_bookData.Set("languagesOfBook", languagesOfBook, false);
		}

		/// <summary>
		/// This is a difficult concept to implement. The current usage of this is in creating metadata indicating which languages
		/// the book contains. How are we to decide whether it contains enough of a particular language to be useful?
		/// Based on BL-2017, we now return a Dictionary of booleans indicating whether a language should be uploaded by default.
		/// The dictionary contains an entry for every language where the book contains non-x-matter text.
		/// The value is true if every non-x-matter field which contains text in any language contains text in this.
		/// </summary>
		public Dictionary<string, bool> AllLanguages
		{
			get
			{
				var result = new Dictionary<string, bool>();
				var parents = new HashSet<XmlElement>(); // of interesting non-empty children
				// editable divs that are in non-x-matter pages and have a potentially interesting language.
				var langDivs = OurHtmlDom.SafeSelectNodes("//div[contains(@class, 'bloom-page') and not(contains(@class, 'bloom-frontMatter')) and not(contains(@class, 'bloom-backMatter'))]//div[@class and @lang]").Cast<XmlElement>()
					.Where(div => div.Attributes["class"].Value.IndexOf("bloom-editable", StringComparison.InvariantCulture) >= 0)
					.Where(div =>
					{
						var lang = div.Attributes["lang"].Value;
						return lang != "*" && lang != "z" && lang != ""; // Not valid languages, though we sometimes use them for special purposes
					}).ToArray();
				// First pass: fill in the dictionary with languages which have non-empty content in relevant divs
				foreach (var div in langDivs)
				{
					var lang = div.Attributes["lang"].Value;
					// The test for ContainsKey is redundant but may save a useful amount of time.
					if (!result.ContainsKey(lang) && !string.IsNullOrWhiteSpace(div.InnerText))
					{
						result[lang] = true;
						parents.Add((XmlElement)div.ParentNode);
					}
				}
				// Second pass: for each parent, if it lacks a non-empty child for one of the languages, set value for that lang to false.
				foreach (var lang in result.Keys.ToList()) // ToList so we can modify original collection as we go
				{
					foreach (var parent in parents)
					{
						if (!HasContentInLang(parent, lang))
						{
							result[lang] = false; // not complete
							break; // no need to check other parents.
						}
					}
				}

				return result;
			}
		}

		bool HasContentInLang(XmlElement parent, string lang)
		{
			foreach (var divN in parent.ChildNodes)
			{
				var div = divN as XmlElement;
				if (div == null || div.Attributes["lang"] == null || div.Attributes["lang"].Value != lang)
					continue;
				return !string.IsNullOrWhiteSpace(div.InnerText); // this one settles it: success if non-empty
			}
			return false; // not found
		}


		public string GetAboutBookHtml
		{
			get
			{
				var options = new MarkdownOptions() {LinkEmails = true, AutoHyperlink=true};
				var m = new Markdown(options);
				var contents = m.Transform(File.ReadAllText(AboutBookMarkdownPath));
				contents = contents.Replace("remove", "");//used to hide email addresses in the md from scanners (probably unneccessary.... do they scan .md files?

				var pathToCss = _storage.GetFileLocator().LocateFileWithThrow("BookReadme.css");
				var html = string.Format("<html><head><link rel='stylesheet' href='file://{0}' type='text/css'><head/><body>{1}</body></html>", pathToCss, contents);
				return html;

			} //todo add other ui languages
		}

		public bool HasAboutBookInformationToShow { get { return File.Exists(AboutBookMarkdownPath); } }
		public string AboutBookMarkdownPath  {
			get
			{
				return _storage.FolderPath.CombineForPath("ReadMe_en.md");
			}
		}

		// Assign the next of the standard cover colors which will be used consistently henceforth for this book
		// (except when actually printing...for that we switch to white so the
		// actual cardstock color comes through unchanged).
		internal void InitCoverColor()
		{
			AddCoverColor(this.OurHtmlDom, NextBookColor());
		}

		private void AddCoverColor(HtmlDom dom, Color coverColor)
		{
			var colorValue = ColorTranslator.ToHtml(coverColor);
//            var colorValue = String.Format("#{0:X2}{1:X2}{2:X2}", coverColor.R, coverColor.G, coverColor.B);
			XmlElement colorStyle = dom.RawDom.CreateElement("style");
			colorStyle.SetAttribute("type","text/css");
			colorStyle.InnerXml = @"
				DIV.coverColor  TEXTAREA	{		background-color: colorValue !important;	}
				DIV.bloom-page.coverColor	{		background-color: colorValue !important;	}
				".Replace("colorValue", colorValue);//string.format has a hard time with all those {'s

			var header = dom.RawDom.SelectSingleNodeHonoringDefaultNS("//head");
			header.AppendChild(colorStyle);
		}


		/// <summary>
		/// Make stuff readonly, which isn't doable via css, surprisingly
		/// </summary>
		/// <param name="dom"></param>
		private void AddPreviewJScript(HtmlDom dom)
		{
//			XmlElement header = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//head");
//			AddJavascriptFile(dom, header, _storage.GetFileLocator().LocateFileWithThrow("jquery.js"));
//			AddJavascriptFile(dom, header, _storage.GetFileLocator().LocateFileWithThrow("jquery.myimgscale.js"));
//
//			XmlElement script = dom.CreateElement("script");
//			script.SetAttribute("type", "text/javascript");
//			script.InnerText = @"jQuery(function() {
//						$('textarea').focus(function() {$(this).attr('readonly','readonly');});
//
//						//make images scale up to their container without distorting their proportions, while being centered within it.
//						$('img').scaleImage({ scale: 'fit' }); //uses jquery.myimgscale.js
//			})";
//			header.AppendChild(script);

			var pathToJavascript = _storage.GetFileLocator().LocateFileWithThrow("bloomPreviewBootstrap.js");
			if(string.IsNullOrEmpty(pathToJavascript))
			{
				throw new ApplicationException("Could not locate " +"bloomPreviewBootstrap.js");
			}
			dom.AddJavascriptFile(pathToJavascript);
		}

		public IEnumerable<IPage> GetPages()
		{
			if (!_haveCheckedForErrorsAtLeastOnce)
			{
				CheckForErrors();
			}

			if (_log.ErrorEncountered)
				yield break;

			if (_pagesCache == null)
			{
				BuildPageCache();
			}

			foreach (var page in _pagesCache)
			{
				yield return page;
			}
		}

		private void BuildPageCache()
		{
			_pagesCache = new List<IPage>();

			foreach (XmlElement pageNode in OurHtmlDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]"))
			{
				//review: we want to show titles for template books, numbers for other books.
				//this here requires that titles be removed when the page is inserted, kind of a hack.
				var caption = GetPageLabelFromDiv(pageNode);
				if (String.IsNullOrEmpty(caption))
				{
					caption = "";
						//we aren't keeping these up to date yet as thing move around, so.... (pageNumber + 1).ToString();
				}
				_pagesCache.Add(CreatePageDecriptor(pageNode, caption));
			}
		}


		private IPage GetPageToShowAfterDeletion(IPage page)
		{
			Guard.AgainstNull(_pagesCache, "_pageCache");
			var matchingPageEvenIfNotActualObject = _pagesCache.First(p => p.Id == page.Id);
			Guard.AgainstNull(matchingPageEvenIfNotActualObject, "Couldn't find page with matching id in cache");
			var index = _pagesCache.IndexOf(matchingPageEvenIfNotActualObject);
			Guard.Against(index <0, "Couldn't find page in cache");

			if (index == _pagesCache.Count - 1)//if it's the last page
			{
				if (index < 1) //if it's the only page
					throw new ApplicationException("Bloom should not have allowed you to delete the last remaining page.");
				return _pagesCache[index - 1];//give the preceding page
			}


			return _pagesCache[index + 1]; //give the following page
		}

		public IEnumerable<IPage> GetTemplatePages()
		{
			if (_log.ErrorEncountered)
				yield break;

			foreach (XmlElement pageNode in OurHtmlDom.SafeSelectNodes("//div[contains(@class,'bloom-page') and not(contains(@data-page, 'singleton'))]"))
			{
				var caption = GetPageLabelFromDiv(pageNode);
				yield return CreatePageDecriptor(pageNode, caption);
			}
		}

		private static string GetPageLabelFromDiv(XmlElement pageNode)
		{
			var englishDiv = pageNode.SelectSingleNode("div[contains(@class,'pageLabel') and @lang='en']");
			var caption = (englishDiv == null) ? String.Empty : englishDiv.InnerText;
			return caption;
		}

		private IPage CreatePageDecriptor(XmlElement pageNode, string caption)//, Action<Image> thumbNailReadyCallback)
		{
			return new Page(this, pageNode, caption,
				(page => FindPageDiv(page)));
		}

		public Image GetPageThumbNail()
		{
			return Resources.Error70x70;
		}

		private XmlElement FindPageDiv(IPage page)
		{
			//review: could move to page
			var pageElement = OurHtmlDom.RawDom.SelectSingleNodeHonoringDefaultNS(page.XPathToDiv);
			Require.That(pageElement != null,"Page could not be found: "+page.XPathToDiv);
			return pageElement as XmlElement;
		}

		public void InsertPageAfter(IPage pageBefore, IPage templatePage)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			ClearPagesCache();

			XmlDocument dom = OurHtmlDom.RawDom;
			var templatePageDiv = templatePage.GetDivNodeForThisPage();
			var newPageDiv = dom.ImportNode(templatePageDiv, true) as XmlElement;

			BookStarter.SetupIdAndLineage(templatePageDiv, newPageDiv);
			BookStarter.SetupPage(newPageDiv, _collectionSettings, _bookData.MultilingualContentLanguage2, _bookData.MultilingualContentLanguage3);//, LockedExceptForTranslation);
			SizeAndOrientation.UpdatePageSizeAndOrientationClasses(newPageDiv, GetLayout());
			newPageDiv.RemoveAttribute("title"); //titles are just for templates [Review: that's not true for front matter pages, but at the moment you can't insert those, so this is ok]C:\dev\Bloom\src\BloomExe\StyleSheetService.cs

			var elementOfPageBefore = FindPageDiv(pageBefore);
			elementOfPageBefore.ParentNode.InsertAfter(newPageDiv, elementOfPageBefore);

			BuildPageCache();
			var newPage = GetPages().First(p=>p.GetDivNodeForThisPage() == newPageDiv);
			Guard.AgainstNull(newPage,"could not find the page we just added");
			_pageSelection.SelectPage(newPage);
			//_pageSelection.SelectPage(CreatePageDecriptor(newPageDiv, "should not show", _collectionSettings.Language1Iso639Code));

			Save();
			if (_pageListChangedEvent != null)
				_pageListChangedEvent.Raise(null);

			InvokeContentsChanged(null);
		}

		public void DuplicatePage(IPage page)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			var pages = GetPageElements();
			var pageDiv = FindPageDiv(page);
			var newpageDiv = (XmlElement) pageDiv.CloneNode(true);
			BookStarter.SetupIdAndLineage(pageDiv, newpageDiv);
			var body = pageDiv.ParentNode;
			int currentPageIndex = -1;

			// Have to compare Ids; can't use _pagesCache.IndexOf(page) -- (BL-467)
			foreach (IPage cachedPage in _pagesCache)
				if (cachedPage.Id.Equals(page.Id))
				{
					currentPageIndex = _pagesCache.IndexOf(cachedPage);
					break;
				}

			// This should never happen. But just in case, don't do something we don't want to do.
			if (currentPageIndex < 0)
				return;

			body.InsertAfter(newpageDiv, pages[currentPageIndex]);

			ClearPagesCache();
			Save();
			if (_pageListChangedEvent != null)
				_pageListChangedEvent.Raise(null);

			InvokeContentsChanged(null);

			if (_pagesCache == null)
				BuildPageCache();
			_pageSelection.SelectPage(_pagesCache[currentPageIndex + 1]);
		}

		public void DeletePage(IPage page)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			if(GetPageCount() <2)
				return;

			var pageToShowNext = GetPageToShowAfterDeletion(page);

			ClearPagesCache();
			//_pagesCache.Remove(page);

			var pageNode = FindPageDiv(page);
		   pageNode.ParentNode.RemoveChild(pageNode);

		   _pageSelection.SelectPage(pageToShowNext);
			Save();
			if(_pageListChangedEvent !=null)
				_pageListChangedEvent.Raise(null);

			InvokeContentsChanged(null);
		}

		private void ClearPagesCache()
		{
			_pagesCache = null;
		}

		private int GetPageCount()
		{
			return GetPages().Count();
		}


		/// <summary>
		/// Earlier, we handed out a single-page version of the document. Now it has been edited,
		/// so we now we need to fold changes back in
		/// </summary>
		public void SavePage(HtmlDom editedPageDom)
		{
			Debug.Assert(IsEditable);
			try
			{
				//replace the corresponding page contents in our DOM with what is in this PageDom
				XmlElement divElement = editedPageDom.SelectSingleNodeHonoringDefaultNS("//div[contains(@class, 'bloom-page')]");
				string pageDivId = divElement.GetAttribute("id");
				var page = GetPageFromStorage(pageDivId);

				HtmlDom.ProcessPageAfterEditing(page, divElement);

				_bookData.SuckInDataFromEditedDom(editedPageDom);//this will do an updatetitle
				// When the user edits the styles on a page, the new or modified rules show up in a <style/> element with title "userModifiedStyles". Here we copy that over to the book DOM.
				var userModifiedStyles = editedPageDom.SelectSingleNode("html/head/style[@title='userModifiedStyles']");
				if (userModifiedStyles != null)
				{
					GetOrCreateUserModifiedStyleElementFromStorage().InnerXml = userModifiedStyles.InnerXml;
					Debug.WriteLine("Incoming User Modified Styles:   " + userModifiedStyles.OuterXml);
				}
				//Debug.WriteLine("User Modified Styles:   " + GetOrCreateUserModifiedStyleElementFromStorage().OuterXml);
				try
				{
					Save();
				}
				catch (Exception error)
				{
					ErrorReport.NotifyUserOfProblem(error, "There was a problem saving");
				}

				_storage.UpdateBookFileAndFolderName(_collectionSettings);
				//review used to have   UpdateBookFolderAndFileNames(data);


				//Enhance: if this is only used to re-show the thumbnail, why not limit it to if this is the cover page?
				//e.g., look for the class "cover"
				InvokeContentsChanged(null); //enhance: above we could detect if anything actually changed
			}
			catch (Exception error)
			{
				var msg = LocalizationManager.GetString("Errors.CouldNotSavePage", "Bloom had trouble saving a page. Please click Details below and report this to us. Then quit Bloom, run it again, and check to see if the page you just edited is missing anything. Sorry!");
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, msg);
			}
		}

//        /// <summary>
//        /// Gets the first element with the given tag & id, within the page-div with the given id.
//        /// </summary>
//        private XmlElement GetStorageNode(string pageDivId, string tag, string elementId)
//        {
//            var query = String.Format("//div[@id='{0}']//{1}[@id='{2}']", pageDivId, tag, elementId);
//            var matches = OurHtmlDom.SafeSelectNodes(query);
//            if (matches.Count != 1)
//            {
//                throw new ApplicationException("Expected one match for this query, but got " + matches.Count + ": " + query);
//            }
//            return (XmlElement)matches[0];
//        }

		/// <summary>
		/// The <style title='userModifiedStyles'/> element is where we keep our user-modifiable style information
		/// </summary>
		private XmlElement GetOrCreateUserModifiedStyleElementFromStorage()
		{
			var matches = OurHtmlDom.SafeSelectNodes("html/head/style[@title='userModifiedStyles']");
			if (matches.Count > 0)
				return (XmlElement) matches[0];

			var emptyUserModifiedStylesElement = OurHtmlDom.RawDom.CreateElement("style");
			emptyUserModifiedStylesElement.SetAttribute("title", "userModifiedStyles");
			emptyUserModifiedStylesElement.SetAttribute("type", "text/css");
			OurHtmlDom.Head.AppendChild(emptyUserModifiedStylesElement);
			return emptyUserModifiedStylesElement;
		}

		/// <summary>
		/// Gets the first element with the given tag & id, within the page-div with the given id.
		/// </summary>
		private XmlElement GetPageFromStorage(string pageDivId)
		{
			var query = String.Format("//div[@id='{0}']", pageDivId);
			var matches = OurHtmlDom.SafeSelectNodes(query);
			if (matches.Count != 1)
			{
				throw new ApplicationException("Expected one match for this query, but got " + matches.Count + ": " + query);
			}
			return (XmlElement)matches[0];
		}

		/// <summary>
		/// Move a page to somewhere else in the book
		/// </summary>
		public bool RelocatePage(IPage page, int indexOfItemAfterRelocation)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			if(!CanRelocatePageAsRequested(indexOfItemAfterRelocation))
			{

				return false;
			}

			ClearPagesCache();

			var pages = GetPageElements();
			var pageDiv = FindPageDiv(page);
			var body = pageDiv.ParentNode;
				body.RemoveChild(pageDiv);
			if(indexOfItemAfterRelocation == 0)
			{
				body.InsertBefore(pageDiv, body.FirstChild);
			}
			else
			{
				body.InsertAfter(pageDiv, pages[indexOfItemAfterRelocation-1]);
			}

			Save();
			InvokeContentsChanged(null);
			return true;
		}

		private XmlNodeList GetPageElements()
		{
			return OurHtmlDom.SafeSelectNodes("/html/body//div[contains(@class,'bloom-page')]");
		}

		private bool CanRelocatePageAsRequested(int indexOfItemAfterRelocation)
		{
			int upperBounds = GetIndexOfFirstBackMatterPage();
			if (upperBounds < 0)
				upperBounds = 10000;

			return indexOfItemAfterRelocation > GetIndexLastFrontkMatterPage ()
				&& indexOfItemAfterRelocation < upperBounds;
		}

		private int GetIndexLastFrontkMatterPage()
		{
			XmlElement lastFrontMatterPage =
				OurHtmlDom.RawDom.SelectSingleNode("(/html/body/div[contains(@class,'bloom-frontMatter')])[last()]") as XmlElement;
			if(lastFrontMatterPage==null)
				return -1;
			return GetIndexOfPage(lastFrontMatterPage);
		}

		private int GetIndexOfFirstBackMatterPage()
		{
			XmlElement firstBackMatterPage =
				OurHtmlDom.RawDom.SelectSingleNode("(/html/body/div[contains(@class,'bloom-backMatter')])[position()=1]") as XmlElement;
			if (firstBackMatterPage == null)
				return -1;
			return GetIndexOfPage(firstBackMatterPage);
		}


		private int GetIndexOfPage(XmlElement pageElement)
		{
			var elements = GetPageElements();
			for (int i = 0; i < elements.Count; i++)
			{
				if (elements[i] == pageElement)
					return i;
			}
			return -1;
		}

		public HtmlDom GetDomForPrinting(PublishModel.BookletPortions bookletPortion, BookCollection currentBookCollection, BookServer bookServer)
		{
			var printingDom = GetBookDomWithStyleSheets("previewMode.css", "origami.css");
			AddCreationTypeAttribute(printingDom);

			if (IsFolio)
			{
				AddChildBookContentsToFolio(printingDom, currentBookCollection, bookServer);
				printingDom.SortStyleSheetLinks();
			}


			//whereas the base is to our embedded server during editing, it's to the file folder
			//when we make a PDF, because we wan the PDF to use the original hi-res versions

			var pathSafeForWkHtml2Pdf = Palaso.IO.FileUtils.MakePathSafeFromEncodingProblems(FolderPath);
			BookStorage.SetBaseForRelativePaths(printingDom, pathSafeForWkHtml2Pdf);

			switch (bookletPortion)
			{
				case PublishModel.BookletPortions.AllPagesNoBooklet:
					break;
				case PublishModel.BookletPortions.BookletCover:
					DeletePages(printingDom.RawDom, p => !p.GetAttribute("class").ToLowerInvariant().Contains("cover"));
					break;
				 case PublishModel.BookletPortions.BookletPages:
					DeletePages(printingDom.RawDom, p => p.GetAttribute("class").ToLowerInvariant().Contains("cover"));
					break;
				 default:
					throw new ArgumentOutOfRangeException("bookletPortion");
			}
			AddCoverColor(printingDom, Color.White);
			AddPreviewJScript(printingDom);
			return printingDom;
		}

		/// <summary>
		/// used when this book is a "master"/"folio" book that is used to bring together a number of other books in the collection
		/// </summary>
		/// <param name="printingDom"></param>
		/// <param name="currentBookCollection"></param>
		/// <param name="bookServer"></param>
		private void AddChildBookContentsToFolio(HtmlDom printingDom, BookCollection currentBookCollection, BookServer bookServer)
		{
			XmlNode currentLastContentPage = GetLastPageForInsertingNewContent(printingDom);

			//currently we have no way of filtering them, we just take them all
			foreach (var bookInfo in currentBookCollection.GetBookInfos())
			{
				if (bookInfo.IsFolio)
					continue;
				var childBook =bookServer.GetBookFromBookInfo(bookInfo);

				//this will set the class bloom-content1 on the correct language
				//this happens anyhow if the page was ever looked at in the Edti Tab
				//But if we are testing a collection's folio pdf'ing ability on a newly-generated
				//SHRP collection, and we don't do this, we see lots of sample text because every
				//bloom-editable has "bloom-content1", even the "Z" language ones.
				childBook.UpdateEditableAreasOfElement(childBook.OurHtmlDom);
				
				//add links to the template css needed by the children.
				//NB: at this point this code can't hand the "userModifiedStyles" from children, it'll ignore them (they would conflict with each other)
				//NB: at this point custom styles (e.g. larger/smaller font rules) from children will be lost.
				var userModifiedStyleSheets = new List<string>();
				foreach (string sheetName in childBook.OurHtmlDom.GetTemplateStyleSheets())
				{
					if (!userModifiedStyleSheets.Contains(sheetName)) //nb: if two books have stylesheets with the same name, we'll only be grabbing the 1st one.
					{
						userModifiedStyleSheets.Add(sheetName);
						printingDom.AddStyleSheetIfMissing(sheetName);
					}
				}
				printingDom.SortStyleSheetLinks();

				foreach (XmlElement pageDiv in childBook.OurHtmlDom.RawDom.SafeSelectNodes("/html/body//div[contains(@class, 'bloom-page') and not(contains(@class,'bloom-frontMatter')) and not(contains(@class,'bloom-backMatter'))]"))
				{
					XmlElement importedPage = (XmlElement) printingDom.RawDom.ImportNode(pageDiv, true);
					currentLastContentPage.ParentNode.InsertAfter(importedPage, currentLastContentPage);
					currentLastContentPage = importedPage;

					foreach(XmlElement img in importedPage.SafeSelectNodes("descendant::img"))
					{
						var bookFolderName = Path.GetFileName(bookInfo.FolderPath);
						var pathRelativeToFolioFolder = ".../" + bookFolderName + "/" + img.GetAttribute("src");
						//NB: URLEncode would replace spaces with '+', which is ok in the parameter section, but not the URL
						//So we are using UrlPathEncode
						var fullPathInLinkFormat = HttpUtility.UrlPathEncode(pathRelativeToFolioFolder);
						img.SetAttribute("src", fullPathInLinkFormat);
					}
				}
			}
		}

		private XmlElement GetLastPageForInsertingNewContent(HtmlDom printingDom)
		{
			var lastPage =
				   printingDom.RawDom.SelectSingleNode("/html/body//div[contains(@class, 'bloom-page') and not(contains(@class,'bloom-frontMatter')) and not(contains(@class,'bloom-backMatter'))][last()]") as XmlElement;
			if(lastPage==null)
			{
				//currently nothing but front and back matter
				var lastFrontMatter= printingDom.RawDom.SelectSingleNode("/html/body//div[contains(@class,'bloom-frontMatter')][last()]") as XmlElement;
				if(lastFrontMatter ==null)
					throw new ApplicationException("GetLastPageForInsertingNewContent() found no content pages nor frontmatter");
				return lastFrontMatter;
			}
			else
			{
				return (XmlElement) lastPage;
			}
		}

		/// <summary>
		/// this is used for configuration, where we do want to offer up the original file.
		/// </summary>
		/// <returns></returns>
		public string GetPathHtmlFile()
		{
			return _storage.PathToExistingHtml;
		}

		public PublishModel.BookletLayoutMethod GetDefaultBookletLayout()
		{
			//NB: all we support at the moment is specifying "Calendar"
			if(OurHtmlDom.SafeSelectNodes(String.Format("//meta[@name='defaultBookletLayout' and @content='Calendar']")).Count>0)
				return PublishModel.BookletLayoutMethod.Calendar;
			else
				return PublishModel.BookletLayoutMethod.SideFold;
		}

		/// <summary>
		/// This stylesheet is used to hide all the elements we don't want to show, e.g. because they are not in the languages of this publication.
		/// We read in the template version, then replace some things and write it out to the publication folder.
		/// </summary>
		private void WriteLanguageDisplayStyleSheet( )
		{
			var template = File.ReadAllText(_storage.GetFileLocator().LocateFileWithThrow("languageDisplayTemplate.css"));
			var path = _storage.FolderPath.CombineForPath("languageDisplay.css");

			using (var temp = TempFile.WithExtension(".css"))
			{
				File.WriteAllText(temp.Path,
					template.Replace("VERNACULAR", _collectionSettings.Language1Iso639Code)
						.Replace("NATIONAL", _collectionSettings.Language2Iso639Code));

				//hoping this helps with the occasional report we were getting where the files were in Dropbox and
				//the previous File.Delete(path) would fail:
				FileUtils.ReplaceFileWithUserInteractionIfNeeded(temp.Path, path, null);
			}
			//ENHANCE: this works for editable books, but for shell collections, it would be nice to show the national language of the user... e.g., when browsing shells,
			//see the French.  But we don't want to be changing those collection folders at runtime if we can avoid it. So, this style sheet could be edited in memory, at runtime.
		}

		/// <summary>
		///Under normal conditions, this isn't needed, because it is done when a book is first created. But thing might have changed:
		/// *changing xmatter pack, and update to it, changing the languages, etc.
		/// *the book was dragged from another project
		/// *the editing language was changed.
		/// Under those conditions, if we didn't, for example, do a PrepareElementsInPageOrDocument, we would end up with no
		/// editable items, because there are no elements in our language.
		/// </summary>
		public void PrepareForEditing()
		{
			if (!_haveDoneUpdate)
			{
				BringBookUpToDate(OurHtmlDom, new NullProgress());
				_haveDoneUpdate = true;
			}
			//We could re-enable RebuildXMatter() here later, so that we get this nice refresh each time.
			//But currently this does some really slow image compression:	RebuildXMatter(RawDom);
			UpdateEditableAreasOfElement(OurHtmlDom);
		}

		/// <summary>
		/// This is called both for the whole book, and for individual pages when the user uses Origami to make changes to the layout of the page
		/// </summary>
		/// <param name="elementToUpdate"></param>
		public void UpdateEditableAreasOfElement(HtmlDom elementToUpdate)
		{
			var language1Iso639Code = _collectionSettings.Language1Iso639Code;
			var language2Iso639Code = _collectionSettings.Language2Iso639Code;
			var language3Iso639Code = _collectionSettings.Language3Iso639Code;
			var multilingualContentLanguage2 = _bookData.MultilingualContentLanguage2;
			var multilingualContentLanguage3 = _bookData.MultilingualContentLanguage3;
			foreach (XmlElement div in elementToUpdate.SafeSelectNodes("//div[contains(@class,'bloom-page')]"))
			{
				TranslationGroupManager.PrepareElementsInPageOrDocument(div, _collectionSettings);
				TranslationGroupManager.UpdateContentLanguageClasses(div, _collectionSettings, language1Iso639Code, multilingualContentLanguage2, multilingualContentLanguage3);
			}
		}

		/// <summary>
		/// Will call either 'callback' or 'errorCallback' UNLESS the thumbnail is readonly, in which case it will do neither.
		/// </summary>
		/// <param name="thumbnailOptions"></param>
		/// <param name="callback"></param>
		/// <param name="errorCallback"></param>
		public void RebuildThumbNailAsync(HtmlThumbNailer.ThumbnailOptions thumbnailOptions,  Action<BookInfo, Image> callback, Action<BookInfo, Exception> errorCallback)
		{
			if (!_storage.RemoveBookThumbnail(thumbnailOptions.FileName))
			{
				// thumbnail is marked readonly, so just use it
				Image thumb;
				_storage.TryGetPremadeThumbnail(thumbnailOptions.FileName, out thumb);
				callback(this.BookInfo, thumb);
				return;
			}

			_thumbnailProvider.RemoveFromCache(_storage.Key);

			thumbnailOptions.BorderStyle = (Type == BookType.Publication)?HtmlThumbNailer.ThumbnailOptions.BorderStyles.Solid : HtmlThumbNailer.ThumbnailOptions.BorderStyles.Dashed;
			GetThumbNailOfBookCoverAsync(thumbnailOptions, image=>callback(this.BookInfo,image),
				error=>
					{
						//Enhance; this isn't a very satisfying time to find out, because it's only going to happen if we happen to be rebuilding the thumbnail.
						//It does help in the case where things are bad, so no thumbnail was created, but by then probably the user has already had some big error.
						//On the other hand, given that they have this bad book in their collection now, it's good to just remind them that it's broken and not
						//keep showing green error boxes.
						CheckForErrors();
						errorCallback(this.BookInfo, error);
					});
		}

		public string GetErrorsIfNotCheckedBefore()
		{
			if (!_haveCheckedForErrorsAtLeastOnce)
			{
				return CheckForErrors();
			}
			return "";
		}

		public string CheckForErrors()
		{
			var errors = _storage.GetValidateErrors();
			_haveCheckedForErrorsAtLeastOnce = true;
			if (!String.IsNullOrEmpty(errors))
			{
				_log.WriteError(errors);
			}
			return errors ?? "";
		}

		public void CheckBook(IProgress progress, string pathToFolderOfReplacementImages=null)
		{
			_storage.CheckBook(progress, pathToFolderOfReplacementImages);
		}

		public Layout GetLayout()
		{
			return Layout.FromDom(OurHtmlDom, Layout.A5Portrait);
		}

		public IEnumerable<Layout> GetLayoutChoices()
		{
			try
			{
				return SizeAndOrientation.GetLayoutChoices(OurHtmlDom, _storage.GetFileLocator());
			}
			catch (Exception error)
			{
				_log.WriteError(error.Message);
				throw error;
			}
		}

		public void SetLayout(Layout layout)
		{
			SizeAndOrientation.AddClassesForLayout(OurHtmlDom, layout);
		}


		/// <summary>
		/// This is used when the user elects to apply the same image metadata to all images.
		/// </summary>
		public void CopyImageMetadataToWholeBookAndSave(Metadata metadata, IProgress progress)
		{
			ImageUpdater.CopyImageMetadataToWholeBook(_storage.FolderPath,OurHtmlDom, metadata, progress);
			Save();
		}

		public Metadata GetLicenseMetadata()
		{
			return _bookData.GetLicenseMetadata();
		}

		public void UpdateLicenseMetdata(Metadata metadata)
		{
			_bookData.SetLicenseMetdata(metadata);
			BookInfo.License = metadata.License.Token;
			BookInfo.Copyright = metadata.CopyrightNotice;
			// obfuscate any emails in the license notes.
			var notes = metadata.License.RightsStatement;
			if (notes == null)
				return;
			// recommended at http://www.regular-expressions.info/email.html.
			// This purposely does not handle non-ascii emails, or ones with special characters, which he says few servers will handle anyway.
			// It is also not picky about exactly valid top-level domains (or country codes), and will exclude the rare 'museum' top-level domain.
			// There are several more complex options we could use there. Just be sure to add () around the bit up to (and including) the @,
			// and another pair around the rest.
			var regex = new Regex("\\b([A-Z0-9._%+-]+@)([A-Z0-9.-]+.[A-Z]{2,4})\\b", RegexOptions.IgnoreCase);
			// We keep the existing email up to 2 characters after the @, and replace the rest with a message.
			// Not making the message localizable as yet, since the web site isn't, and I'm not sure what we would need
			// to put to make it so. A fixed string seems more likely to be something we can replace with a localized version,
			// in the language of the web site user rather than the language of the uploader.
			notes = regex.Replace(notes,
				new MatchEvaluator(
					m =>
						m.Groups[1].Value + m.Groups[2].Value.Substring(0, 2) +
						"(download book to read full email address)"));
			BookInfo.LicenseNotes = notes;
		}

		public void SetTitle(string name)
		{
			OurHtmlDom.Title = name;
		}

		public void ExportXHtml(string path)
		{
			XmlHtmlConverter.GetXmlDomFromHtmlFile(_storage.PathToExistingHtml,true).Save(path);
		}

		/// <summary>
		/// public for use in external scripts that set up pre-packaged books/collections
		/// </summary>
		/// <param name="key"></param>
		/// <param name="value"></param>
		/// <param name="libaryValue"></param>
		public void SetDataItem(string key, string value, string languageCode)
		{
			_bookData.Set(key, value,languageCode);
		}

		public void Save()
		{
			Guard.Against(Type != Book.BookType.Publication, "Tried to save a non-editable book.");
			_bookData.UpdateVariablesAndDataDivThroughDOM(BookInfo);//will update the title if needed
			_storage.UpdateBookFileAndFolderName(_collectionSettings); //which will update the file name if needed
			_storage.Save();
		}

		//TODO: remove this in favor of meta data (the later currently doesn't appear to have access to lineage, I need to ask JT about that)
		public string GetBookLineage()
		{
			return OurHtmlDom.GetMetaValue("bloomBookLineage","");
		}


		public bool IsCalendar
		{
			get
			{
				if (OurHtmlDom == null)
					return false;

				return OurHtmlDom.GetMetaValue("defaultBookletLayout", "") == "Calendar";
			}
		}
		public MultiTextBase GetDataItem(string name)
		{
			return _bookData.GetMultiTextVariableOrEmpty(name);
		}
	}
}
