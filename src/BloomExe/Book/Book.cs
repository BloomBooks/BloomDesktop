using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using System.Windows.Forms;
using System.Xml;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.ImageProcessing;
using Bloom.Publish;
using Bloom.WebLibraryIntegration;
using L10NSharp;
using MarkdownSharp;
using SIL.Code;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Text;
using SIL.Windows.Forms.ClearShare;
using SIL.Xml;

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

		private readonly PageSelection _pageSelection;
		private readonly PageListChangedEvent _pageListChangedEvent;
		private readonly BookRefreshEvent _bookRefreshEvent;
		private readonly IBookStorage _storage;
		private List<IPage> _pagesCache;
		internal const string kIdOfBasicBook = "056B6F11-4A6C-4942-B2BC-8861E62B03B3";

		public event EventHandler ContentsChanged;

		//nb: it looks like nothing is currently writing to it.
		//Instead, the code is currently writing to the global application Logger.
		//Should we remove this, or return some errant code to using it instead of Logger?
		private readonly IProgress _log = new StringBuilderProgress();

		private bool _haveCheckedForErrorsAtLeastOnce;
		private readonly BookData _bookData;

		//for moq'ing only
		public Book()
		{
		}

		public Book(BookInfo info, IBookStorage storage, ITemplateFinder templateFinder,
		   CollectionSettings collectionSettings,
			PageSelection pageSelection,
			PageListChangedEvent pageListChangedEvent,
			BookRefreshEvent bookRefreshEvent)
		{
			BookInfo = info;
			UserPrefs = UserPrefs.LoadOrMakeNew(Path.Combine(info.FolderPath, "book.userPrefs"));

			Guard.AgainstNull(storage,"storage");

			// This allows the _storage to
			storage.MetaData = info;

			_storage = storage;

			//this is a hack to keep these two in sync (in one direction)
			_storage.FolderPathChanged += _storage_FolderPathChanged;

			_templateFinder = templateFinder;

			_collectionSettings = collectionSettings;

			_pageSelection = pageSelection;
			_pageListChangedEvent = pageListChangedEvent;
			_bookRefreshEvent = bookRefreshEvent;
			_bookData = new BookData(OurHtmlDom,
					_collectionSettings, UpdateImageMetadataAttributes);

			InjectStringListingActiveLanguagesOfBook(); 

			if (!HasFatalError && IsEditable)
			{
				_bookData.SynchronizeDataItemsThroughoutDOM();

				WriteLanguageDisplayStyleSheet(); //NB: if you try to do this on a file that's in program files, access will be denied
				OurHtmlDom.AddStyleSheet(@"languageDisplay.css");
			}

			//if we're showing the user a shell/template book, pick a color for it 
			//If it is editable, then we don't want to change to the next color, we
			//want to use the color that we used for the sample shell/template we
			//showed them previously.
			if (!info.IsEditable)
			{
				Book.SelectNextCoverColor(); // we only increment when showing a template or shell
				InitCoverColor();
			}

			// If it doesn't already have a cover color give it one.
			if (OurHtmlDom.SafeSelectNodes("//head/style/text()[contains(., 'coverColor')]").Count == 0)
			{
				InitCoverColor(); // should use the same color as what they saw in the preview of the template/shell
			}
			FixBookIdAndLineageIfNeeded();
			_storage.Dom.RemoveExtraBookTitles();
            _storage.Dom.RemoveExtraContentTypesMetas();
			Guard.Against(OurHtmlDom.RawDom.InnerXml=="","Bloom could not parse the xhtml of this document");
		}

		void _storage_FolderPathChanged(object sender, EventArgs e)
		{
			BookInfo.FolderPath = _storage.FolderPath;
			UserPrefs.UpdateFileLocation(_storage.FolderPath);
		}

		/// <summary>
		/// This just increments the color index so that the next book to be constructed that doesn't already have a color will use it
		/// </summary>
		public static void SelectNextCoverColor()
		{
			_coverColorIndex = _coverColorIndex+1;
			if( _coverColorIndex >= CoverColors.Length)
				_coverColorIndex = 0;
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
		public virtual string TitleBestForUserDisplay
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
					var SHRPMatches = new string[] { "p1", "p2", "p3", "p4", "SHRP" };
					var couldBeOldStyleUgandaSHRPBook = SHRPMatches.Any(m => englishTitle.Contains(m.ToLowerInvariant()));

					//if this book is one of the ones we're editing in our collection, it really
					//needs a title in our main language, it would be confusing to show a title from some other langauge
					if (!couldBeOldStyleUgandaSHRPBook && (IsEditable || title.Empty))
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
				display = display.Replace("<br />", " ").Replace("\r\n", " ").Replace("\n", " ").Replace("  ", " ");
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

			//dom.AddJavascriptFile("lib/ckeditor/ckeditor.js".ToLocalhost());

			//reviewslog: added this to get the "WebFXTabPane()" working in StyleEditor. Previously tried adding "export" to the function and then importing it
			dom.AddJavascriptFile("lib/tabpane.js".ToLocalhost());

			//reviewslog: four lines are prompted by the qtip "too much recursion" error, which I got on certain pages. The qtip
			//code in question says it is for when jquery-ui is not found. I "solved" this by loading jquery, jquery-ui,
			//and finally qtip into the global space here
			dom.AddJavascriptFile("jquery.min.js".ToLocalhost());
			dom.AddJavascriptFile("modified_libraries/jquery-ui/jquery-ui-1.10.3.custom.min.js".ToLocalhost());
//			dom.AddJavascriptFile("lib/jquery.qtip.js".ToLocalhost());
//			dom.AddJavascriptFile("lib/jquery.qtipSecondary.js".ToLocalhost());

			// first tried this as import 'jquery.hotkeys' in bloomEditing, but that didn't work
			//dom.AddJavascriptFile("jquery.hotkeys.js".ToLocalhost());
			
			dom.AddJavascriptFile("commonBundle.js".ToLocalhost());
			dom.AddJavascriptFile("editablePageBundle.js".ToLocalhost());
			// Add this last because currently its document ready function has to execute AFTER the bootstrap call in bloomEditing.ts,
			// which is compiled into editablePageIFrame.js. The bootstrap function sets CKEDITOR.disableAutoInline = true,
			// which suppresses a document ready function in CKEditor iself from calling inline() on all content editable
			// elements, which we don't want (a) because some content editable elements shouldn't have CKEditor functions, and
			// (b) because it causes crashes when we intentionally do our own inline() calls on the elements where we DO
			// want CKEditor.
			// ReviewSlog: It would be much more robust not to depend on the order in which document ready functions
			// execute, especially if the only control over that is the order of loading files. But I don't know
			// where we can put the CKEDITOR.disableAutoInline = true so that it will reliably execute AFTER CKEDITOR is
			// defined and BEFORE its document ready function.
			dom.AddJavascriptFile("lib/ckeditor/ckeditor.js".ToLocalhost());
		}


		private void UpdateMultilingualSettings(HtmlDom dom)
		{
			TranslationGroupManager.UpdateContentLanguageClasses(dom.RawDom, _collectionSettings, _collectionSettings.Language1Iso639Code, _bookData.MultilingualContentLanguage2,
													 _bookData.MultilingualContentLanguage3);

			BookStarter.SetLanguageForElementsWithMetaLanguage(dom.RawDom, _collectionSettings);
		}

		private HtmlDom GetHtmlDomWithJustOnePage(IPage page)
		{
			var divNodeForThisPage = page.GetDivNodeForThisPage();
			if (divNodeForThisPage == null)
			{
				throw new ApplicationException(String.Format("The requested page {0} from book {1} isn't in this book {2}.", page.Id,
															 page.Book.FolderPath, FolderPath));
			}

			return GetHtmlDomWithJustOnePage(divNodeForThisPage);
		}

		public HtmlDom GetHtmlDomWithJustOnePage(XmlElement divNodeForThisPage)
		{
			var headXml = _storage.Dom.SelectSingleNodeHonoringDefaultNS("/html/head").OuterXml;
			var dom = new HtmlDom(@"<html>" + headXml + "<body></body></html>");
			dom = _storage.MakeDomRelocatable(dom, _log);
			// Don't let spaces between <strong>, <em>, or <u> elements be removed. (BL-2484)
			dom.RawDom.PreserveWhitespace = true;
			var body = dom.RawDom.SelectSingleNodeHonoringDefaultNS("//body");

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
			pageDom.RemoveModeStyleSheets();
			foreach (var cssFileName in new[] { @"basePage.css","previewMode.css", "origami.css" })
			{
				pageDom.AddStyleSheet(cssFileName);
			}
			pageDom.SortStyleSheetLinks();

			AddPreviewJavascript(pageDom);//review: this is just for thumbnails... should we be having the javascript run?
			return pageDom;
		}

		// Differs from GetPreviewXmlDocumentForPage() by not adding the three stylesheets
		// adding them will full paths seems to be diastrous. I think cross-domain rules
		// prevent them from being loaded, and so we lose the page size information, and the
		// thumbs come out random sizes. Not sure why this isn't a problem in GetPreviewXmlDocumentForPage.
		// Also, since this is used for thumbnails of template pages, we insert some arbitrary text
		// into empty editable divs to give a better idea of what a typical page will look like.
		internal HtmlDom GetThumbnailXmlDocumentForPage(IPage page)
		{
			if (_log.ErrorEncountered)
			{
				return GetErrorDom();
			}
			var pageDom = GetHtmlDomWithJustOnePage(page);
			pageDom.SortStyleSheetLinks();
			AddPreviewJavascript(pageDom);
			AddFillerTextToEmptyEditDivs(pageDom);
			return pageDom;
		}

		private static void AddFillerTextToEmptyEditDivs(HtmlDom pageDom)
		{
			var placeHolderText =
				"Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed porttitor ex at sapien accumsan convallis. Aenean varius nisi justo. Pellentesque habitant morbi tristique senectus et netus et malesuada fames ac turpis egestas.";
			var emptyDivs =
				pageDom.RawDom.SafeSelectNodes("//div[@contenteditable='true' and string-length(normalize-space(text()))=0]");
			if (emptyDivs.Count > 3)
				placeHolderText = "Lorem";
			foreach (XmlElement div in emptyDivs)
				div.InnerText = placeHolderText;
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

		private HtmlDom GetErrorDom(string extraMessages="")
		{
			var builder = new StringBuilder();
			builder.Append("<html><body style='font-family:arial,sans'>");

			if(_storage != null)
			{
				builder.AppendLine(_storage.GetBrokenBookRecommendationHtml());
			}
			else
			{
				builder.AppendLine(BookStorage.GenericBookProblemNotice);
			}

			builder.Append(((StringBuilderProgress) _log).Text);//review: is this ever non-empty?

			builder.Append("<p>"+ WebUtility.HtmlEncode(extraMessages)+"</p>");

			var message = LocalizationManager.GetString("Errors.ReportThisProblemButton", "Report this problem to Bloom Support");
			builder.AppendFormat(
				"<input type='button' value='"+message+"' href='ReportProblem'></input>");

			builder.Append("</body></html>");
			
			return new HtmlDom(builder.ToString());
		}

		private bool IsDownloaded
		{
			get { return FolderPath.StartsWith(BookTransfer.DownloadFolder); }
		}

		public virtual bool CanDelete
		{
			// BL-2678: we want the user to be able to delete troublesome/no longer needed books
			// downloaded from BloomLibrary.org
			get { return IsEditable || IsDownloaded; }
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


		/// <summary>
		/// First page in the book (or null if there are none)
		/// </summary>
		public IPage FirstPage
		{
			get { return GetPages().FirstOrDefault(); }
		}

		public IPage GetPageByIndex(int pageIndex)
		{
			// index must be >= 0
			if (pageIndex < 0) return null;

			// index must be less than the number of pages
			var pages = GetPages().ToList();
			if (pages.Count <= pageIndex) return null;

			return pages[pageIndex];
		}

		// Reduce repetitive reloading of books when looking up the related "TemplateBook".
		string _cachedTemplateKey;
		Book _cachedTemplateBook;

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
					// We can assume that a book's "TemplateBook" does not change over time.  To be even safer,
					// we'll add a check for the same "TemplateKey" to allow reusing a cached "TemplateBook".
					// See https://silbloom.myjetbrains.com/youtrack/issue/BL-3782.
					if (templateKey == _cachedTemplateKey && _cachedTemplateBook != null)
						return _cachedTemplateBook;
					book = _templateFinder.FindAndCreateTemplateBookByFileName(templateKey);
					_cachedTemplateBook = book;
					_cachedTemplateKey = templateKey;
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

		public virtual HtmlDom OurHtmlDom
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
				return GetErrorDom(_storage.GetValidateErrors());
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

			AddPreviewJavascript(previewDom);
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
			string oldMetaData = "";
			if (RobustFile.Exists(BookInfo.MetaDataPath))
				oldMetaData = RobustFile.ReadAllText(BookInfo.MetaDataPath); // Have to read this before other migration overwrites it.
			BringBookUpToDate(OurHtmlDom, progress);
			if (Type == BookType.Publication)
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

			if (oldMetaData.Contains("readerToolsAvailable"))
			{
				var newMetaString = oldMetaData.Replace("readerToolsAvailable", "toolboxIsOpen");
				var newMetaData = BookMetaData.FromString(newMetaString);
				BookInfo.ToolboxIsOpen = newMetaData.ToolboxIsOpen;
			}

			Save();
			if (_bookRefreshEvent != null)
			{
				_bookRefreshEvent.Raise(this);
			}
		}

		class GuidAndPath
		{
			public string Guid; // replacement guid
			public string Path; // where to find file, relative to root templates directory
		}

		private static Dictionary<string, GuidAndPath> _pageMigrations;

		/// <summary>
		/// Get (after initializing, if necessary) the dictionary mapping page IDs we know how to migrate
		/// onto the ID and file location of the page we want to update it to.
		/// Paths are relative to root templates directory
		/// </summary>
		private static Dictionary<string, GuidAndPath> PageMigrations
		{
			get
			{
				if (_pageMigrations == null)
				{
					_pageMigrations = new Dictionary<string, GuidAndPath>();
					// Basic Book
					_pageMigrations["5dcd48df-e9ab-4a07-afd4-6a24d0398382"] = new GuidAndPath() { Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398382", Path = "Basic Book/Basic Book.html" }; // Basic Text and Picture
					_pageMigrations["5dcd48df-e9ab-4a07-afd4-6a24d0398383"] = new GuidAndPath() { Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398383", Path = "Basic Book/Basic Book.html" }; // Picture in Middle
					_pageMigrations["5dcd48df-e9ab-4a07-afd4-6a24d0398384"] = new GuidAndPath() { Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398384", Path = "Basic Book/Basic Book.html" }; // Picture on Bottom
					_pageMigrations["5dcd48df-e9ab-4a07-afd4-6a24d0398385"] = new GuidAndPath() { Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398385", Path = "Basic Book/Basic Book.html" }; // Just a Picture
					_pageMigrations["d31c38d8-c1cb-4eb9-951b-d2840f6a8bdb"] = new GuidAndPath() { Guid = "a31c38d8-c1cb-4eb9-951b-d2840f6a8bdb", Path = "Basic Book/Basic Book.html" }; // Just Text
					_pageMigrations["FD115DFF-0415-4444-8E76-3D2A18DBBD27"] = new GuidAndPath() { Guid = "aD115DFF-0415-4444-8E76-3D2A18DBBD27", Path = "Basic Book/Basic Book.html" }; // Picture & Word
					// Big book [see commit 7bfefd0dbc9faf8930c4926b0156e44d3447e11b]
					_pageMigrations["AF708725-E961-44AA-9149-ADF66084A04F"] = new GuidAndPath() { Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398385", Path = "Big Book/BigBook.html" }; // Just a Picture
					_pageMigrations["D9A55EB6-43A8-4C6A-8891-2C1CDD95772C"] = new GuidAndPath() { Guid = "a31c38d8-c1cb-4eb9-951b-d2840f6a8bdb", Path = "Big Book/BigBook.html" }; // Just Text
					// Decodable reader [see commit 7bfefd0dbc9faf8930c4926b0156e44d3447e11b]
					_pageMigrations["f95c0314-ce47-4b47-a638-06325ad1a963"] = new GuidAndPath() { Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398382", Path = "Decodable Reader/Decodable Reader.html" }; // Basic Text and Picture
					_pageMigrations["c0847f89-b58a-488a-bbee-760ce4a13567"] = new GuidAndPath() { Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398383", Path = "Decodable Reader/Decodable Reader.html" }; // Picture in Middle
					_pageMigrations["f99b252a-26b1-40c8-b543-dbe0b05f08a5"] = new GuidAndPath() { Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398384", Path = "Decodable Reader/Decodable Reader.html" }; // Picture on Bottom
					_pageMigrations["c506f278-cb9f-4053-9e29-f7a9bdf64445"] = new GuidAndPath() { Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398385", Path = "Decodable Reader/Decodable Reader.html" }; // Just a Picture
					_pageMigrations["e4ff6195-b0b6-4909-8025-4424ee9188ea"] = new GuidAndPath() { Guid = "a31c38d8-c1cb-4eb9-951b-d2840f6a8bdb", Path = "Decodable Reader/Decodable Reader.html" }; // Just Text
					_pageMigrations["bd85f898-0a45-45b3-8e34-faaac8945a0c"] = new GuidAndPath() { Guid = "aD115DFF-0415-4444-8E76-3D2A18DBBD27", Path = "Decodable Reader/Decodable Reader.html" }; // Picture & Word
					// Leveled reader [see commit 7bfefd0dbc9faf8930c4926b0156e44d3447e11b]
					_pageMigrations["e9f2142b-f135-4bcd-9123-5a2623f5302f"] = new GuidAndPath() { Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398382", Path = "Leveled Reader/Leveled Reader.html" }; // Basic Text and Picture
					_pageMigrations["c5aae471-f801-4c5d-87b7-1614d56b0c53"] = new GuidAndPath() { Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398383", Path = "Leveled Reader/Leveled Reader.html" }; // Picture in Middle
					_pageMigrations["a1f437fe-c002-4548-af02-fe84d048b8fc"] = new GuidAndPath() { Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398384", Path = "Leveled Reader/Leveled Reader.html" }; // Picture on Bottom
					_pageMigrations["d7599aa7-f35c-4029-8aa2-9afda870bcfa"] = new GuidAndPath() { Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398385", Path = "Leveled Reader/Leveled Reader.html" }; // Just a Picture
					_pageMigrations["d93a28c6-9ff8-4f61-a820-49093e3e275b"] = new GuidAndPath() { Guid = "a31c38d8-c1cb-4eb9-951b-d2840f6a8bdb", Path = "Leveled Reader/Leveled Reader.html" }; // Just Text
					_pageMigrations["a903467a-dad2-4767-8be9-54336cae7731"] = new GuidAndPath() { Guid = "aD115DFF-0415-4444-8E76-3D2A18DBBD27", Path = "Leveled Reader/Leveled Reader.html" }; // Picture & Word
				}
				return _pageMigrations;
			}
		}

		/// <summary>
		/// Bring the page up to date. Currently this is used to switch various old page types to new versions
		/// based on Custom Page (so they can actually be customized).
		/// </summary>
		/// <param name="page"></param>
		public void BringPageUpToDate(XmlElement page)
		{
			var lineageAttr = page.Attributes["data-pagelineage"];
			if (lineageAttr == null)
				return;
			var lineage = lineageAttr.Value;
			var originalTemplateGuid = lineage;
			int index = lineage.IndexOf(";", StringComparison.InvariantCulture);
			if (index >= 0)
				originalTemplateGuid = lineage.Substring(0, index);
			GuidAndPath updateTo;
			if (!PageMigrations.TryGetValue(originalTemplateGuid, out updateTo))
				return; // Not one we want to migrate. Possibly already done, or one we don't want to convert, or created in the field...
			var layoutOfThisBook = GetLayout();
			var bookPath = BloomFileLocator.GetFactoryBookTemplateDirectory(updateTo.Path);
			var templateDoc = XmlHtmlConverter.GetXmlDomFromHtmlFile(bookPath, false);
			var newPage = (XmlElement)templateDoc.SafeSelectNodes("//div[@id='" + updateTo.Guid + "']")[0];
			var classesToDrop = new[] { "imageWholePage","imageOnTop","imageInMiddle","imageOnBottom","textWholePage","pictureAndWordPage" };
            HtmlDom.MergeClassesIntoNewPage(page, newPage, classesToDrop);
			SizeAndOrientation.UpdatePageSizeAndOrientationClasses(newPage, layoutOfThisBook);
			OurHtmlDom.MigrateEditableData(page, newPage, lineage.Replace(originalTemplateGuid, updateTo.Guid));
		}

		private object _updateLock = new object();
		private bool _doingBookUpdate = false;

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
			if (Title.Contains("allowSharedUpdate"))
			{
				// Original version of this code that suffers BL_3166
				progress.WriteStatus("Updating Front/Back Matter...");
				BringXmatterHtmlUpToDate(bookDOM);

				progress.WriteStatus("Gathering Data...");
				TranslationGroupManager.PrepareElementsInPageOrDocument(bookDOM.RawDom, _collectionSettings);
				progress.WriteStatus("Updating Data...");

				InjectStringListingActiveLanguagesOfBook();

				//hack
				if (bookDOM == OurHtmlDom) //we already have a data for this
				{
					_bookData.SynchronizeDataItemsThroughoutDOM();

					// I think we should only mess with tags if we are updating the book for real.
					var oldTagsPath = Path.Combine(_storage.FolderPath, "tags.txt");
					if (RobustFile.Exists(oldTagsPath))
					{
						ConvertTagsToMetaData(oldTagsPath, BookInfo);
						RobustFile.Delete(oldTagsPath);
					}
				}
				else //used for making a preview dom
				{
					var bd = new BookData(bookDOM, _collectionSettings, UpdateImageMetadataAttributes);
					bd.SynchronizeDataItemsThroughoutDOM();
				}
				// get any license info into the json and restored in the replaced front matter.
				BookCopyrightAndLicense.SetMetadata(GetLicenseMetadata(), bookDOM, FolderPath, CollectionSettings);

				bookDOM.RemoveMetaElement("bloomBookLineage", () => BookInfo.BookLineage, val => BookInfo.BookLineage = val);
				bookDOM.RemoveMetaElement("bookLineage", () => BookInfo.BookLineage, val => BookInfo.BookLineage = val);
				// BookInfo will always have an ID, the constructor makes one even if there is no json file.
				// To allow migration, pretend it has no ID if there is not yet a meta.json.
				bookDOM.RemoveMetaElement("bloomBookId", () => (RobustFile.Exists(BookInfo.MetaDataPath) ? BookInfo.Id : null),
					val => BookInfo.Id = val);

				// Title should be replicated in json
				//if (!string.IsNullOrWhiteSpace(Title)) // check just in case we somehow have more useful info in json.
				//    bookDOM.Title = Title;
				// Bit of a kludge, but there's no way to tell whether a boolean is already set in the JSON, so we fake that it is not,
				// thus ensuring that if something is in the metadata we use it.
				// If there is nothing there the default of true will survive.
				bookDOM.RemoveMetaElement("SuitableForMakingVernacularBooks", () => null,
					val => BookInfo.IsSuitableForVernacularLibrary = val == "yes" || val == "definitely");

				UpdateTextsNewlyChangedToRequiresParagraph(bookDOM);

				//we've removed and possible added pages, so our page cache is invalid
				_pagesCache = null;
			}
			else
			{
				// New version that we hope prevents BL_3166
				if (_doingBookUpdate)
					MessageBox.Show("Caught Bloom doing two updates at once! Possible BL-3166 is being prevented");
				lock (_updateLock)
				{
					_doingBookUpdate = true;
					progress.WriteStatus("Updating Front/Back Matter...");
					// Nothing in the update process should change the license info, so save what is current before we mess with
					// anything (may fix BL-3166).
					var licenseMetadata = GetLicenseMetadata();
					BringXmatterHtmlUpToDate(bookDOM);

					progress.WriteStatus("Gathering Data...");
					TranslationGroupManager.PrepareElementsInPageOrDocument(bookDOM.RawDom, _collectionSettings);
					progress.WriteStatus("Updating Data...");

					InjectStringListingActiveLanguagesOfBook();

					//hack
					if (bookDOM == OurHtmlDom) //we already have a data for this
					{
						_bookData.SynchronizeDataItemsThroughoutDOM();

						// I think we should only mess with tags if we are updating the book for real.
						var oldTagsPath = Path.Combine(_storage.FolderPath, "tags.txt");
						if (RobustFile.Exists(oldTagsPath))
						{
							ConvertTagsToMetaData(oldTagsPath, BookInfo);
							RobustFile.Delete(oldTagsPath);
						}
					}
					else //used for making a preview dom
					{
						var bd = new BookData(bookDOM, _collectionSettings, UpdateImageMetadataAttributes);
						bd.SynchronizeDataItemsThroughoutDOM();
					}
					// get any license info into the json and restored in the replaced front matter.
					BookCopyrightAndLicense.SetMetadata(licenseMetadata, bookDOM, FolderPath, CollectionSettings);

					bookDOM.RemoveMetaElement("bloomBookLineage", () => BookInfo.BookLineage, val => BookInfo.BookLineage = val);
					bookDOM.RemoveMetaElement("bookLineage", () => BookInfo.BookLineage, val => BookInfo.BookLineage = val);
					// BookInfo will always have an ID, the constructor makes one even if there is no json file.
					// To allow migration, pretend it has no ID if there is not yet a meta.json.
					bookDOM.RemoveMetaElement("bloomBookId", () => (RobustFile.Exists(BookInfo.MetaDataPath) ? BookInfo.Id : null),
						val => BookInfo.Id = val);

					// Title should be replicated in json
					//if (!string.IsNullOrWhiteSpace(Title)) // check just in case we somehow have more useful info in json.
					//    bookDOM.Title = Title;
					// Bit of a kludge, but there's no way to tell whether a boolean is already set in the JSON, so we fake that it is not,
					// thus ensuring that if something is in the metadata we use it.
					// If there is nothing there the default of true will survive.
					bookDOM.RemoveMetaElement("SuitableForMakingVernacularBooks", () => null,
						val => BookInfo.IsSuitableForVernacularLibrary = val == "yes" || val == "definitely");

					UpdateTextsNewlyChangedToRequiresParagraph(bookDOM);

					//we've removed and possible added pages, so our page cache is invalid
					_pagesCache = null;
					_doingBookUpdate = false;
				}
			}
		}

		private void BringXmatterHtmlUpToDate(HtmlDom bookDOM)
		{
			//by default, this comes from the collection, but the book can select one, including "null" to select the factory-supplied empty xmatter
			var nameOfXMatterPack = OurHtmlDom.GetMetaValue("xMatter", _collectionSettings.XMatterPackName);
			nameOfXMatterPack = Storage.HandleRetiredXMatterPacks(OurHtmlDom, nameOfXMatterPack);
			var helper = new XMatterHelper(bookDOM, nameOfXMatterPack, _storage.GetFileLocator());
			//note, we determine this before removing xmatter to fix the situation where there is *only* xmatter, no content, so if
			//we wait until we've removed the xmatter, we no how no way of knowing what size/orientation they had before the update.
			Layout layout = Layout.FromDom(bookDOM, Layout.A5Portrait);
			XMatterHelper.RemoveExistingXMatter(bookDOM);
			layout = Layout.FromDom(bookDOM, layout);
				//this says, if you can't figure out the page size, use the one we got before we removed the xmatter
			helper.InjectXMatter(_bookData.GetWritingSystemCodes(), layout);

			var dataBookLangs = bookDOM.GatherDataBookLanguages();
			TranslationGroupManager.PrepareDataBookTranslationGroups(RawDom, dataBookLangs);
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
			var oldTags = RobustFile.ReadAllText(oldTagsPath);
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
		public string ID { get { return _storage.MetaData.Id; } }

		private void UpdateImageMetadataAttributes(XmlElement imgNode)
		{
			ImageUpdater.UpdateImgMetdataAttributesToMatchImage(FolderPath, imgNode, new NullProgress());
		}

		private void UpdatePageFromFactoryTemplates(HtmlDom bookDom, IProgress progress)
		{
			var originalLayout = Layout.FromDom(bookDom, Layout.A5Portrait);

			var templatePath = BloomFileLocator.GetFactoryBookTemplateDirectory( "Basic Book");

			var templateDom = XmlHtmlConverter.GetXmlDomFromHtmlFile(templatePath.CombineForPath("Basic Book.html"), false);

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

		public void UpdatePageToTemplate(HtmlDom pageDom, XmlElement templatePageDiv, string pageId)
		{
			OurHtmlDom.UpdatePageToTemplate(pageDom, templatePageDiv, pageId);
			UpdateEditableAreasOfElement(pageDom);
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
				if (TemporarilyUnlocked)
					return false;
				return RecordedAsLockedDown;
			}
		}

		/// <summary>
		/// used during editing where the user consciously unlocks a shellbook in order to make changes
		/// </summary>
		public bool TemporarilyUnlocked { get; set; }

		/// <summary>
		/// This is how the book's LockedDown state will be reported in a vernacular collection.
		/// </summary>
		public bool RecordedAsLockedDown
		{
			get { return HtmlHasLockedDownFlag(OurHtmlDom); }
			set
			{
				RecordAsLockedDown(OurHtmlDom, value);
			}
		}

		public static bool HtmlHasLockedDownFlag(HtmlDom dom)
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
			get { return _log.ErrorEncountered || !string.IsNullOrEmpty(_storage.ErrorMessagesHtml); }
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

		public BookInfo BookInfo { get; protected set; }

		public UserPrefs UserPrefs { get; private set; }


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
				var contents = m.Transform(RobustFile.ReadAllText(AboutBookMarkdownPath));
				contents = contents.Replace("remove", "");//used to hide email addresses in the md from scanners (probably unneccessary.... do they scan .md files?

				var pathToCss = _storage.GetFileLocator().LocateFileWithThrow("BookReadme.css");
				var html = string.Format("<html><head><link rel='stylesheet' href='file://{0}' type='text/css'><head/><body>{1}</body></html>", pathToCss, contents);
				return html;

			} //todo add other ui languages
		}

		public bool HasAboutBookInformationToShow { get { return _storage!=null && RobustFile.Exists(AboutBookMarkdownPath); } }
		public string AboutBookMarkdownPath  {
			get
			{
				return BloomFileLocator.GetBestLocalizedFile(_storage.FolderPath.CombineForPath("ReadMe-en.md"));
			}
		}

		public void InitCoverColor()
		{
			AddCoverColor(this.OurHtmlDom, CoverColors[_coverColorIndex]);
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
		internal void AddPreviewJavascript(HtmlDom dom)
		{
			dom.AddJavascriptFile("commonBundle.js".ToLocalhost());
			dom.AddJavascriptFile("bookPreviewBundle.js".ToLocalhost());
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
				string captionI18nId;
				var caption = GetPageLabelFromDiv(pageNode, out captionI18nId);
				if (String.IsNullOrEmpty(caption))
				{
					caption = "";
						//we aren't keeping these up to date yet as thing move around, so.... (pageNumber + 1).ToString();
				}
				_pagesCache.Add(CreatePageDecriptor(pageNode, caption, captionI18nId));
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

		public Dictionary<string, IPage> GetTemplatePagesIdDictionary()
		{
			if (_log.ErrorEncountered)
				return null;

			var result = new Dictionary<string, IPage>();

			foreach (XmlElement pageNode in OurHtmlDom.SafeSelectNodes("//div[contains(@class,'bloom-page') and not(contains(@data-page, 'singleton'))]"))
			{
				string captionI18nId;
				var caption = GetPageLabelFromDiv(pageNode, out captionI18nId);
				result.Add(GetPageIdFromDiv(pageNode), CreatePageDecriptor(pageNode, caption, captionI18nId));
			}
			return result;
		}

		private static string GetPageIdFromDiv(XmlElement pageNode)
		{
			return pageNode.GetAttribute("id");
		}

		private static string GetPageLabelFromDiv(XmlElement pageNode, out string captionI18nId)
		{
			var englishDiv = pageNode.SelectSingleNode("div[contains(@class,'pageLabel') and @lang='en']");
			var caption = (englishDiv == null) ? String.Empty : englishDiv.InnerText;
			captionI18nId = null;
			if (englishDiv != null && englishDiv.Attributes["data-i18n"] != null)
			captionI18nId = englishDiv.Attributes["data-i18n"].Value;
			return caption;
		}

		private IPage CreatePageDecriptor(XmlElement pageNode, string caption, string captionI18nId)//, Action<Image> thumbNailReadyCallback)
		{
			return new Page(this, pageNode, caption, captionI18nId,
				(page => FindPageDiv(page)));
		}

		private XmlElement FindPageDiv(IPage page)
		{
			//review: could move to page
			var pageElement = OurHtmlDom.RawDom.SelectSingleNodeHonoringDefaultNS(page.XPathToDiv);
			Require.That(pageElement != null,"Page could not be found: "+page.XPathToDiv);
			if (pageElement != null)
				pageElement.InnerXml = XmlHtmlConverter.RemoveEmptySelfClosingTags(pageElement.InnerXml);
				
			return pageElement as XmlElement;
		}

		public void InsertPageAfter(IPage pageBefore, IPage templatePage)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			ClearPagesCache();

			if(templatePage.Book !=null) // will be null in some unit tests that are unconcerned with stylesheets
				HtmlDom.AddStylesheetFromAnotherBook(templatePage.Book.OurHtmlDom, OurHtmlDom);

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
			
			//_pageSelection.SelectPage(CreatePageDecriptor(newPageDiv, "should not show", _collectionSettings.Language1Iso639Code));

			// If copied page references images, copy them.
			foreach (var pathFromBook in BookStorage.GetImagePathsRelativeToBook(newPageDiv))
			{
				var path = Path.Combine(FolderPath, pathFromBook);
				if (!RobustFile.Exists(path))
				{
					var fileName = Path.GetFileName(path);
					var sourcePath = Path.Combine(templatePage.Book.FolderPath, fileName);
					if (RobustFile.Exists(sourcePath))
						RobustFile.Copy(sourcePath, path);
				}
			}

			//similarly, if the page has stylesheet files we don't have, copy them
			foreach(string sheetName in templatePage.Book.OurHtmlDom.GetTemplateStyleSheets())
			{
				var destinationPath = Path.Combine(FolderPath, sheetName);
				if (!File.Exists(destinationPath))
				{
					var sourcePath = Path.Combine(templatePage.Book.FolderPath, sheetName);
					if (File.Exists(sourcePath))
						File.Copy(sourcePath, destinationPath);
				}
			}

			Save();
			if (_pageListChangedEvent != null)
				_pageListChangedEvent.Raise(null);

			_pageSelection.SelectPage(newPage);

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

			// If we copy audio markup, the new page will be linked to the SAME audio files,
			// and the pages might well continue to share markup even when text on one of them
			// is changed. If we WANT to copy the audio links, we need to do something like
			// assigning a new guid each time a new recording is made, or at least if we
			// find another sentence in the book sharing the same recording and with different
			// text.
			RemoveAudioMarkup(newpageDiv);

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

		private static void RemoveAudioMarkup(XmlElement newpageDiv)
		{
			foreach (var span in newpageDiv.SafeSelectNodes(".//span[contains(@class,'audio-sentence')]").Cast<XmlElement>().ToList())
			{
				XmlNode after = span;
				foreach (XmlNode child in span.ChildNodes)
				{
					span.ParentNode.InsertAfter(child, after);
					after = child;
				}
				span.ParentNode.RemoveChild(span);
			}
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
				// This is needed if the user did some ChangeLayout (origami) manipulation. This will populate new
				// translationGroups with .bloom-editables and set the proper classes on those editables to match the current multilingual settings. 
				UpdateEditableAreasOfElement(editedPageDom);

				//replace the corresponding page contents in our DOM with what is in this PageDom
				XmlElement pageFromEditedDom = editedPageDom.SelectSingleNodeHonoringDefaultNS("//div[contains(@class, 'bloom-page')]");
				string pageId = pageFromEditedDom.GetAttribute("id");
				var pageFromStorage = GetPageFromStorage(pageId);

				HtmlDom.ProcessPageAfterEditing(pageFromStorage, pageFromEditedDom);

				_bookData.SuckInDataFromEditedDom(editedPageDom); //this will do an updatetitle
				// When the user edits the styles on a page, the new or modified rules show up in a <style/> element with title "userModifiedStyles". Here we copy that over to the book DOM.
				var userModifiedStyles = editedPageDom.SelectSingleNode("html/head/style[@title='userModifiedStyles']");
				if (userModifiedStyles != null)
				{
					GetOrCreateUserModifiedStyleElementFromStorage().InnerXml = userModifiedStyles.InnerXml;
					//Debug.WriteLine("Incoming User Modified Styles:   " + userModifiedStyles.OuterXml);
				}
				Save();

				_storage.UpdateBookFileAndFolderName(_collectionSettings);
				//review used to have   UpdateBookFolderAndFileNames(data);

				//Enhance: if this is only used to re-show the thumbnail, why not limit it to if this is the cover page?
				//e.g., look for the class "cover"
				InvokeContentsChanged(null); //enhance: above we could detect if anything actually changed
			}
			catch (Exception error)
			{
				var msg = LocalizationManager.GetString("Errors.CouldNotSavePage",
					"Bloom had trouble saving a page. Please click Details below and report this to us. Then quit Bloom, run it again, and check to see if the page you just edited is missing anything. Sorry!");
				ErrorReport.NotifyUserOfProblem(error, msg);
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
			BuildPageCache();
			Save();
			InvokeContentsChanged(null);
			return true;
		}

		internal XmlNodeList GetPageElements()
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

		internal int GetIndexLastFrontkMatterPage()
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

			var pathSafeForWkHtml2Pdf = FileUtils.MakePathSafeFromEncodingProblems(FolderPath);
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
			AddPreviewJavascript(printingDom);
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

				HtmlDom.AddStylesheetFromAnotherBook(childBook.OurHtmlDom, printingDom);
				printingDom.SortStyleSheetLinks();

				foreach (XmlElement pageDiv in childBook.OurHtmlDom.RawDom.SafeSelectNodes("/html/body//div[contains(@class, 'bloom-page') and not(contains(@class,'bloom-frontMatter')) and not(contains(@class,'bloom-backMatter'))]"))
				{
					XmlElement importedPage = (XmlElement) printingDom.RawDom.ImportNode(pageDiv, true);
					currentLastContentPage.ParentNode.InsertAfter(importedPage, currentLastContentPage);
					currentLastContentPage = importedPage;

					foreach(XmlElement img in HtmlDom.SelectChildImgAndBackgroundImageElements(importedPage))
					{
						var bookFolderName = Path.GetFileName(bookInfo.FolderPath);
						var path = HtmlDom.GetImageElementUrl(img);
						var pathRelativeToFolioFolder = ".../" + bookFolderName + "/" + path.NotEncoded;
						//NB: URLEncode would replace spaces with '+', which is ok in the parameter section, but not the URL
						//So we are using UrlPathEncode

						HtmlDom.SetImageElementUrl(new ElementProxy(img), UrlPathString.CreateFromUnencodedString(pathRelativeToFolioFolder));

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
			var template = RobustFile.ReadAllText(_storage.GetFileLocator().LocateFileWithThrow("languageDisplayTemplate.css"));
			var path = _storage.FolderPath.CombineForPath("languageDisplay.css");

			// Use a temporary file pathname in the current book's folder.  This is needed to ensure proper permissions are granted
			// to the resulting file later after FileUtils.ReplaceFileWithUserInteractionIfNeeded is called.  That method may call
			// File.Replace which replaces both the file content and the file metadata (permissions).  The result of that if we use
			// the user's temp directory is described in http://issues.bloomlibrary.org/youtrack/issue/BL-3954.
			using (var temp = TempFile.InFolderOf(path))	// We don't need a .css extension for the temporary file.
			{
				RobustFile.WriteAllText(temp.Path,
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
		/// This is called both for the whole book, and for individual pages when the user uses Origami to make changes to the layout of the page.
		/// It would be nicer in the HtmlDom, but it uses knowledge about the collection and book languages that the DOM doesn't have.
		/// </summary>
		/// <param name="elementToUpdate"></param>
		public void UpdateEditableAreasOfElement(HtmlDom dom)
		{
			var language1Iso639Code = _collectionSettings.Language1Iso639Code;
			var language2Iso639Code = _collectionSettings.Language2Iso639Code;
			var language3Iso639Code = _collectionSettings.Language3Iso639Code;
			var multilingualContentLanguage2 = _bookData.MultilingualContentLanguage2;
			var multilingualContentLanguage3 = _bookData.MultilingualContentLanguage3;
			foreach (XmlElement div in dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]"))
			{
				TranslationGroupManager.PrepareElementsInPageOrDocument(div, _collectionSettings);
				TranslationGroupManager.UpdateContentLanguageClasses(div, _collectionSettings, language1Iso639Code, multilingualContentLanguage2, multilingualContentLanguage3);
			}
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

		public virtual Layout GetLayout()
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
			//BookCopyrightAndLicense.LogMetdata(OurHtmlDom);
			var result = BookCopyrightAndLicense.GetMetadata(OurHtmlDom, _collectionSettings.BrandingProjectName);
			
			//Logger.WriteEvent("After");
			//BookCopyrightAndLicense.LogMetdata(OurHtmlDom);
			return result;
		}

		public void SetMetadata(Metadata metadata)
		{
			BookCopyrightAndLicense.SetMetadata(metadata, OurHtmlDom, FolderPath, CollectionSettings);
			BookInfo.SetLicenseAndCopyrightMetadata(metadata);
		}

		public void SetTitle(string name)
		{
			OurHtmlDom.Title = name;
		}

		public void ExportXHtml(string path)
		{
			XmlHtmlConverter.GetXmlDomFromHtmlFile(_storage.PathToExistingHtml,true).Save(path);
		}

		public void Save()
		{
			Guard.Against(Type != BookType.Publication, "Tried to save a non-editable book.");
			_bookData.UpdateVariablesAndDataDivThroughDOM(BookInfo);//will update the title if needed
			if(!LockDownTheFileAndFolderName)
			{
				_storage.UpdateBookFileAndFolderName(_collectionSettings); //which will update the file name if needed
			}
			_storage.Save();
		}

		//used by the command-line "hydrate" command
		public bool LockDownTheFileAndFolderName { get; set; }

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

		internal IBookStorage Storage {get { return _storage; }}

		/// <summary>
		/// This gets called as a result of a UI action. It sets the new topic in our data, 
		/// but doesn't do anything related to how it is displayed on the page.
		/// The way to think about this is that we're aiming for a more react™-style flow.
		/// </summary>
		public void SetTopic(string englishTopicAsKey)
		{
			_bookData.Set("topic",englishTopicAsKey,"en");
		}
	}
}
