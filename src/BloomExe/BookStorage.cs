using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml;
using Bloom.Publish;
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
		string PathToExistingHtml { get; }
		void Save();
		bool TryGetPremadeThumbnail(out Image image);
		XmlDocument GetRelocatableCopyOfDom();
		bool DeleteBook();
		void HideAllTextAreasThatShouldNotShow(string vernacularIso639Code, string optionalPageSelector);
		string SaveHtml(XmlDocument bookDom);
		void SetBookName(string name);
	}

	public class BookStorage : IBookStorage
	{
		private  string _folderPath;
		private readonly IFileLocator _fileLocator;

		public delegate BookStorage Factory(string folderPath);//autofac uses this

		public BookStorage(string folderPath, Palaso.IO.IFileLocator fileLocator)
		{
			_folderPath = folderPath;
			_fileLocator = fileLocator;

			RequireThat.Directory(folderPath).Exists();
			if (File.Exists(PathToExistingHtml))
			{
				Dom = new XmlDocument();
				Dom.Load(PathToExistingHtml);

				//todo: this would be better just to add to those temporary copies of it. As it is, we have to remove it for the webkit printing
				//SetBaseForRelativePaths(Dom, folderPath); //needed because the file itself may be off in the temp directory

				//UpdateStyleSheetLinkPaths(fileLocator);

				//add a unique id for our use
				foreach (XmlElement node in Dom.SafeSelectNodes("/html/body/div"))
				{
					node.SetAttribute("id", Guid.NewGuid().ToString());
				}

				UpdateSupportFiles();
			}
		}

		/// <summary>
		/// we update these so that the file continues to look the same when you just open it in firefox
		/// </summary>
		private void UpdateSupportFiles()
		{
			UpdateIfNewer("placeHolder.png");
			UpdateIfNewer("basePage.css");
			UpdateIfNewer("previewMode.css");

			foreach (var paperStyleSheet in Directory.GetFiles(_folderPath, "*Portrait.css"))
			{
				UpdateIfNewer(Path.GetFileName(paperStyleSheet));
			}
			foreach (var paperStyleSheet in Directory.GetFiles(_folderPath, "*Landscape.css"))
			{
				UpdateIfNewer(Path.GetFileName(paperStyleSheet));
			}
		}
		private void UpdateIfNewer(string fileName)
		{
			try
			{
				var factoryPath = _fileLocator.LocateFile(fileName);
				if(string.IsNullOrEmpty(factoryPath))//happens during unit testing
					return;

				var factoryTime = File.GetLastWriteTimeUtc(factoryPath);
				var documentPath = Path.Combine(_folderPath, fileName);
				if(!File.Exists(documentPath))
				{
					File.Copy(factoryPath, documentPath);
					return;
				}
				var documentTime = File.GetLastWriteTimeUtc(documentPath);
				if(factoryTime> documentTime)
				{
					File.Copy(factoryPath, documentPath,true);
				}
			}
			catch (Exception e)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e,
					"Could not update one of the support files in this document ({0}). This may or not be a problem, but is sure unusual.", fileName);
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
		private static void MakeCssLinksAppropriateForStoredFile(XmlDocument dom)
		{
			RemoveModeStyleSheets(dom);
			dom.AddStyleSheet("previewMode.css");
			dom.AddStyleSheet("basePage.css");
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
				var pathToHtml = PathToExistingHtml;
				if (pathToHtml.EndsWith("templatePages.htm"))
					return Book.BookType.Template;
				if (pathToHtml.EndsWith("shellPages.htm"))
					return Book.BookType.Shell;

				//directory name matches htm name
//                if (!string.IsNullOrEmpty(pathToHtml) && Path.GetFileName(Path.GetDirectoryName(pathToHtml)) == Path.GetFileNameWithoutExtension(pathToHtml))
//                {
//                    return Book.BookType.Publication;
//                }
				return Book.BookType.Publication;
			}
		}


		public string PathToExistingHtml
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
			get { return File.Exists(PathToExistingHtml); }
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
			string tempPath = SaveHtml(Dom);
			File.Replace(tempPath, PathToExistingHtml, PathToExistingHtml + ".bak");
		}

		public string SaveHtml(XmlDocument dom)
		{
			string tempPath = Path.GetTempFileName();
			MakeCssLinksAppropriateForStoredFile(dom);
			SetBaseForRelativePaths(dom, string.Empty);// remove any dependency on this computer, and where files are on it.

			XmlWriterSettings settings = new XmlWriterSettings();
			settings.Indent = true;
			settings.CheckCharacters = true;

			using (var writer = XmlWriter.Create(tempPath, settings))
			{
				dom.WriteContentTo(writer);
				writer.Close();
			}
			return tempPath;
		}


		public bool TryGetPremadeThumbnail(out Image image)
		{
			string path = Path.Combine(_folderPath, "thumbnail.png");
			if (File.Exists(path))
			{
				//this FromFile thing locks the file until the image is disposed of. Therefore, we copy the image and dispose of the original.
				using (var tempImage = Image.FromFile(path))
				{
					image = new Bitmap(tempImage);
				}
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
		/// <param name="bookletStyle"></param>
//        public string GetHtmlFileForPrintingWithWkHtmlToPdf(PublishModel.BookletStyleChoices bookletStyle)
//        {
//            switch (bookletStyle)
//            {
//                case PublishModel.BookletStyleChoices.None:
//                    return PathToHtml;
//                case PublishModel.BookletStyleChoices.BookletCover:
//                    break;
//                case PublishModel.BookletStyleChoices.BookletPages:
//                    break;
//                default:
//                    throw new ArgumentOutOfRangeException("bookletStyle");
//            }
//        }

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

		public void HideAllTextAreasThatShouldNotShow(string vernacularIso639Code, string optionalPageSelector)
		{
			HideAllTextAreasThatShouldNotShow(Dom, vernacularIso639Code,optionalPageSelector);
		}


		public static void HideAllTextAreasThatShouldNotShow(XmlNode rootElement, string iso639CodeToKeepShowing, string optionalPageSelector)
		{
			if (optionalPageSelector == null)
				optionalPageSelector = string.Empty;

			foreach (XmlElement storageNode in rootElement.SafeSelectNodes(optionalPageSelector + "//textarea"))
			{
				if (storageNode.HasAttribute("style"))
					storageNode.RemoveAttribute("style");
				if (storageNode.GetAttribute("lang") != iso639CodeToKeepShowing && !ContainsClass(storageNode,"showNational"))
				{
					storageNode.SetAttribute("style", Book.StyleOfHiddenElements);
				}
			}
		}

		private static bool ContainsClass(XmlNode element, string className)
		{
			return ((XmlElement)element).GetAttribute("class").Contains(className);
		}

		public void SetBookName(string name)
		{
			name = SanitizeNameForFileSystem(name);

			var currentFilePath =PathToExistingHtml;
			if (name == Path.GetFileNameWithoutExtension(currentFilePath))
				return;

			//figure out what name we're really going to use (might need to add a number suffix)
			var newFolderPath = Path.Combine(Directory.GetParent(FolderPath).FullName, name);
			newFolderPath = GetUniqueFolderPath(newFolderPath);

			//next, rename the file
			File.Move(currentFilePath, Path.Combine(FolderPath, Path.GetFileName(newFolderPath) + ".htm"));

			 //next, rename the enclosing folder
			try
			{
				Directory.Move(FolderPath, newFolderPath);
				_folderPath = newFolderPath;
			}
			catch (Exception)
			{
				Debug.Fail("(debug mode only): could not rename the folder");
			}
		}

		private string SanitizeNameForFileSystem(string name)
		{
			foreach(char c in Path.GetInvalidFileNameChars())
			{
				name = name.Replace(c, ' ');
			}
			return name.Trim();
		}

		/// <summary>
		/// if necessary, append a number to make the folder path unique
		/// </summary>
		/// <param name="folderPath"></param>
		/// <returns></returns>
		private string GetUniqueFolderPath(string folderPath)
		{
			int i = 0;
			string suffix = "";
			var parent = Directory.GetParent(folderPath).FullName;
			var name = Path.GetFileName(folderPath);
			while (Directory.Exists(Path.Combine(parent, name + suffix)))
			{
				++i;
				suffix = i.ToString();
			}
			return Path.Combine(parent, name + suffix);
		}
	}
}