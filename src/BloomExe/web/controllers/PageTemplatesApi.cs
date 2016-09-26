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

			addPageSettings.groups = GetBookTemplatePaths(GetPathToCurrentTemplateHtml(), _sourceCollectionsList.GetSourceBookPaths())
				.Select(bookTemplatePath => GetPageGroup(bookTemplatePath));
			addPageSettings.currentLayout = _pageSelection.CurrentSelection.IdOfFirstAncestor;

			request.ReplyWithJson(JsonConvert.SerializeObject(addPageSettings));
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
			IPage templatePage = templateBook.GetPages().FirstOrDefault(page => page.Caption.Replace("&", "+") == caption);
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

			// 1) we start the list with the template that was used to start this book
			bookTemplatePaths.Add(pathToCurrentTemplateHtml);

			// 2) Future, add those in their current collection

			// 3) then add in all other template books they have in their sources 
			//requiring "template" to be in the path is low budget, but fast. Maybe we'll do something better later.
			bookTemplatePaths.AddRange(sourceBookPaths
				.Where(path => path.ToLowerInvariant().Contains("template")
				               && path.ToLowerInvariant() != pathToCurrentTemplateHtml.ToLowerInvariant())
				.Select(path => path));

			var indexOfBasicBook = bookTemplatePaths.FindIndex(p => p.ToLowerInvariant().Contains("basic book"));
			if (indexOfBasicBook > 1)
			{
				var pathOfBasicBook = bookTemplatePaths[indexOfBasicBook];
				bookTemplatePaths.RemoveAt(indexOfBasicBook);
				bookTemplatePaths.Insert(1,pathOfBasicBook);
			}
			return bookTemplatePaths;
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
			return templateBook == null ? null : templateBook.GetPathHtmlFile();
		}

		private static string MassageUrlForJavascript(string url)
		{
			return url/*.Replace(':', '$')*/.Replace('\\', '/');
		}
	}
}
