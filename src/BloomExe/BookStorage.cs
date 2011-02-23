using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml;
using BloomTemp;
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
		string FileName { get; }
		string FolderPath { get; }
		void Save();
		bool TryGetPremadeThumbnail(out Image image);
		string GetHtmlFileForPrintingWithWkHtmlToPdf();
		XmlDocument GetRelocatableCopyOfDom();
		bool DeleteBook();
	}

	public class BookStorage : IBookStorage
	{
		private readonly string _folderPath;
		private readonly IFileLocator _fileLocator;

		public delegate BookStorage Factory(string folderPath);//autofac uses this

		public BookStorage(string folderPath, Palaso.IO.IFileLocator fileLocator)
		{
			_folderPath = folderPath;
			_fileLocator = fileLocator;

			RequireThat.Directory(folderPath).Exists();
			if (File.Exists(PathToHtml))
			{
				Dom = new XmlDocument();
				Dom.Load(PathToHtml);

				//todo: this would be better just to add to those temporary copies of it. As it is, we have to remove it for the webkit printing
				//SetBaseForRelativePaths(Dom, folderPath); //needed because the file itself may be off in the temp directory

				//UpdateStyleSheetLinkPaths(fileLocator);

				//add a unique id for our use
				foreach (XmlElement node in Dom.SafeSelectNodes("/html/body/div"))
				{
					node.SetAttribute("id", Guid.NewGuid().ToString());
				}
			}
		}

		private void UpdateStyleSheetLinkPaths(XmlDocument dom, Palaso.IO.IFileLocator fileLocator)
		{
			foreach (XmlElement linkNode in dom.SafeSelectNodes("/html/head/link"))
			{
				var href = linkNode.GetAttribute("href");
				if (href == null)
				{
					continue;
				}

				var fileName = Path.GetFileName(href);
				if (!fileName.StartsWith("xx")) //I use xx  as a convenience to temporarily turn off stylesheets during development
				{
					var path = fileLocator.LocateOptionalFile(fileName);
					if (string.IsNullOrEmpty(path))
					{
						//look in the same directory as the book
						var local = Path.Combine(_folderPath, fileName);
						if (File.Exists(local))
							path = local;
					}
					if (!string.IsNullOrEmpty(path))
					{
						linkNode.SetAttribute("href", "file://" + path);
					}
					else
					{
						Palaso.Reporting.ErrorReport.NotifyUserOfProblem(string.Format("Bloom could not find the stylesheet '{0}', which is used in {1}", fileName, _folderPath));
					}
				}
			}
		}

		/// <summary>
		/// the wkhtmltopdf thingy can't find stuff if we have any "file://" references (used for getting to pdf)
		/// </summary>
		/// <param name="dom"></param>
		private void StripStyleSheetLinkPaths(XmlDocument dom)
		{
			foreach (XmlElement linkNode in dom.SafeSelectNodes("/html/head/link"))
			{
				var href = linkNode.GetAttribute("href");
				if (href == null)
				{
					continue;
				}
				linkNode.SetAttribute("href", Path.GetFileName(href));
			}
		}

		//while in Bloom, we could have and edit style sheet or (someday) other modes. But when stored,
		//we want to make sure it's ready to be opened in a browser.
		private void MakeCssLinksAppropriateForStoredFile()
		{
			RemoveModeStyleSheets(Dom);
			//not needed. Dom.AddStyleSheet("previewMode.css");
		}

		public static void RemoveModeStyleSheets(XmlDocument dom)
		{
			foreach (XmlElement linkNode in dom.SafeSelectNodes("/html/head/link"))
			{
				var href = linkNode.GetAttribute("href");
				if (href == null)
				{
					continue;
				}

				var fileName = Path.GetFileName(href);
				if (fileName.Contains("edit") || fileName.Contains("preview"))
				{
					linkNode.ParentNode.RemoveChild(linkNode);
				}
			}
		}


		public static void SetBaseForRelativePaths(XmlDocument dom, string folderPath)
		{
			var head = dom.SelectSingleNodeHonoringDefaultNS("//head");
			foreach (XmlNode baseNode in head.SafeSelectNodes("base"))
			{
				head.RemoveChild(baseNode);
			}
			if (!string.IsNullOrEmpty(folderPath))
			{
				var baseElement = dom.CreateElement("base", "http://www.w3.org/1999/xhtml");
				baseElement.SetAttribute("href", "file://" + folderPath + Path.DirectorySeparatorChar);
				head.AppendChild(baseElement);
			}
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

				//ok, so maybe they changed the name of the folder and not the htm. Can we find a *single* html doc?
				var candidates = Directory.GetFiles(_folderPath, "*.htm");
				if (candidates.Length == 1)
					return candidates[0];

				//template
				p = Path.Combine(_folderPath, "templatePages.htm");
				if (File.Exists(p))
					return p;

				return string.Empty;
			}
		}

		public string GetTemplateKey()
		{
			//TODO it's not clear what we want to do, eventually.
			//for now, we're just using the name of the first css we find. See htmlthumnailer for code which extracts it.
			foreach (var path in Directory.GetFiles(_folderPath, "*.css"))
			{
				return Path.GetFileNameWithoutExtension(path);
			}
			return null;
		}

		public string Key
		{
			get
			{
				return _folderPath;
			}
		}

		public bool LooksOk
		{
			get { return File.Exists(PathToHtml); }
		}

		public string FileName
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
			MakeCssLinksAppropriateForStoredFile();
			SetBaseForRelativePaths(Dom, string.Empty);// remove any dependency on this computer, and where files are on it.

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
			if (File.Exists(path))
			{
				image = Image.FromFile(path);
				return true;
			}
			image = null;
			return false;
		}

		/// <summary>
		/// Get a path to a file that has whatever it needs (in terms of the links, base, etc) to
		/// come out right for WkHtmlToPdf.  It turns out that this means no file:\\, so no <base>,
		/// etc.  Which is fine as long as the way we save the file is without this stuff, as
		/// it really should be, because that way you can take it to another location or computer
		/// and it will still look right in a browser.
		/// </summary>
		public string GetHtmlFileForPrintingWithWkHtmlToPdf()
		{
			return PathToHtml;
		}

		public XmlDocument GetRelocatableCopyOfDom()
		{
			XmlDocument dom = (XmlDocument)Dom.Clone();

			SetBaseForRelativePaths(dom, _folderPath);
			UpdateStyleSheetLinkPaths(dom, _fileLocator);
			return dom;
		}

		public bool DeleteBook()
		{
			try
			{
				#if MONO
					return false;//TODO implement the appropriate thing in Linux
				#else

				//moves it to the recyle bin
				var shf = new SHFILEOPSTRUCT();
				shf.wFunc = FO_DELETE;
				shf.fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION ;
				string pathWith2Nulls = _folderPath + "\0\0";
				shf.pFrom = pathWith2Nulls;

				SHFileOperation(ref shf);
				return !shf.fAnyOperationsAborted;
				#endif
			}
			catch (Exception exception)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(exception, "Could not delete that book.");
				return false;
			}
		}

		#if !MONO
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto, Pack = 1)]
		public struct SHFILEOPSTRUCT
		{
			public IntPtr hwnd;
			[MarshalAs(UnmanagedType.U4)]
			public int wFunc;
			public string pFrom;
			public string pTo;
			public short fFlags;
			[MarshalAs(UnmanagedType.Bool)]
			public bool fAnyOperationsAborted;
			public IntPtr hNameMappings;
			public string lpszProgressTitle;
		}

		[DllImport("shell32.dll", CharSet = CharSet.Auto)]
		public static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

		public const int FO_DELETE = 3;
		public const int FOF_ALLOWUNDO = 0x40;
		public const int FOF_NOCONFIRMATION = 0x10; // Don't prompt the user
		public const int FOF_SIMPLEPROGRESS = 0x0100;
		#endif
	}
}