using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Xml;
using Bloom.Edit;
using Bloom.Properties;
using Bloom.Publish;
using Palaso.Code;
using Palaso.Extensions;
using Palaso.Xml;

namespace Bloom.Book
{
	public class Book
	{
		public const string ClassOfHiddenElements = "hideMe"; //"visibility:hidden !important; position:fixed  !important;";

		public delegate Book Factory(BookStorage storage, bool editable);//autofac uses this

		private readonly ITemplateFinder _templateFinder;
		private readonly Palaso.IO.IFileLocator _fileLocator;
		private readonly ProjectSettings _projectSettings;

		private  List<string> _builtInConstants = new List<string>(new[] { "-bloom-vernacularBookTitle", "-bloom-topicInNationalLanguage", "-bloom-nameOfLanguage" });
		private HtmlThumbNailer _thumbnailProvider;
		private readonly PageSelection _pageSelection;
		private readonly PageListChangedEvent _pageListChangedEvent;
		private IBookStorage _storage;
		private List<IPage> _pagesCache;

		public event EventHandler ContentsChanged;

		public enum SizeAndShapeChoice
		{
			Unknown, A5Landscape, A5Portrait, A4Landscape, A4Portrait, A3Landscape,

		}

		static private int _coverColorIndex = 0;
		private  Color[] kCoverColors= new Color[]{Color.LightCoral, Color.LightBlue, Color.LightGreen};

		public Book(IBookStorage storage, bool editable, ITemplateFinder templateFinder,
			Palaso.IO.IFileLocator fileLocator, ProjectSettings projectSettings, HtmlThumbNailer thumbnailProvider,
			PageSelection pageSelection,
			PageListChangedEvent pageListChangedEvent)
		{
			CanEdit = editable && storage.LooksOk;
			Id = Guid.NewGuid().ToString();
			CoverColor = kCoverColors[_coverColorIndex++ % kCoverColors.Length];
			_storage = storage;
			_templateFinder = templateFinder;
			_fileLocator = fileLocator;
			_projectSettings = projectSettings;

			_thumbnailProvider = thumbnailProvider;
			_pageSelection = pageSelection;
			_pageListChangedEvent = pageListChangedEvent;

			if (CanEdit)
			{
				MakeAllFieldsConsistent();
			}

			LockedExceptForTranslation = HasSourceTranslations && !_projectSettings.IsShellMakingProject;

		}


		public void InvokeContentsChanged(EventArgs e)
		{
			EventHandler handler = ContentsChanged;
			if (handler != null) handler(this, e);
		}

		public SizeAndShapeChoice SizeAndShape
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
				else return SizeAndShapeChoice.Unknown;
			}
		}

		public enum BookType { Unknown, Template, Shell, Publication }

		/// <summary>
		/// we could get the title from the <title/> element, the name of the html, or the name of the folder...
		/// for now, we're going with the folder.
		/// </summary>
		public string Title
		{
			get
			{
				if (Type == BookType.Publication)
				{
//                    var node = _storage.Dom.SelectSingleNodeHonoringDefaultNS("//textarea[contains(@class,'vernacularBookTitle')]");
//                    if (node == null)
//                        return "unknown";
//                    return (node.InnerText);
					return _storage.GetVernacularTitleFromHtml(_projectSettings.Iso639Code);
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

			XmlDocument dom = GetHtmlDomWithJustOnePage(page, _projectSettings.Iso639Code);
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
			XmlElement element = node.AppendChild(dom.CreateElement("script", "http://www.w3.org/1999/xhtml")) as XmlElement;
			element.SetAttribute("type", "text/javascript");
			element.SetAttribute("src", "file://"+ pathToJavascript);
			node.AppendChild(element);
		}

		private XmlDocument GetHtmlDomWithJustOnePage(IPage page,string iso639CodeToLeaveVisible)
		{
			var dom = new XmlDocument();
			var head = _storage.GetRelocatableCopyOfDom().SelectSingleNodeHonoringDefaultNS("/html/head").OuterXml;
			dom.LoadXml(@"<html xmlns='http://www.w3.org/1999/xhtml'>"+head+"<body></body></html>");
			var body = dom.SelectSingleNodeHonoringDefaultNS("//body");
			var pageDom = dom.ImportNode(page.GetDivNodeForThisPage(), true);
			body.AppendChild(pageDom);

				BookStorage.HideAllTextAreasThatShouldNotShow(dom, iso639CodeToLeaveVisible, Page.GetPageSelectorXPath(dom));

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

			return dom;
		}

		private static void AddSheet(XmlDocument dom, XmlNode head, string cssFilePath, bool useFullFilePath)
		{
			var link = dom.CreateElement("link", "http://www.w3.org/1999/xhtml");
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
			HideEverythingButFirstPage(bookDom);
			return bookDom;
		}


		private static void HideEverythingButFirstPage(XmlDocument bookDom)
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
		}

		private static void HidePages(XmlDocument bookDom, Func<XmlElement,bool> hidePredicate)
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
				builder.AppendFormat("<li>{0}</li>\n", HttpUtility.HtmlEncode(line));
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
			get { return CanEdit; }
		}

		public bool CanPublish
		{
			get { return CanEdit; }
		}

		public bool CanEdit  { get; private set;}

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
				string templateKey = _storage.GetTemplateKey();
				Book book=null;
				if (!string.IsNullOrEmpty(templateKey))
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
				return CanEdit ? BookType.Publication : BookType.Template; //TODO
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
			//dom.AddStyleSheet(_fileLocator.LocateFile(@"basePage.css"));

			//todo: choose a language... right now we just get the first one.
			string languageIsoToShow;

			if (Type == BookType.Shell || Type== BookType.Template)
			{
				languageIsoToShow= GetTheLanguagesUsedInTextAreasOfDom(dom).FirstOrDefault();
			}
			else
			{
				languageIsoToShow = _projectSettings.Iso639Code;
			}
			BookStorage.HideAllTextAreasThatShouldNotShow(dom, languageIsoToShow, null);

			AddCoverColor(dom, CoverColor);
			return dom;
		}

		private IEnumerable<string> GetTheLanguagesUsedInTextAreasOfDom(XmlDocument dom)
		{
			var langs = new Dictionary<string, int>();
			foreach (XmlElement element in dom.SafeSelectNodes(string.Format("//textarea[@lang]")))
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
				return !CanEdit;
			}
		}

		public bool HasSourceTranslations
		{
			get
			{
				//review
				var x = _storage.Dom.SafeSelectNodes(string.Format("//textarea[@lang and @lang!='{0}' and not(contains(@class,'-bloom-showNational'))]", _projectSettings.Iso639Code));
				return x.Count > 0;
			}

		}

		/// <summary>
		/// Is this a shell we're translating?
		/// </summary>
		public bool LockedExceptForTranslation
		{
			get; private set;
		}


		public bool HasFatalError
		{
			get { return !_storage.LooksOk; }
		}

		private void AddCoverColor(XmlDocument dom, Color coverColor)
		{

			var colorValue = string.Format("{0:X}{1:X}{2:X}", coverColor.R, coverColor.G, coverColor.B);
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
					if (string.IsNullOrEmpty(caption))
					{
						caption = "";//we aren't keeping these up to date yet as thing move around, so.... (pageNumber + 1).ToString();
					}
					_pagesCache.Add(CreatePageDecriptor(pageNode, caption, _projectSettings.Iso639Code));
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

			foreach (XmlElement pageNode in _storage.Dom.SafeSelectNodes("//div[contains(@class,'-bloom-page') and not(contains(@class, '-bloom-singleton'))]"))
			{
				var caption = pageNode.GetAttribute("title");
				var iso639CodeToShow = "";//REVIEW: should it be "en"?  what will the Lorum Ipsum's be?
				yield return CreatePageDecriptor(pageNode, caption, iso639CodeToShow);
			}
		}

		private IPage CreatePageDecriptor(XmlElement pageNode, string caption, string iso639Code)
		{
			return new Page(pageNode, caption,
					(page => _thumbnailProvider.GetThumbnail(string.Empty, page.Id, GetPreviewXmlDocumentForPage(page, iso639Code), Color.White, false)),
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
			//newPageElement.SetAttribute("id", Guid.NewGuid().ToString());

			BookStarter.SetupIdAndLineage(templatePageDiv, newPageDiv);
			BookStarter.SetupPage(newPageDiv, _projectSettings.Iso639Code);
			ClearEditableValues(newPageDiv);
			newPageDiv.RemoveAttribute("title"); //titles are just for templates [Review: that's not true for front matter pages, but at the moment you can't insert those, so this is ok]

			var elementOfPageBefore = FindPageDiv(pageBefore);
			elementOfPageBefore.ParentNode.InsertAfter(newPageDiv, elementOfPageBefore);
			_pageSelection.SelectPage(CreatePageDecriptor(newPageDiv, "should not show", _projectSettings.Iso639Code));

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
					editNode.SetAttribute("value", string.Empty);
				}
			}
			foreach (XmlElement editNode in newPageElement.SafeSelectNodes(string.Format("//textarea[@lang='{0}']", _projectSettings.Iso639Code)))
			{
				if (editNode.InnerText.ToLower().StartsWith("lorem ipsum"))
				{
					editNode.InnerText = string.Empty;
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
			Debug.Assert(CanEdit);

			string pageSelector = Page.GetPageSelectorXPath(pageDom);
			//review: does this belong down in the storage?

			XmlElement divElement = (XmlElement) pageDom.SelectSingleNodeHonoringDefaultNS("//div[contains(@class, '-bloom-page')]");
			string pageDivId = divElement.GetAttribute("id");

			foreach (XmlElement editNode in pageDom.SafeSelectNodes(pageSelector + "//img"))
			{
				var imgId = editNode.GetAttribute("id");
				var storageNode = GetStorageNode(pageDivId, "img", imgId);
				Guard.AgainstNull(storageNode, imgId);
				storageNode.SetAttribute("src", editNode.GetAttribute("src"));
			}

			foreach (XmlElement editNode in pageDom.SafeSelectNodes(pageSelector + string.Format("//input[@lang='{0}' or contains(@class,'-bloom-showNational')]", _projectSettings.Iso639Code)))
			{
				var languageCode = editNode.GetAttribute("lang");

				var inputElementId = editNode.GetAttribute("id");
				var storageNode = GetStorageNode(pageDivId, "input", inputElementId);// _storage.Dom.SelectSingleNodeHonoringDefaultNS("//input[@id='" + inputElementId + "']") as XmlElement;
				Guard.AgainstNull(storageNode,inputElementId);
				storageNode.SetAttribute("value", editNode.GetAttribute("value"));
			}


			foreach (XmlElement editNode in pageDom.SafeSelectNodes(pageSelector + string.Format("//textarea[@lang='{0}'  or contains(@class,'-bloom-showNational')]", _projectSettings.Iso639Code)))
			{
				var textareaElementId = editNode.GetAttribute("id");
				var languageCode = editNode.GetAttribute("lang");

				if (string.IsNullOrEmpty(textareaElementId))
				{
					Debug.Fail(textareaElementId);
				}
				else
				{
					var destNode = GetStorageNode(pageDivId, "textarea", textareaElementId);//_storage.Dom.SelectSingleNodeHonoringDefaultNS(pageSelector+"//textarea[@id='" + textareaElementId + "']") as XmlElement;
					Guard.AgainstNull(destNode, textareaElementId);
					//the following prevents us from double-encoding reserved characters
					destNode.InnerText = editNode.InnerText.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">");
				}
			}

			MakeAllFieldsConsistent();

			_storage.HideAllTextAreasThatShouldNotShow(_projectSettings.Iso639Code, pageSelector);

			try
			{
				_storage.Save();
			}
			catch (Exception error)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "There was a problem saving");
			}

			InvokeContentsChanged(null);//enhance: above we could detect if anything actually changed
		}


		/// <summary>
		/// Gets the first element with the given tag & id, within the page-div with the given id.
		/// </summary>
		private XmlElement GetStorageNode(string pageDivId, string tag, string elementId)
		{
			var query = string.Format("//div[@id='{0}']//{1}[@id='{2}']", pageDivId, tag, elementId);
			var matches = _storage.Dom.SafeSelectNodes(query);
			if (matches.Count != 1)
			{
				throw new ApplicationException("Expected one match for this query, but got " + matches.Count + ": " + query);
			}
			return (XmlElement)matches[0];
		}


		/// <summary>
		/// The first encountered one wins... so the rest better be read-only to the user, or they're in for some frustration!
		/// If we don't like that, we'd need to create an event to notice when field are changed.
		/// </summary>
		public void MakeAllFieldsConsistent()
		{
//            string[] fieldClasses = new string[] { "vernacularBookTitle", "natLangBookLabel", "natLangBookName" };
//
//            foreach (var name in fieldClasses)
//            {
//                string value=string.Empty;
//                foreach (XmlElement node in RawDom.SafeSelectNodes("//textarea[contains(@class,'"+name+"')]"))
//                {
//                    if (value == string.Empty)
//                        value = node.InnerText;
//                    else
//                        node.InnerText = value;
//                }
//            }
//
//            foreach (var name in fieldClasses)
//            {
//                string value = string.Empty;
//                foreach (XmlElement node in RawDom.SafeSelectNodes("//input[contains(@class,'" + name + "')]"))
//                {
//                    if (value == string.Empty)
//                        value = node.GetAttribute("value");
//                    else
//                        node.SetAttribute("value",value);
//                }
//            }

			Dictionary<string,string> classes = new Dictionary<string, string>();
			classes.Add("-bloom-nameOfLanguage", _projectSettings.LanguageName);

			MakeAllFieldsOfElementTypeConsistent(classes, "textarea");
			//nb: we intentionally go through twice, in case the value is not in the first occurrence
			MakeAllFieldsOfElementTypeConsistent(classes, "textarea");

			MakeAllFieldsOfElementTypeConsistent(classes, "p");
			MakeAllFieldsOfElementTypeConsistent(classes, "span");

			string title;
			if (classes.TryGetValue("-bloom-vernacularBookTitle", out title))
			{
				GetOrCreateElement("//html", "head");
				GetOrCreateElement("//head", "title").InnerText = title;
			}
			_storage.SetBookName(title);
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

		private void MakeAllFieldsOfElementTypeConsistent(Dictionary<string, string> classes, string elementName)
		{
			try
			{
				foreach (
					XmlElement node in
						RawDom.SafeSelectNodes(
							string.Format(
								"//{0}[(contains(@class, '_')  or contains(@class, '-bloom-')) and (@lang='{1}' or contains(@class,'-bloom-showNational'))]",
								elementName, _projectSettings.Iso639Code)))
				{
					var theseClasses = node.GetAttribute("class").Split(new char[] {' '},
																		StringSplitOptions.RemoveEmptyEntries);
					foreach (var key in theseClasses)
					{
						if (!(key.StartsWith("_") || _builtInConstants.Contains(key)))
							continue;

						if (!classes.ContainsKey(key))
						{
							if (!string.IsNullOrEmpty(node.InnerText.Trim()))
								classes.Add(key, node.InnerText.Trim());
						}
						else
							node.InnerText = classes[key];

						break; //only one variable name per item, of course.
					}
				}
			}
			catch (Exception error)
			{
				throw new ApplicationException("Error in MakeAllFieldsOfElementTypeConsistent(,"+elementName+"). RawDom was:\r\n"+RawDom.OuterXml,error);
			}
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


		public XmlDocument GetDomForPrinting(PublishModel.BookletStyleChoices bookletStyle)
		{
			var dom = GetBookDomWithStyleSheet("previewMode.css");
			//dom.LoadXml(_storage.Dom.OuterXml);

			switch (bookletStyle)
			{
				case PublishModel.BookletStyleChoices.None:
					break;
				case PublishModel.BookletStyleChoices.BookletCover:
					HidePages(dom, p=>!p.GetAttribute("class").ToLower().Contains("cover"));
					break;
				 case PublishModel.BookletStyleChoices.BookletPages:
					HidePages(dom, p=>p.GetAttribute("class").ToLower().Contains("cover"));
					break;
				default:
					throw new ArgumentOutOfRangeException("bookletStyle");
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

		public string GetPageSizeName()
		{
			var css =  BookStorage.GetPaperStyleSheetName(_storage.Dom);
			int i = css.ToLower().IndexOf("portrait");
			if(i > 0)
			{
				return css.Substring(0, i).ToUpperFirstLetter();
			}
			i = css.ToLower().IndexOf("landscape");
			if (i > 0)
			{
				return css.Substring(0, i).ToUpperFirstLetter();
			}
			throw new ApplicationException("Bloom could not determine the paper size because it could not find a stylesheet in the document which contained the words 'portrait' or 'landscape'");
		}
	}
}