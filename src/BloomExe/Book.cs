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
using Palaso.IO;

namespace Bloom
{
	public class Book
	{
		public delegate Book Factory(BookStorage storage);//autofac uses this

	   // private readonly string _folderPathx;
		private readonly ITemplateFinder _templateFinder;
		private readonly IFileLocator _fileLocator;
		private HtmlThumbNailer _thumbnailProvider;
		private readonly PageSelection _pageSelection;
		private readonly PageListChangedEvent _pageListChangedEvent;
		private IBookStorage _storage;
//        public event EventHandler PageDeleted;
//        public event EventHandler PageInserted;


		public Book(IBookStorage storage, ITemplateFinder templateFinder,
			IFileLocator fileLocator, HtmlThumbNailer thumbnailProvider,
			PageSelection pageSelection,
			PageListChangedEvent pageListChangedEvent)
		{
		   // _folderPath = folderPath;
			_storage = storage;
			_templateFinder = templateFinder;
			_fileLocator = fileLocator;
			_thumbnailProvider = thumbnailProvider;
			_pageSelection = pageSelection;
			_pageListChangedEvent = pageListChangedEvent;
		}

		public enum BookType { Unknown, Template, Shell, Publication }

		public string Title
		{
			get { return _storage.Title; }
		}

		public  Image GetThumbNail()
		{
			Image thumb;
			if(_storage.TryGetPremadeThumbnail(out thumb))
				return thumb;

			var dom = GetPreviewXmlDocumentForFirstPage();
			if(dom == null)
			{
				return Resources.GenericPage32x32;
			}
			return _thumbnailProvider.GetThumbnail(_storage.Key, GetPreviewXmlDocumentForFirstPage());
		}

		public XmlDocument GetDomForPage(Page page)
		{
			return GetDomForPage(page.Id);
		}
		public XmlDocument GetDomForPage(string id)
		{
			if (!_storage.LooksOk)
			{
				return GetErrorDom();
			}

			XmlDocument dom = GetDomWithStyleSheet( "editMode.css");
			HideEverythingButCurrentPage(dom,  id);
			return dom;
		}

		public XmlDocument GetPreviewXmlDocumentForPage(string pageId)
		{
			if(!_storage.LooksOk)
			{
				return GetErrorDom();
			}

			XmlDocument dom = GetDomWithStyleSheet( "previewMode.css");
			HideEverythingButCurrentPage(dom, pageId);
			return dom;
		}

		public XmlDocument GetPreviewXmlDocumentForFirstPage()
		{
			if (!_storage.LooksOk)
			{
				return null;
			}

			XmlDocument dom = GetDomWithStyleSheet("previewMode.css");
			HideEverythingButFirstPage(dom);
			return dom;
		}




		private void HideEverythingButFirstPage(XmlDocument dom)
		{
			bool onFirst = true;
			foreach (XmlElement node in dom.SafeSelectNodes("//x:div[contains(@class, 'page')]", GetNamespaceManager(dom)))
			{
				if (!onFirst)
				{
					node.SetAttribute("style", "", "display:none");
				}
				onFirst =false;
			}
		}

		//Note here we're just hiding the other pages... we could instead just create the file
		//for the one page
		private void HideEverythingButCurrentPage(XmlDocument dom,  string pageId)
		{
			foreach (XmlElement node in dom.SafeSelectNodes("//x:div[contains(@class, 'page')]", GetNamespaceManager(dom)))
			{
				if (node.GetStringAttribute("id") != pageId)
				{
					node.SetAttribute("style", "", "display:none");
				}
			}
		}


		private XmlDocument GetDomWithStyleSheet(string cssFileName)
		{
			XmlDocument dom = (XmlDocument)_storage.Dom.Clone();
			var head = dom.SelectSingleNode("//x:head", GetNamespaceManager(dom));
			AddSheet(dom, head, cssFileName);
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


		public bool CanPublish
		{
			get { return CanEdit; }
		}

		public bool CanEdit
		{
			get {return _storage.BookType == BookType.Publication;  }
		}

		public Page FirstPage
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
				return _templateFinder.FindTemplateBook(_storage.GetTemplateKey());
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


		public XmlDocument GetPreviewHtmlFileForWholeBook()
		{
			if (!_storage.LooksOk)
			{
				return GetPageSayingCantShowBook();
			}

//            XmlNamespaceManager namespaceManager;
//            XmlDocument dom = GetDomWithStyleSheet("previewMode.css");
//
//            string tempPath = _storage.PathToHtml.Replace(".htm", "-tempPreview.htm");
//
//            using (var writer = XmlWriter.Create(tempPath))
//            {
//                dom.WriteContentTo(writer);
//                writer.Close();
//            }

			return GetDomWithStyleSheet("previewMode.css");
		}

		private void AddSheet(XmlDocument dom, XmlNode head, string cssFileName)
		{
			string path = _fileLocator.LocateFile(cssFileName);//, "stylesheet '"+cssFileName+"'");
//
//            if (string.IsNullOrEmpty(path) || !File.Exists(path))
//                return;

			var link = dom.CreateElement("link", "http://www.w3.org/1999/xhtml");
			link.SetAttribute("rel", "stylesheet");
			link.SetAttribute("href", "file://" + path);
			link.SetAttribute("type", "text/css");
			head.AppendChild(link);
		}

		public IEnumerable<Page> GetPages()
		{
			if (!_storage.LooksOk)
				yield break;

			int pageNumber = 0;
			foreach (XmlNode pageNode in _storage.Dom.SafeSelectNodes("//x:div[contains(@class,'page')]", GetNamespaceManager(_storage.Dom)))
			{
				pageNumber++;

//                var id = pageNode.GetOptionalStringAttribute("id","missing");
//                if(id=="missing")
//                    throw new ApplicationException("page divs must have id attributes");
				var dom = GetPreviewXmlDocumentForPage(id);
				yield return new Page(id, pageNumber.ToString(), (()=>_thumbnailProvider.GetThumbnail(_storage.Key+":"+id, dom)), (()=>FindPageDiv(id)));
			}

		}



		private XmlNodeList GetElementsFromFile(string path, string queryWithXForNamespace)
		{
			XmlDocument dom = new XmlDocument();
			dom.Load(path);
			 return dom.SafeSelectNodes(queryWithXForNamespace, GetNamespaceManager(dom));
		}

		private XmlNamespaceManager GetNamespaceManager(XmlDocument dom)
		{
			XmlNamespaceManager namespaceManager = new XmlNamespaceManager(dom.NameTable);
			namespaceManager.AddNamespace("x", "http://www.w3.org/1999/xhtml");
			return namespaceManager;
		}



		private XmlElement FindPageDiv(string id)
		{
			foreach (XmlElement node in _storage.Dom.SafeSelectNodes("//x:div[contains(@class, 'page')]", GetNamespaceManager(_storage.Dom)))
			{
				if (node.GetStringAttribute("id") == id)
				{
					return node;
				}
			}
			return null;
		}


		public string WriteDomToTempHtml(string stylesheetName)
		{
			string tempPath = Path.GetTempFileName() +".htm";

			XmlDocument dom = GetDomWithStyleSheet("previewMode.css");

			using (var writer = XmlWriter.Create(tempPath))
			{
				dom.WriteContentTo(writer);
				writer.Close();
			}
			return tempPath;
		}

		public void InsertPageAfter(Page selection, IPage templatePage)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			XmlDocument dom = _storage.Dom;
			var pageNode = FindPageDiv(selection.Id);
			var node = dom.ImportNode(templatePage.GetDivNodeForThisPage(), true);
			pageNode.ParentNode.InsertAfter(node, pageNode);
			_pageSelection.SelectPage(GetPageFromNode(pageNode));
			_storage.Save();
			if (_pageListChangedEvent != null)
				_pageListChangedEvent.Raise(null);
		}

		public void DeletePage(IPage page)
		{
			Guard.Against(Type != BookType.Publication, "Tried to edit a non-editable book.");

			if(GetPageCount() <2)
				return;

			var pageNode = FindPageDiv(page.Id);
		   pageNode.ParentNode.RemoveChild(pageNode);
	//        InvokePageDeleted(page);

			var prevNod = pageNode.PreviousSibling;
			if(prevNod == null)
			{
				_pageSelection.SelectPage(FirstPage);
			}
			else
			{
				var previousPage = GetPageFromNode(pageNode);
				_pageSelection.SelectPage(previousPage);
			}
			_storage.Save();
			if(_pageListChangedEvent !=null)
				_pageListChangedEvent.Raise(null);
		}

		private Page GetPageFromNode(XmlElement element)
		{
			var id = element.GetStringAttribute("id");
			return GetPages().Where(p => p.Id == id).First();
		}

		private int GetPageCount()
		{
			return GetPages().Count();
		}

//        private void InvokePageDeleted(IPage page)
//        {
//            EventHandler handler = PageDeleted;
//            if (handler != null)
//            {
//                handler(page, null);
//            }
//        }
//        private void InvokePageInserted(Page page)
//        {
//            EventHandler inserted = PageInserted;
//            if (inserted != null)
//            {
//                inserted(page, null);
//            }
//        }


		/// <summary>
		/// Earlier, we handed out a single-page version of the document. Now it has been edited,
		/// so we now we need to fold changes back in
		/// </summary>
		public void SavePage(XmlDocument pageDom)
		{
			//review: does this belong down in the storage?

			foreach (XmlElement editNode in pageDom.SafeSelectNodes("//input"))
			{
				var id = editNode.GetAttribute("id");
				var storageNode = _storage.Dom.SelectSingleNodeHonoringDefaultNS("//input[@id='"+id+"']") as XmlElement;
				Guard.AgainstNull(storageNode,id);
				storageNode.SetAttribute("value", editNode.GetAttribute("value"));
			}
			foreach (XmlElement sourceNode in pageDom.SafeSelectNodes("//textarea"))
			{
				var id = sourceNode.GetAttribute("id");
				if (string.IsNullOrEmpty(id))
				{
					Debug.Fail(id);
				}
				else
				{
					var destNode = _storage.Dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='" + id + "']") as XmlElement;
					Guard.AgainstNull(destNode, id);
					destNode.InnerText = sourceNode.InnerText;
				}
			}
			_storage.Save();
		}
	}
}