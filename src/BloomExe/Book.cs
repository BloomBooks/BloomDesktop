using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Bloom.Properties;
using Palaso.IO;

namespace Bloom
{
	public class Book
	{
		public delegate Book Factory(string path);//autofac uses this

		private readonly string _path;
		private readonly IFileLocator _fileLocator;
		private HtmlThumbNailer _thumbnailProvider;
		private XmlNamespaceManager _namespaceManager;

		public Book(string path, IFileLocator fileLocator, HtmlThumbNailer thumbnailProvider)
		{
			_path = path;
			_fileLocator = fileLocator;
			_thumbnailProvider = thumbnailProvider;
		}


		public string Title
		{
			get { return Path.GetFileNameWithoutExtension(_path); }
		}

		public  Image GetThumbNail()
		{
//            var existing = Path.Combine(_path, "thumbnail.png");
//            if(!File.Exists(existing))
//                return null;
//
//            try
//            {
//                return Image.FromFile(existing);
//            }
//            catch (Exception)
//            {
//                return null;
//            }
			var path = GetPreviewHtmlFileForFirstPage();
			if(string.IsNullOrEmpty(path))
			{
				return Resources.GenericPage32x32;
			}
			return _thumbnailProvider.GetThumbnail(_path, path);
		}

		public string GetEditableHtmlFileForPage(Page page)
		{
			return GetEditableHtmlFileForPage(page.Id);
		}
		public string GetEditableHtmlFileForPage(string id)
		{

			if (!File.Exists(PathToHtml))
			{
				return GetPageSayingCantShowBook();
			}

			XmlDocument dom = GetDomWithStyleSheet( "editMode.css");
			HideEverythingButCurrentPage(dom,  id);
			AddJavaScriptForEditing(dom);
			return WriteDomToTempHtml(dom, "-tempEdit.htm");
		}

		public string GetPreviewHtmlFileForPage(string pageId)
		{
			if(!File.Exists(PathToHtml))
			{
				return string.Empty;
			}

			XmlDocument dom = GetDomWithStyleSheet( "previewMode.css");
			HideEverythingButCurrentPage(dom, pageId);
			return WriteDomToTempHtml(dom, "-tempPreview.htm");
		}

		public string GetPreviewHtmlFileForFirstPage()
		{
			if (!File.Exists(PathToHtml))
			{
				return null;
			}

			XmlDocument dom = GetDomWithStyleSheet("previewMode.css");
			HideEverythingButFirstPage(dom);
			return WriteDomToTempHtml(dom, "-tempPreview.htm");
		}

		private string WriteDomToTempHtml(XmlDocument dom, string suffix)
		{
			string tempPath = PathToHtml.Replace(".htm", suffix);

			using (var writer = XmlWriter.Create(tempPath))
			{
				dom.WriteContentTo(writer);
				writer.Close();
			}
			return tempPath;
		}

		private void HideEverythingButFirstPage(XmlDocument dom)
		{
			bool onFirst = true;
			foreach (XmlElement node in dom.SafeSelectNodes("//x:div[contains(@class, 'page')]", _namespaceManager))
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
			foreach (XmlElement node in dom.SafeSelectNodes("//x:div[contains(@class, 'page')]", _namespaceManager))
			{
				if (node.GetStringAttribute("id") != pageId)
				{
					node.SetAttribute("style", "", "display:none");
				}
			}
		}

		private void AddJavaScriptForEditing(XmlDocument dom)
		{
			foreach (XmlElement node in dom.SafeSelectNodes("//x:input", _namespaceManager))
			{
				node.SetAttribute("onblur", "", "this.setAttribute('newValue',this.value);");
			}
			foreach (XmlElement node in dom.SafeSelectNodes("//x:textarea", _namespaceManager))
			{
				node.SetAttribute("onblur", "", "this.setAttribute('newValue',this.value);");
			}
		}

		private XmlDocument GetDomWithStyleSheet(string cssFileName)
		{
			XmlDocument dom = new XmlDocument();
			dom.Load(PathToHtml);
			_namespaceManager = new XmlNamespaceManager(dom.NameTable);
			_namespaceManager.AddNamespace("x", "http://www.w3.org/1999/xhtml");

			var head = dom.SelectSingleNode("//x:head", _namespaceManager);

			AddSheet(dom, head, cssFileName);
			return dom;
		}

		private string GetPageSayingCantShowBook()
		{
			string tempNoticePath = Path.GetTempFileName();
			using (var stream = File.CreateText(tempNoticePath))
			{
				stream.WriteLine("<html><body>Could not display that book.</body></html>");
			}
			return tempNoticePath;
		}

		protected string PathToHtml
		{
			get
			{
				string p = Path.Combine(_path, Path.GetFileName(_path)+".htm");
				if(File.Exists(p))
					return p;

				//template
				p = Path.Combine(_path, "templatePages.htm");
				if (File.Exists(p))
					return p;

				return string.Empty;
			}
		}

		public enum BookType {Unknown, Template, Shell, Publication}
		public BookType Type {
			get
			{
				var pathToHtml = PathToHtml;
				if (pathToHtml.EndsWith("templatePages.htm"))
					return BookType.Template;
				if (pathToHtml.EndsWith("shellPages.htm"))
					return BookType.Shell;

				//directory name matches htm name
				if (!string.IsNullOrEmpty(pathToHtml) && Path.GetFileName(Path.GetDirectoryName(pathToHtml)) == Path.GetFileNameWithoutExtension(pathToHtml))
				{
					return BookType.Publication;
				}
				return BookType.Unknown;
			}
		}

		public bool CanPublish
		{
			get { return CanEdit; }
		}

		public bool CanEdit
		{
			get {return Type == Book.BookType.Publication;  }
		}

		public Page FirstPage
		{
			get { return GetPages().First(); }
		}

		public string GetPreviewHtmlFileForWholeBook()
		{
			if (!File.Exists(PathToHtml))
			{
				return GetPageSayingCantShowBook();
			}

			XmlNamespaceManager namespaceManager;
			XmlDocument dom = GetDomWithStyleSheet("previewMode.css");

			string tempPath = PathToHtml.Replace(".htm", "-tempPreview.htm");

			using (var writer = XmlWriter.Create(tempPath))
			{
				dom.WriteContentTo(writer);
				writer.Close();
			}
			return tempPath;
		}

		private void AddSheet(XmlDocument dom, XmlNode head, string cssFileName)
		{
			string path = _fileLocator.LocateFile(cssFileName, "stylesheet");

			if (string.IsNullOrEmpty(path) || !File.Exists(path))
				return;

			var link = dom.CreateElement("link", "http://www.w3.org/1999/xhtml");
			link.SetAttribute("rel", "stylesheet");
			link.SetAttribute("href", "file://" + path);
			link.SetAttribute("type", "text/css");
			head.AppendChild(link);
		}

		public IEnumerable<Page> GetPages()
		{
			int pageNumber = 0;
			foreach (XmlNode page in GetElementsFromFile(PathToHtml, "//x:div[contains(@class,'page')]"))
			{
				pageNumber++;
				var id = page.GetStringAttribute("id");
				var htmlPath = GetPreviewHtmlFileForPage(id);
				yield return new Page(id, pageNumber.ToString(), _thumbnailProvider.GetThumbnail(_path+":"+id, htmlPath));
			}

		}



		private XmlNodeList GetElementsFromFile(string path, string queryWithXForNamespace)
		{
			XmlDocument dom = new XmlDocument();
			dom.Load(path);
			XmlNamespaceManager namespaceManager = new XmlNamespaceManager(dom.NameTable);
			namespaceManager.AddNamespace("x", "http://www.w3.org/1999/xhtml");
			return dom.SafeSelectNodes(queryWithXForNamespace, namespaceManager);
		}
	}

	public class Page
	{
		public Page(string id, string caption, Image thumbnail)
		{
			Id = id;
			Caption = caption;
			Thumbnail = thumbnail;
		}


		public string Id {get;private set;}
		public string Caption { get; private set; }
		public Image Thumbnail { get; private set; }
	}
}