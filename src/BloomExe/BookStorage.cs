using System;
using System.Drawing;
using System.IO;
using System.Xml;

using Palaso.Code;
using Palaso.IO;
using Palaso.Xml;

namespace Bloom
{
	/* The role of this class is simply to isolate the actual storage mechanism (e.g. file system)
	 * to a single place.  All the other classes can then just pass around DOMs.
	 */


	public interface IBookStorage
	{
		XmlDocument Dom { get; }
		Book.BookType BookType { get; }
		string GetTemplateKey();
		string Key { get; }
		bool LooksOk { get; }
		string Title { get; }
		string FolderPath { get; }
		void Save();
		bool TryGetPremadeThumbnail(out Image image);
	}

	public class BookStorage : IBookStorage
	{
		private readonly string _folderPath;
		public delegate BookStorage Factory(string folderPath);//autofac uses this

		public BookStorage(string folderPath, IFileLocator fileLocator)
		{
			_folderPath = folderPath;

			RequireThat.Directory(folderPath).Exists();
			if (File.Exists(PathToHtml))
			{
				Dom = new XmlDocument();
				Dom.Load(PathToHtml);
				SetBaseForRelativePaths(folderPath);

				UpdateStyleSheetLinkPaths(fileLocator);

				//add a unique id for our use
				foreach(XmlElement node in Dom.SafeSelectNodes("/html/body/div"))
				{
					node.SetAttribute("id", Guid.NewGuid().ToString());
				}
			}
		}

		private void UpdateStyleSheetLinkPaths(IFileLocator fileLocator)
		{
			foreach(XmlElement linkNode in Dom.SafeSelectNodes("/html/head/link"))
			{
				var href = linkNode.GetAttribute("href");
				if(href==null)
				{
					continue;
				}

				var fileName = Path.GetFileName(href);
				if(!fileName.StartsWith("xx")) //I use xx  as a convenience to temporarily turn off stylesheets during development
				{
					var path = fileLocator.LocateFile(fileName, string.Format("Stylesheet '{0}', which is used in {1}", fileName, _folderPath));
					if(!string.IsNullOrEmpty(path))
					{
						linkNode.SetAttribute("href", "file://"+path);
					}
				}
			}
		}


		private void SetBaseForRelativePaths(string folderPath)
		{
			var head = Dom.SelectSingleNodeHonoringDefaultNS("//head");
			foreach (XmlNode baseNode in head.SafeSelectNodes("base"))
			{
				head.RemoveChild(baseNode);
			}
			var baseElement = Dom.CreateElement("base", "http://www.w3.org/1999/xhtml");
			baseElement.SetAttribute("href", "file://"+folderPath+Path.DirectorySeparatorChar);
			head.AppendChild(baseElement);
		}

		public XmlDocument Dom
		{
			get;
			private set;
		}

		public Book.BookType BookType
		{
			get
			{
				var pathToHtml = PathToHtml;
				if (pathToHtml.EndsWith("templatePages.htm"))
					return Book.BookType.Template;
				if (pathToHtml.EndsWith("shellPages.htm"))
					return Book.BookType.Shell;

				//directory name matches htm name
				if (!string.IsNullOrEmpty(pathToHtml) && Path.GetFileName(Path.GetDirectoryName(pathToHtml)) == Path.GetFileNameWithoutExtension(pathToHtml))
				{
					return Book.BookType.Publication;
				}
				return Book.BookType.Unknown;
			}
		}


		protected string PathToHtml
		{
			get
			{
				string p = Path.Combine(_folderPath, Path.GetFileName(_folderPath) + ".htm");
				if (File.Exists(p))
					return p;

				//template
				p = Path.Combine(_folderPath, "templatePages.htm");
				if (File.Exists(p))
					return p;

				return string.Empty;
			}
		}

		public string GetTemplateKey()
		{
			//for now, we're just using the name of the first css we find. See htmlthumnailer for code which extracts it.
			foreach (var path in Directory.GetFiles(_folderPath, "*.css"))
			{
				return Path.GetFileNameWithoutExtension(path);
			}
			return null;
		}

		public string Key
		{
			get {
				return _folderPath;
			}
		}

		public bool LooksOk
		{
			get { return File.Exists(PathToHtml); }
		}

		public string Title
		{
			get { return Path.GetFileNameWithoutExtension(_folderPath); }
		}

		public string FolderPath
		{
			get { return _folderPath; }
		}

		public void Save()
		{
			Guard.Against(BookType != Book.BookType.Publication, "Tried to save a non-editable book.");
			string tempPath = Path.GetTempFileName();
			XmlWriterSettings settings = new XmlWriterSettings();
			settings.Indent = true;
			settings.CheckCharacters = true;


			using (var writer = XmlWriter.Create(tempPath, settings))
			{
				Dom.WriteContentTo(writer);
				writer.Close();
			}
			File.Replace(tempPath, PathToHtml, PathToHtml + ".bak");
		}


		public bool TryGetPremadeThumbnail(out Image image)
		{
			string path = Path.Combine(_folderPath, "thumbnail.png");
			if(File.Exists(path))
			{
				image= Image.FromFile(path);
				return true;
			}
			image = null;
			return false;
		}


	}
}