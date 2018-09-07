using System;
using System.Collections.Generic;
using System.Drawing;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Web;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using Newtonsoft.Json;
using SIL.IO;
using SIL.PlatformUtilities;

namespace Bloom.web.controllers
{
	/// <summary>
	///  Delivers info on page templates that the user can add.
	/// </summary>
	public class PageTemplatesApi
	{
		public const string TemplateFolderName = "template";
		private readonly SourceCollectionsList _sourceCollectionsList;
		private readonly BookSelection _bookSelection;
		private readonly PageSelection _pageSelection;
		private readonly TemplateInsertionCommand _templateInsertionCommand;
		private readonly BookThumbNailer _thumbNailer;

		//these two factories are needed to instantiate template books if we need to generate thumbnails for them
		private readonly Book.Book.Factory _bookFactory;
		private readonly BookStorage.Factory _storageFactory;

		public static bool ForPageLayout = false; // set when most recent relevant command is ShowChangeLayoutDialog

		public PageTemplatesApi(SourceCollectionsList  sourceCollectionsList,BookSelection bookSelection,
			PageSelection pageSelection, TemplateInsertionCommand templateInsertionCommand,
			BookThumbNailer thumbNailer, Book.Book.Factory bookFactory, BookStorage.Factory storageFactory)
		{
			_sourceCollectionsList = sourceCollectionsList;
			_bookSelection = bookSelection;
			_pageSelection = pageSelection;
			_templateInsertionCommand = templateInsertionCommand;
			_thumbNailer = thumbNailer;
			_bookFactory = bookFactory;
			_storageFactory = storageFactory;
		}

		public void RegisterWithServer(BloomServer server)
		{
			// We could probably get away with using the server thread here, but the code interacts quite a bit with the
			// current book and other state.
			server.RegisterEndpointHandler("pageTemplates", HandleTemplatesRequest, true);
			// Being on the UI thread causes a deadlock on Linux/Mono.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-3818.
			server.RegisterEndpointHandler("pageTemplateThumbnail", HandleThumbnailRequest, false);
		}

		/// <summary>
		/// Returns a json string for initializing the AddPage dialog. It gives paths to our current TemplateBook
		/// and specifies whether the dialog is to be used for adding pages or choosing a different layout.
		/// </summary>
		public void HandleTemplatesRequest(ApiRequest request)
		{
			dynamic addPageSettings = new ExpandoObject();
			addPageSettings.defaultPageToSelect = _templateInsertionCommand.MostRecentInsertedTemplatePage == null ? "" : _templateInsertionCommand.MostRecentInsertedTemplatePage.Id;
			addPageSettings.orientation = _bookSelection.CurrentSelection.GetLayout().SizeAndOrientation.IsLandScape ? "landscape" : "portrait";

			addPageSettings.groups = GetBookTemplatePaths(GetPathToCurrentTemplateHtml(), GetCurrentAndSourceBookPaths())
				.Select(bookTemplatePath => GetPageGroup(bookTemplatePath));
			addPageSettings.currentLayout = _pageSelection.CurrentSelection.IdOfFirstAncestor;
			// This works because this is only used for the add/change page dialog and we never show them
			// both at once. Pushing this information into the settings that the dialog loads removes the
			// need for cross-domain communication between the dialog and the page that launches it.
			addPageSettings.forChooseLayout = ForPageLayout;

			request.ReplyWithJson(JsonConvert.SerializeObject(addPageSettings));
		}

		/// <summary>
		/// Gives paths to the html files for all source books and those in the current collection
		/// </summary>
		public IEnumerable<string> GetCurrentAndSourceBookPaths()
		{
			return new [] {_bookSelection.CurrentSelection.CollectionSettings.FolderPath} // Start with the current collection
				.Concat(_sourceCollectionsList.GetCollectionFolders()) // add all other source collections
				.Distinct() //seems to be needed in case a shortcut points to a folder that's already in the list.
				.SelectMany(Directory.GetDirectories) // get all the (book) folders in those collections
					.Select(BookStorage.FindBookHtmlInFolder); // and get the book from each
		}

		/// <summary>
		/// Called by the server to handle API calls for page thumbnails.
		/// </summary>
		public void HandleThumbnailRequest(ApiRequest request)
		{
			var filePath = request.LocalPath().Replace("api/pageTemplateThumbnail/","");
			var pathToExistingOrGeneratedThumbnail = FindOrGenerateThumbnail(filePath);
			if(string.IsNullOrEmpty(pathToExistingOrGeneratedThumbnail) || !RobustFile.Exists(pathToExistingOrGeneratedThumbnail))
			{
				request.Failed("Could not make a page thumbnail for "+filePath);
				return;
			}
			request.ReplyWithImage(pathToExistingOrGeneratedThumbnail);
		}

		/// <summary>
		/// Usually we expect that a file at the same path but with extension .svg will
		/// be found and returned. Failing this we try for one ending in .png. If this still fails we
		/// start a process to generate an image from the template page content.
		/// </summary>
		/// <returns>Should always return a valid image path, unless we really can't come up with an image at all.</returns>
		private string FindOrGenerateThumbnail(string expectedPathOfThumbnailImage)
		{
			var localPath = AdjustPossibleLocalHostPathToFilePath(expectedPathOfThumbnailImage);

			var svgpath = Path.ChangeExtension(localPath, "svg");
			if (RobustFile.Exists(svgpath))
			{
				return svgpath;
			}

			var pngpath = Path.ChangeExtension(localPath, "png");
			bool mustRegenerate = false;

			if (RobustFile.Exists(pngpath))
			{
				var f = new FileInfo(pngpath);
				if (f.IsReadOnly)
					return pngpath; // it's locked, don't try and replace it

				if (!IsPageTypeFromCurrentBook(localPath))
					return pngpath;

				mustRegenerate = true; // prevent thumbnailer using cached (obsolete) image
			}

			var altpath = GetAlternativeWritablePath(pngpath);
			if (RobustFile.Exists(altpath))
			{
				if (!IsPageTypeFromCurrentBook(localPath))
					return altpath;

				mustRegenerate = true; // prevent thumbnailer using cached (obsolete) image
			}

			// We don't have an image, or we want to make a fresh one
			var templatesDirectoryInTemplateBook = Path.GetDirectoryName(expectedPathOfThumbnailImage);
			var bookPath = Path.GetDirectoryName(templatesDirectoryInTemplateBook);
			var templateBook = _bookFactory(new BookInfo(bookPath,false), _storageFactory(bookPath));

			//note: the caption is used here as a key to find the template page.
			var caption = Path.GetFileNameWithoutExtension(expectedPathOfThumbnailImage).Trim();
			var isLandscape = caption.EndsWith("-landscape"); // matches string in page-chooser.ts
			if (isLandscape)
				caption = caption.Substring(0, caption.Length - "-landscape".Length);

			// The Replace of & with + corresponds to a replacement made in page-chooser.ts method loadPagesFromCollection.
			// The Trim is needed because template may now be created by users editing the pageLabel div, and those
			// labels typically include a trailing newline.
			IPage templatePage = templateBook.GetPages().FirstOrDefault(page => page.Caption.Replace("&", "+").Trim() == caption);
			if (templatePage == null)
				templatePage = templateBook.GetPages().FirstOrDefault(); // may get something useful?? or throw??

			Image thumbnail = _thumbNailer.GetThumbnailForPage(templateBook, templatePage, isLandscape, mustRegenerate);

			// lock to avoid BL-3781 where we got a "Object is currently in use elsewhere" while doing the Clone() below.
			// Note: it would appear that the clone isn't even needed, since it was added in the past to overcome this
			// same contention problem (but, in hindsight, only partially, see?). But for some reason if we just lock the image
			// until it is saved, we get all gray rectangles. So for now, we just quickly do the clone and unlock.
			var resultPath = "";
			Bitmap clone;
			// Review: the coarse lock(SyncObj) in BloomServer.ProcessRequest() may have removed the need for this finer grained lock.
			lock (thumbnail)
			{
				clone = new Bitmap((Image)thumbnail.Clone());
			}
			using (clone)
			{
				try
				{
					//if the directory doesn't exist in the template's directory, make it (i.e. "templates/").
					Directory.CreateDirectory(templatesDirectoryInTemplateBook);
					//save this thumbnail so that we don't have to generate it next time
					clone.Save(pngpath);
					resultPath = pngpath;
				}
				catch (Exception)
				{
					var folder = Path.GetDirectoryName(altpath);
					Directory.CreateDirectory(folder);
					clone.Save(altpath);
					resultPath = altpath;
				}
			}
			return resultPath;
		}

		/// <summary>
		/// If there is no svg, then we assume that we are using a generated image.
		/// If the book we want a thumbnail from is also the one we are currently editing,
		/// then the thumbnail we generated last time might not reflect how the page is laid out
		/// now. So in that case we ignore the existing png thumbnail (and any cached version
		/// of it we previously saved) and make a new one. Bloom saves the current page
		/// before invoking AddPage, so the file contains the right content for making the new one.
		/// </summary>
		private bool IsPageTypeFromCurrentBook(string localPath)
		{
			var testLocalPath = localPath.Replace ("/", "\\");
			var testFolderPath = _bookSelection.CurrentSelection.FolderPath.Replace ("/", "\\");
			if (Platform.IsWindows)
			{
				// Not a formality, since localPath comes from GetBookTemplatePaths and has been
				// forced to LC on Windows, while the book's FolderPath has not.
				testLocalPath = testLocalPath.ToLowerInvariant();
				testFolderPath = testFolderPath.ToLowerInvariant();
			}
			return testLocalPath.Contains(testFolderPath);
		}

		private string GetAlternativeWritablePath(string pngpath)
		{
			var idx = pngpath.IndexOf("browser");
			string newpath;
			if (idx >= 0)
			{
				var subpath = pngpath.Substring(idx);
				newpath = Path.Combine(Path.GetTempPath(), "Bloom", subpath);
			}
			else
			{
				// this path will probably never be used
				newpath = Path.Combine(Path.GetTempPath(), "Bloom", "browser", Path.GetFileName(pngpath));
			}
			return newpath;
		}

		private static string AdjustPossibleLocalHostPathToFilePath(string path)
		{
			if (!path.StartsWith("localhost/", StringComparison.InvariantCulture))
				return path;
			return BloomServer.LocalHostPathToFilePath(path);
		}

		/// <summary>
		/// Give a list of paths to template books, considering desired presentation order and anything else.
		/// </summary>
		/// <remarks>This method is the focus of the logic of this class. So is designed to be unit-testable
		/// without a ton of setup; making it static makes it easier to keep that constraint.</remarks>
		/// <returns></returns>
		internal static List<string> GetBookTemplatePaths(string pathToCurrentTemplateHtml, IEnumerable<string> sourceBookPaths )
		{
			var bookTemplatePaths = new List<string>();

			// 1) we start the list with the template that was used to start this book (or the book itself if it IS a template)
			bookTemplatePaths.Add(Platform.IsWindows ? pathToCurrentTemplateHtml.ToLowerInvariant() : pathToCurrentTemplateHtml);

			// 2) Look in their current collection...this is the first one used to make sourceBookPaths

			// 3) Next look through the books that came with bloom and other that this user has installed (e.g. via download or bloompack)
			//    and add in all other template books that are designed for inclusion in other books. These should contain a folder
			//    called "template" (which contains thumbnails of the pages that can be inserted).
			//    Template books whose pages are not suitable for extending Add Pages can be identified by creating a file
			//    template/NotForAddPage.txt (which can contain any explanation you like).
			bookTemplatePaths.AddRange(sourceBookPaths
				.Where(path =>
				{
					if (string.IsNullOrEmpty(path))
						return false; // not sure how this happens.
					var pathToTemplatesFolder = Path.Combine(Path.GetDirectoryName(path), TemplateFolderName);
					if (!Directory.Exists(pathToTemplatesFolder))
						return false;
					return !RobustFile.Exists(Path.Combine(pathToTemplatesFolder, "NotForAddPage.txt"));
				})
				.Select(path => Platform.IsWindows ? path.ToLowerInvariant() : path));

			var indexOfBasicBook = bookTemplatePaths.FindIndex(p => p.ToLowerInvariant().Contains("basic book"));
			if (indexOfBasicBook > 1)
			{
				var pathOfBasicBook = bookTemplatePaths[indexOfBasicBook];
				bookTemplatePaths.RemoveAt(indexOfBasicBook);
				bookTemplatePaths.Insert(1,pathOfBasicBook);
			}
			// Remove invisible templates.
			for (int i = bookTemplatePaths.Count - 1; i >= 0; --i)
			{
				if (IsTemplateInvisible(bookTemplatePaths[i]))
					bookTemplatePaths.RemoveAt(i);
			}

			return bookTemplatePaths.Distinct().ToList();
		}

		/// <summary>
		/// The pages from the template are unwanted if the template itself isn't visible.
		/// </summary>
		/// <remarks>
		/// Experimental templates are invisible if the user doesn't want experimental features.
		/// </remarks>
		private static bool IsTemplateInvisible(string templatePath)
		{
			var folderPath = Path.GetDirectoryName(templatePath);
			var info = new BookInfo(folderPath, true);
			return !info.ShowThisBookAsSource();
		}

		private dynamic GetPageGroup(string path)
		{
			dynamic pageGroup = new ExpandoObject();
			pageGroup.templateBookFolderUrl = MassageUrlForJavascript(Path.GetDirectoryName(path));
			pageGroup.templateBookPath = MassageUrlForJavascript(path);
			return pageGroup;
		}

		private string GetPathToCurrentTemplateHtml()
		{
			var templateBook = _bookSelection.CurrentSelection.FindTemplateBook();
			if (templateBook != null)
				return templateBook.GetPathHtmlFile();
			// Returning null here won't work. For one thing, we're going to pass this path through
			// various filters that will fail with nulls. For another, we want to be able to
			// eventually extract a folder name from the path to report the missing template
			// from within the dialog.
			// So, manufacture a 'path' something like where we might find the missing template.
			var templateKey = _bookSelection.CurrentSelection.PageTemplateSource;
			if (string.IsNullOrEmpty(templateKey))
			{
				templateKey = "MissingTemplate";  // avoid crashing
			}
			// Don't get smart and change these to Path.Combine() etc. Page-chooser.ts really wants
			// the separators to be slashes, and we aren't going to find this file anyway, on either OS.
			return "missingPageTemplate/" + templateKey + "/" +templateKey + ".html";
		}

		private static string MassageUrlForJavascript(string url)
		{
			return url/*.Replace(':', '$')*/.Replace('\\', '/');
		}
	}
}
