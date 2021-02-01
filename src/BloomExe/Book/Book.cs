using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using System.Xml;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.Publish;
using Bloom.ToPalaso;
using Bloom.Utils;
using Bloom.web.controllers;
using Bloom.WebLibraryIntegration;
using L10NSharp;
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
		public static Color[] CoverColors = { Color.FromArgb(228, 140, 132), Color.FromArgb(176, 222, 228), Color.FromArgb(152, 208, 185), Color.FromArgb(194, 166, 191) };


		//We only randomize the initial value for each run. Without it, we were making a lot
		// more red books than any other color, because the
		//first book for a given run would always be red, and people are unlikely to make more
		//than one book per session.
		private static int s_coverColorIndex=new Random().Next(CoverColors.Length-1);

		private readonly ITemplateFinder _templateFinder;
		private readonly PageSelection _pageSelection;
		private readonly PageListChangedEvent _pageListChangedEvent;
		private readonly BookRefreshEvent _bookRefreshEvent;
		private readonly BookSavedEvent _bookSavedEvent;
		private List<IPage> _pagesCache;
		internal const string kIdOfBasicBook = "056B6F11-4A6C-4942-B2BC-8861E62B03B3";

		public event EventHandler ContentsChanged;
		private readonly BookData _bookData;
		public const string ReadMeImagesFolderName = "ReadMeImages";

		//for moq'ing only; parameterless ctor required by Moq
		public Book()
		{
			Guard.Against(!Program.RunningUnitTests, "Only use this ctor for tests!");
		}

		public Book(BookInfo info = null, IBookStorage storage = null):
			this()
		{
			BookInfo = info;
			Storage = storage;
		}

		public Book(BookInfo info, IBookStorage storage, ITemplateFinder templateFinder,
		   CollectionSettings collectionSettings,
			PageSelection pageSelection,
			PageListChangedEvent pageListChangedEvent,
			BookRefreshEvent bookRefreshEvent,
			BookSavedEvent bookSavedEvent=null)
		{
			BookInfo = info;
			if (bookSavedEvent == null) // unit testing
			{
				bookSavedEvent = new BookSavedEvent();
			}
			UserPrefs = UserPrefs.LoadOrMakeNew(Path.Combine(info.FolderPath, "book.userPrefs"));

			Guard.AgainstNull(storage,"storage");

			// This allows the _storage to
			storage.BookInfo = info;

			// We always validate the book during the process of loading the storage,
			// so we don't need to do it again until something changes...just note the result.
			if (!string.IsNullOrEmpty(storage.ErrorMessagesHtml))
			{
				HasFatalError = true;
				FatalErrorDescription = storage.ErrorMessagesHtml;
			}
			else if (!string.IsNullOrEmpty(storage.InitialLoadErrors))
			{
				HasFatalError = true;
				FatalErrorDescription = storage.InitialLoadErrors;
			}

			Storage = storage;

			//this is a hack to keep these two in sync (in one direction)
			Storage.FolderPathChanged += _storage_FolderPathChanged;

			_templateFinder = templateFinder;

			CollectionSettings = collectionSettings;

			_pageSelection = pageSelection;
			_pageListChangedEvent = pageListChangedEvent;
			_bookRefreshEvent = bookRefreshEvent;
			_bookSavedEvent = bookSavedEvent;

			_bookData = new BookData(OurHtmlDom,
					CollectionSettings, UpdateImageMetadataAttributes);

			InjectStringListingActiveLanguagesOfBook();

			if (!HasFatalError && IsEditable)
			{
				_bookData.SynchronizeDataItemsThroughoutDOM();
			}

			// If it doesn't already have a userModifiedStyles element, give it one.
			// BL-4266 Somehow it is important for there to be a userModifiedStyles element BEFORE (in order!)
			// the coverColor style element in the Head of the DOM in order for the new Css Rules
			// to get inserted properly. So we make sure there is one.
			GetOrCreateUserModifiedStyleElementFromStorage();

			//if we're showing the user a shell/template book, pick a color for it
			//If it is editable, then we don't want to change to the next color, we
			//want to use the color that we used for the sample shell/template we
			//showed them previously.
			if (!info.IsEditable)
			{
				SelectNextCoverColor(); // we only increment when showing a template or shell
				InitCoverColor();
			}

			// If it doesn't already have a cover color, give it one.
			if (HtmlDom.GetCoverColorStyleElement(OurHtmlDom.Head) == null)
			{
				InitCoverColor(); // should use the same color as what they saw in the preview of the template/shell
			}
			FixBookIdAndLineageIfNeeded();
			Storage.Dom.RemoveExtraBookTitles();
			Storage.Dom.RemoveExtraContentTypesMetas();
			Guard.Against(OurHtmlDom.RawDom.InnerXml=="","Bloom could not parse the xhtml of this document");

			// We introduced "template starter" in 3.9, but books you made with it could be used in 3.8 etc.
			// If those books came back to 3.9 or greater (which would happen eventually),
			// they would still have this tag that they didn't really understand, and which should have been removed.
			// At the moment, only templates are suitable for making shells, so use that to detect that someone has
			// edited a user defined template book in a version that doesn't know about user defined templates.
			if (Storage.Dom.GetGeneratorVersion() < new System.Version(3,9))
			{
				if (IsSuitableForMakingShells)
					Storage.Dom.FixAnyAddedCustomPages();
				else
					Storage.Dom.RemoveMetaElement("xmatter");
			}
		}

		void _storage_FolderPathChanged(object sender, EventArgs e)
		{
			BookInfo.FolderPath = Storage.FolderPath;
			UserPrefs.UpdateFileLocation(Storage.FolderPath);
		}

		/// <summary>
		/// This just increments the color index so that the next book to be constructed that doesn't already have a color will use it
		/// </summary>
		public static void SelectNextCoverColor()
		{
			s_coverColorIndex = s_coverColorIndex+1;
			if( s_coverColorIndex >= CoverColors.Length)
				s_coverColorIndex = 0;
		}

		public CollectionSettings CollectionSettings { get; }

		public void InvokeContentsChanged(EventArgs e)
		{
			ContentsChanged?.Invoke(this, e);
		}

		/// <summary>
		/// If we have to just show title in one language, which should it be?
		/// Note, this isn't going to be the best for choosing a filename, which we are more likely to want in a national language
		/// </summary>
		public virtual string TitleBestForUserDisplay
		{
			get
			{
				return GetBestTitleForDisplay(_bookData.GetMultiTextVariableOrEmpty("bookTitle"), _bookData.GetBasicBookLanguageCodes().ToList(), IsEditable);
			}
		}

		public static string GetBestTitleForDisplay(MultiTextBase title, List<string> langCodes, bool isEditable)
		{
			var display = title.GetExactAlternative(langCodes[0]);
			if (string.IsNullOrEmpty(display))
			{
				//the SIL-LEAD project, SHRP (around 2012-2016) had books that just had an English name, before we changed Bloom
				//to not show English names. But the order was also critical. So we want those old books to go ahead and use their
				//English names.
				var englishTitle = title.GetExactAlternative("en").ToLowerInvariant();
				var SHRPMatches = new[] { "p1", "p2", "p3", "p4", "SHRP" };
				var couldBeOldStyleUgandaSHRPBook = SHRPMatches.Any(m => englishTitle.Contains(m.ToLowerInvariant()));

				//if this book is one of the ones we're editing in our collection, it really
				//needs a title in our main language, it would be confusing to show a title from some other langauge
				if (!couldBeOldStyleUgandaSHRPBook && (isEditable || title.Empty))
				{
					display = LocalizationManager.GetString("CollectionTab.TitleMissing", "Title Missing",
						"Shown as the thumbnail caption when the book doesn't have a title.");
				}
				//but if this book is just in our list of sources, well then let's look through the names
				//and try to get one that is likely to be helpful
				else
				{
					var orderedPreferences = new List<string>();
					orderedPreferences.Add(LocalizationManager.UILanguageId);

					// we already know that langCodes[0] does not have a title
					for (int i = 1; i < langCodes.Count; ++i)
						orderedPreferences.Add(langCodes[i]);
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
				Debug.Assert(BookInfo.FolderPath == Storage.FolderPath);

				if (IsEditable)
				{
					//REVIEW: evaluate and explain when we would choose the value in the html over the name of the folder.
					//1 advantage of the folder is that if you have multiple copies, the folder tells you which one you are looking at
					var s = OurHtmlDom.Title;
					if(string.IsNullOrEmpty(s))
						return Path.GetFileName(Storage.FolderPath);
					return s;
				}
				else //for templates and such, we can already just use the folder name
				{
					return Path.GetFileName(Storage.FolderPath);
				}
			}
		}

		public string PrettyPrintLanguage(string code)
		{
			return _bookData.PrettyPrintLanguage(code);
		}

		public virtual HtmlDom GetEditableHtmlDomForPage(IPage page)
		{
			if (HasFatalError)
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
			pageDom.AddStyleSheet("");
			pageDom.SortStyleSheetLinks();
			AddJavaScriptForEditing(pageDom);
			RuntimeInformationInjector.AddUIDictionaryToDom(pageDom, _bookData);
			RuntimeInformationInjector.AddUISettingsToDom(pageDom, _bookData, Storage.GetFileLocator());
			UpdateMultilingualSettings(pageDom);

			if (IsSuitableForMakingShells && !page.IsXMatter)
			{
				// We're editing a template page in a template book.
				// Make the label editable. Note: HtmlDom.ProcessPageAfterEditing knows about removing this.
				// I don't like the knowledge being in two places, but the place to remove the attribute is in the
				// middle of a method in HtmlDom and it's this class that knows about the book being a template
				// and whether it should be added.
				// (Note: we don't want this for xmatter pages because they don't function as actual template pages.)
				HtmlDom.MakeEditableDomShowAsTemplate(pageDom);
			}
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
			TranslationGroupManager.UpdateContentLanguageClasses(dom.RawDom, _bookData, _bookData.Language1.Iso639Code,
				_bookData.MultilingualContentLanguage2, _bookData.MultilingualContentLanguage3);
			BookInfo.IsRtl = _bookData.Language1.IsRightToLeft;

			BookStarter.SetLanguageForElementsWithMetaLanguage(dom.RawDom, _bookData);
		}

		private HtmlDom GetHtmlDomWithJustOnePage(IPage page)
		{
			var divNodeForThisPage = page.GetDivNodeForThisPage();
			if (divNodeForThisPage == null)
			{
				throw new ApplicationException($"The requested page {page.Id} from book {page.Book.FolderPath} isn't in this book {FolderPath}.");
			}

			return GetHtmlDomWithJustOnePage(divNodeForThisPage);
		}

		public HtmlDom GetHtmlDomWithJustOnePage(XmlElement divNodeForThisPage)
		{
			var headXml = Storage.Dom.SelectSingleNodeHonoringDefaultNS("/html/head").OuterXml;
			var originalBody = Storage.Dom.SelectSingleNodeHonoringDefaultNS("/html/body");

			var enterpriseStatusClass = this.CollectionSettings.HaveEnterpriseFeatures ? "enterprise-on" : "enterprise-off";
			var dom = new HtmlDom(@"<html>" + headXml + $"<body class='{enterpriseStatusClass}'></body></html>");
			dom = Storage.MakeDomRelocatable(dom);
			// Don't let spaces between <strong>, <em>, or <u> elements be removed. (BL-2484)
			dom.RawDom.PreserveWhitespace = true;
			var newBody = dom.RawDom.SelectSingleNodeHonoringDefaultNS("/html/body") as XmlElement;

			// copy over any attributes on body (we store a form of the book feature flags there so that css can get at them)
			 foreach (XmlAttribute attr in originalBody.Attributes)
			 {
				 newBody.SetAttribute(attr.Name, attr.Value);
			 }

			var pageDom = dom.RawDom.ImportNode(divNodeForThisPage, true);
			newBody.AppendChild(pageDom);

//                BookStorage.HideAllTextAreasThatShouldNotShow(dom, iso639CodeToLeaveVisible, Page.GetPageSelectorXPath(dom));

			return dom;
		}

		public HtmlDom GetHtmlDomReadyToAddPages(HtmlDom inputDom)
		{
			var headNode = Storage.Dom.SelectSingleNodeHonoringDefaultNS("/html/head");
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
			return Storage.MakeDomRelocatable(inputDom);
		}

		public HtmlDom GetPreviewXmlDocumentForPage(IPage page)
		{
			if(HasFatalError)
			{
				return GetErrorDom();
			}
			var pageDom = GetHtmlDomWithJustOnePage(page);
			pageDom.RemoveModeStyleSheets();
			// note: order is significant here, but I added branding.css at the end (the most powerful position) arbitrarily, until
			// such time as it's clear if it matters.
			foreach (var cssFileName in new[] { @"basePage.css","previewMode.css", "origami.css", "langVisibility.css"})
			{
				pageDom.AddStyleSheet(cssFileName);
			}
			// Only add brandingCSS is there is one for the current branding
			// Note: it would be a fine enhancement here to first check for "branding-{flavor}.css",
			// but we'll leave that until we need it.
			var brandingCssPath = BloomFileLocator.GetBrowserFile(true, "branding", CollectionSettings.GetBrandingFolderName(), "branding.css");
			if (!string.IsNullOrEmpty(brandingCssPath))
			{
				pageDom.AddStyleSheet("branding.css");
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
			if (HasFatalError)
			{
				return GetErrorDom();
			}
			var pageDom = GetHtmlDomWithJustOnePage(page);
			pageDom.SortStyleSheetLinks();
			AddPreviewJavascript(pageDom);
			HtmlDom.AddClassIfMissing(pageDom.Body, "bloom-templateThumbnail");
			return pageDom;
		}

		public HtmlDom GetPreviewXmlDocumentForFirstPage()
		{
			if (HasFatalError)
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
			return Storage.GetFileLocator();
		}

		private HtmlDom GetBookDomWithStyleSheets(params string[] cssFileNames)
		{
			var dom = Storage.GetRelocatableCopyOfDom();
			dom.RemoveModeStyleSheets();
			foreach (var cssFileName in cssFileNames)
			{
				dom.AddStyleSheet(cssFileName);
			}
			dom.SortStyleSheetLinks();

			return dom;
		}

		public virtual string StoragePageFolder => Storage.FolderPath;

		private HtmlDom GetErrorDom(string extraMessages="")
		{
			var builder = new StringBuilder();
			builder.Append("<html><head><meta charset=\"UTF-8\" /></head><body style='font-family:arial,sans'>");

			builder.AppendLine(
				Storage != null ?
					Storage.GetBrokenBookRecommendationHtml() :
					BookStorage.GenericBookProblemNotice);

			// Often GetBrokenBookRecommendation and FatalErrorDescription both come from _storage.ErrorMessagesHtml.
			// Try not to say the same thing twice.
			if (FatalErrorDescription != null && !builder.ToString().Contains(FatalErrorDescription))
				builder.Append(FatalErrorDescription);

			builder.Append("<p>"+ WebUtility.HtmlEncode(extraMessages)+"</p>");

			if (Storage.ErrorAllowsReporting)
			{
				var message = LocalizationManager.GetString("Errors.ReportThisProblemButton", "Report this problem to Bloom Support");
				builder.AppendFormat(
					"<input type='button' value='" + message + "' href='ReportProblem'></input>");
			}

			builder.Append("</body></html>");

			return new HtmlDom(builder.ToString());
		}

		private bool IsDownloaded => FolderPath.StartsWith(BookTransfer.DownloadFolder);

		// BL-2678: we want the user to be able to delete troublesome/no longer needed books
		// downloaded from BloomLibrary.org
		public virtual bool CanDelete => IsEditable || IsDownloaded;

		public bool CanPublish
		{
			get
			{
				if (!BookInfo.IsEditable)
					return false;
				return !HasFatalError;
			}
		}

		/// <summary>
		/// In the Bloom app, only one collection at a time is editable; that's the library they opened. All the other collections of templates, shells, etc., are not editable.
		/// So, a book is editable if it's in that one collection (unless it's in an error state).
		/// </summary>
		public bool IsEditable {
			get
			{
				if (!BookInfo.IsEditable)
					return false;
				return !HasFatalError;
			}
		}


		/// <summary>
		/// First page in the book (or null if there are none)
		/// </summary>
		public IPage FirstPage => GetPages().FirstOrDefault();

		public IPage GetPageByIndex(int pageIndex)
		{
			// index must be >= 0
			if (pageIndex < 0) return null;

			// index must be less than the number of pages
			var pages = GetPages().ToList();
			if (pages.Count <= pageIndex) return null;

			return pages[pageIndex];
		}

		string _cachedTemplateKey;
		Book _cachedTemplateBook;

		public Book FindTemplateBook()
		{
			Guard.AgainstNull(_templateFinder, "_templateFinder");
			if(!IsEditable)
				return null; // won't be adding pages, don't need source of templates
			string templateKey = PageTemplateSource;

			Book book=null;
			if (!String.IsNullOrEmpty(templateKey))
			{
				if (templateKey.ToLowerInvariant() == "basicbook") //catch this pre-beta spelling with no space
					templateKey = "Basic Book";
				// Template was renamed for 3.8 (and needs to end in Template, see PageTemplatesApi.GetBookTemplatePaths)
				if (templateKey.ToLowerInvariant() == "arithmetic")
					templateKey = "Arithmetic Template";
				// We can assume that a book's "TemplateBook" does not change over time.  To be even safer,
				// we'll add a check for the same "TemplateKey" to allow reusing a cached "TemplateBook".
				// See https://silbloom.myjetbrains.com/youtrack/issue/BL-3782.
				if (templateKey == _cachedTemplateKey && _cachedTemplateBook != null)
					return _cachedTemplateBook;
				// a template book is its own primary template...and might not be found by templateFinder,
				// since we might be in a vernacular collection that it won't look in.
				book = IsSuitableForMakingShells ? this : _templateFinder.FindAndCreateTemplateBookByFileName(templateKey);
				_cachedTemplateBook = book;
				_cachedTemplateKey = templateKey;
			}
			return book;
		}

		//This is the set of pages that we show first in the Add Page dialog.
		public string PageTemplateSource
		{
			get { return OurHtmlDom.GetMetaValue("pageTemplateSource", ""); }
			set { OurHtmlDom.UpdateMetaElement("pageTemplateSource", value);}
		}

		/// <summary>
		/// once in our lifetime, we want to do any migrations needed for this version of bloom
		/// </summary>
		private bool _haveDoneUpdate = false;

		public virtual HtmlDom OurHtmlDom => Storage.Dom;

		public virtual XmlDocument RawDom => OurHtmlDom.RawDom;

		// Tests can run without ever setting Storage.  This check is currently enough for them to work.
		public virtual string FolderPath => Storage?.FolderPath;

		public virtual HtmlDom GetPreviewHtmlFileForWholeBook()
		{
			//we may already know we have an error (we might not discover until later)
			if (HasFatalError)
			{
				return GetErrorDom();
			}
			if (!Storage.GetLooksOk())
			{
				return GetErrorDom(Storage.GetValidateErrors());
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
			var primaryLanguage = _bookData.Language1.Iso639Code;
			if (IsShellOrTemplate)
			{
				//TODO: this won't be enough, if our national language isn't, say, English, and the shell just doesn't have our national language. But it might have some other language we understand.

				// If it DOES have text in the Language1Iso639Code (e.g., a French collection, and we're looking at Moon and Cap...BL-6465),
				// don't mess with it.
				if (previewDom.SelectSingleNode($"//*[@lang='{primaryLanguage}' and contains(@class, 'bloom-editable') and text()!='']") == null)
					primaryLanguage = _bookData.Language2.Iso639Code;
			}

			TranslationGroupManager.UpdateContentLanguageClasses(previewDom.RawDom, _bookData, primaryLanguage,
				_bookData.MultilingualContentLanguage2, _bookData.MultilingualContentLanguage3);

			AddPreviewJavascript(previewDom);
			previewDom.AddPublishClassToBody("preview");
			return previewDom;
		}

		private void AddCreationTypeAttribute(HtmlDom htmlDom)
		{
			htmlDom.AddCreationType(LockedDown ? "translation" : "original");
		}

		public void BringBookUpToDate(IProgress progress, bool forCopyOfUpToDateBook = false)
		{
			_pagesCache = null;
			string oldMetaData = string.Empty;
			if (RobustFile.Exists(BookInfo.MetaDataPath))
			{
				oldMetaData = RobustFile.ReadAllText(BookInfo.MetaDataPath); // Have to read this before other migration overwrites it.
			}
			BringBookUpToDate(OurHtmlDom, progress, oldMetaData);
			progress.WriteStatus("Updating pages...");
			foreach (XmlElement pageDiv in OurHtmlDom.SafeSelectNodes("//body/div[contains(@class, 'bloom-page')]"))
			{
				BringPageUpToDate(pageDiv);
			}

			if (IsEditable)
			{
				// If the user might be editing it we want it more thoroughly up-to-date
				try
				{
					ImageUpdater.UpdateAllHtmlDataAttributesForAllImgElements(FolderPath, OurHtmlDom, progress);
				}
				catch (UnauthorizedAccessException e)
				{
					BookStorage.ShowAccessDeniedErrorReport(e);
				}

				VerifyLayout(OurHtmlDom); // make sure we have something recognizable for layout
				// Restore possibly messed up multilingual settings.
				UpdateMultilingualSettings(OurHtmlDom);
				// This is only needed for updating from old Bloom versions. No need if we're copying the current
				// edit book, on which it's already been done, to make an epub or similar.
				if (!forCopyOfUpToDateBook)
					Storage.PerformNecessaryMaintenanceOnBook();
				Save();
			}

			if (SHRP_TeachersGuideExtension.ExtensionIsApplicable(this))
			{
				SHRP_TeachersGuideExtension.UpdateBook(OurHtmlDom, _bookData.Language1.Iso639Code);
			}

			OurHtmlDom.FixDivOrdering();

			Save();
			_bookRefreshEvent?.Raise(this);
		}

		private void VerifyLayout(HtmlDom dom)
		{
			SetLayout(Layout.FromDom(dom, Layout.A5Portrait)); // if layout is not recognized, default to A5Portrait
		}

		private void BringBookInfoUpToDate(string oldMetaData)
		{
			if (oldMetaData.Contains("readerToolsAvailable"))
			{
				var newMetaString = oldMetaData.Replace("readerToolsAvailable", "toolboxIsOpen");
				var newMetaData = BookMetaData.FromString(newMetaString);
				BookInfo.ToolboxIsOpen = newMetaData.ToolboxIsOpen;
			}
			BookInfo.CountryName = CollectionSettings.Country;
			BookInfo.ProvinceName = CollectionSettings.Province;
			BookInfo.DistrictName = CollectionSettings.District;

			// Propagate the IsFolio value determined by the Book class (which determines it from the HTML file)
			// into BookInfo (which represents the meta.json file).
			// The version in Book.cs should be the ultimate source of truth, but it's handy to have BookInfo have a copy
			// of it too because BookInfo's meta.json is faster to parse than the book's HTML file.
			// This helps out the AddChildBookContentsToFolio() method.
			BookInfo.IsFolio = IsFolio;
		}

		/// <summary>
		/// Fix errors that users have encountered.  For now, this is only a duplication of language div elements
		/// inside of translationGroup divs.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-6923.
		/// </remarks>
		private void FixErrorsEncounteredByUsers(HtmlDom bookDOM)
		{
			foreach (
				XmlElement groupElement in
				bookDOM.Body.SafeSelectNodes("descendant::*[contains(@class,'bloom-translationGroup')]"))
			{
				// Even though the user only had duplicate vernacular divs, let's check all the book
				// languages just to be safe.
				foreach (var lang in _bookData.GetAllBookLanguageCodes())
					TranslationGroupManager.FixDuplicateLanguageDivs(groupElement, lang);
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
					_pageMigrations["AF708725-E961-44AA-9149-ADF66084A04F"] = new GuidAndPath() { Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398385", Path = "Big Book/Big Book.html" }; // Just a Picture
					_pageMigrations["D9A55EB6-43A8-4C6A-8891-2C1CDD95772C"] = new GuidAndPath() { Guid = "a31c38d8-c1cb-4eb9-951b-d2840f6a8bdb", Path = "Big Book/Big Book.html" }; // Just Text
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
			foreach (XmlAttribute attr in page.Attributes)
			{
				if (newPage.HasAttribute(attr.Name))
					continue; // don't overwrite things specified in template, typically class, id, data-page
				// Otherwise copy everything, even things we don't know about at the time of writing this
				// code; if we add a new attribute that gets set before this code runs, we'd like to transfer
				// it to the new element without having to remember to update this code.
				newPage.SetAttribute(attr.Name, attr.Value);
			}
			bool dummy;
			OurHtmlDom.MigrateEditableData(page, newPage, lineage.Replace(originalTemplateGuid, updateTo.Guid), true, out dummy);
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
		/// <param name="oldMetaData">optional</param>
		private void BringBookUpToDate(HtmlDom bookDOM /* may be a 'preview' version*/, IProgress progress, string oldMetaData = "")
		{
			RemoveImgTagInDataDiv(bookDOM);
			RemoveCkeEditorResidue(bookDOM);
			if (Title.Contains("allowSharedUpdate"))
			{
				// Original version of this code that suffers BL_3166
				BringBookUpToDateUnprotected(bookDOM, progress);
			}
			else
			{
				// New version that we hope prevents BL_3166
				if (_doingBookUpdate)
					MessageBox.Show("Caught Bloom doing two updates at once! Possible BL-3166 is being prevented");
				lock (_updateLock)
				{
					_doingBookUpdate = true;
					BringBookUpToDateUnprotected(bookDOM, progress);
					_doingBookUpdate = false;
				}
			}
			RemoveObsoleteSoundAttributes(bookDOM);
			RemoveObsoleteImageAttributes(bookDOM);
			BringBookInfoUpToDate(oldMetaData);
			FixErrorsEncounteredByUsers(bookDOM);
			AddReaderBodyClass(bookDOM);
			AddLanguageAttributesToBody(bookDOM);
		}

		private void AddLanguageAttributesToBody(HtmlDom bookDom)
		{
			// TODO: figure out what to do when we expand beyond three languages.  (Perhaps
			// nothing? what are these attributes used for?)
			bookDom.Body.SetAttribute("data-L1", this._bookData.Language1.Iso639Code);
			bookDom.Body.SetAttribute("data-L2", this._bookData.Language2.Iso639Code);
			bookDom.Body.SetAttribute("data-L3", this._bookData.Language3.Iso639Code );
		}

		private void AddReaderBodyClass(HtmlDom bookDom){
			// Bloom prior to late 4.7beta had decodable and leveled reader templates without body classes, which
			// became necessary for an SIL LEAD "ABC+" xmatter. Here we add that if we can tell the book descended
			// from those templates, then add the required classes if they are missing.
			const string kDecodableParentGuid = "f0434a0b-791f-408e-b6e6-ee92f0f02f2d";
			const string kLeveledParentGuid = "ea43ce61-a752-429d-ad1a-ec282db33328";
			if(BookInfo.BookLineage != null && BookInfo.BookLineage.Contains(kDecodableParentGuid))
			{
				HtmlDom.AddClassIfMissing(bookDom.Body,"decodable-reader");
			}
			else if(BookInfo.BookLineage != null && BookInfo.BookLineage.Contains(kLeveledParentGuid))
			{
				HtmlDom.AddClassIfMissing(bookDom.Body,"leveled-reader");
			}
		}

		// Some books got corrupted with CKE temp data, possibly before we prevented this happening when
		// pasting HTML (e.g., from Word). A typical example (BL-6050/BL-6058) is
		//  <div data-cke-hidden-sel="1" data-cke-temp="1" style="position:fixed;top:0;left:-1000px" class="bloom-contentNational2">
		//		<br>
		//	</div>
		// We want to remove this. In the example problem book, every example has both data-cke-temp and data-cke-hidden-sel
		// (both set to 1), but data-cke-temp feels like a more generic thing to look for, increasing our chances
		// of catching more junk.
		private void RemoveCkeEditorResidue(HtmlDom bookDom)
		{
			foreach (var problemDiv in bookDom.SafeSelectNodes(".//div[@data-cke-temp]").Cast<XmlElement>().ToArray())
			{
				problemDiv.ParentNode.RemoveChild(problemDiv);
			}
		}

		private void BringBookUpToDateUnprotected(HtmlDom bookDOM, IProgress progress)
		{
			// With one exception, handled below, nothing in the update process should change the license info, so save what is current before we mess with
			// anything (may fix BL-3166).
			var licenseMetadata = GetLicenseMetadata();

			progress.WriteStatus("Updating collection settings...");
			try
			{
				UpdateCollectionRelatedStylesAndSettings(bookDOM);
			}
			catch (UnauthorizedAccessException e)
			{
				BookStorage.ShowAccessDeniedErrorReport(e);
				return;
			}

			progress.WriteStatus("Updating Front/Back Matter...");
			BringXmatterHtmlUpToDate(bookDOM);
			RepairBrokenSmallCoverCredits(bookDOM);
			RepairCoverImageDescriptions(bookDOM);

			progress.WriteStatus("Repair page label localization");
			RepairPageLabelLocalization(bookDOM);

			progress.WriteStatus("Repair possible messed up Questions pages and migrate classes");
			RepairQuestionsPages(bookDOM);
			MigrateNonstandardClassNames(bookDOM);

			progress.WriteStatus("Gathering Data...");
			TranslationGroupManager.PrepareElementsInPageOrDocument(bookDOM.RawDom, _bookData);
			progress.WriteStatus("Updating Data...");

			InjectStringListingActiveLanguagesOfBook();

			if (_bookData.BookIsDerivative())
				Storage.EnsureOriginalTitle();

			//hack
			if (bookDOM == OurHtmlDom) //we already have a data for this
			{
				// The one step that can legitimately change the metadata...though current branding packs
				// will only do so if it is originally empty. So set the saved one before it and then get a new one.
				BookCopyrightAndLicense.SetMetadata(licenseMetadata, bookDOM, FolderPath, _bookData, BookInfo.MetaData.UseOriginalCopyright);
				_bookData.MergeBrandingSettings(CollectionSettings.BrandingProjectKey);
				_bookData.SynchronizeDataItemsThroughoutDOM();
				licenseMetadata = GetLicenseMetadata();
				// I think we should only mess with tags if we are updating the book for real.
				var oldTagsPath = Path.Combine(Storage.FolderPath, "tags.txt");
				if (RobustFile.Exists(oldTagsPath))
				{
					ConvertTagsToMetaData(oldTagsPath, BookInfo);
					RobustFile.Delete(oldTagsPath);
				}
				BookInfo.BrandingProjectKey = CollectionSettings.BrandingProjectKey;
			}
			else //used for making a preview dom
			{
				var bd = new BookData(bookDOM, CollectionSettings, UpdateImageMetadataAttributes);
				bd.SynchronizeDataItemsThroughoutDOM();
			}
			// get any license info into the json and restored in the replaced front matter.
			BookCopyrightAndLicense.SetMetadata(licenseMetadata, bookDOM, FolderPath, _bookData, BookInfo.MetaData.UseOriginalCopyright);

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

			bookDOM.UpdatePageNumberAndSideClassOfPages(CollectionSettings.CharactersForDigitsForPageNumbers, _bookData.Language1.IsRightToLeft);

			UpdateTextsNewlyChangedToRequiresParagraph(bookDOM);

			UpdateCharacterStyleMarkup(bookDOM);

			bookDOM.SetImageAltAttrsFromDescriptions(_bookData.Language1.Iso639Code);

			//we've removed and possible added pages, so our page cache is invalid
			_pagesCache = null;
		}

		const string kCustomStyles = "customCollectionStyles.css";
		public const string kOldCollectionStyles = "settingsCollectionStyles.css";
		/// <summary>
		/// Adjust several external stylesheet links and associated files.  Also adjust some book level
		/// settings (in json or html) to match current collection settings.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-7343.
		/// </remarks>
		private void UpdateCollectionRelatedStylesAndSettings(HtmlDom bookDom)
		{
			foreach (XmlElement link in bookDom.SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var fileName = link.GetStringAttribute("href");
				if (fileName == "languageDisplay.css")
					link.SetAttribute("href", "langVisibility.css");
				else if (fileName == kOldCollectionStyles || fileName == "../"+kOldCollectionStyles || fileName == "..\\"+kOldCollectionStyles)
					link.SetAttribute("href", "defaultLangStyles.css");
				else if (fileName == "../"+kCustomStyles || fileName == "..\\"+kCustomStyles)
					link.SetAttribute("href", kCustomStyles);
			}
			// Rename/remove/create files that have changed names or locations to match link href changes above.
			// Don't do this in distributed folders.  See https://issues.bloomlibrary.org/youtrack/issue/BL-7550.
			if (!FolderPath.StartsWith(BloomFileLocator.FactoryCollectionsDirectory))
			{
				if (RobustFile.Exists(Path.Combine(FolderPath, "languageDisplay.css")))
				{
					if (RobustFile.Exists(Path.Combine(FolderPath, "langVisibility.css")))
						RobustFile.Delete(Path.Combine(FolderPath, "languageDisplay.css"));
					else
						RobustFile.Move(Path.Combine(FolderPath, "languageDisplay.css"), Path.Combine(FolderPath, "langVisibility.css"));
				}
				if (RobustFile.Exists(Path.Combine(Path.GetDirectoryName(FolderPath), kOldCollectionStyles)))
					RobustFile.Delete(Path.Combine(Path.GetDirectoryName(FolderPath), kOldCollectionStyles));
				CreateOrUpdateDefaultLangStyles();
				// Copy files from collection to book folders to match link href changes above.
				if (RobustFile.Exists(Path.Combine(Path.GetDirectoryName(FolderPath), kCustomStyles)))
					RobustFile.Copy(Path.Combine(Path.GetDirectoryName(FolderPath), kCustomStyles), Path.Combine(FolderPath, kCustomStyles), true);
			}
			// Update book settings from collection settings
			UpdateCollectionSettingsInBookMetaData();
		}

		/// <summary>
		/// Write the appropriate styles to the defaultLangStyles.css file.
		/// You would normally expect that we just write this afresh, but actually
		/// we want to preserve font information from languages that are no longer
		/// part of L1,L2,or L3 but are still in the book and thus may still be
		/// rendered by Bloom Player.
		/// </summary>
		private void CreateOrUpdateDefaultLangStyles()
		{
			var path = Path.Combine(FolderPath, "defaultLangStyles.css");
			bool doesAlreadyExist = RobustFile.Exists(path);
			if (Program.RunningHarvesterMode && doesAlreadyExist)
			{
				// Would overwrite, but overwrite not allowed in Harvester mode.
				return;
			}

			var collectionStylesCss = CollectionSettings.GetCollectionStylesCss(false);
			var cssBuilder = new StringBuilder(collectionStylesCss);
			if (doesAlreadyExist)
			{
				var cssLangs = new HashSet<string>();
				cssLangs.Add(_bookData.Language1.Iso639Code);
				cssLangs.Add(_bookData.Language2.Iso639Code);
				if (!String.IsNullOrEmpty(_bookData.Language3.Iso639Code))
					cssLangs.Add(_bookData.Language3.Iso639Code);

				var cssLines = RobustFile.ReadAllLines(path);
				const string kLangTag = "[lang='";
				var copyCurrentRule = false;
				for (var index = 0 ; index < cssLines.Length; ++index)
				{
					var line = cssLines[index].Trim();
					if (line.StartsWith(kLangTag))
					{
						var idxQuote = line.IndexOf("'", kLangTag.Length);
						if (idxQuote > 0)
						{
							var lang = line.Substring(kLangTag.Length, idxQuote - kLangTag.Length);
							copyCurrentRule = !cssLangs.Contains(lang);
						}
					}
					if (copyCurrentRule)
						cssBuilder.AppendLine(cssLines[index]);
					if (line == "}")
						copyCurrentRule = false;
				}
			}
			try
			{
				RobustFile.WriteAllText(path, cssBuilder.ToString());
			}
			catch (UnauthorizedAccessException e)
			{
				// Re-throw with additional debugging info.
				throw new BloomUnauthorizedAccessException(path, e);
			}
		}

		private void UpdateCollectionSettingsInBookMetaData()
		{
			Debug.WriteLine($"updating page number style and language display names in {FolderPath}/meta.json");
			BookInfo.MetaData.PageNumberStyle = CollectionSettings.PageNumberStyle;
			if (BookInfo.MetaData.DisplayNames == null)
				BookInfo.MetaData.DisplayNames = new Dictionary<string,string>();
			foreach (var lang in _bookData.GetAllBookLanguages())
				BookInfo.MetaData.DisplayNames[lang.Iso639Code] = lang.Name;
			// These settings will be saved to the meta.json file the next time the book itself is saved.
		}

		/// <summary>
		/// For awhile in v4.1, Question pages used a "nonprinting" class,
		/// but later we made the class fit our usual "bloom-x" class naming scheme for classes
		/// which are known to the C# code.
		/// </summary>
		/// <param name="bookDOM"></param>
		private static void MigrateNonstandardClassNames(HtmlDom bookDOM)
		{
			if (bookDOM?.Body == null)
				return;     // must be a test running...

			var nonPrintingPages = bookDOM.Body.SelectNodes("//div[contains(@class,'nonprinting')]");
			foreach (XmlElement nonPrintingPageElement in nonPrintingPages)
			{
				nonPrintingPageElement.Attributes["class"].InnerText = HtmlDom.RemoveClass("nonprinting", nonPrintingPageElement.Attributes["class"].InnerText);
				HtmlDom.AddClassIfMissing(nonPrintingPageElement, "bloom-nonprinting");
			}
		}

		/// <summary>
		/// At one point in v4.1, Question pages were able to be recorded with the Talking Book tool, but opening the
		/// tool on these pages embedded tons of span elements which messed up BR display. New question pages have classes
		/// that keep them from being recorded. Here we fix up any existing question pages from the old code.
		/// </summary>
		/// <param name="bookDOM"></param>
		private static void RepairQuestionsPages(HtmlDom bookDOM)
		{
			if (bookDOM?.Body == null)
				return;     // must be a test running...

			// classes to add
			const string classNoStyleMods = "bloom-userCannotModifyStyles";
			const string classNoAudio = "bloom-noAudio";

			var questionNodes = bookDOM.Body.SelectNodes("//div[contains(@class,'quizContents')]");
			foreach (XmlElement quizContentsElement in questionNodes)
			{
				if (!HtmlDom.HasClass(quizContentsElement, "bloom-noAudio")) // Needs migration
				{
					HtmlDom.AddClassIfMissing(quizContentsElement, classNoStyleMods);
					HtmlDom.AddClassIfMissing(quizContentsElement, classNoAudio);
					HtmlDom.StripUnwantedTagsPreservingText(bookDOM.RawDom, quizContentsElement, new []{ "div", "p", "br" });
				}
			}
		}

		/// <summary>
		/// Prior to Bloom 4.0 there was an attempt at localizing Page labels and the translation
		/// for several languages was done, but was not showing up. It turns out this was due to an
		/// incomplete implementation. Hopefully the implementation is complete now, but for older
		/// books that were not built with the proper attribute on the page label DIV we now insert it
		/// so that page labels can be localized properly.
		/// </summary>
		/// <remarks>
		/// See https://silbloom.myjetbrains.com/youtrack/issue/BL-5313
		/// </remarks>
		internal static void RepairPageLabelLocalization(HtmlDom bookDOM)
		{
			if (bookDOM?.Body == null)
				return;     // must be a test running...

			const string i18nPrefix = "TemplateBooks.PageLabel.";
			const string i18nAttr = "data-i18n";
			var prefixLength = i18nPrefix.Length;

			var pageLabelNodes = bookDOM.Body.SelectNodes("//div[@id]/div[@class='pageLabel']");
			foreach (XmlElement pageLabelElt in pageLabelNodes)
			{
				// If we already have a data-i18n attribute with the right contents, skip this one.
				var i18nValue = pageLabelElt.GetOptionalStringAttribute(i18nAttr, i18nPrefix);
				if (i18nValue.StartsWith(i18nPrefix) && i18nValue.Length > prefixLength)
				{
					// As best we can tell, this already has the right localization attribute contents.
					continue;
				}
				// Need to fix this one up with the right attribute and contents
				var englishContents = pageLabelElt.InnerText.Trim();
				i18nValue = i18nPrefix + englishContents;
				pageLabelElt.SetAttribute(i18nAttr, i18nValue);
			}
		}

		/// <summary>
		/// A bug in the initial release of Bloom 3.8 resulted in the nested editable divs being stored
		/// for the smallCoverCredits data under the general language tag "*".  (The bug was actually in
		/// the pug mixins for xmatter.)  The manifestation experienced by users was having the front page
		/// credits disappear from older books.  New books appeared to work properly.  Fixing the xmatter
		/// without fixing the book's html would have much the same effect, but for any books that had been
		/// created or edited with Bloom 3.8 (or newer).  This method restores sanity to the bloomDataDiv
		/// for the smallCoverCredits content.
		/// </summary>
		/// <remarks>
		/// See http://issues.bloomlibrary.org/youtrack/issue/BL-4591.
		/// </remarks>
		internal static void RepairBrokenSmallCoverCredits(HtmlDom bookDOM)
		{
			var dataDiv = bookDOM?.Body?.SelectSingleNode("div[@id='bloomDataDiv']");
			var badSmallCoverDiv = dataDiv?.SelectSingleNode("div[@data-book='smallCoverCredits' and @lang='*']");
			if (badSmallCoverDiv != null)
			{
				var divs = badSmallCoverDiv.SelectNodes("div[@lang!='']");
				foreach (XmlNode div in divs)
				{
					var lang = div.GetStringAttribute("lang");
					Debug.Assert(lang != "*");
					var existingDiv = dataDiv.SelectSingleNode("div[@data-book='smallCoverCredits' and @lang='"+lang+"']");
					if (existingDiv != null)
						continue;	// I don't think this should ever happen, but just in case...
					var innerText = div.InnerText;
					if (String.IsNullOrWhiteSpace(innerText))
						continue;	// ignore empty content regardless of XML markup
					var newDiv = dataDiv.OwnerDocument.CreateElement("div");
					newDiv.SetAttribute("data-book", "smallCoverCredits");
					newDiv.SetAttribute("lang", lang);
					newDiv.InnerXml = div.InnerXml.Trim();		// ignore surrounding newlines (or other whitespace)
					dataDiv.AppendChild(newDiv);
				}
				dataDiv.RemoveChild(badSmallCoverDiv);
			}
		}

		internal static void RemoveImgTagInDataDiv(HtmlDom bookDom)
		{
			// BL-4586 Some old books ended up with background-image urls containing XML img tags
			// in the HTML-encoded string. This happened because the coverImage data-book element
			// contained an img tag instead of a bare filename.
			// If such a thing exists in this book we will strip it out and replace it with the
			// filename in the img src attribute.
			const string dataDivImgXpath = "//div[@id='bloomDataDiv']/div[@data-book='coverImage']";
			var elementsToCheck = bookDom.SafeSelectNodes(dataDivImgXpath);
			foreach (XmlNode coverImageElement in elementsToCheck)
			{
				var imgNodes = coverImageElement.SafeSelectNodes("img");
				if (imgNodes.Count == 0)
				{
					continue;
				}
				var imgNode = imgNodes[0];
				coverImageElement.InnerText = (imgNode.Attributes == null || imgNode.Attributes["src"] == null) ?
					string.Empty : HttpUtility.UrlDecode(imgNode.Attributes["src"].Value);
			}
		}

		/// <summary>
		/// Repair the cover image descriptions to be language specific, storing only the internal paragraph(s) for
		/// each language, instead of the entire translationGroup div as a single piece.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-7039.
		/// This replaces the need to update the bloomDataDiv item to use ImageDescriptionEdit-style
		/// instead of normal-style as done for https://issues.bloomlibrary.org/youtrack/issue/BL-7804.
		/// </remarks>
		internal static void RepairCoverImageDescriptions(HtmlDom bookDOM)
		{
			var dataDiv = bookDOM?.Body?.SelectSingleNode("div[@id='bloomDataDiv']");
			var coverImageDiv = dataDiv?.SelectSingleNode("div[@data-book='coverImageDescription' and @lang='*']");
			if (coverImageDiv == null)
				return;		// nothing to fix
			foreach (XmlElement coverImageDescription in dataDiv.SafeSelectNodes("div[@data-book='coverImageDescription']").Cast<XmlElement>().ToList())
			{
				var lang = coverImageDescription.GetAttribute("lang");
				if (lang == "*")
				{
					foreach (XmlNode descriptionDiv in coverImageDescription.SafeSelectNodes("div[contains(@class,'bloom-editable')]"))
					{
						var innerLang = descriptionDiv.Attributes["lang"]?.Value;
						if (String.IsNullOrEmpty(innerLang) || innerLang == "z")
							continue;
						var newDescriptionDiv = dataDiv.OwnerDocument.CreateElement("div");
						newDescriptionDiv.SetAttribute("data-book", "coverImageDescription");
						newDescriptionDiv.SetAttribute("lang", innerLang);
						newDescriptionDiv.InnerXml = descriptionDiv.InnerXml;
						dataDiv.AppendChild(newDescriptionDiv);
					}
				}
				// Get rid of the obsolete data-book element.  If the user went from 4.7 with separate language specific entries
				// back to 4.6 with the single lang="*" entry, then all of the entries got filled in with the same obsolete data,
				// so all of them have to be removed.
				dataDiv.RemoveChild(coverImageDescription);
			}
		}

		public void UpdateBrandingForCurrentOrientation(HtmlDom bookDOM)
		{
			BringBookUpToDate(bookDOM, new NullProgress());
			// We need this to reinstate the classes that control visibility, otherwise no bloom-editable
			// text is shown
			UpdateMultilingualSettings(bookDOM);

			// The following is PROBABLY enough; but since this is such a rare case that two major versions
			// of Bloom shipped with it badly broken before we noticed (and no user ever reported it), it seems
			// worth using the fullest possible version of updating the book to bring it in line
			// with the current orientation.
			//BringXmatterHtmlUpToDate(bookDOM); // wipes out xmatter content!
			//bookDOM.UpdatePageNumberAndSideClassOfPages(_collectionSettings.CharactersForDigitsForPageNumbers, _collectionSettings.Language1.IsRightToLeft);
			// // restore xmatter page content from datadiv
			//var bd = new BookData(bookDOM, _collectionSettings, UpdateImageMetadataAttributes);
			//bd.SynchronizeDataItemsThroughoutDOM();
			//UpdateMultilingualSettings(bookDOM); // fix visibility classes
		}

		public void BringXmatterHtmlUpToDate(HtmlDom bookDOM)
		{
			var fileLocator = Storage.GetFileLocator();
			var helper = new XMatterHelper(bookDOM, CollectionSettings.XMatterPackName, fileLocator, BookInfo.UseDeviceXMatter);
			// If it's not the real book DOM we won't copy branding images into the real book folder, for fear
			// of messing up the real book, if the temporary one is in a different orientation.
			if (bookDOM != OurHtmlDom)
				helper.TemporaryDom = true;

			//note, we determine this before removing xmatter to fix the situation where there is *only* xmatter, no content, so if
			//we wait until we've removed the xmatter, we no how no way of knowing what size/orientation they had before the update.
			// Per BL-3571, if it's using a layout we don't know (e.g., from a newer Bloom) we switch to A5Portrait.
			// Various things, especially publication, don't work with unknown page sizes.
			Layout layout = Layout.FromDomAndChoices(bookDOM, Layout.A5Portrait, fileLocator);
			XMatterHelper.RemoveExistingXMatter(bookDOM);
			// this says, if you can't figure out the page size, use the one we got before we removed the xmatter...
			// still requiring it to be a valid layout.
			layout = Layout.FromDomAndChoices(bookDOM, layout, fileLocator);
			helper.InjectXMatter(_bookData.GetWritingSystemCodes(), layout, BookInfo.UseDeviceXMatter, _bookData.Language2.Iso639Code);

			var dataBookLangs = bookDOM.GatherDataBookLanguages();
			TranslationGroupManager.PrepareDataBookTranslationGroups(bookDOM.RawDom, dataBookLangs);
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

		/// <summary>
		/// Convert old &lt;b&gt; and &lt;i&gt; to &lt;strong&gt; and &lt;em&gt; respectively.
		/// Also remove instances like &lt;/b&gt;&lt;b&gt; altogether since such markup is redundant.
		/// </summary>
		public void UpdateCharacterStyleMarkup(HtmlDom bookDOM)
		{
			var preserve = bookDOM.RawDom.PreserveWhitespace;
			bookDOM.RawDom.PreserveWhitespace = true;
			var paragraphs = bookDOM.SafeSelectNodes("//div[contains(@class,'bloom-editable')]/p");
			foreach (XmlElement para in paragraphs)
			{
				string inner = para.InnerXml;
				if (String.IsNullOrEmpty(inner) || !inner.Contains("<"))
					continue;
				inner = Regex.Replace(inner, @"</b>(\p{Z}*)<b>", "$1",
					RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
				inner = Regex.Replace(inner, @"</i>(\p{Z}*)<i>", "$1",	// we've handled "</i></b> <b><i>"
					RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
				inner = Regex.Replace(inner, @"</b>(\p{Z}*)<b>", "$1",	// repeat in case "</b></i> <i><b>" happens
					RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
				// Note that .*? is the non-greedy match for any number of any characters.
				inner = Regex.Replace(inner, @"<b>(.*?)</b>", "<strong>$1</strong>",
					RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
				inner = Regex.Replace(inner, @"<i>(.*?)</i>", "<em>$1</em>",
					RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
				if (inner != para.InnerXml)
					para.InnerXml = inner;
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
			HtmlDom bookDOM = Storage.Dom;
//at version 0.9.71, we introduced this book lineage for real. At that point almost all books were from Basic book,
			//so let's get further evidence by looking at the page source and then fix the lineage
			// However, if we have json lineage, it is normal not to have it in HTML metadata.
			if (string.IsNullOrEmpty(BookInfo.BookLineage) && bookDOM.GetMetaValue("bloomBookLineage", "") == "")
				if (PageTemplateSource == "Basic Book")
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
		/// The bloomBookId meta value
		/// </summary>
		public string ID => Storage.BookInfo.Id;

		private void UpdateImageMetadataAttributes(XmlElement imgNode)
		{
			try
			{
				ImageUpdater.UpdateImgMetadataAttributesToMatchImage(FolderPath, imgNode, new NullProgress());
			}
			catch (UnauthorizedAccessException e)
			{
				BookStorage.ShowAccessDeniedErrorReport(e);
			}
		}

		// Returns true if it updated something.
		public bool UpdatePageToTemplate(HtmlDom pageDom, IPage templatePage, string pageId, bool allowDataLoss = true)
		{
			if (!OurHtmlDom.UpdatePageToTemplate(pageDom, templatePage.GetDivNodeForThisPage(), pageId, allowDataLoss))
				return false;
			AddMissingStylesFromTemplatePage(templatePage);
			CopyMissingStylesheetFiles(templatePage);
			UpdateEditableAreasOfElement(pageDom);
			return true;
		}

		// returns true if it updated something
		public bool UpdatePageToTemplateAndUpdateLineage(IPage pageToChange, IPage templatePage, bool allowDataLoss = true)
		{
			if (!UpdatePageToTemplate(OurHtmlDom, templatePage, pageToChange.Id, allowDataLoss))
				return false;
			// The Page objects are cached in the page list and may be used if we issue another
			// change layout command. We must update their lineage so the right "current layout"
			// will be shown if the user changes the layout of the same page again.
			var pageChanged = pageToChange as Page;
			pageChanged?.UpdateLineage(new[] { templatePage.Id });
			return true;
		}

		public void ChangeLayoutForAllContentPages(IPage templatePage)
		{
			foreach (var page in GetPages())
				if (!page.IsXMatter)
					UpdatePageToTemplateAndUpdateLineage(page, templatePage, allowDataLoss: false);
			Save();
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

		//hack. Eventually we might be able to lock books so that you can't edit them.
		public bool IsShellOrTemplate => !IsEditable;

		public bool HasOriginalCopyrightInfoInSourceCollection
		{
			get
			{
				var x = OurHtmlDom.SafeSelectNodes("//div[contains(@id, 'bloomDataDiv')]/div[contains(@data-book, 'originalCopyright') and string-length(translate(normalize-space(text()), ' ', '')) > 0]");
				return x.Count > 0 && CollectionSettings.IsSourceCollection;
			}
		}

		public bool HasSourceTranslations
		{
			get
			{
				//is there a textarea with something other than the vernacular, which has a containing element marked as a translation group?
				var x = OurHtmlDom.SafeSelectNodes($"//*[contains(@class,'bloom-translationGroup')]//textarea[@lang and @lang!='{_bookData.Language1.Iso639Code}']");
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
				if(CollectionSettings.IsSourceCollection) //nothing is locked if we're in a shell-making library
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
		public bool RecordedAsLockedDown => OurHtmlDom.RecordedAsLockedDown;

//		/// <summary>
//        /// Is this a shell we're translating? And if so, is this a shell-making project?
//        /// </summary>
//        public bool LockedExceptForTranslation
//        {
//            get
//            {
//            	return !_librarySettings.IsSourceCollection &&
//            		   RawDom.SafeSelectNodes("//meta[@name='editability' and @content='translationOnly']").Count > 0;
//            }
//        }

		public string CategoryForUsageReporting
		{
			get
			{
				if (CollectionSettings.IsSourceCollection)
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

		// Anything that sets HasFatalError true should appropriately set FatalErrorDescription.
		public virtual bool HasFatalError { get; private set; }
		private string FatalErrorDescription { get; set; }

		public string ThumbnailPath => Path.Combine(FolderPath, "thumbnail.png");

		public virtual bool CanUpdate => IsEditable && !HasFatalError;

		public virtual bool CanExport => IsEditable && !HasFatalError;

		/// <summary>
		/// In a vernacular library, we want to hide books that are meant only for people making shells
		/// </summary>
		public bool IsSuitableForVernacularLibrary => BookInfo.IsSuitableForVernacularLibrary;


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
		/// In a shell-making library, we want to hide books that are just shells, so rarely make sense as a starting point for more shells.
		/// Note: the setter on this property just sets the flag to the appropriate state. To actually change
		/// a book to or from a template, use SwitchSuitableForMakingShells()
		/// </summary>
		public bool IsSuitableForMakingShells
		{
			get
			{
				return BookInfo.IsSuitableForMakingShells;
			}
			set { BookInfo.IsSuitableForMakingShells = value; }

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
		public string MultilingualContentLanguage2 => _bookData.MultilingualContentLanguage2;

		/// <summary>
		/// For trilingual books, this is the third language to show
		/// </summary>
		public string MultilingualContentLanguage3 => _bookData.MultilingualContentLanguage3;

		public IEnumerable<string> ActiveLanguages
		{
			get
			{
				var result = new HashSet<string>(new [] {_bookData.Language1.Iso639Code});
				if (MultilingualContentLanguage2 != null)
					result.Add(MultilingualContentLanguage2);
				if (MultilingualContentLanguage3 != null)
					result.Add(MultilingualContentLanguage3);
				return result;
			}
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
		/// <remarks>
		/// We use the name of the language assigned by the user when the language was chosen rather
		/// than attempting to use the name in the national language (most likely getting the autonyms
		/// from a buggy list).  This keeps things simpler and follows the principle of least surprise.
		/// </remarks>
		private void InjectStringListingActiveLanguagesOfBook()
		{
			var languagesOfBook = _bookData.Language1.Name;

			if (MultilingualContentLanguage2 != null)
			{
				languagesOfBook += ", " + ((MultilingualContentLanguage2 == _bookData.Language2.Iso639Code) ?
					_bookData.Language2.Name :
					_bookData.Language3.Name);
			}
			if (MultilingualContentLanguage3 != null)
			{
				languagesOfBook += ", " + _bookData.Language3.Name;
			}

			_bookData.Set("languagesOfBook", languagesOfBook, false);
		}

		/// <summary>
		/// All the languages in a book is a difficult concept to define.
		/// So much so that the result isn't simply a list, but a dictionary: the keys indicate the languages
		/// that are present, and the (boolean) values are true if the book is considered to be "complete"
		/// in that language, which in some contexts (such as publishing to bloom library) governs whether
		/// the language is published by default.
		/// It also makes a difference whether an interesting element occurs in xmatter or not.
		/// A language is never considered an "incomplete translation" because it is missing in an xmatter element,
		/// mainly because it's so common for elements there to be in just a national language (BL-8527).
		/// For some purposes, a language that occurs ONLY in xmatter doesn't count at all... it won't even be
		/// a key in the dictionary unless includeLangsOccurringOnlyInXmatter is true.
		///  
		/// For determining which Text Languages to display in Publish -> Android and which pages to delete
		/// from a .bloomd file, we pass the parameter as true. I (gjm) am unclear as to why historically
		/// we did it this way, but BL-7967 might be part of the problem
		/// (where xmatter pages only in L2 were erroneously deleted).
		/// </summary>
		/// <remarks>The logic here is used to determine which language checkboxes to show in the web upload.
		/// Nearly identical logic is used in bloom-player to determine which languages to show on the Language Menu,
		/// so changes here may need to be reflected there and vice versa.</remarks>
		public Dictionary<string, bool> AllLanguages(bool includeLangsOccurringOnlyInXmatter = false)
		{
			var result = new Dictionary<string, bool>();
			var parents = new HashSet<XmlElement>(); // of interesting non-empty children
			var langDivs = OurHtmlDom.GetLanguageDivs(includeLangsOccurringOnlyInXmatter).ToArray();

			// First pass: fill in the dictionary with languages which have non-empty content in relevant divs
			foreach (var div in langDivs)
			{
				var lang = div.Attributes["lang"].Value;
				if (!string.IsNullOrWhiteSpace(div.InnerText))
				{
					result[lang] = true;	// may be set repeatedly, but no harm.
					// Add each parent only once, but add every parent for divs with any text.
					var parent = (XmlElement)div.ParentNode;
					if (!parents.Contains(parent))
						parents.Add(parent);
				}
			}
			// Second pass: for each parent, if it lacks a non-empty child for one of the languages, set value for that lang to false.
			// OTOH, if the parent is in XMatter, don't set the value.
			foreach (var lang in result.Keys.ToList()) // ToList so we can modify original collection as we go
			{
				foreach (var parent in parents)
				{
					if (ElementIsInXMatter(parent))
						continue;
					if (IsLanguageWanted(parent, lang) && !HasContentInLang(parent, lang))
					{
						result[lang] = false; // not complete
						break; // no need to check other parents.
					}
				}
			}
			return result;
		}

		private static bool ElementIsInXMatter(XmlElement element)
		{
			if (element == null)
				return false;
			while (element.ParentNode.Name != "body")
			{
				if (element.ParentWithClass("bloom-frontMatter") != null ||
				    element.ParentWithClass("bloom-backMatter") != null)
					return true;
				element = element.ParentNode as XmlElement;
			}

			return false;
		}

		/// Return all the languages we should currently offer to include when publishing this book.
		public Dictionary<string, bool> AllPublishableLanguages(bool includeLangsOccurringOnlyInXmatter = false)
		{
			var result = AllLanguages(includeLangsOccurringOnlyInXmatter);
			// For comical books, we only publish a single language. It's not currently feasible to
			// allow the reader to switch language in a Comical book, because typically that requires
			// adjusting the positions of the bubbles, and we don't yet support having more than one
			// set of bubble locations in a single book. See BL-7912 for some ideas on how we might
			// eventually improve this. In the meantime, switching language would have bad effects,
			// and if you can't switch language, there's no point in the book containing more than one.
			// Not including other languages neatly prevents switching and automatically saves the space.
			if (OurHtmlDom.SelectSingleNode(BookStorage.ComicalXpath) != null)
			{
				result.Clear();
				result[_bookData.Language1.Iso639Code] = true;
			}
			return result;
		}

		private bool IsLanguageWanted(XmlElement parent, string lang)
		{
			var defaultLangs = parent.GetAttribute("data-default-languages");
			if (String.IsNullOrEmpty(defaultLangs) || defaultLangs == "auto")
				return true;	// assume we want everything
			var dataDefaultLanguages = defaultLangs.Split(new char[] {',',' '}, StringSplitOptions.RemoveEmptyEntries);
			foreach (var defaultLang in dataDefaultLanguages)
			{
				switch (defaultLang)
				{
				case "V":
				case "L1":
					if (lang == _bookData.Language1.Iso639Code)
						return true;
					break;
				case "N1":
				case "L2":
					if (lang == _bookData.Language2.Iso639Code)
						return true;
					break;
				case "N2":
				case "L3":
					if (lang == _bookData.Language3.Iso639Code)
						return true;
					break;
				}
			}
			return false;
		}

		private static bool HasContentInLang(XmlElement parent, string lang)
		{
			foreach (var divN in parent.ChildNodes)
			{
				var div = divN as XmlElement;
				if (div?.Attributes["lang"] == null || div.Attributes["lang"].Value != lang)
					continue;
				return !string.IsNullOrWhiteSpace(div.InnerText); // this one settles it: success if non-empty
			}
			return false; // not found
		}

		public void RemoveObsoleteAudioMarkup()
		{
			RemoveObsoleteAudioMarkup(AudioProcessor.DoesAudioExistForSegment);
		}

		/// <summary>
		/// Remove any obsolete audio markup, using the supplied function to detect whether the markup is actually obsolete.
		/// (based on whether the relevant audio file exists or not.)
		/// </summary>
		/// <remarks>
		/// The extra complication of passing in a function is used to enable testing that non-obsolete markup is not removed.
		/// </remarks>
		public void RemoveObsoleteAudioMarkup(Func<string, string, bool> doesFileExist)
		{
			foreach (var spanOrDiv in HtmlDom.SelectAudioSentenceElements(RawDom.DocumentElement).Cast<XmlElement>().ToList())
			{
				if (!doesFileExist(Storage.FolderPath, spanOrDiv.Attributes["id"]?.Value))
				{
					if (spanOrDiv.Name == "span")
					{
						HtmlDom.RemoveElementLayer(spanOrDiv);
					}
					else
					{
						// TextBox mode recording: clean up multiple attributes on the div, and remove unneeded spans while
						// preserving the span content.
						spanOrDiv.RemoveAttribute("data-audiorecordingmode");
						spanOrDiv.RemoveAttribute("data-audiorecordingendtimes");
						spanOrDiv.RemoveAttribute("data-duration");
						HtmlDom.RemoveClass(spanOrDiv, "audio-sentence");
						HtmlDom.RemoveClass(spanOrDiv, "bloom-postAudioSplit");
						foreach (var span in spanOrDiv.SafeSelectNodes(".//span[@class='bloom-highlightSegment']").Cast<XmlElement>().ToList())
						{
							HtmlDom.RemoveElementLayer(span);
						}
					}
				}
			}
			foreach (var div in RawDom.DocumentElement.SafeSelectNodes("//div[@data-audiorecordingmode]").Cast<XmlElement>().ToList())
			{
				var nodes = div.SafeSelectNodes("descendant-or-self::node()[contains(@class,'audio-sentence')]");
				if (nodes == null || nodes.Count == 0)
					div.RemoveAttribute("data-audiorecordingmode");
			}
		}

		/// <summary>
		/// Determines if the book references an existing audio file.
		/// </summary>
		/// <returns></returns>
		public bool HasAudio()
		{
			return GetRecordedAudioSentences().Any();
		}

		/// <summary>
		/// Returns the elements that reference an audio file that exist
		/// </summary>
		/// <returns></returns>
		public IEnumerable<XmlElement> GetRecordedAudioSentences()
		{
			return
				HtmlDom.SelectAudioSentenceElements(RawDom.DocumentElement)
					.Cast<XmlElement>()
					.Where(span => AudioProcessor.DoesAudioExistForSegment(Storage.FolderPath, span.Attributes["id"]?.Value));
		}

		/// <summary>
		/// Check whether all text is covered by audio recording.
		/// </summary>
		/// <remarks>
		/// Only editable text in numbered pages is checked at the moment.
		/// </remarks>
		public bool HasFullAudioCoverage()
		{
			// REVIEW: should any of the xmatter pages be checked (front cover, title, credits?)
			foreach (var divWantPage in RawDom.SafeSelectNodes("//div[@class]").Cast<XmlElement>())
			{
				if (!HtmlDom.HasClass(divWantPage, "numberedPage"))
					continue;
				foreach (var div in divWantPage.SafeSelectNodes(".//div[@class]").Cast<XmlElement>())
				{
					if (!HtmlDom.HasClass(div, "bloom-editable"))
						continue;
					var lang = div.GetStringAttribute("lang");
					if (lang != _bookData.Language1.Iso639Code)
						continue;   // this won't go into the book -- it's a different language.
					// TODO: Ensure handles image descriptions once those get implemented.
					var textOfDiv = div.InnerText.Trim();

					// Note: Prior to version 4.4, audio-sentences were only in spans, but in version 4.4 they can be divs
					// (because the boundaries may need to cross multiple paragraphs, and <span> cannot contain <p> (because span is inline, and p is not))
					foreach (var audioSentenceChildNode in HtmlDom.SelectAudioSentenceElements(div).Cast<XmlElement>())
					{
						var id = audioSentenceChildNode.GetOptionalStringAttribute("id", "");
						if (string.IsNullOrEmpty(id) || !AudioProcessor.DoesAudioExistForSegment(Storage.FolderPath, audioSentenceChildNode.Attributes["id"].Value))
							return false;   // missing audio file
						if (!textOfDiv.StartsWith(audioSentenceChildNode.InnerText))
							return false;   // missing audio span?
						textOfDiv = textOfDiv.Substring(audioSentenceChildNode.InnerText.Length);
						textOfDiv = textOfDiv.TrimStart();
					}

					if (!string.IsNullOrEmpty(textOfDiv))
						return false;       // non-whitespace not covered by functional audio spans
				}
			}
			return true;
		}

		public bool HasBrokenAudioSentenceElements()
		{
			foreach (var divPage in RawDom.SafeSelectNodes("/html/body/div").Cast<XmlElement>())
			{
				if (HtmlDom.HasAudioSentenceElementsWithoutId(divPage))
					return true;
			}
			return false;
		}

		public void ReportIfBrokenAudioSentenceElements()
		{
			if (HasBrokenAudioSentenceElements())
			{
				string shortMsg = L10NSharp.LocalizationManager.GetString(@"PublishTab.Audio.ElementsMissingId",
					"Some audio elements are missing ids",
					@"Message briefly displayed to the user in a toast");
				var longMsg = "This book has elements marked audio-sentence that have no IDs. Usually this means that the book has been edited using some other program than Bloom.";
				NonFatalProblem.Report(ModalIf.None, PassiveIf.All, shortMsg, longMsg);
			}
		}

		/// <summary>
		/// Determines whether the book references an existing image file other than
		/// branding, placeholder, or license images.
		/// </summary>
		/// <returns></returns>
		public bool HasImages()
		{
			return RawDom.SafeSelectNodes("//img[@src]").Cast<XmlElement>().Any(NonTrivialImageFileExists) ||
				   RawDom.SafeSelectNodes("//div[@style]").Cast<XmlElement>().Any(NonTrivialImageFileExists);
		}

		private bool NonTrivialImageFileExists(XmlElement image)
		{
			if (image.Name == "img")
			{
				if (HtmlDom.HasClass(image, "branding") || HtmlDom.HasClass(image, "licenseImage"))
					return false;
			}
			var imageUrl = HtmlDom.GetImageElementUrl(image);
			var file = imageUrl.PathOnly.NotEncoded;
			if (string.IsNullOrEmpty(file))
				return false;
			if (file == "placeHolder.png" && image.Attributes["data-license"] == null)
				return false;
			return RobustFile.Exists(Path.Combine(Storage.FolderPath, file));
		}

		/// <summary>
		/// Determines whether the book references any existing video files other than branding.
		/// </summary>
		/// <returns></returns>
		public bool HasVideos()
		{
			return OurHtmlDom.SelectVideoElements()
				.Cast<XmlElement>().Any(NonTrivialVideoFileExists);
		}

		/// <summary>
		/// Returns whether the book references any existing sign language video files.
		/// </summary>
		public bool HasSignLanguageVideos()
		{
			// Currently no difference between videos and sign language videos
			return HasVideos();
		}

		private bool NonTrivialVideoFileExists(XmlElement vidSource)
		{
			Debug.Assert(vidSource.Name == "source");
			// In case future books have video branding...
			if (HtmlDom.HasClass(vidSource, "branding") || HtmlDom.HasClass(vidSource.ParentNode as XmlElement, "branding"))
				return false;
			// video reference HTML structure is:
			//   <div class='bloom-videoContainer'>
			//     <video>
			//       <source src="video/guid.mp4#t=time1,time2" />   (# and after is optional)
			//     </video>
			//   </div>
			var vidNode = vidSource.ParentNode;
			if (vidNode == null)
				return false;
			// HtmlDom.GetVideoElementUrl() takes the .bloom-videoContainer node as a parameter.
			var videoUrl = HtmlDom.GetVideoElementUrl(new ElementProxy(vidNode.ParentNode as XmlElement));
			var file = videoUrl.PathOnly.NotEncoded;
			return !string.IsNullOrEmpty(file) && RobustFile.Exists(Path.Combine(Storage.FolderPath, file));
		}

		// Book Information should show only for templates, not for created books.
		// See https://issues.bloomlibrary.org/youtrack/issue/BL-5190.
		public bool HasAboutBookInformationToShow
		{
			get
			{
				if (Storage.FolderPath == null)
					return false;
				if (Storage.FolderPath.Replace("\\", "/").Contains("/browser/templates/"))
					return RobustFile.Exists(AboutBookHtmlPath);	// built-in template shipped with Bloom
				if (BookInfo.IsSuitableForMakingShells || BookInfo.IsSuitableForMakingTemplates)
					return RobustFile.Exists(AboutBookMdPath);
				return false;
			}
		}

		public string AboutBookHtmlPath => BloomFileLocator.GetBestLocalizedFile(Storage.FolderPath.CombineForPath("ReadMe-en.htm"));

		public string AboutBookMdPath => BloomFileLocator.GetBestLocalizedFile(Storage.FolderPath.CombineForPath("ReadMe-en.md"));

		public void InitCoverColor()
		{
			// for digital comic template, we want a black cover.
			// NOTE as this writing, at least, xmatter cannot set <meta> values, so this isn't a complete solution. It's only
			// useful for starting off a book from a template book.
			var preserve = this.OurHtmlDom.GetMetaValue("preserveCoverColor", "false");
			if ( preserve == "false")
			{
				AddCoverColor(this.OurHtmlDom, CoverColors[s_coverColorIndex]);
			}
		}

		private void AddCoverColor(HtmlDom dom, Color coverColor)
		{
			var colorValue = ColorTranslator.ToHtml(coverColor);
//            var colorValue = String.Format("#{0:X2}{1:X2}{2:X2}", coverColor.R, coverColor.G, coverColor.B);
			XmlElement colorStyle = dom.RawDom.CreateElement("style");
			colorStyle.SetAttribute("type","text/css");
			colorStyle.InnerXml = @"
				DIV.bloom-page.coverColor	{		background-color: colorValue !important;	}
				".Replace("colorValue", colorValue);//string.format has a hard time with all those {'s

			dom.Head.AppendChild(colorStyle);
		}

		public String GetCoverColor()
		{
			return GetCoverColorFromDom(RawDom);
		}

		public static String GetCoverColorFromDom(XmlDocument dom)
		{
			foreach (XmlElement stylesheet in dom.SafeSelectNodes("//style"))
			{
				string content = stylesheet.InnerText;
				// Our XML representation of an HTML DOM doesn't seem to have any object structure we can
				// work with. The Stylesheet content is just raw CDATA text.
				var match = new Regex(@"DIV.bloom-page.coverColor\s*{\s*background-color:\s*(#[0-9a-fA-F]*)")
					.Match(content);
				if (match.Success)
				{
					return match.Groups[1].Value;
				}
			}
			return "#FFFFFF";
		}

		/// <summary>
		/// Set the cover color. Not used initially; assumes there is already an (unfortunately unmarked)
		/// stylesheet created as in AddCoverColor.
		/// </summary>
		/// <param name="color"></param>
		public void SetCoverColor(string color)
		{
			if (SetCoverColorInternal(color))
			{
				Save();
				ContentsChanged?.Invoke(this, new EventArgs());	
			}
		}

		/// <summary>
		/// Internal method is testable
		/// </summary>
		/// <param name="color"></param>
		/// <returns>true if a change was made</returns>
		internal bool SetCoverColorInternal(string color)
		{
			foreach (XmlElement stylesheet in RawDom.SafeSelectNodes("//style"))
			{
				string content = stylesheet.InnerXml;
				var regex =
					new Regex(
						@"(DIV.(coverColor\s*TEXTAREA|bloom-page.coverColor)\s*{\s*background-color:\s*)(#[0-9a-fA-F]*)",
						RegexOptions.IgnoreCase);
				if (!regex.IsMatch(content))
					continue;
				var newContent = regex.Replace(content, "$1" + color);
				stylesheet.InnerXml = newContent;
				return true;
			}

			return false;
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
			if (HasFatalError)
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
			if (HasFatalError)
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

		public void InsertPageAfter(IPage pageBefore, IPage templatePage, int numberToAdd = 1)
		{
			Guard.Against(HasFatalError, "Insert page failed: " + FatalErrorDescription);
			Guard.Against(!IsEditable, "Tried to edit a non-editable book.");

			// we need to break up the effects of changing the selected page.
			// The before-selection-changes stuff includes saving the old page. We want any changes
			// (e.g., newly defined styles) from the old page to be saved before we start
			// possibly merging in things (e.g., imported styles) from the template page.
			// On the other hand, we do NOT want stuff from the old page (e.g., its copy
			// of the old book styles) overwriting what we figure out in the process of
			// doing the insertion. So, do the stuff that involves the old page here,
			// and later do the stuff that involves the new page.
			_pageSelection.PrepareToSelectPage();

			ClearPagesCache();

			if(templatePage.Book !=null) // will be null in some unit tests that are unconcerned with stylesheets
				HtmlDom.AddStylesheetFromAnotherBook(templatePage.Book.OurHtmlDom, OurHtmlDom);

			// And, if it comes from a different book, we may need to copy over some of the user-defined
			// styles from that book. Do this before we set up the new page, which will get a copy of this
			// book's (possibly updated) stylesheet.
			AddMissingStylesFromTemplatePage(templatePage);

			XmlDocument dom = OurHtmlDom.RawDom;
			var templatePageDiv = templatePage.GetDivNodeForThisPage();
			var newPageDiv = dom.ImportNode(templatePageDiv, true) as XmlElement;
			BookStarter.SetupPage(newPageDiv, _bookData, _bookData.MultilingualContentLanguage2, _bookData.MultilingualContentLanguage3);//, LockedExceptForTranslation);
			SizeAndOrientation.UpdatePageSizeAndOrientationClasses(newPageDiv, GetLayout());
			newPageDiv.RemoveAttribute("title"); //titles are just for templates [Review: that's not true for front matter pages, but at the moment you can't insert those, so this is ok]C:\dev\Bloom\src\BloomExe\StyleSheetService.cs
			// If we're a template, make the new page a template one.
			HtmlDom.MakePageWithTemplateStatus(IsSuitableForMakingShells, newPageDiv);
			var elementOfPageBefore = FindPageDiv(pageBefore);

			// This is the only part that needs repeating if we're adding multiple pages at a time.
			var firstPageAdded = newPageDiv; // temporarily
			for (var i = 0; i < numberToAdd; i++)
			{
				var clonedDiv = (XmlElement)newPageDiv.CloneNode(true);
				if (i == 0)
					firstPageAdded = clonedDiv;
				BookStarter.SetupIdAndLineage(templatePageDiv, clonedDiv);
				elementOfPageBefore.ParentNode.InsertAfter(clonedDiv, elementOfPageBefore);

				CopyAndRenameAudioFiles(clonedDiv, templatePage.Book.FolderPath);
				CopyAndRenameVideoFiles(clonedDiv, templatePage.Book.FolderPath);
			}
			// Why audio and video but not image or widget? The logic is that within Bloom
			// someone can record over the audio or video, and it will replace the file,
			// and it would be unexpected for this to alter the audio or video on another page,
			// especially since in the case of the audio the text may have been edited on one
			// page but not the other. On the other hand, if the user selects a new image,
			// it will come with a unique file name and not affect any other page.
			// And there's no way to edit an activity within Bloom, so no reason I can see
			// not to share a possibly expensive resource. Of course, if someone edits the
			// resource outside Bloom, they might be either annoyed or pleased to have it
			// changed in both places. I don't have any reason to think one is more likely
			// than the other, so I'm going with the option that is less costly, both in
			// code to write and size of the resulting book.

			OrderOrNumberOfPagesChanged();
			BuildPageCache();
			var newPage = GetPages().First(p=>p.GetDivNodeForThisPage() == firstPageAdded);
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
			CopyMissingStylesheetFiles(templatePage);

			// and again for scripts (but we currently only worry about ones in the page itself)
			foreach (XmlElement scriptElt in newPageDiv.SafeSelectNodes(".//script[@src]"))
			{
				var fileName = scriptElt.Attributes["src"]?.Value;
				// Bloom Desktop accesses simpleComprehensionQuiz.js from the output/browser folder.
				// Bloom Reader uses the copy of that file which comes with bloom-player.
				// See https://issues.bloomlibrary.org/youtrack/issue/BL-8480.
				if (string.IsNullOrEmpty(fileName) || fileName == PublishHelper.kSimpleComprehensionQuizJs)
					continue;
				var destinationPath = Path.Combine(FolderPath, fileName);
				// In other similar operations above we don't overwrite an existing file (e.g., images, css).
				// But our general policy for JS is to go with the latest from our templates.
				// So we copy the smart page's current code; the page content may
				// be specific to it. Of course, if the book contains existing pages that expect
				// an old version of this code, anything could happen. Maintainers of released
				// smart pages will need to consider this. But this will sure help while developing
				// new smart pages.
				// Note that if it's important for old pages to keep using old JS, the maintainer
				// of the quiz page can simply rename the JS file; this code will copy the new JS
				// and any copies of the old page will keep using the old JS which will still be around.
				// But only this strategy allows the code to be updated (e.g., to make the old and new
				// versions of the page work properly together).
				var sourcePath = Path.Combine(templatePage.Book.FolderPath, fileName);
				// Don't try to copy a file over itself.  (See https://issues.bloomlibrary.org/youtrack/issue/BL-7349.)
				if (sourcePath == destinationPath)
					continue;
				if (RobustFile.Exists(sourcePath))
					RobustFile.Copy(sourcePath, destinationPath, true);
			}

			if (IsSuitableForMakingShells)
			{
				// If we just added the first template page to a template, it's now usable for adding
				// pages to other books. But the thumbnail for that template, and the template folder
				// it lives in, won't get created unless the user chooses Add Page again.
				// Even if he doesn't (maybe it's a one-page template), we want it to have the folder
				// that identifies it as a template book for the add pages dialog.
				// (We don't want to do so when the book is first created, because it's no good in
				// Add Pages until it has at least one addable page.)
				var templateFolderPath = Path.Combine(FolderPath, PageTemplatesApi.TemplateFolderName);
				Directory.CreateDirectory(templateFolderPath); // harmless if it exists already
			}

			Save();
			_pageListChangedEvent?.Raise(null);

			_pageSelection.SelectPage(newPage, true);

			InvokeContentsChanged(null);
		}

		/// <summary>
		/// Copy stylesheet files referenced by the template page that this book doesn't yet have.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-7170.
		/// </remarks>
		private void CopyMissingStylesheetFiles(IPage templatePage)
		{
			foreach (string sheetName in templatePage.Book.OurHtmlDom.GetTemplateStyleSheets())
			{
				var destinationPath = Path.Combine(FolderPath, sheetName);
				if (!RobustFile.Exists(destinationPath))
				{
					var sourcePath = Path.Combine(templatePage.Book.FolderPath, sheetName);
					if (RobustFile.Exists(sourcePath))
						RobustFile.Copy(sourcePath, destinationPath);
				}
			}
		}

		/// <summary>
		/// If we are inserting a page from a different book, or updating the layout of our page to one from a
		/// different book, we may need to copy user-defined styles from that book to our own.
		/// </summary>
		/// <param name="templatePage"></param>
		private void AddMissingStylesFromTemplatePage(IPage templatePage)
		{
			if (templatePage.Book.FolderPath != FolderPath)
			{
				var domForPage = templatePage.Book.GetEditableHtmlDomForPage(templatePage);
				if (domForPage != null) // possibly null only in unit tests?
				{
					var userStylesOnPage = HtmlDom.GetUserModifiableStylesUsedOnPage(domForPage); // could be empty
					var existingUserStyles = GetOrCreateUserModifiedStyleElementFromStorage();
					var newMergedUserStyleXml = HtmlDom.MergeUserStylesOnInsertion(existingUserStyles, userStylesOnPage);
					existingUserStyles.InnerXml = newMergedUserStyleXml;
				}
			}
		}

		public void DuplicatePage(IPage page)
		{
			// Can be achieved by just using the current page as both the place to insert after
			// and the template to copy.
			// Note that Pasting a page uses the same routine; unit tests for duplicate and copy/paste
			// take advantage of our knowledge that the code is shared so that between them they cover
			// the important code paths. If the code stops being shared, we should extend test
			// coverage appropriately.
			InsertPageAfter(page, page);
		}

		private void CopyAndRenameAudioFiles(XmlElement newpageDiv, string sourceBookFolder)
		{
			foreach (var audioElement in HtmlDom.SelectRecordableDivOrSpans(newpageDiv).Cast<XmlElement>().ToList())
			{
				// The "i" makes sure that the ID does not start with digit. It's unnecessary but harmless
				// if it already starts with a non-digit. The JS code that usually generates these only
				// adds it if necessary, but nothing knows enough about the nature of the IDs to make
				// consistency about that necessary. It just needs to be unique (and a valid ID).
				// (It would be pretty easy to make it consistent, but considerably harder to cover
				// the new path adequately with unit tests.)
				var id = "i" + Guid.NewGuid();
				var oldId = audioElement.Attributes["id"]?.Value;
				audioElement.SetAttribute("id", id);
				if (string.IsNullOrEmpty(oldId))
					continue;
				var sourceAudioFilePath = Path.Combine(Path.Combine(sourceBookFolder, "audio"), oldId + ".wav");
				var newAudioFolderPath = Path.Combine(FolderPath, "audio");
				var newAudioFilePath = Path.Combine(newAudioFolderPath, id + ".wav");
				Directory.CreateDirectory(newAudioFolderPath);
				if (RobustFile.Exists(sourceAudioFilePath))
				{
					RobustFile.Copy(sourceAudioFilePath, newAudioFilePath);
				}

				var mp3Path = Path.ChangeExtension(sourceAudioFilePath, "mp3");
				var newMp3Path = Path.ChangeExtension(newAudioFilePath, "mp3");
				if (RobustFile.Exists(mp3Path))
				{
					RobustFile.Copy(mp3Path, newMp3Path);
				}
			}
		}

		private void CopyAndRenameVideoFiles(XmlElement newpageDiv, string sourceBookFolder)
		{
			foreach (var source in newpageDiv.SafeSelectNodes(".//video/source").Cast<XmlElement>().ToList())
			{
				var src = source.GetAttribute("src");
				// old source may have a param, too, but we don't currently need to keep it.
				string timings;
				src = SignLanguageApi.StripTimingFromVideoUrl(src, out timings);
				if (String.IsNullOrWhiteSpace(src))
					continue;
				var oldVideoPath = Path.Combine(sourceBookFolder, src);
				// If the video file doesn't exist, don't bother adjusting anything.
				// If it does exist, copy it with a new name based on the current one, similarly
				// to how we've been renaming image files that already exist in the book's folder.
				if (RobustFile.Exists(oldVideoPath))
				{
					var extension = Path.GetExtension(src);
					var oldFileName = Path.GetFileNameWithoutExtension(src);
					int count = 0;
					string newVideoPath;
					do
					{
						++count;
						var newFileName = oldFileName + "-" + count.ToString(CultureInfo.InvariantCulture);
						newVideoPath = Path.Combine(FolderPath, "video", newFileName + extension);
					} while (RobustFile.Exists(newVideoPath));

					Directory.CreateDirectory(Path.GetDirectoryName(newVideoPath));
					RobustFile.Copy(oldVideoPath, newVideoPath, false);

					source.SetAttribute("src", "video/" +
						UrlPathString.CreateFromUnencodedString(Path.GetFileName(newVideoPath)).UrlEncoded +
						(string.IsNullOrEmpty(timings) ? "" : "#t=" + timings));
				}
			}
		}

		public void DeletePage(IPage page)
		{
			Guard.Against(HasFatalError, "Delete page failed: " + FatalErrorDescription);
			Guard.Against(!IsEditable, "Tried to edit a non-editable book.");

			if(GetPages().Count() < 2)
				return;

			var pageToShowNext = GetPageToShowAfterDeletion(page);

			ClearPagesCache();
			//_pagesCache.Remove(page);
			OrderOrNumberOfPagesChanged();

			var pageNode = FindPageDiv(page);
			pageNode.ParentNode.RemoveChild(pageNode);
			Storage.Dom.UpdatePageNumberAndSideClassOfPages(CollectionSettings.CharactersForDigitsForPageNumbers, _bookData.Language1.IsRightToLeft);

			_pageSelection.SelectPage(pageToShowNext);
			Save();
			_pageListChangedEvent?.Raise(null);

			InvokeContentsChanged(null);
		}

		private void OrderOrNumberOfPagesChanged()
		{
			OurHtmlDom.UpdatePageNumberAndSideClassOfPages(CollectionSettings.CharactersForDigitsForPageNumbers,
				_bookData.Language1.IsRightToLeft);
		}

		private void ClearPagesCache()
		{
			_pagesCache = null;
		}

		/// <summary>
		/// Internal for use by BookMetadataApi, which wants to know the number of the highest page number in the book.
		/// </summary>
		/// <returns></returns>
		internal int GetLastNumberedPageNumber()
		{
			var lastPageNumber = 0;
			foreach (var page in GetPages())
			{
				if (page.IsBackMatter)
					return lastPageNumber;

				string dummy;
				page.GetCaptionOrPageNumber(ref lastPageNumber, out dummy);
			}
			return lastPageNumber;
		}

		public BookData BookData => _bookData;

		public void InsertFullBleedMarkup(XmlElement body)
		{
			if (FullBleed)
			{
				HtmlDom.InsertFullBleedMarkup(body);
			}
		}

		public bool FullBleed => BookData.GetVariableOrNull("fullBleed", "*") == "true" && CollectionSettings.HaveEnterpriseFeatures;

		/// <summary>
		/// Earlier, we handed out a single-page version of the document. Now it has been edited,
		/// so we now we need to fold changes back in
		/// </summary>
		public void SavePage(HtmlDom editedPageDom, bool needToDoFullSave = true)
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
				HtmlDom.SetImageAltAttrsFromDescriptions(pageFromStorage, _bookData.Language1.Iso639Code);

				// The main condition for being able to just write the page is that no shareable data on the
				// page changed during editing. If that's so we can skip this step.
				if (needToDoFullSave)
					_bookData.SuckInDataFromEditedDom(editedPageDom, BookInfo); //this will do an updatetitle

				// When the user edits the styles on a page, the new or modified rules show up in a <style/> element with title "userModifiedStyles".
				// Here we copy that over to the book DOM.
				var userModifiedStyles = HtmlDom.GetUserModifiedStyleElement(editedPageDom.Head);
				var stylesChanged = false;
				if (userModifiedStyles != null)
				{
					var userModifiedStyleElementFromStorage = GetOrCreateUserModifiedStyleElementFromStorage();
					if (userModifiedStyleElementFromStorage.InnerXml != userModifiedStyles.InnerXml)
					{
						userModifiedStyleElementFromStorage.InnerXml = userModifiedStyles.InnerXml;
						stylesChanged = true; // note, this is not shared data in the sense that needs SuckInDataFromEditedDom
					}

					//Debug.WriteLine("Incoming User Modified Styles:   " + userModifiedStyles.OuterXml);
				}

				try
				{
					if (!needToDoFullSave && !stylesChanged)
					{
						// nothing changed outside this page. We can do a much more efficient write operation.
						// (On a 200+ page book, like the one in BL-7253, this version of updating the page
						// runs in about a half second instead of two and a half. Moreover, on such a book,
						// running the full Save rather quickly fragments the heap...allocating about 16 7-megabyte
						// memory chunks in each Save...to the point where Bloom runs out of memory.)

						SaveForPageChanged(pageId, pageFromStorage);
					}
					else
					{
						Save();
					}
				}
				catch (UnauthorizedAccessException e)
				{
					BookStorage.ShowAccessDeniedErrorReport(e);
					return;
				}

				Storage.UpdateBookFileAndFolderName(CollectionSettings);
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
		internal XmlElement GetOrCreateUserModifiedStyleElementFromStorage()
		{
			var headElement = OurHtmlDom.Head;
			var userStyleElement = HtmlDom.GetUserModifiedStyleElement(headElement);
			if (userStyleElement == null)
				return HtmlDom.AddEmptyUserModifiedStylesNode(headElement);

			var coverColorElement = HtmlDom.GetCoverColorStyleElement(headElement);
			// If the user defines the cover color, the two elements could end up being the same.
			if (coverColorElement == null || coverColorElement == userStyleElement)
				return userStyleElement;

			// We have both style elements. Make sure they're in the right order.
			// BL -4266 was a problem if the 'coverColor' was listed first.
			headElement.RemoveChild(coverColorElement);
			headElement.InsertAfter(coverColorElement, userStyleElement);
			return userStyleElement;
		}

		/// <summary>
		/// Gets the first element with the given tag & id, within the page-div with the given id.
		/// </summary>
		private XmlElement GetPageFromStorage(string pageDivId)
		{
			var query = $"//div[@id='{pageDivId}']";
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
			Guard.Against(HasFatalError, "Move page failed: " + FatalErrorDescription);
			Guard.Against(!IsEditable, "Tried to edit a non-editable book.");

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
			OrderOrNumberOfPagesChanged();
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

			return indexOfItemAfterRelocation > GetIndexOfLastFrontMatterPage ()
				&& indexOfItemAfterRelocation < upperBounds;
		}

		private int GetIndexOfLastFrontMatterPage()
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

		public HtmlDom GetDomForPrinting(PublishModel.BookletPortions bookletPortion, BookCollection currentBookCollection,
			BookServer bookServer, bool orientationChanging, Layout pageLayout)
		{
			var printingDom = GetBookDomWithStyleSheets("previewMode.css", "origami.css");
			AddCreationTypeAttribute(printingDom);

			if (IsFolio)
			{
				AddChildBookContentsToFolio(printingDom, currentBookCollection, bookServer);
				printingDom.SortStyleSheetLinks();
			}

			//we do this now becuase the publish ui allows the user to select a different layout for the pdf than what is in the book file
			SizeAndOrientation.UpdatePageSizeAndOrientationClasses(printingDom.RawDom, pageLayout);

			if (orientationChanging)
			{
				// Need to update the xmatter in the print dom...it may use different images.
				// Make sure we do this AFTER setting PageOrientation in Dom.
				// Also must be BEFORE we delete unwanted pages
				UpdateBrandingForCurrentOrientation(printingDom);
			}
			//whereas the base is to our embedded server during editing, it's to the file folder
			//when we make a PDF, because we wan the PDF to use the original hi-res versions

			BookStorage.SetBaseForRelativePaths(printingDom, FolderPath);

			DeletePages(printingDom.RawDom, p=>p.GetAttribute("class").ToLowerInvariant().Contains("bloom-nonprinting"));
			PublishHelper.RemoveEnterprisePagesIfNeeded(_bookData, printingDom, printingDom.GetPageElements().ToList());

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
			// Do this after we remove unwanted pages; otherwise, the page removal code must also remove the media boxes.
			if (FullBleed)
			{
				InsertFullBleedMarkup(printingDom.Body);
			}
			if (!FullBleed)
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

			int cumulativePageNum = 1;
			string lastPageNumStr = currentLastContentPage.Attributes?.GetNamedItem("data-page-number")?.InnerText;
			if (int.TryParse(lastPageNumStr, out int lastPageNum))
			{
				cumulativePageNum = lastPageNum;
			}

			//currently we have no way of filtering them, we just take them all
			foreach (var bookInfo in currentBookCollection.GetBookInfos())
			{
				if (bookInfo.IsFolio)
					continue;
				var childBook = bookServer.GetBookFromBookInfo(bookInfo);

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

					if (!String.IsNullOrWhiteSpace(importedPage.GetAttribute("data-page-number")))
					{
						++cumulativePageNum;
						importedPage.SetAttribute("data-page-number", cumulativePageNum.ToString());
					}

					currentLastContentPage.ParentNode.InsertAfter(importedPage, currentLastContentPage);
					currentLastContentPage = importedPage;

					foreach(XmlElement img in HtmlDom.SelectChildImgAndBackgroundImageElements(importedPage))
					{
						var bookFolderName = Path.GetFileName(bookInfo.FolderPath);
						var path = HtmlDom.GetImageElementUrl(img);
						var pathRelativeToFolioFolder = "../" + bookFolderName + "/" + path.NotEncoded;	// want query as well as filepath
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
				return lastPage;
			}
		}

		/// <summary>
		/// this is used for configuration, where we do want to offer up the original file.
		/// </summary>
		/// <returns></returns>
		public string GetPathHtmlFile()
		{
			return Storage.PathToExistingHtml;
		}

		public PublishModel.BookletLayoutMethod GetDefaultBookletLayoutMethod()
		{
			return GetBookletLayoutMethod(GetLayout());
		}

		public PublishModel.BookletLayoutMethod GetBookletLayoutMethod(Layout layout)
		{
			//NB: all we support at the moment is specifying "Calendar"
			if (OurHtmlDom.SafeSelectNodes(String.Format("//meta[@name='defaultBookletLayout' and @content='Calendar']")).Count >
				0)
				return PublishModel.BookletLayoutMethod.Calendar;
			else
			{
				if (layout.SizeAndOrientation.IsLandScape && layout.SizeAndOrientation.PageSizeName == "A5")
					return PublishModel.BookletLayoutMethod.CutAndStack;
				return PublishModel.BookletLayoutMethod.SideFold;
			}
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
			var language1Iso639Code = _bookData.Language1.Iso639Code;
			var multilingualContentLanguage2 = _bookData.MultilingualContentLanguage2;
			var multilingualContentLanguage3 = _bookData.MultilingualContentLanguage3;
			foreach (XmlElement div in dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]"))
			{
				TranslationGroupManager.PrepareElementsInPageOrDocument(div, _bookData);
				TranslationGroupManager.UpdateContentLanguageClasses(div, _bookData, language1Iso639Code, multilingualContentLanguage2, multilingualContentLanguage3);
			}
		}

		public string CheckForErrors()
		{
			var errors = Storage.GetValidateErrors();
			if (!String.IsNullOrEmpty(errors))
			{
				HasFatalError = true;
				FatalErrorDescription = errors;
			}
			return errors ?? "";
		}

		public void CheckBook(IProgress progress, string pathToFolderOfReplacementImages=null)
		{
			Storage.CheckBook(progress, pathToFolderOfReplacementImages);
		}

		public virtual Layout GetLayout()
		{
			return Layout.FromDom(OurHtmlDom, Layout.A5Portrait);
		}

		public IEnumerable<Layout> GetLayoutChoices()
		{
			try
			{
				return SizeAndOrientation.GetLayoutChoices(OurHtmlDom, Storage.GetFileLocator());
			}
			catch (Exception error)
			{
				HasFatalError = true;
				FatalErrorDescription = error.Message;
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
			try
			{
				ImageUpdater.CopyImageMetadataToWholeBook(Storage.FolderPath,OurHtmlDom, metadata, progress);
			}
			catch (UnauthorizedAccessException e)
			{
				BookStorage.ShowAccessDeniedErrorReport(e);
				return;	// Probably not much point to saving if copying the image metadata didn't fully complete successfully
			}
			Save();
		}

		public Metadata GetLicenseMetadata()
		{
			//BookCopyrightAndLicense.LogMetdata(OurHtmlDom);
			var result = BookCopyrightAndLicense.GetMetadata(OurHtmlDom, _bookData);

			//Logger.WriteEvent("After");
			//BookCopyrightAndLicense.LogMetdata(OurHtmlDom);
			return result;
		}

		public void SetMetadata(Metadata metadata)
		{
			BookCopyrightAndLicense.SetMetadata(metadata, OurHtmlDom, FolderPath, _bookData, BookInfo.MetaData.UseOriginalCopyright);
			BookInfo.SetLicenseAndCopyrightMetadata(metadata);
		}

		public void SetTitle(string name)
		{
			OurHtmlDom.Title = name;
		}

		public void ExportXHtml(string path)
		{
			XmlHtmlConverter.GetXmlDomFromHtmlFile(Storage.PathToExistingHtml,true).Save(path);
		}

		public void Save()
		{
			// If you add something here, consider whether it is needed in SaveForPageChanged().
			// I believe all the things currently here before the actual Save are not needed
			// in the cases where we use SaveForPageChanged(). We switch to Save if any
			// book data changed, which will be true if we're changing the title and thus
			// the book's location. We also do a full Save after bringing the book up to date;
			// after that, there shouldn't be any obsolete sound attributes.
			// (In fact, since we bring a book up to date before editing, and that code
			// does the removal, I don't see why it's needed here either.)
			Guard.Against(HasFatalError, "Save failed: " + FatalErrorDescription);
			Guard.Against(!IsEditable, "Tried to save a non-editable book.");
			RemoveObsoleteSoundAttributes(OurHtmlDom);
			_bookData.UpdateVariablesAndDataDivThroughDOM(BookInfo);//will update the title if needed
			if(!LockDownTheFileAndFolderName)
			{
				Storage.UpdateBookFileAndFolderName(CollectionSettings); //which will update the file name if needed
			}
			if(IsSuitableForMakingShells)
			{
				// A template book is considered to be its own source, so update the source to match the
				// current book location.
				PageTemplateSource = Path.GetFileName(FolderPath);
			}

			try
			{
				Storage.Save();
			}
			catch (UnauthorizedAccessException e)
			{
				BookStorage.ShowAccessDeniedErrorReport(e);
				return;
			}

			DoPostSaveTasks();
		}

		public void SaveForPageChanged(string pageId, XmlElement modifiedPage)
		{
			Guard.Against(HasFatalError, "Save failed: " + FatalErrorDescription);
			Guard.Against(!IsEditable, "Tried to save a non-editable book.");
			Storage.SaveForPageChanged(pageId, modifiedPage);
			DoPostSaveTasks();
		}

		private void DoPostSaveTasks()
		{
			// Tell the accessibility checker window (and any future subscriber) to re-compute.
			// This Task.Delay() helps even with a delay of 0, becuase it means we get to finish with this command.
			// I'm chooing 1 second at the moment as that feels about the longest I would want to
			// wait to see if what I did made and accesibility check change. Of course we're *probably*
			// ready to run this much, much sooner.
			Task.Delay(1000).ContinueWith((task) => _bookSavedEvent.Raise(this));
		}

		/// <summary>
		/// Remove any obsolete data-duration attributes that the typescript code failed to remove.
		/// </summary>
		/// <remarks>
		/// See https://silbloom.myjetbrains.com/youtrack/issue/BL-3671.
		/// </remarks>
		private void RemoveObsoleteSoundAttributes(HtmlDom htmlDom)
		{
			foreach (var span in htmlDom.RawDom.SafeSelectNodes("//span[@data-duration and @id]").Cast<XmlElement>())
			{
				if (!AudioProcessor.DoesAudioExistForSegment(Storage.FolderPath, span.GetStringAttribute("id")))
					span.RemoveAttribute("data-duration");	// file no longer exists, shouldn't have any duration setting
			}
		}

		/// <summary>
		/// Remove any obsolete explicit image size and position left over from earlier versions of Bloom, before we had object-fit:contain.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-9401.  A similar fix exists in bookEdit/js/bloomImages.ts, but that applies
		/// only when a page is opened in the edit tab.
		/// </remarks>
		private void RemoveObsoleteImageAttributes(HtmlDom htmlDom)
		{
			foreach (var img in htmlDom.RawDom.SafeSelectNodes("//div[contains(@class,'bloom-imageContainer')]/img[@style|@width|@height]").Cast<XmlElement>())
			{
				var style = img.GetOptionalStringAttribute("style", "");
				var fixedStyle = RemoveSizeStyling(style);
				if (String.IsNullOrEmpty(fixedStyle))
					img.RemoveAttribute("style");
				else if (fixedStyle != style)
					img.SetAttribute("style", fixedStyle);
				img.RemoveAttribute("width");
				img.RemoveAttribute("height");
			}
		}

		private string RemoveSizeStyling(string style)
		{
			// For example, style="width: 404px; height: 334px; margin-left: 1px; margin-top: 0px;"
			// should reduce to "".
			var style1 = Regex.Replace(style, "width: *[0-9a-z]+;", "");
			var style2 = Regex.Replace(style1, "height: *[0-9a-z]+;", "");
			var style3 = Regex.Replace(style2, "margin-left: *[0-9a-z]+;", "");
			var style4 = Regex.Replace(style3, "margin-top: *[0-9a-z]+;", "");
			return style4.Trim();
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

		internal IBookStorage Storage { get; }

		/// <summary>
		/// This gets called as a result of a UI action. It sets the new topic in our data,
		/// but doesn't do anything related to how it is displayed on the page.
		/// The way to think about this is that we're aiming for a more react™-style flow.
		/// </summary>
		public void SetTopic(string englishTopicAsKey)
		{
			_bookData.Set("topic",englishTopicAsKey,"en");
		}

		public void SwitchSuitableForMakingShells(bool isSuitable)
		{
			if (isSuitable)
			{
				IsSuitableForMakingShells = true;
				OurHtmlDom.RecordAsLockedDown(false);
				// Note that in Book.Save(), we set the PageTemplateSource(). We do that
				// there instead of here so that it stays up to date if the user changes
				// the template name.

				OurHtmlDom.MarkPagesWithTemplateStatus(true);
			}
			else
			{
				IsSuitableForMakingShells = false;
				OurHtmlDom.MarkPagesWithTemplateStatus(false);
				// The logic in BookStarter.UpdateEditabilityMetadata is that if we're in a source collection
				// a book that is not a template should be recorded as locked down (though because we're in
				// a source collection it won't actually BE locked down).
				if (CollectionSettings.IsSourceCollection)
					OurHtmlDom.RecordAsLockedDown(true);
			}
		}

		/// <summary>
		/// Make a version code which will detect any significant changes to the content of a bloom book.
		/// fileContent is typically the content of the file at filePath which is the book's main HTM file;
		/// however (for testing) filePath may be omitted.
		/// The method computes a SHA of the file content and, if a path is passed, all other files
		/// in the same folder and its subfolders. The file is transformed somewhat so that (some) changes
		/// that are not significant are ignored.
		/// Notes:
		/// - renaming a file may or may not produce a different code (depends on whether it changes
		/// the alphabetical order of the files).
		/// - pdf files are currently omitted
		/// - audio files could be omitted until we start supporting audio, but as that is planned I
		/// have not chosen to omit them
		/// - I am not sure that this will reliably give the same result when run on Linux and Windows.
		/// For one thing, depending on the exact file transfer process, one or more files might have
		/// different line endings, which is enough to produce a different SHA.
		/// </summary>
		/// <param name="fileContent"></param>
		/// <param name="filePath"></param>
		/// <returns></returns>
		public static string MakeVersionCode(string fileContent, string filePath = null)
		{
			var simplified = fileContent;
			// In general, whitespace sequences are equivalent to a single space.
			// If the user types multiple spaces all but one will be turned to &nbsp;
			simplified = new Regex(@"\s+").Replace(simplified, " ");
			// Between the end of one tag and the start of the next white space doesn't count at all
			simplified = new Regex(@">\s+<").Replace(simplified, "><");
			// Page IDs (actually any element ids) are ignored
			// (the bit before the 'id' matches an opening wedge followed by anything but a closing one,
			// and is transferred to the output by $1. Then we look for an id='whatever', with optional
			// whitespace, where (['\"]) matches either kind of opening quote while \2 matches the same one at the end.
			// The question mark makes sure we end with the first possible closing quote.
			// Then we grab everything up to the closing wedge and transfer that to the output as $3.)
			simplified = new Regex("(<[^>]*)\\s*id\\s*=\\s*(['\"]).*?\\2\\s*([^>]*>)").Replace(simplified, "$1$3");
			var bytes = Encoding.UTF8.GetBytes(simplified);
			var sha = SHA256Managed.Create();
			sha.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
			if (filePath != null)
			{
				var folder = Path.GetDirectoryName(filePath);
				// Order must be predictable but does not otherwise matter.
				foreach (var path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(x => x))
				{
					var ext = Path.GetExtension(path);
					// PDF files are generated, we don't care whether they are identical.
					// .status files contain the output of this function among other team collection
					// information; counting them would mean that writing a new status with the
					// new version code would immediately change the next version code computed.
					if (ext == ".pdf" || ext == ".status")
						continue;
					if (path == filePath)
						continue; // we already included a simplified version of the main HTML file
					using (var input = new FileStream(path, FileMode.Open))
					{
						byte[] buffer = new byte[4096];
						int count;
						while ((count = input.Read(buffer, 0, 4096)) > 0)
						{
							sha.TransformBlock(buffer, 0, count, buffer, 0);
						}
					}
				}
			}
			sha.TransformFinalBlock(new byte[0], 0, 0);
			return Convert.ToBase64String(sha.Hash);
		}

		/// <summary>
		/// Gets the complete path to the book's cover image, or null if there isn't one.
		/// </summary>
		/// <returns></returns>
		public string GetCoverImagePath()
		{
			if (Storage == null)
				return null;	// can happen in tests
			// This first branch covers the currently obsolete approach to images using background-image.
			// In that approach the data-book attribute is on the imageContainer.
			// Note that we want the coverImage from from a page instead of the dataDiv because the former
			// "doesn't have the data in the form that GetImageElementUrl can handle." 
			var coverImgElt = Storage.Dom.SafeSelectNodes("//div[@id!='bloomDataDiv']/div[@data-book='coverImage']")
				.Cast<XmlElement>()
				.FirstOrDefault();
			// If that fails, we look for an img with the relevant attribute. Happily this doesn't conflict with the data-div.
			if (coverImgElt == null)
			{
				coverImgElt = Storage.Dom.SafeSelectNodes("//img[@data-book='coverImage']")
					.Cast<XmlElement>()
					.FirstOrDefault();
			}
			if (coverImgElt == null)
				return null;
			var coverImageUrl = HtmlDom.GetImageElementUrl(coverImgElt);
			var coverImageFileName = coverImageUrl.PathOnly.NotEncoded;
			if (string.IsNullOrEmpty(coverImageFileName))
				return null;
			var coverImagePath = Path.Combine(StoragePageFolder, coverImageFileName);
			if (!File.Exists(coverImagePath))
				return null;
			return coverImagePath;
		}

		/// <summary>
		/// Check whether the given image file should have a transparent background.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-4816 for why we want to limit
		/// which image files are given a transparent background.
		/// </remarks>
		public bool ImageFileShouldBeRenderedWithTransparency(string imagePath)
		{
			// At the moment, only the cover image needs a transparent background.
			// Note that if an image file is used more than once in a book, it gets a different
			// name each time.
			// For publishing, the imagePath will be in a temporary folder location instead of
			// the Bloom collection folder, so comparing the full paths does not work.  In publishing,
			// we do get a few images requested from the PDF framework whose names may inadvertently
			// match files we use, so we double-check by also comparing file sizes.
			var coverImagePath = GetCoverImagePath();
			if (Path.GetFileName(imagePath) == Path.GetFileName(coverImagePath))
				return (new FileInfo(imagePath).Length == new FileInfo(coverImagePath).Length);
			return false;;
		}

		/// <summary>
		/// The primary focus of this method is removing pages we don't want in bloomd files,
		/// particularly xmatter pages that often don't have content but just might.
		/// It will detect pages with img elements or bloom-imageContainer elements with
		/// background images, and as long as the image isn't our placeholder, such pages
		/// are non-blank. For text, it is looking for divs that have the bloom-visibility-code-on
		/// class (and some non-white content). This means that it's looking for content that is
		/// visible given the current collection settings. Blank pages might have content in other
		/// languages. It's even possible (see comments on the code that inserts the
		/// bloom-visibility-code-on class) that the user might override it somehow.
		/// It's conceivable that pages contain text that's not in our editable divs.
		/// Thus, this mechanism is not as reliable as the process used in epub publishing to delete
		/// invisible text, which involves actually building a display of the page in the browser,
		/// but it is much faster and simpler and seems adequate to the current purpose.
		/// Also, (BL-7586) we don't want to delete activity pages, that may not have text, but still
		/// could be fully functioning activities.
		/// Currently the intention is to apply this to a copy of the book, not the original.
		/// If the optional argument is provided, having 'visible' text is defined as text
		/// in one of the specified languages.
		/// </summary>
		public void RemoveBlankPages(HashSet<string> languagesToInclude = null)
		{
			foreach (var page in RawDom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]").Cast<XmlElement>().ToArray())
			{
				if (PublishHelper.IsActivityPage(page))
					continue;
				if (PageHasImages(page))
					continue;
				if (languagesToInclude == null && PageHasVisibleText(page))
					continue;
				if (languagesToInclude != null && PageHasTextInLanguage(page, languagesToInclude))
					continue;
				if (PageHasVideo(page))
					continue;
				if (IsPageProtectedFromRemoval(page))
					continue;
				page.ParentNode.RemoveChild(page);
			}
			OrderOrNumberOfPagesChanged();
		}

		private bool IsPageProtectedFromRemoval(XmlElement pageElement)
		{
			// One final check to see if we have explicitly disallowed this page from being removed.
			// As of May 2020, this is used to protect the Afghan xmatters from having these pages removed:
			// 1) The national anthem page
			//      The only thing on this page is an image which is added by css, so PageHasImages() doesn't see it.
			// 2) The message page
			//      The only thing on this page is language-neutral text.
			//      It has to be language-neutral to make sure we don't strip it out if the language isn't part of the book.
			//      However, then PageHasTextInLanguage() won't return true.
			return HtmlDom.HasClass(pageElement, "bloom-force-publish");
		}

		private bool PageHasVisibleText(XmlElement page)
		{
			foreach (XmlElement div in page.SafeSelectNodes(".//div[contains(@class, 'bloom-visibility-code-on')]"))
			{
				if (!string.IsNullOrWhiteSpace(div.InnerText))
					return true;
			}
			return false;
		}

		// Return true if the element contains text in (a div) whose lang is one of the specified set.
		// Note that we're NOT checking visibility here...this is used in publishing modes where we may
		// want to publish a page if it has content in languages the user has said to publish, even if it has
		// none in the visible languages of this collection.
		private static bool PageHasTextInLanguage(XmlElement page, HashSet<string> languagesToLookFor)
		{
			foreach (XmlElement div in page.SafeSelectNodes(".//div[@lang]"))
			{
				if (languagesToLookFor.Contains(div.GetStringAttribute("lang")) && !string.IsNullOrWhiteSpace(div.InnerText))
					return true;
			}
			return false;
		}

		private bool PageHasImages(XmlElement page)
		{
			foreach (XmlElement img in page.SafeSelectNodes(".//img"))
			{
				if (img.Attributes["src"]?.Value != "placeHolder.png")
					return true;
			}
			foreach (XmlElement div in page.SafeSelectNodes(".//div[contains(@class, 'bloom-imageContainer')]"))
			{
				var imgUrl = HtmlDom.GetImageElementUrl(div).PathOnly.NotEncoded;
				// Actually getting a background img url is a good indication that it's one we want.
				if (!string.IsNullOrEmpty(imgUrl) && imgUrl != "placeHolder.png")
					return true;
			}
			return false;
		}

		private bool PageHasVideo(XmlElement page)
		{
			foreach (XmlElement videoSource in page.SafeSelectNodes(".//video/source"))
			{
				var src = videoSource.GetAttribute("src");
				if (!string.IsNullOrEmpty(src))
					return true;
			}
			return false;
		}

		public void SetAnimationDurationsFromAudioDurations()
		{
			foreach (XmlElement page in RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]"))
			{
				// For now we only apply this to the first image container.
				var imgContainer = page.SelectSingleNode(".//div[contains(@class, 'bloom-imageContainer') and @data-initialrect]") as XmlElement;
				if (imgContainer == null)
					continue;
				double duration = 0.0;
				foreach (XmlElement editable in page.SafeSelectNodes(
					".//div[contains(@class,'bloom-editable') and contains(@class, 'bloom-content1')]"))
				{
					foreach (XmlElement span in HtmlDom.SelectAudioSentenceElementsWithDataDuration(editable))
					{
						double time;
						double.TryParse(span.Attributes["data-duration"].Value, out time);
						duration += time;
					}
				}
				if (duration == 0.0)
					duration = 4.0; // per BL-5393, if we don't have voice durations use 4 seconds.
				imgContainer.SetAttribute("data-duration", duration.ToString());
			}
		}

		public bool HasMotionPages => HtmlDom.HasMotionFeature(OurHtmlDom.Body);

		public bool HasQuizPages => HtmlDom.HasQuizFeature(OurHtmlDom.Body);

		public bool HasComicPages => HtmlDom.HasComicFeature(OurHtmlDom.Body);

		public bool HasOnlyPictureOnlyPages()
		{
			foreach (var page in GetPages())
			{
				if (page.IsXMatter)
					continue;
				var pageDiv = page.GetDivNodeForThisPage();
				foreach (var groupDiv in pageDiv.SafeSelectNodes(".//div[contains(@class, 'bloom-translationGroup')]").Cast<XmlElement>())
				{
					var classes = groupDiv.GetAttribute("class");
					if (!classes.Contains("bloom-imageDescription"))
						return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Re-compute and update all of the metadata features for the book
		/// </summary>
		/// <param name="allowedLanguages">If non-null, limits the calculation to only considering the languages specified. Applies only to language-specific features (e.g. blind and talkingBook. Does not apply to Sign Language or language-independent faetures</param>
		internal void UpdateMetadataFeatures(
			bool isBlindEnabled, bool isTalkingBookEnabled, bool isSignLanguageEnabled,
			IEnumerable<string> allowedLanguages = null)
		{
			// Language-specific features
			UpdateBlindFeature(isBlindEnabled, allowedLanguages);
			UpdateTalkingBookFeature(isTalkingBookEnabled, allowedLanguages);

			// Sign Language is a special case - the SL videos are not marked up with lang attributes
			UpdateSignLanguageFeature(isSignLanguageEnabled);

			UpdateVideoFeature();

			// Language-independent features
			UpdateQuizFeature();
			UpdateMotionFeature();
			UpdateComicFeature();
			UpdateActivityFeature();
		}

		/// <summary>
		/// Updates the feature in bookInfo.metadata to indicate whether the book is accessible to the blind/visually impaired
		/// </summary>
		/// <param name="isEnabled">True to indicate the feature is enabled, or false for disabled (will clear the feature in the metadata)</param>
		/// <param name="allowedLanguages">If non-null, limits the calculation to only considering the languages specified</param>
		private void UpdateBlindFeature(bool isEnabled, IEnumerable<string> allowedLanguages = null)
		{
			if (!isEnabled)
			{
				BookInfo.MetaData.Feature_Blind_LangCodes = Enumerable.Empty<string>();
				return;
			}

			// Set the metadata feature that indicates a book is accessible to the blind. Our current automated
			// definition of this is the presence of image descriptions in the non-xmatter part of the book.
			// This is very imperfect. Minimally, to be accessible to the blind, it should also be a talking book
			// and everything, including image descriptions, should have audio; but talkingBook is a separate feature.
			// Also we aren't checking that EVERY image has a description. What we have is therefore too weak,
			// but EVERY image might be too strong...some may just be decorative. Then there are considerations
			// like contrast and no essential information conveyed by color and other stuff that the DAISY code
			// checks. If we were going to use this feature to actually help blind people find books they could
			// use, we might well want a control (like in upload to BL) to allow the author to specify whether
			// to claim the book is accessible to the blind. But currently this is just used for reporting the
			// feature in analytics, so it's not worth bothering the author with something that has no obvious
			// effect. If it has image descriptions, there's been at least some effort to make it accessible
			// to the blind.
			var langCodes = OurHtmlDom.GetLangCodesWithImageDescription();

			if (allowedLanguages != null)
				langCodes = langCodes.Intersect(allowedLanguages);

			BookInfo.MetaData.Feature_Blind_LangCodes = langCodes.ToList();
		}

		/// <summary>
		/// Updates the feature in bookInfo.metadata to indicate whether the book contains meaningful narration audio
		/// Narration audio in XMatter DOES count (for now?)
		/// </summary>
		/// <param name="isEnabled">True to indicate the feature is enabled, or false for disabled (will clear the feature in the metadata)</param>
		/// <param name="allowedLanguages">If non-null, limits the calculation to only considering the languages specified</param>
		private void UpdateTalkingBookFeature(bool isEnabled, IEnumerable<string> allowedLanguages = null)
		{
			if (!isEnabled)
			{
				BookInfo.MetaData.Feature_TalkingBook_LangCodes = Enumerable.Empty<string>();
				return;
			}

			var langCodes = GetRecordedAudioSentences()
				.Select(HtmlDom.GetClosestLangCode)
				.Where(HtmlDom.IsLanguageValid)
				.Distinct();

			if (allowedLanguages != null)
				langCodes = langCodes.Intersect(allowedLanguages);

			BookInfo.MetaData.Feature_TalkingBook_LangCodes = langCodes.ToList();
		}

		/// <summary>
		/// Updates the feature in bookInfo.metadata to indicate whether the book contains sign language video
		/// </summary>
		/// <param name="isEnabled">True to indicate the feature is enabled, or false for disabled (will clear the feature in the metadata)</param>
		private void UpdateSignLanguageFeature(bool isEnabled)
		{
			if (isEnabled && HasSignLanguageVideos())
				// FYI: this might be "", but that's OK. Pass it through anyway
				BookInfo.MetaData.Feature_SignLanguage_LangCodes = new string[] { this.CollectionSettings.SignLanguageIso639Code };
			else
				BookInfo.MetaData.Feature_SignLanguage_LangCodes = Enumerable.Empty<string>();
		}

		private void UpdateVideoFeature()
		{
			BookInfo.MetaData.Feature_Video = HasVideos();
		}

		/// <summary>
		/// Updates the feature in bookInfo.metadata to indicate whether the book contains quizzes
		/// </summary>
		private void UpdateQuizFeature()
		{
			// For now, our model is that quizzes are a language-independent feature.
			// If the book has a quiz and the user uploads it with an incomplete translation (quizzes not translated), so be it.
			// If we wanted to, it is also possible to compute it as a language-specific feature.
			// (That is, check if the languages in the book have non-empty text for part of the quiz section)
			BookInfo.MetaData.Feature_Quiz = CollectionSettings.HaveEnterpriseFeatures && HasQuizPages;
		}

		/// <summary>
		/// Updates the feature in bookInfo.metadata to indicate whether the book contains activities
		/// </summary>
		private void UpdateActivityFeature()
		{
			BookInfo.MetaData.Feature_Activity = Storage.GetActivityFolderNamesReferencedInBook().Any();
		}

		/// <summary>
		/// Updates the feature in bookInfo.metadata to indicate whether the book contains comic pages
		/// </summary>
		private void UpdateComicFeature()
		{
			BookInfo.MetaData.Feature_Comic = HasComicPages;
		}

		/// <summary>
		/// Updates the feature in bookInfo.metadata to indicate whether the book is a motion book
		/// </summary>
		private void UpdateMotionFeature()
		{
			BookInfo.MetaData.Feature_Motion = MotionMode;
		}

		// This is a shorthand for a whole set of features.
		// Note: we are currently planning to eventually store this primarily in the data-div, with the
		// body feature attributes present only so that CSS can base things on it. This method would then
		// be responsible to set that too...and probably that is what it should read.
		public bool MotionMode
		{
			// Review: the issue suggested that it's only true if it has all of them. Currently they all get
			// set or cleared together, so it makes no difference.
			// I don't think it's helpful to have yet another place in our code
			// that knows which six features make up MotionBookMode, so I decided to just check the most
			// characteristic one.
			get { return OurHtmlDom.BookHasFeature("fullscreenpicture", "landscape", "bloomReader"); }
			set
			{
				Action<string, string, string> addOrRemove;
					if (value) addOrRemove = (string featureName, string orientationConstraint, string mediaConstraint) =>
						OurHtmlDom.SetBookFeature(featureName, orientationConstraint, mediaConstraint);
					else addOrRemove = (string featureName, string orientationConstraint, string mediaConstraint) =>
						OurHtmlDom.ClearBookFeature(featureName, orientationConstraint, mediaConstraint);
				// Enhance: we can probably put all this in HtmlDom and have it not know about the particular features, just copy them
				// from the datadiv. That means it will need to be possible to identify them by some attribute, e.g. data-isBookFeature="true"
				// these are read by Bloom Reader (and eventually Reading App Builder?)
				addOrRemove("autoadvance", "landscape", "bloomReader");
				addOrRemove("canrotate", "allOrientations", "bloomReader");
				addOrRemove("playanimations", "landscape", "bloomReader");// could be ignoreAnimations
				addOrRemove("playmusic", "landscape", "bloomReader");
				addOrRemove("playnarration", "landscape", "bloomReader");

				// these are read by css
				//modifiedBook.OurHtmlDom.SetBookFeature("hideMargin", "landscape", "bloomReader");
				//modifiedBook.OurHtmlDom.SetBookFeature("hidePageNumbers", "landscape", "bloomReader");
				addOrRemove("fullscreenpicture", "landscape", "bloomReader");

				// Though the feature was/is getting set correctly at publish time, somehow it was getting out of
				// sync (see BL-8049). So, now we also set it here immediately when the value changes.
				BookInfo.MetaData.Feature_Motion = value;

				Save();
			}
		}

		/// <summary>
		/// BL-5886 Translation Instructions page should not end up in BR (or Epub or Pdf, but other classes ensure that).
		/// N.B. This is only intended for use on temporary files.
		/// </summary>
		public void RemoveNonPublishablePages(HashSet<string> removedLabels = null)
		{
			const string xpath = "//div[contains(@class,'bloom-noreader')]";

			var dom = OurHtmlDom.RawDom;
			var nonpublishablePages = dom.SafeSelectNodes(xpath);
			foreach (XmlNode doomedPage in nonpublishablePages)
			{
				PublishHelper.CollectPageLabel((XmlElement)doomedPage, removedLabels);
				doomedPage.ParentNode.RemoveChild(doomedPage);
			}
		}

		public static bool IsPageBloomEnterpriseOnly(XmlElement page)
		{
			var classAttrib = page.GetAttribute("class");
			return classAttrib.Contains("enterprise-only") ||
				// legacy quiz pages don't have 'enterprise-only'
			    classAttrib.Contains("questions") ||
				page.SafeSelectNodes(".//video").Count > 0 ||
				page.SafeSelectNodes(".//div[contains(@class,'bloom-widgetContainer')]").Count > 0;
		}

		public WritingSystem GetLanguage(LanguageSlot slot)
		{
			return _bookData.GetLanguage(slot);
		}

		/// <summary>
		/// Given a choice, what language should we use to display text on the page (not in the UI, which is controlled by the UI Language)
		/// </summary>
		/// <returns>A prioritized enumerable of language codes</returns>
		public IEnumerable<string> GetLanguagePrioritiesForTranslatedTextOnPage(bool includeLang1 = true)
		{
			return _bookData.GetLanguagePrioritiesForLocalizedTextOnPage(includeLang1);
		}

		/// <summary>
		/// Get a name for Language1 that is safe for using as part of a file name.
		/// (Currently used for suggesting a pdf filename when publishing.)
		/// </summary>
		/// <param name="inLanguage"></param>
		/// <returns></returns>
		public string GetFilesafeLanguage1Name(string inLanguage)
		{
			var languageName = _bookData.Language1.GetNameInLanguage(inLanguage);
			return Path.GetInvalidFileNameChars().Aggregate(
				languageName, (current, character) => current.Replace(character, ' '));
		}
}
}

