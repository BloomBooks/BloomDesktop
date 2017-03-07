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
		private readonly SourceCollectionsList _sourceCollectionsList;
		private readonly BookSelection _bookSelection;
		private readonly PageSelection _pageSelection;
		private readonly TemplateInsertionCommand _templateInsertionCommand;
		private readonly BookThumbNailer _thumbNailer;

		//these two factories are needed to instantiate template books if we need to generate thumbnails for them
		private readonly Book.Book.Factory _bookFactory;
		private readonly BookStorage.Factory _storageFactory;

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

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("pageTemplates", HandleTemplatesRequest);
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
			if(string.IsNullOrEmpty(pathToExistingOrGeneratedThumbnail) || !File.Exists(pathToExistingOrGeneratedThumbnail))
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
		/// <param name="expectedPathOfThumbnailImage"></param>
		/// <returns>Should always return true, unless we really can't come up with an image at all.</returns>
		private string FindOrGenerateThumbnail(string expectedPathOfThumbnailImage)
		{
			var localPath = AdjustPossibleLocalHostPathToFilePath(expectedPathOfThumbnailImage);
			var svgpath = Path.ChangeExtension(localPath, "svg");
			if (File.Exists(svgpath))
			{
				return svgpath;
			}
			var pngpath = Path.ChangeExtension(localPath, "png");
			if (File.Exists(pngpath))
			{
				return pngpath;
			}

			// We don't have an image; try to make one.
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

			Image thumbnail = _thumbNailer.GetThumbnailForPage(templateBook, templatePage, isLandscape);

			// lock to avoid BL-3781 where we got a "Object is currently in use elsewhere" while doing the Clone() below.
			// Note: it would appear that the clone isn't even needed, since it was added in the past to overcome this
			// same contention problem (but, in hindsight, only partially, see?). But for some reason if we just lock the image
			// until it is saved, we get all gray rectangles. So for now, we just quickly do the clone and unlock.
			var resultPath = "";
			Bitmap clone;
			// Review: the coarse lock(SyncObj) in EnhancedImageServer.ProcessRequest() may have removed the need for this finer grained lock.
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
					using (var file = new TempFile())
					{
						clone.Save(file.Path);
						resultPath = file.Path;
					}
				}
			}
			return resultPath;
		}



		private static string AdjustPossibleLocalHostPathToFilePath(string path)
		{
			if (!path.StartsWith("localhost/", StringComparison.InvariantCulture))
				return path;
			return EnhancedImageServer.LocalHostPathToFilePath(path);
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
			bookTemplatePaths.Add(pathToCurrentTemplateHtml);

			// 2) Look in their current collection...this is the first one used to make sourceBookPaths

			// 3) Next look through the books that came with bloom and other that this user has installed (e.g. via download or bloompack)
			//    and add in all other template books that are designed for inclusion in other books. These should end in "template.htm{l}".
			//    Requiring the book to end in the word "template" is low budget, but fast. Maybe we'll do something better later.
			//    (It's unfortunate that we have to check for .html as well as htm. Our own templates end in html, while user-created ones
			//    end in .htm, Bloom's standard for created books.)
			var pathToCurrentTemplateLC = pathToCurrentTemplateHtml.ToLowerInvariant();
			bookTemplatePaths.AddRange(sourceBookPaths
				.Where(path =>
					{
						var pathLC = path.ToLowerInvariant();
						if (pathLC.Equals(pathToCurrentTemplateLC))
							return false;
						return pathLC.EndsWith("template.html") || pathLC.EndsWith("basic book.html") || pathLC.EndsWith("template.htm");
					})
				.Select(path => Platform.IsWindows ? path.ToLowerInvariant() : path));

			var indexOfBasicBook = bookTemplatePaths.FindIndex(p => p.ToLowerInvariant().Contains("basic book"));
			if (indexOfBasicBook > 1)
			{
				var pathOfBasicBook = bookTemplatePaths[indexOfBasicBook];
				bookTemplatePaths.RemoveAt(indexOfBasicBook);
				bookTemplatePaths.Insert(1,pathOfBasicBook);
			}

			return bookTemplatePaths.Distinct().ToList();
		}

		private dynamic GetPageGroup(string path)
		{
			dynamic pageGroup = new ExpandoObject();
			pageGroup.templateBookFolderUrl = MassageUrlForJavascript(Path.GetDirectoryName(path));
			pageGroup.templateBookPath = HttpUtility.HtmlEncode(path);
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
