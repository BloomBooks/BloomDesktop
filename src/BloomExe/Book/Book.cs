using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using System.Xml;
using Bloom.Edit;
using Bloom.Properties;
using Bloom.Publish;
using Palaso.Code;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.Text;
using Palaso.Xml;

namespace Bloom.Book
{
	public class Book
	{
		//public const string ClassOfHiddenElements = "hideMe"; //"visibility:hidden !important; position:fixed  !important;";

		public delegate Book Factory(BookStorage storage, bool projectIsEditable);//autofac uses this

		private readonly ITemplateFinder _templateFinder;
		private readonly IFileLocator _fileLocator;
		private readonly LibrarySettings _librarySettings;

		private  List<string> _builtInConstants = new List<string>(new[] { "bookTitle", "topic", "nameOfLanguage" });
		private HtmlThumbNailer _thumbnailProvider;
		private readonly PageSelection _pageSelection;
		private readonly PageListChangedEvent _pageListChangedEvent;
		private IBookStorage _storage;
		private List<IPage> _pagesCache;

		public event EventHandler ContentsChanged;

//        public enum SizeAndShapeChoice
//		{
//			Unknown, A5Landscape, A5Portrait, A4Landscape, A4Portrait, A3Landscape,USLetterPortrait,USLetterLandscape,USHalfLetterPortrait,USHalfLetterLandscape
//		}

		static private int _coverColorIndex = 0;
		private  Color[] kCoverColors= new Color[]{Color.LightCoral, Color.LightBlue, Color.LightGreen};

		public Book(IBookStorage storage, bool projectIsEditable, ITemplateFinder templateFinder,
			IFileLocator fileLocator, LibrarySettings librarySettings, HtmlThumbNailer thumbnailProvider,
			PageSelection pageSelection,
			PageListChangedEvent pageListChangedEvent)
		{
			IsInEditableLibrary = projectIsEditable && storage.LooksOk;
			Id = Guid.NewGuid().ToString();
			CoverColor = kCoverColors[_coverColorIndex++ % kCoverColors.Length];
			_storage = storage;
			_templateFinder = templateFinder;
			_fileLocator = fileLocator;
			_librarySettings = librarySettings;

			_thumbnailProvider = thumbnailProvider;
			_pageSelection = pageSelection;
			_pageListChangedEvent = pageListChangedEvent;


			//Under normal conditions, this isn't needed, because it is done when a book is first created.
			//However we're doing it again with each open, just in case the book was dragged from another
			//project, or the editing language was changed.  Under those conditions, if we didn't do this, we end up with no
			//editable items, because there are no elements in our language.
			foreach (XmlElement div in storage.Dom.SafeSelectNodes("//div[contains(@class,'-bloom-page')]"))
			{
				BookStarter.MakeVernacularElementsForPage(div,_librarySettings.VernacularIso639Code);
			}

			if (IsInEditableLibrary)
			{
				UpdateFieldsAndVariables();
				WriteLanguageDisplayStyleSheet();
				RawDom.AddStyleSheet(@"languageDisplay.css");
			}

			Guard.Against(_storage.Dom.InnerXml=="","Bloom could not parse the xhtml of this document");
			//LockedExceptForTranslation = HasSourceTranslations && !_librarySettings.IsShellLibrary;

		}


		public void InvokeContentsChanged(EventArgs e)
		{
			EventHandler handler = ContentsChanged;
			if (handler != null) handler(this, e);
		}

 /*       public SizeAndShapeChoice SizeAndShape
		{
			get
			{
				if(_storage.Dom ==null)//at least during early development, we're allowing books with no actual htm page
				{
					return SizeAndShapeChoice.Unknown;
				}
				var body = _storage.Dom.SelectSingleNodeHonoringDefaultNS("//body");
				var bodyClass = body.GetStringAttribute("class");
				if (bodyClass.Contains("a5Portrait"))
				{
					return SizeAndShapeChoice.A5Portrait;
				}
				else if (bodyClass.Contains("a5Landscape"))
				{
					return SizeAndShapeChoice.A5Landscape;
				}
				else if (bodyClass.Contains("A4Portrait"))
				{
					return SizeAndShapeChoice.A4Portrait;
				}
				else if (bodyClass.Contains("A5Landscape"))
				{
					return SizeAndShapeChoice.A5Landscape;
				}
				else if (bodyClass.Contains("A3Landscape"))
				{
					return SizeAndShapeChoice.A3Landscape;
				}
				else if (bodyClass.Contains("USLetterPortrait"))
				{
					return SizeAndShapeChoice.USLetterPortrait;
				}
				else if (bodyClass.Contains("USLetterLandscape"))
				{
					return SizeAndShapeChoice.USLetterLandscape;
				}
				else if (bodyClass.Contains("USHalfLetterPortrait"))
				{
					return SizeAndShapeChoice.USHalfLetterPortrait;
				}
				else if (bodyClass.Contains("USHalfLetterLandscape"))
				{
					return SizeAndShapeChoice.USHalfLetterLandscape;
				}

				else return SizeAndShapeChoice.Unknown;
			}
		}
		*/

		public enum BookType { Unknown, Template, Shell, Publication }

		/// <summary>
		/// we could get the title from the <title/> element, the name of the html, or the name of the folder...
		/// </summary>
		public string Title
		{
			get
			{
				if (Type == BookType.Publication)
				{
					//REVIEW: evaluate and document when we would choose the value in the html over the name of the folder.
					//1 advantage of the folder is that if you have multiple copies, the folder tells you which one you are looking at


					//                    var node = _storage.Dom.SelectSingleNodeHonoringDefaultNS("//textarea[contains(@class,'vernacularBookTitle')]");
//                    if (node == null)
//                        return "unknown";
//                    return (node.InnerText);
					var s =  _storage.GetVernacularTitleFromHtml(_librarySettings.VernacularIso639Code);
					if(String.IsNullOrEmpty(s))
						return Path.GetFileName(_storage.FolderPath);
					return s;
				}
				else //for templates and such, we can already just use the folder name
				{
					return Path.GetFileName(_storage.FolderPath);
				}
			}
		}

		public  Image GetThumbNailOfBookCover(bool drawBorderDashed)
		{
			if(HasFatalError)
			{
				return Resources.Error70x70;
			}
			Image thumb;
			if(_storage.TryGetPremadeThumbnail(out thumb))
				return thumb;

			var dom = GetPreviewXmlDocumentForFirstPage();
			if(dom == null)
			{
				return Resources.Error70x70;
			}
			string folderForCachingThumbnail = null;

			folderForCachingThumbnail = _storage.FolderPath;

			return _thumbnailProvider.GetThumbnail(folderForCachingThumbnail, _storage.Key, dom, Color.Transparent, drawBorderDashed);
		}

//        protected string PathToThumbnailCache
//        {
//            get {return Path.Combine(_storage.FolderPath, "thumbnail.") }
//
//        }

		public XmlDocument GetEditableHtmlDomForPage(IPage page)
		{
			if (!_storage.LooksOk)
			{
				return GetErrorDom();
			}

			XmlDocument dom = GetHtmlDomWithJustOnePage(page, _librarySettings.VernacularIso639Code);
			BookStorage.RemoveModeStyleSheets(dom);
			dom.AddStyleSheet(_fileLocator.LocateFile(@"basePage.css"));
			dom.AddStyleSheet(_fileLocator.LocateFile(@"editMode.css"));
			AddJavaScriptForEditing(dom);
			AddCoverColor(dom, CoverColor);
			return dom;
		}

		private void AddJavaScriptForEditing(XmlDocument dom)
		{
			XmlElement head = dom.SelectSingleNodeHonoringDefaultNS("//head") as XmlElement;
		   // AddJavascriptFile(dom, head, _fileLocator.LocateFile("jquery-1.4.4.min.js"));
			AddJavascriptFile(dom, head, _fileLocator.LocateFile("Edit-TimeScripts.js"));
		}

		private void AddJavascriptFile(XmlDocument dom, XmlElement node, string pathToJavascript)
		{
			XmlElement element = node.AppendChild(dom.CreateElement("script")) as XmlElement;
			element.SetAttribute("type", "text/javascript");
			element.SetAttribute("src", "file://"+ pathToJavascript);
			node.AppendChild(element);
		}

		private XmlDocument GetHtmlDomWithJustOnePage(IPage page,string iso639CodeToLeaveVisible)
		{
			var dom = new XmlDocument();
			var relocatableCopyOfDom = _storage.GetRelocatableCopyOfDom();
			var head = relocatableCopyOfDom.SelectSingleNodeHonoringDefaultNS("/html/head").OuterXml;
			dom.LoadXml(@"<html>"+head+"<body></body></html>");
			var body = dom.SelectSingleNodeHonoringDefaultNS("//body");
			var pageDom = dom.ImportNode(page.GetDivNodeForThisPage(), true);
			body.AppendChild(pageDom);

//                BookStorage.HideAllTextAreasThatShouldNotShow(dom, iso639CodeToLeaveVisible, Page.GetPageSelectorXPath(dom));

			return dom;
		}

		/// <summary>
		///
		/// </summary>
		/// <param name="page"></param>
		/// <param name="iso639CodeToShow">NB: this isn't always the vernacular. If we're showing template pages, it will be, um, English?</param>
		/// <returns></returns>
		public XmlDocument GetPreviewXmlDocumentForPage(IPage page, string iso639CodeToShow)
		{
			if(!_storage.LooksOk)
			{
				return GetErrorDom();
			}
			var dom = GetHtmlDomWithJustOnePage(page, iso639CodeToShow);
			dom.AddStyleSheet(_fileLocator.LocateFile(@"basePage.css"));
			dom.AddStyleSheet(_fileLocator.LocateFile(@"previewMode.css"));
			AddCoverColor(dom, CoverColor);

			//scripts kill the rendering, when the file is sitting in temp.  Don't need to waste time loading scripts anyhow
			foreach (XmlElement node in dom.SafeSelectNodes("//script"))
			{
				node.ParentNode.RemoveChild(node);
			}
			return dom;
		}

		private static void AddSheet(XmlDocument dom, XmlNode head, string cssFilePath, bool useFullFilePath)
		{
			var link = dom.CreateElement("link");
			link.SetAttribute("rel", "stylesheet");
			if (useFullFilePath)
			{
				link.SetAttribute("href", "file://" + cssFilePath);
			}
			else
			{
				link.SetAttribute("href", Path.GetFileName(cssFilePath));
			}
			link.SetAttribute("type", "text/css");
			head.AppendChild(link);
		}
		public XmlDocument GetPreviewXmlDocumentForFirstPage()
		{
			if (!_storage.LooksOk)
			{
				return null;
			}

			XmlDocument bookDom = GetBookDomWithStyleSheet("previewMode.css");

			AddCoverColor(bookDom, CoverColor);
			HideEverythingButFirstPageAndRemoveScripts(bookDom);
			return bookDom;
		}


		private static void HideEverythingButFirstPageAndRemoveScripts(XmlDocument bookDom)
		{
			bool onFirst = true;
			foreach (XmlElement node in bookDom.SafeSelectNodes("//div[contains(@class, '-bloom-page')]"))
			{
				if (!onFirst)
				{
					node.SetAttribute("style", "", "display:none");
				}
				onFirst =false;
			}
			foreach (XmlElement node in bookDom.SafeSelectNodes("//script"))
			{
				node.ParentNode.RemoveChild(node);
			}
		}

		private static void HidePages(XmlDocument bookDom, Func<XmlElement, bool> hidePredicate)
		{
			foreach (XmlElement node in bookDom.SafeSelectNodes("//div[contains(@class, '-bloom-page')]"))
			{
				if (hidePredicate(node))
				{
					node.SetAttribute("style", "", "display:none");
				}
			}
		}

		private XmlDocument GetBookDomWithStyleSheet(string cssFileName)
		{
			XmlDocument dom = (XmlDocument) _storage.GetRelocatableCopyOfDom();
			//dom.AddStyleSheet("file://"+_fileLocator.LocateFile(cssFileName));
			dom.AddStyleSheet(_fileLocator.LocateFile(cssFileName));
			return dom;
		}


		private XmlDocument GetPageListingErrorsWithBook(string contents)
		{
			var dom = new XmlDocument();
			var builder = new StringBuilder();
			builder.Append("<html><body>");
			builder.AppendLine("<p>This book ("+_storage.FolderPath+") has errors.");
			builder.AppendLine(
				"This doesn't mean your work is lost, but it does mean that something is out of date or has gone wrong, and the someone needs to find and fix the problem (and your book).</p>");

			foreach (var line in contents.Split(new []{'\n'}))
			{
				builder.AppendFormat("<li>{0}</li>\n", WebUtility.HtmlEncode(line));
			}
			builder.Append("</body></html>");
			dom.LoadXml(builder.ToString());
			return dom;
		}

		private XmlDocument GetErrorDom()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body>Something went wrong</body></html>");
			return dom;
		}


		public bool CanDelete
		{
			get { return IsInEditableLibrary; }
		}

		public bool CanPublish
		{
			get { return IsInEditableLibrary; }
		}

		/// <summary>
		/// In the Bloom app, only one collection at a time is editable; that's the library they opened. All the other collections of templates, shells, etc., are not editable.
		/// </summary>
		public bool IsInEditableLibrary  { get; private set;}

		public IPage FirstPage
		{
			get { return GetPages().First(); }
		}

		public Book TemplateBook
		{
			get
			{
				Guard.AgainstNull(_templateFinder, "_templateFinder");
				if(Type!=BookType.Publication)
					return null;
				string templateKey = _storage.GetTemplateName();
				Book book=null;
				if (!String.IsNullOrEmpty(templateKey))
				{
					book = _templateFinder.FindTemplateBook(templateKey);
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
		}

		public BookType Type
		{
			get
			{
				return IsInEditableLibrary ? BookType.Publication : BookType.Template; //TODO
				//return _storage.BookType;
			}
		}

		public XmlDocument RawDom
		{
			get {return  _storage.Dom; }
		}

		public string FolderPath
		{
			get { return _storage.FolderPath; }
		}

		public string Id { get; set; }

		public XmlDocument GetPreviewHtmlFileForWholeBook()
		{
			if (!_storage.LooksOk)
			{
				return GetPageListingErrorsWithBook(_storage.GetValidateErrors());
			}
			var dom= GetBookDomWithStyleSheet("previewMode.css");

			if (Type == BookType.Shell || Type == BookType.Template)
			{
				//now we need the template fields in that xmatter to be updated to this document, this national language, etc.
				var data = new DataSet();
				var lang = new MultiTextBase();
				lang.SetAlternative("*", "(Your Language Name)");		//we don't wan to make it confusing by putting in the actual langauge name
				data.TextVariables.Add("nameOfLanguage", lang);
				data.WritingSystemCodes.Add("V", _librarySettings.NationalLanguage1Iso639Code);//This is not an error; we don't want to use the verncular when we're just previewing a book in a non-verncaulr collection
				data.WritingSystemCodes.Add("N1", _librarySettings.NationalLanguage1Iso639Code);
				data.WritingSystemCodes.Add("N2", _librarySettings.NationalLanguage2Iso639Code);

				var helper = new XMatterHelper(dom,_librarySettings.NameOfXMatterTemplate, _fileLocator);
				helper.InjectXMatter( data);
				GatherFieldValues(data, "*", dom);
				SetFieldsValues(data, "*", dom);
			}

			AddCoverColor(dom, CoverColor);
			return dom;
		}

		private IEnumerable<string> GetTheLanguagesUsedInTextAreasOfDom(XmlDocument dom)
		{
			var langs = new Dictionary<string, int>();
			foreach (XmlElement element in dom.SafeSelectNodes(String.Format("//textarea[@lang]")))
			{
				langs[element.GetAttribute("lang").Trim()] = 1;
			}
			return langs.Keys;
		}

		public Color CoverColor { get; set; }

		public bool IsShellOrTemplate
		{
			get
			{
				//hack. Eventually we might be able to lock books so that you can't edit them.
				return !IsInEditableLibrary;
			}
		}

		public bool HasSourceTranslations
		{
			get
			{
				//review
				var x = _storage.Dom.SafeSelectNodes(String.Format("//textarea[@lang and @lang!='{0}' and not(contains(@class,'-bloom-showNational'))]", _librarySettings.VernacularIso639Code));
				return x.Count > 0;
			}

		}


		public bool NormallyHasTemplatePages
		{
			//review: my thinking here (nov 2011) is definitely fuzzy
			get
			{
				//default is "true"
				var specificallyNo = _storage.Dom.SafeSelectNodes(String.Format("//meta[@name='normallyShowTemplatePages' and @content='false']"));
				return specificallyNo.Count ==0;
			}
		}

		/// <summary>
		/// Is this a shell we're translating? And if so, is this a shell-making project?
		/// </summary>
		public bool LockedExceptForTranslation
		{
			get
			{
				return !_librarySettings.IsShellLibrary &&
					   RawDom.SafeSelectNodes("//meta[@name='editability' and @content='translationOnly']").Count > 0;
			}
		}

		public string CategoryForUsageReporting
		{
			get
			{
				if (_librarySettings.IsShellLibrary)
				{
					return "ShellEditing";
				}
				else if (LockedExceptForTranslation)
				{
					return "ShellTranslating";
				}
				else
				{
					return "CustomVernacularBook";
				}
			}
		}



		public bool HasFatalError
		{
			get { return !_storage.LooksOk; }
		}



		private void AddCoverColor(XmlDocument dom, Color coverColor)
		{

			var colorValue = String.Format("{0:X}{1:X}{2:X}", coverColor.R, coverColor.G, coverColor.B);
			var header = dom.SelectSingleNodeHonoringDefaultNS("//head");

			XmlElement colorStyle = dom.CreateElement("style");
			colorStyle.SetAttribute("type","text/css");
			colorStyle.InnerXml = @"<!--

				TEXTAREA.coverColor	{		background-color: #colorValue;	}
				DIV.-bloom-page.coverColor	{		background-color: #colorValue;	}
				-->".Replace("colorValue", colorValue);//string.format has a hard time with all those {'s

			header.AppendChild(colorStyle);
		}


		public IEnumerable<IPage> GetPages()
		{
			if (!_storage.LooksOk)
				yield break;

			if (_pagesCache == null)
			{
				_pagesCache = new List<IPage>();

				int pageNumber = 0;
				foreach (XmlElement pageNode in _storage.Dom.SafeSelectNodes("//div[contains(@class,'-bloom-page')]"))
				{
					//review: we want to show titles for template books, numbers for other books.
					//this here requires that titles be removed when the page is inserted, kind of a hack.
					var caption = pageNode.GetAttribute("title");
					if (String.IsNullOrEmpty(caption))
					{
						caption = "";//we aren't keeping these up to date yet as thing move around, so.... (pageNumber + 1).ToString();
					}
					_pagesCache.Add(CreatePageDecriptor(pageNode, caption, _librarySettings.VernacularIso639Code));
					++pageNumber;
				}
			}

			foreach (var page in _pagesCache)
			{
				yield return page;
			}
		}


		public IEnumerable<IPage> GetTemplatePages()
		{
			if (!_storage.LooksOk)
				yield break;

			foreach (XmlElement pageNode in _storage.Dom.SafeSelectNodes("//div[contains(@class,'-bloom-page') and not(contains(@data-page, 'singleton'))]"))
			{
				var caption = pageNode.GetAttribute("title");
				var iso639CodeToShow = "";//REVIEW: should it be "en"?  what will the Lorum Ipsum's be?
				yield return CreatePageDecriptor(pageNode, caption, iso639CodeToShow);
			}
		}

		private IPage CreatePageDecriptor(XmlElement pageNode, string caption, string iso639Code)
		{
			return new Page(pageNode, caption,
					(page => _thumbnailProvider.GetThumbnail(String.Empty, page.Id, GetPreviewXmlDocumentForPage(page, iso639Code), Color.White, false)),
					(page => FindPageDiv(page)));
		}

		private XmlElement FindPageDiv(IPage page)
		{
			//review: could move to page
			return _storage.Dom.SelectSingleNodeHonoringDefaultNS(page.XPathToDiv) as XmlElement;
		}

		public void InsertPageAfter(IPage pageBefore, IPage templatePage)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			ClearPagesCache();

			XmlDocument dom = _storage.Dom;
			var templatePageDiv = templatePage.GetDivNodeForThisPage();
			var newPageDiv = dom.ImportNode(templatePageDiv, true) as XmlElement;

			BookStarter.SetupIdAndLineage(templatePageDiv, newPageDiv);
			BookStarter.SetupPage(newPageDiv, _librarySettings.VernacularIso639Code);
			ClearEditableValues(newPageDiv);
			newPageDiv.RemoveAttribute("title"); //titles are just for templates [Review: that's not true for front matter pages, but at the moment you can't insert those, so this is ok]

			var elementOfPageBefore = FindPageDiv(pageBefore);
			elementOfPageBefore.ParentNode.InsertAfter(newPageDiv, elementOfPageBefore);
			_pageSelection.SelectPage(CreatePageDecriptor(newPageDiv, "should not show", _librarySettings.VernacularIso639Code));

			_storage.Save();
			if (_pageListChangedEvent != null)
				_pageListChangedEvent.Raise(null);

			InvokeContentsChanged(null);
		}



		private void ClearEditableValues(XmlElement newPageElement)
		{
			foreach (XmlElement editNode in newPageElement.SafeSelectNodes("//input"))
			{
				if (editNode.GetAttribute("value").ToLower().StartsWith("lorem ipsum"))
				{
					editNode.SetAttribute("value", String.Empty);
				}
			}
			foreach (XmlElement editNode in newPageElement.SafeSelectNodes(String.Format("//textarea[@lang='{0}']", _librarySettings.VernacularIso639Code)))
			{
				if (editNode.InnerText.ToLower().StartsWith("lorem ipsum"))
				{
					editNode.InnerText = String.Empty;
				}
			}
		}

		public void DeletePage(IPage page)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			if(GetPageCount() <2)
				return;

			ClearPagesCache();

			var pageNode = FindPageDiv(page);
		   pageNode.ParentNode.RemoveChild(pageNode);
	//        InvokePageDeleted(page);

			var prevNod = pageNode.PreviousSibling;
			if(prevNod == null)
			{
				_pageSelection.SelectPage(FirstPage);
			}
			else
			{
				_pageSelection.SelectPage(FirstPage);

				//todo       var previousPage = GetPageFromNode(pageNode);
//                _pageSelection.SelectPage(previousPage);
			}
			_storage.Save();
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
		public void SavePage(XmlDocument pageDom)
		{
			Debug.Assert(IsInEditableLibrary);

			string pageSelector = Page.GetPageSelectorXPath(pageDom);
			//review: does this belong down in the storage?

			XmlElement divElement = (XmlElement) pageDom.SelectSingleNodeHonoringDefaultNS("//div[contains(@class, '-bloom-page')]");
			string pageDivId = divElement.GetAttribute("id");

			var page = GetPageFromStorage(pageDivId);
			page.InnerXml = divElement.InnerXml;

			UpdateFieldsAndVariables();

//		    _storage.HideAllTextAreasThatShouldNotShow(_librarySettings.VernacularIso639Code, pageSelector);

			try
			{
				_storage.Save();
			}
			catch (Exception error)
			{
				ErrorReport.NotifyUserOfProblem(error, "There was a problem saving");
			}

			InvokeContentsChanged(null);//enhance: above we could detect if anything actually changed
		}


		/// <summary>
		/// Gets the first element with the given tag & id, within the page-div with the given id.
		/// </summary>
		private XmlElement GetStorageNode(string pageDivId, string tag, string elementId)
		{
			var query = String.Format("//div[@id='{0}']//{1}[@id='{2}']", pageDivId, tag, elementId);
			var matches = _storage.Dom.SafeSelectNodes(query);
			if (matches.Count != 1)
			{
				throw new ApplicationException("Expected one match for this query, but got " + matches.Count + ": " + query);
			}
			return (XmlElement)matches[0];
		}


		/// <summary>
		/// Gets the first element with the given tag & id, within the page-div with the given id.
		/// </summary>
		private XmlElement GetPageFromStorage(string pageDivId)
		{
			var query = String.Format("//div[@id='{0}']", pageDivId);
			var matches = _storage.Dom.SafeSelectNodes(query);
			if (matches.Count != 1)
			{
				throw new ApplicationException("Expected one match for this query, but got " + matches.Count + ": " + query);
			}
			return (XmlElement)matches[0];
		}

		/// <summary>
		/// Go through the document, reading in values from fields, and then pushing variable values back into fields.
		/// Here we're calling "fields" the html supplying or receiving the data, and "variables" being key-value pairs themselves, which
		/// are, for library variables, saved in a separate file.
		/// </summary>
		public void UpdateFieldsAndVariables()
		{
			var data = new DataSet();
			var lang = new MultiTextBase();
			lang.SetAlternative("*", _librarySettings.LanguageName);
			data.TextVariables.Add("nameOfLanguage", lang);
			var code = new MultiTextBase();
			code.SetAlternative("*", _librarySettings.VernacularIso639Code);
			data.TextVariables.Add("iso639Code", code);
			data.WritingSystemCodes.Add("V", _librarySettings.VernacularIso639Code);
			data.WritingSystemCodes.Add("N1", _librarySettings.NationalLanguage1Iso639Code);
			data.WritingSystemCodes.Add("N2", _librarySettings.NationalLanguage2Iso639Code);

			// The first encountered value for data-book/data-library wins... so the rest better be read-only to the user, or they're in for some frustration!
			// If we don't like that, we'd need to create an event to notice when field are changed.

			GatherFieldValues(data, "*", RawDom);
			SetFieldsValues(data, "*", RawDom);

			MultiTextBase title;
			if (data.TextVariables.TryGetValue("bookTitle", out title))
			{
				GetOrCreateElement("//html", "head");
				var t = title.GetBestAlternativeString(new string[]{_librarySettings.VernacularIso639Code});
				GetOrCreateElement("//head", "title").InnerText = t;
				_storage.SetBookName(t);
			}
			Check();
		}


		private XmlElement GetOrCreateElement(string parentPath, string name)
		{
			XmlElement element = (XmlElement)RawDom.SelectSingleNodeHonoringDefaultNS(parentPath + "/" + name);
			if (element == null)
			{
				XmlElement parent = (XmlElement)RawDom.SelectSingleNodeHonoringDefaultNS(parentPath);
				if (parent == null)
					return null;
				element = parent.OwnerDocument.CreateElement(name, parent.NamespaceURI);
				parent.AppendChild(element);
			}
			return element;
		}

		private void GatherFieldValues(DataSet data, string elementName, XmlDocument dom)
		{
			try
			{
				string query = String.Format("//{0}[(@data-book or @data-library)]", elementName);

				var nodesOfInterest = dom.SafeSelectNodes(query);

				foreach (XmlElement node in nodesOfInterest)
				{
					var key = node.GetAttribute("data-book").Trim();
					if (key == String.Empty)
						key = node.GetAttribute("data-library").Trim();

					var value = node.InnerXml.Trim();//may contain formatting
					if (!String.IsNullOrEmpty(value) && !value.StartsWith("{"))//ignore placeholder stuff like "{Book Title}"; that's not a value we want to collect
					{
						var lang = node.GetOptionalStringAttribute("lang", "*");
						if ((elementName.ToLower() == "textarea" || elementName.ToLower() == "input" || node.GetOptionalStringAttribute("contenteditable", "false") == "true") && (lang == "V" || lang == "N1" || lang == "N2"))
						{
							throw new ApplicationException("Editable element (e.g. TextArea) should not have placeholder @lang attributes (V,N1,N2): "+_storage.FileName +"\r\n\r\n"+node.OuterXml);
						}

						//if we don't have a value for this variable and this language, add it
						if (!data.TextVariables.ContainsKey(key))
						{
							var t = new MultiTextBase();
							t.SetAlternative(lang, value);
							data.TextVariables.Add(key, t);
						}
						else if (!data.TextVariables[key].ContainsAlternative(lang))
						{
							var t = data.TextVariables[key];
							t.SetAlternative(lang, value);
						}
					}
				}
			}
			catch (Exception error)
			{
				throw new ApplicationException("Error in GatherFieldValues(," + elementName + "). RawDom was:\r\n" + RawDom.OuterXml, error);
			}
		}

		private void SetFieldsValues(DataSet data, string elementName, XmlDocument dom)
		{
			Check();

			try
			{
				string query= String.Format("//{0}[(@data-book or @data-library)]", elementName);
				var nodesOfInterest = dom.SafeSelectNodes(query);

				foreach (XmlElement node in nodesOfInterest)
				{
					var key = node.GetAttribute("data-book").Trim();
					if(key==String.Empty)
						key = node.GetAttribute("data-library").Trim();
					if(!String.IsNullOrEmpty(key) && data.TextVariables.ContainsKey(key))
					{
						if (node.Name.ToLower() == "img")
						{
							node.SetAttribute("src", data.TextVariables[key].GetFirstAlternative());
						}
						else
						{
							var lang = node.GetOptionalStringAttribute("lang", "*");
							if (lang == "N1" || lang == "N2" || lang == "V")
								lang = data.WritingSystemCodes[lang];
							node.InnerXml = data.TextVariables[key].GetBestAlternativeString(new string[] { lang, "*" });//meaning, we'll take "*" if you have it but not the exact choice. * is used for languageName, at least in dec 2011
						}
					}
					else
					{
						//Review: Leave it to the ui to let them fill it in?  At the moment, we're only allowing that on textarea's. What if it's something else?
					}
				}
			}
			catch (Exception error)
			{
				throw new ApplicationException("Error in MakeAllFieldsOfElementTypeConsistent(," + elementName + "). RawDom was:\r\n" + dom.OuterXml, error);
			}
			Check();
		}

		private void Check()
		{
//    		XmlNode p = RawDom.SelectSingleNode("//p[@class='titleV']");
//    		Debug.Assert(p.InnerXml.Length < 25);
		}

		/// <summary>
		/// Move a page to somewhere else in the book
		/// </summary>
		public void RelocatePage(IPage page, int indexOfItemAfterRelocation)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			ClearPagesCache();

			var pages = _storage.Dom.SafeSelectNodes("/html/body/div");
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

			_storage.Save();
			InvokeContentsChanged(null);
		}

		public void UpdatePagePreview(IPage currentSelection)
		{
			_thumbnailProvider.PageChanged(currentSelection.Id);

			//this is for the library view, so that, so long as it asks us, we'll give it a new
			//thumbnail when it is shown again.
			if(currentSelection.Id==FirstPage.Id)
			{
				_thumbnailProvider.PageChanged(_storage.Key);
			}
		}

		public bool Delete()
		{
			return _storage.DeleteBook();
		}


		public XmlDocument GetDomForPrinting(PublishModel.BookletPortions bookletPortion)
		{
			var dom = GetBookDomWithStyleSheet("previewMode.css");
			//dom.LoadXml(_storage.Dom.OuterXml);

			switch (bookletPortion)
			{
				case PublishModel.BookletPortions.None:
					break;
				case PublishModel.BookletPortions.BookletCover:
					HidePages(dom, p=>!p.GetAttribute("class").ToLower().Contains("cover"));
					break;
				 case PublishModel.BookletPortions.BookletPages:
					HidePages(dom, p=>p.GetAttribute("class").ToLower().Contains("cover"));
					break;
				default:
					throw new ArgumentOutOfRangeException("bookletPortion");
			}
			AddCoverColor(dom, Color.White);
			return dom;
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
			if(_storage.Dom.SafeSelectNodes(String.Format("//meta[@name='defaultBookletLayout' and @content='Calendar']")).Count>0)
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
			var template = File.ReadAllText(_fileLocator.LocateFile("languageDisplayTemplate.css"));
			var path = _storage.FolderPath.CombineForPath("languageDisplay.css");
			if (File.Exists(path))
				File.Delete(path);
			File.WriteAllText(path, template.Replace("VERNACULAR", _librarySettings.VernacularIso639Code).Replace("NATIONAL", _librarySettings.NationalLanguage1Iso639Code));

			//ENHANCE: this works for editable books, but for shell collections, it would be nice to show the national language of the user... e.g., when browsing shells,
			//see the French.  But we don't want to be changing those collection folders at runtime if we can avoid it. So, this style sheet could be edited in memory, at runtime.
		}
	}
}