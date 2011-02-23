using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Edit;
using Bloom.Properties;
using Palaso.Code;
using Palaso.Xml;

namespace Bloom
{
	public class Book
	{


		public delegate Book Factory(BookStorage storage, bool editable);//autofac uses this

		private readonly ITemplateFinder _templateFinder;
		private readonly Palaso.IO.IFileLocator _fileLocator;
		private HtmlThumbNailer _thumbnailProvider;
		private readonly PageSelection _pageSelection;
		private readonly PageListChangedEvent _pageListChangedEvent;
		private IBookStorage _storage;

		public event EventHandler ContentsChanged;

		public enum SizeAndShapeChoice
		{
			Unknown, A5Landscape, A5Portrait, A4Landscape, A4Portrait, A3Landscape,

		}

		static private int _coverColorIndex = 0;
		private  Color[] kCoverColors= new Color[]{Color.LightCoral, Color.LightBlue, Color.LightGreen};

		public Book(IBookStorage storage, bool editable, ITemplateFinder templateFinder,
			Palaso.IO.IFileLocator fileLocator, HtmlThumbNailer thumbnailProvider,
			PageSelection pageSelection,
			PageListChangedEvent pageListChangedEvent)
		{
			CanEdit = editable;
			Id = Guid.NewGuid().ToString();
			CoverColor = kCoverColors[_coverColorIndex++ % kCoverColors.Length];
			_storage = storage;
			_templateFinder = templateFinder;
			_fileLocator = fileLocator;
			_thumbnailProvider = thumbnailProvider;
			_pageSelection = pageSelection;
			_pageListChangedEvent = pageListChangedEvent;
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

		public string Title
		{
			get
			{
				//TODO: we need to rename the books when the title changes; until then, we use this:
				if (Type == BookType.Publication)
				{
					var node = _storage.Dom.SelectSingleNodeHonoringDefaultNS("//textarea[contains(@class,'vernacularBookTitle')]");
					if (node == null)
						return "unknown";
					return (node.InnerText);
				}
				else //for templates and such, we can already just use the folder name
				{
					return Path.GetFileName(_storage.FolderPath);
				}
			}
		}

		public  Image GetThumbNailOfBookCover(bool drawBorder)
		{
			Image thumb;
			if(_storage.TryGetPremadeThumbnail(out thumb))
				return thumb;

			var dom = GetPreviewXmlDocumentForFirstPage();
			if(dom == null)
			{
				return Resources.GenericPage32x32;//todo: make an error icon
			}
			string folderForCachingThumbnail = null;

			//eventually, we need to cache the thumbnails of vernacular books, too. But then we need
			//to refresh them when the cover image should change.  Until then, only cache shells/templates
			if(this.IsShellOrTemplate)
			{
				folderForCachingThumbnail = _storage.FolderPath;
			}
			return _thumbnailProvider.GetThumbnail(folderForCachingThumbnail, _storage.Key, dom, Color.Transparent, drawBorder);
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

			XmlDocument dom = GetHtmlDomWithJustOnePage(page);
			BookStorage.RemoveModeStyleSheets(dom);
			dom.AddStyleSheet(_fileLocator.LocateFile(@"editMode.css"));
			AddCoverColor(dom);
			return dom;
		}

		private XmlDocument GetHtmlDomWithJustOnePage(IPage page)
		{
			var dom = new XmlDocument();
			var head = _storage.GetRelocatableCopyOfDom().SelectSingleNodeHonoringDefaultNS("/html/head").OuterXml;
			dom.LoadXml(@"<html xmlns='http://www.w3.org/1999/xhtml'>"+head+"<body></body></html>");
			var body = dom.SelectSingleNodeHonoringDefaultNS("//body");
			var pageDom = dom.ImportNode(page.GetDivNodeForThisPage(), true);
			body.AppendChild(pageDom);
			return dom;
		}

		public XmlDocument GetPreviewXmlDocumentForPage(IPage page)
		{
			if(!_storage.LooksOk)
			{
				return GetErrorDom();
			}
			var dom = GetHtmlDomWithJustOnePage(page);
			dom.AddStyleSheet(_fileLocator.LocateFile(@"previewMode.css"));
			AddCoverColor(dom);
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

			AddCoverColor(bookDom);
			HideEverythingButFirstPage(bookDom);
			return bookDom;
		}


		private static void HideEverythingButFirstPage(XmlDocument bookDom)
		{
			bool onFirst = true;
			foreach (XmlElement node in bookDom.SafeSelectNodes("//div[contains(@class, 'page')]"))
			{
				if (!onFirst)
				{
					node.SetAttribute("style", "", "display:none");
				}
				onFirst =false;
			}
		}


		private XmlDocument GetBookDomWithStyleSheet(string cssFileName)
		{
			XmlDocument dom = (XmlDocument) _storage.GetRelocatableCopyOfDom();
			dom.AddStyleSheet(_fileLocator.LocateFile(cssFileName));
			return dom;
		}


		private XmlDocument GetPageSayingCantShowBook()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body>Could not display that book.</body></html>");
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
				if(_storage.BookType!=BookType.Publication)
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
			get { return _storage.BookType; }
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
				return GetPageSayingCantShowBook();
			}
			var dom= GetBookDomWithStyleSheet("previewMode.css");

			AddCoverColor(dom);
			return dom;
		}

		public string GetHtmlFileForPrintingWholeBook()
		{
//            if (!_storage.LooksOk)
//            {
//                return GetPageSayingCantShowBook();
//            }
			//var dom = GetBookDomWithStyleSheet("previewMode.css");
			return _storage.GetHtmlFileForPrintingWithWkHtmlToPdf();
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

		private void AddCoverColor(XmlDocument dom)
		{

			var colorValue = string.Format("{0:X}{1:X}{2:X}", CoverColor.R, CoverColor.G, CoverColor.B);
			var header = dom.SelectSingleNodeHonoringDefaultNS("//head");

			XmlElement colorStyle = dom.CreateElement("style");
			colorStyle.SetAttribute("type","text/css");
			colorStyle.InnerXml = @"<!--
				DIV.page.cover	{		background-color: #colorValue;	}
				TEXTAREA.coverColor	{		background-color: #colorValue;	}
				INPUT.coverColor	{		background-color: #colorValue;	}
				-->".Replace("colorValue", colorValue);//string.format has a hard time with all those {'s

			header.AppendChild(colorStyle);
		}


		public IEnumerable<IPage> GetPages()
		{
			if (!_storage.LooksOk)
				yield break;

			int pageNumber = 0;
			foreach (XmlElement pageNode in _storage.Dom.SafeSelectNodes("//div[contains(@class,'page')]"))
			{
				//review: we want to show titles for template books, numbers for other books.
				//this here requires that titles be removed when the page is inserted, kind of a hack.
				var caption = pageNode.GetAttribute("title");
				if(string.IsNullOrEmpty(caption))
				{
					caption = (pageNumber + 1).ToString();
				}
				yield return CreatePageDecriptor(pageNode, caption);
				++pageNumber;
			}
		}

		private IPage CreatePageDecriptor(XmlElement pageNode, string caption)
		{
			return new Page(pageNode, caption,
					(page => _thumbnailProvider.GetThumbnail(string.Empty, page.Id, GetPreviewXmlDocumentForPage(page), Color.White, false)),
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

			XmlDocument dom = _storage.Dom;
			var newPageElement = dom.ImportNode(templatePage.GetDivNodeForThisPage(), true) as XmlElement;
			newPageElement.SetAttribute("id", Guid.NewGuid().ToString());
			ClearEditableValues(newPageElement);
			newPageElement.RemoveAttribute("title"); //titles are just for templates

			var elementOfPageBefore = FindPageDiv(pageBefore);
			elementOfPageBefore.ParentNode.InsertAfter(newPageElement, elementOfPageBefore);
			_pageSelection.SelectPage(CreatePageDecriptor(newPageElement, "should not show"));
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
			foreach (XmlElement editNode in newPageElement.SafeSelectNodes("//textarea"))
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
			string pageSelector = Page.GetPageSelectorXPath(pageDom);
			//review: does this belong down in the storage?

			XmlElement divElement = (XmlElement) pageDom.SelectSingleNodeHonoringDefaultNS("//div[contains(@class, 'page')]");
			string pageDivId = divElement.GetAttribute("id");

			foreach (XmlElement editNode in pageDom.SafeSelectNodes(pageSelector + "//img"))
			{
				var imgId = editNode.GetAttribute("id");
				var storageNode = GetStorageNode(pageDivId, "img", imgId);
				Guard.AgainstNull(storageNode, imgId);
				storageNode.SetAttribute("src", editNode.GetAttribute("src"));
			}

			foreach (XmlElement editNode in pageDom.SafeSelectNodes(pageSelector + "//input"))
			{
				var inputElementId = editNode.GetAttribute("id");
				var storageNode = GetStorageNode(pageDivId, "input", inputElementId);// _storage.Dom.SelectSingleNodeHonoringDefaultNS("//input[@id='" + inputElementId + "']") as XmlElement;
				Guard.AgainstNull(storageNode,inputElementId);
				storageNode.SetAttribute("value", editNode.GetAttribute("value"));
			}
			foreach (XmlElement editNode in pageDom.SafeSelectNodes("//textarea"))
			{
				var textareaElementId = editNode.GetAttribute("id");
				if (string.IsNullOrEmpty(textareaElementId))
				{
					Debug.Fail(textareaElementId);
				}
				else
				{
					var destNode = GetStorageNode(pageDivId, "textarea", textareaElementId);//_storage.Dom.SelectSingleNodeHonoringDefaultNS(pageSelector+"//textarea[@id='" + textareaElementId + "']") as XmlElement;
					Guard.AgainstNull(destNode, textareaElementId);
					destNode.InnerText = editNode.InnerText;
				}
			}

			MakeAllFieldsConsistent();
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
		private XmlElement GetStorageNode(string pageDivId, string tag, string imgId)
		{
			var query = string.Format("//div[@id='{0}']//{1}[@id='{2}']", pageDivId, tag, imgId);
			var matches = _storage.Dom.SafeSelectNodes(query);
			if(matches.Count != 1)
			{
				throw new ApplicationException("Expected one match for this query, but got "+matches.Count+": "+query);
			}
			return (XmlElement) matches[0];
//            return _storage.Dom.SelectSingleNodeHonoringDefaultNS(string.Format("//div[@id='{0}']//{1}[@id='{2}']", pageDivId, tag, imgId)) as XmlElement;
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
			//can't use starts-with becuase it could be the second or third word in the class attribute
			foreach (XmlElement node in RawDom.SafeSelectNodes("//input[contains(@class, '_')]"))
			{
				var theseClasses = node.GetAttribute("class").Split(new char[] {' '},StringSplitOptions.RemoveEmptyEntries);
				foreach (var key in theseClasses)
				{
					if (!key.StartsWith("_"))
						continue;

					if(!classes.ContainsKey(key))
						classes.Add(key, node.GetAttribute("value"));
					else
						node.SetAttribute("value", classes[key]);

					break;//only one variable name per item, of course.
				}
			}

			foreach (XmlElement node in RawDom.SafeSelectNodes("//textarea[contains(@class, '_')]"))
			{
				var theseClasses = node.GetAttribute("class").Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (var key in theseClasses)
				{
					if (!key.StartsWith("_"))
						continue;

					if (!classes.ContainsKey(key))
						classes.Add(key, node.InnerText);
					else
						node.InnerText= classes[key];

					break;//only one variable name per item, of course.
				}
			}
		}

		/// <summary>
		/// Move a page to somewhere else in the book
		/// </summary>
		public void RelocatePage(IPage page, int indexOfItemAfterRelocation)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

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
	}
}