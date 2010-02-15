using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Palaso.IO;

namespace Bloom
{
	public class Book
	{
		public delegate Book Factory(string path);//autofac uses this

		private readonly string _path;
		private readonly IFileLocator _fileLocator;
		private string _currentPageId;

		public Book(string path, IFileLocator fileLocator)
		{
			_path = path;
			_fileLocator = fileLocator;
		}

		public string Title
		{
			get { return Path.GetFileNameWithoutExtension(_path); }
		}

		public  Image GetThumbNail()
		{
			var existing = Path.Combine(_path, "thumbnail.png");
			if(!File.Exists(existing))
				return null;

			try
			{
				return Image.FromFile(existing);
			}
			catch (Exception)
			{
				return null;
			}
		}

		public void ShowPage(string id)
		{
			_currentPageId = id;
		}

		public string GetHtmlFileForCurrentPage()
		{
			if(!File.Exists(PathToHtml))
			{
				return GetPageSayingCantShowBook();
			}

			XmlNamespaceManager namespaceManager;
			XmlDocument dom = GetDomWithStyleSheet(out namespaceManager, "editMode.css");

			foreach (XmlElement node in dom.SafeSelectNodes("//x:input", namespaceManager))
			{
				node.SetAttribute("onblur", "", "this.setAttribute('newValue',this.value);");
			}
			foreach (XmlElement node in dom.SafeSelectNodes("//x:textarea", namespaceManager))
			{
				node.SetAttribute("onblur", "", "this.setAttribute('newValue',this.value);");
			}

			//Note here we're just hiding the other pages... we could instead just create the file
			//for the one page
			foreach (XmlElement node in dom.SafeSelectNodes("//x:div[contains(@class, 'page')]", namespaceManager))
			{
				if (string.IsNullOrEmpty(_currentPageId))
				{
					_currentPageId = node.GetStringAttribute("id");
				}
				if (node.GetStringAttribute("id") != _currentPageId)
				{
					node.SetAttribute("style", "", "display:none");
				}
			}
			string tempPath = PathToHtml.Replace(".htm", "-tempEdit.htm");// Path.GetTempFileName() + ".htm";

			using (var writer = XmlWriter.Create(tempPath))
			{
				dom.WriteContentTo(writer);
				writer.Close();
			}
			return tempPath;
		}

		private XmlDocument GetDomWithStyleSheet(out XmlNamespaceManager namespaceManager, string cssFileName)
		{
			XmlDocument dom = new XmlDocument();
			dom.Load(PathToHtml);
			namespaceManager = new XmlNamespaceManager(dom.NameTable);
			namespaceManager.AddNamespace("x", "http://www.w3.org/1999/xhtml");
			var head = dom.SelectSingleNode("//x:head", namespaceManager);

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

		public string GetPreviewHtmlFileForWholeBook()
		{
			if (!File.Exists(PathToHtml))
			{
				return GetPageSayingCantShowBook();
			}

			XmlNamespaceManager namespaceManager;
			XmlDocument dom = GetDomWithStyleSheet(out namespaceManager, "previewMode.css");

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
	}


}