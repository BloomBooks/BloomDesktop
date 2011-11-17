using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security;
using System.Text;
using System.Xml;
using Palaso.Code;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.FileSystem;
using Palaso.Xml;

namespace Bloom.Book
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
		string GetVernacularTitleFromHtml(string Iso639Code);
		void SetBookName(string name);
		string GetValidateErrors();
		void UpdateBookFileAndFolderName(LanguageSettings settings);
	}

	public class BookStorage : IBookStorage
	{
		private  string _folderPath;
		private readonly IFileLocator _fileLocator;
		public string ErrorMessages;
		private static bool _alreadyNotifiedAboutOneFailedCopy;

		public delegate BookStorage Factory(string folderPath);//autofac uses this

		public BookStorage(string folderPath, Palaso.IO.IFileLocator fileLocator)
		{
			_folderPath = folderPath;
			_fileLocator = fileLocator;
			Dom = new XmlDocument();

			RequireThat.Directory(folderPath).Exists();
			if (File.Exists(PathToExistingHtml))
			{
				ErrorMessages = ValidateBook(PathToExistingHtml);
				if (!string.IsNullOrEmpty(ErrorMessages))
				{
					//hack so we can package this for palaso reporting
//                    var ex = new XmlSyntaxException(ErrorMessages);
//                    Palaso.Reporting.ErrorReport.NotifyUserOfProblem(ex, "Bloom did an integrity check of the book named '{0}', and found something wrong. This doesn't mean your work is lost, but it does mean that there is a bug in the system or templates somewhere, and the developers need to find and fix the problem (and your book).  Please click the 'Details' button and send this report to the developers.", Path.GetFileName(PathToExistingHtml));
					Dom.LoadXml("<html><body>There is a problem with the html structure of this book which will require expert help.</body></html>");
			   }
				else
				{
					Logger.WriteEvent("BookStorage Loading Dom from {0}", PathToExistingHtml);
					Dom.Load(PathToExistingHtml);
				}

				//todo: this would be better just to add to those temporary copies of it. As it is, we have to remove it for the webkit printing
				//SetBaseForRelativePaths(Dom, folderPath); //needed because the file itself may be off in the temp directory

				//UpdateStyleSheetLinkPaths(fileLocator);

				//add a unique id for our use
	 //review: bookstarter sticks in the ids, this one updates (and skips if it it didn't have an id before). At a minimum, this needs explanation
				foreach (XmlElement node in Dom.SafeSelectNodes("/html/body/div"))
				{
					if(string.IsNullOrEmpty(node.GetAttribute("id")))
						node.SetAttribute("id", Guid.NewGuid().ToString());
				}

				foreach (XmlElement node in Dom.SafeSelectNodes("//textarea"))
				{
					if (string.IsNullOrEmpty(node.GetAttribute("id")))
						node.SetAttribute("id", Guid.NewGuid().ToString());
				}

				foreach (XmlElement node in Dom.SafeSelectNodes("//img"))
				{
					if (string.IsNullOrEmpty(node.GetAttribute("id")))
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
			string factoryPath="notSet";
			string documentPath="notSet";
			try
			{
				factoryPath = _fileLocator.LocateFile(fileName);
				if(string.IsNullOrEmpty(factoryPath))//happens during unit testing
					return;

				var factoryTime = File.GetLastWriteTimeUtc(factoryPath);
				documentPath = Path.Combine(_folderPath, fileName);
				if(!File.Exists(documentPath))
				{
					Logger.WriteEvent("BookStorage.UpdateIfNewer() Copying mising file {0} to {1}", factoryPath, documentPath);
					File.Copy(factoryPath, documentPath);
					return;
				}
				var documentTime = File.GetLastWriteTimeUtc(documentPath);
				if(factoryTime> documentTime)
				{
					if((File.GetAttributes(documentPath) & FileAttributes.ReadOnly) != 0)
					{
						Palaso.Reporting.ErrorReport.NotifyUserOfProblem("Could not update one of the support files in this document ({0}) because the destination was marked ReadOnly.", documentPath);
						return;
					}
					Logger.WriteEvent("BookStorage.UpdateIfNewer() Updating file {0} to {1}", factoryPath, documentPath);

					File.Copy(factoryPath, documentPath,true);
					//if the source was locked, don't copy the lock over
					File.SetAttributes(documentPath,FileAttributes.Normal);
				}
			}
			catch (Exception e)
			{
				if(documentPath.Contains(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles))
					|| documentPath.ToLower().Contains("program"))//english only
				{
					Logger.WriteEvent("Could not update file {0} because it was in the program directory.", documentPath);
					return;
				}
				if(_alreadyNotifiedAboutOneFailedCopy)
					return;//don't keep bugging them
				_alreadyNotifiedAboutOneFailedCopy = true;
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e,
					"Could not update one of the support files in this document ({0} to {1}). This is normally because the folder is 'locked' or the file is marked 'read only'.", factoryPath,documentPath);
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
						Palaso.Reporting.ErrorReport.NotifyUserOfProblem(new ShowOncePerSessionBasedOnExactMessagePolicy(), string.Format("Bloom could not find the stylesheet '{0}', which is used in {1}", fileName, _folderPath));
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
		/// <summary>
		/// looks for the css which sets the paper size/orientation
		/// </summary>
		/// <param name="dom"></param>
		public static string GetPaperStyleSheetName(XmlDocument dom)
		{
			foreach (XmlElement linkNode in dom.SafeSelectNodes("/html/head/link"))
			{
				var href = linkNode.GetAttribute("href");
				if (href == null)
				{
					continue;
				}

				var fileName = Path.GetFileName(href);
				if (fileName.ToLower().Contains("portrait") || fileName.ToLower().Contains("landscape"))
				{
					return fileName;
				}
			}
			return string.Empty;
		}


		public static void SetBaseForRelativePaths(XmlDocument dom, string folderPath)
		{
		   var head = dom.SelectSingleNodeHonoringDefaultNS("//head");
		   if (head == null)
			   return;

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
			get { return FindBookHtmlInFolder(_folderPath); }
		}

		public static string FindBookHtmlInFolder(string folderPath)
		{
			string p = Path.Combine(folderPath, Path.GetFileName(folderPath) + ".htm");
			if (File.Exists(p))
				return p;

			//ok, so maybe they changed the name of the folder and not the htm. Can we find a *single* html doc?
			var candidates = Directory.GetFiles(folderPath, "*.htm");
			if (candidates.Length == 1)
				return candidates[0];

			//template
			p = Path.Combine(folderPath, "templatePages.htm");
			if (File.Exists(p))
				return p;

			return string.Empty;
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
			get { return File.Exists(PathToExistingHtml) && string.IsNullOrEmpty(ErrorMessages); }
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
			Logger.WriteEvent("BookStorage.Saving... (eventual destination: {0})",PathToExistingHtml);
			Guard.Against(BookType != Book.BookType.Publication, "Tried to save a non-editable book.");
			string tempPath = SaveHtml(Dom);

			string errors = ValidateBook(tempPath);
			if (!string.IsNullOrEmpty(errors))
			{
				var badFilePath = PathToExistingHtml + ".bad";
				File.Copy(tempPath, badFilePath,true);
				//hack so we can package this for palaso reporting
				var ex = new XmlSyntaxException(errors);
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(ex, "Oops. Before saving, Bloom did an integrity check of your book, and found something wrong. This doesn't mean your work is lost, but it does mean that there is a bug in the system or templates somewhere, and the developers need to find and fix the problem (and your book).  Please click the 'Details' button and send this report to the developers.  Bloom has saved the bad version of this book as " + badFilePath + ".  Bloom will now exit, and your book will probably not have this recent damage.  If you are willing, please try to do the same steps again, so that you can report exactly how to make it happen.");
				Process.GetCurrentProcess().Kill();
			}
			else
			{
				Logger.WriteMinorEvent("ReplaceFileWithUserInteractionIfNeeded({0},{1})", tempPath, PathToExistingHtml);
				if (!string.IsNullOrEmpty(tempPath))
					Palaso.IO.FileUtils.ReplaceFileWithUserInteractionIfNeeded(tempPath, PathToExistingHtml, PathToExistingHtml + ".bak");
			}
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

		private string ValidateBook(string path)
		{
			XmlDocument dom = new XmlDocument();
			dom.Load(path);
			var ids = new List<string>();
			StringBuilder builder = new StringBuilder();

			Ensure(dom.SafeSelectNodes("//div[contains(@class,'-bloom-page')]").Count >0, "Must have at least one page", builder);
			EnsureIdsAreUnique(dom, "textarea", ids, builder);
			EnsureIdsAreUnique(dom, "p", ids, builder);
			EnsureIdsAreUnique(dom, "img", ids, builder);

			//TODO: validate other things, including html
			var x = builder.ToString().Trim();
			if (x.Length == 0)
				Logger.WriteEvent("BookStorage.ValidateBook({0}): No Errors", path);
			else
			{
				Logger.WriteEvent("BookStorage.ValidateBook({0}): {1}", path, x);
			}

			return builder.ToString();
		}

		private void Ensure(bool passes, string message, StringBuilder builder)
		{
			if (!passes)
				builder.AppendLine(message);
		}

		private void EnsureIdsAreUnique(XmlDocument dom, string elementTag, List<string> ids, StringBuilder builder)
		{
			foreach (XmlElement element in dom.SafeSelectNodes("//"+elementTag+"[@id]"))
			{
				var id = element.GetAttribute("id");
				if (ids.Contains(id))
					builder.AppendLine("The id of this " + elementTag + " must be unique, but is not: " + element.OuterXml);
				else
					ids.Add(id);
			}
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
			var didDelete= ConfirmRecycleDialog.Recycle(_folderPath);
			if(didDelete)
				Logger.WriteEvent("After BookStorage.DeleteBook({0})", _folderPath);
			return didDelete;
		}


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
				string cssClass = storageNode.GetAttribute("class");
				if (storageNode.GetAttribute("lang") == iso639CodeToKeepShowing || ContainsClass(storageNode,"-bloom-showNational"))
				{
					cssClass = cssClass.Replace(Book.ClassOfHiddenElements, "");
				}
				else if (!ContainsClass(storageNode, Book.ClassOfHiddenElements))
				{
					cssClass += (" " + Book.ClassOfHiddenElements);
				}
				cssClass = cssClass.Trim();
				if (string.IsNullOrEmpty(cssClass))
				{
					storageNode.RemoveAttribute("class");
				}
				else
				{
					storageNode.SetAttribute("class", cssClass);
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
			if (Path.GetFileNameWithoutExtension(currentFilePath).StartsWith(name)) //starts with because maybe we have "myBook1"
				return;

			//figure out what name we're really going to use (might need to add a number suffix)
			var newFolderPath = Path.Combine(Directory.GetParent(FolderPath).FullName, name);
			newFolderPath = GetUniqueFolderPath(newFolderPath);

			//next, rename the file
			File.Move(currentFilePath, Path.Combine(FolderPath, Path.GetFileName(newFolderPath) + ".htm"));

			 //next, rename the enclosing folder
			try
			{
				Palaso.IO.DirectoryUtilities.MoveDirectorySafely(FolderPath, newFolderPath);
				_folderPath = newFolderPath;
			}
			catch (Exception)
			{
				Debug.Fail("(debug mode only): could not rename the folder");
			}
		}

		public string GetValidateErrors()
		{
			if(!Directory.Exists(_folderPath))
			{
				return "The directory (" + _folderPath + ") could not be found.";
			}
			if(!File.Exists(PathToExistingHtml))
			{
				return "Could not find an html file to use.";
			}
			return ValidateBook(PathToExistingHtml);
		}

		public void UpdateBookFileAndFolderName(LanguageSettings languageSettings)
		{
			SetBookName(GetVernacularTitleFromHtml(languageSettings.VernacularIso639Code));
		 }

		public string GetVernacularTitleFromHtml(string Iso639Code)
		{
			var textWithTitle = Dom.SelectSingleNodeHonoringDefaultNS(
				string.Format("//textarea[contains(@class,'-bloom-vernacularBookTitle') and @lang='{0}']", Iso639Code));
			if (textWithTitle == null)
			{
				Logger.WriteEvent("UpdateBookFileAndFolderName(): Could not find title in html.");
				return "unknown";
			}
			string title = textWithTitle.InnerText.Trim();
			if (string.IsNullOrEmpty(title))
			{
				Logger.WriteEvent("UpdateBookFileAndFolderName(): Found title element but it was empty.");
				return "unknown";
			}
			return title;
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