﻿using System;
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

		public PageTemplatesApi(SourceCollectionsList  sourceCollectionsList,BookSelection bookSelection, 
			PageSelection pageSelection, TemplateInsertionCommand templateInsertionCommand,
			BookThumbNailer thumbNailer)
		{
			_sourceCollectionsList = sourceCollectionsList;
			_bookSelection = bookSelection;
			_pageSelection = pageSelection;
			_templateInsertionCommand = templateInsertionCommand;
			_thumbNailer = thumbNailer;
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("pageTemplates", HandleTemplatesRequest);
			server.RegisterEndpointHandler("pageTemplateThumbnail", HandleThumbnailRequest);
		}

		/// <summary>
		/// Returns a json string for initializing the AddPage dialog. It gives paths to our current TemplateBook
		/// and specifies whether the dialog is to be used for adding pages or choosing a different layout.
		/// </summary>
		/// <remarks>If forChooseLayout is true, page argument is required.</remarks>
		public void HandleTemplatesRequest(ApiRequest request)
		{
			dynamic addPageSettings = new ExpandoObject();
			addPageSettings.defaultPageToSelect = _templateInsertionCommand.MostRecentInsertedTemplatePage == null ? "" : _templateInsertionCommand.MostRecentInsertedTemplatePage.Id;
			addPageSettings.orientation = _bookSelection.CurrentSelection.GetLayout().SizeAndOrientation.IsLandScape ? "landscape" : "portrait";

			var groups = GetBookTemplatePaths(GetPathToCurrentTemplateHtml(), _sourceCollectionsList.GetSourceBookPaths())
				.Select(bookTemplatePath => GetPageGroup(bookTemplatePath)).ToList();
			addPageSettings.groups = groups.ToArray();
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
		/// <param name="path"></param>
		/// <returns>Should always return true, unless we really can't come up with an image at all.</returns>
		private string FindOrGenerateThumbnail(string path)
		{
			var localPath = AdjustPossibleLocalHostPathToFilePath(path);
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
			// This is the one remaining place where the EIS is aware that there is such a thing as a current book.
			// Unfortunately it is part of a complex bit of logic that mostly doesn't have to do with current book,
			// so it doesn't feel right to move it to CurrentBookHandler, especially as it's not possible to
			// identify the queries which need the knowledge in the usual way (by a leading URL fragment).
			if (_bookSelection.CurrentSelection == null)
				return ""; // paranoia
			var template = _bookSelection.CurrentSelection.FindTemplateBook();
			if (template == null)
				return ""; // paranoia
			var caption = Path.GetFileNameWithoutExtension(path).Trim();
			var isLandscape = caption.EndsWith("-landscape"); // matches string in page-chooser.ts
			if (isLandscape)
				caption = caption.Substring(0, caption.Length - "-landscape".Length);

			// The Replace of & with + corresponds to a replacement made in page-chooser.ts method loadPagesFromCollection.
			var templatePage = template.GetPages().FirstOrDefault(page => page.Caption.Replace("&", "+") == caption);
			if (templatePage == null)
				templatePage = template.GetPages().FirstOrDefault(); // may get something useful?? or throw??

			var image = _thumbNailer.GetThumbnailForPage(template, templatePage, isLandscape);

			// The clone here is an attempt to prevent an unexplained exception complaining that the source image for the bitmap is in use elsewhere.
			using (var b = new Bitmap((Image)image.Clone()))
			{
				try
				{
					Directory.CreateDirectory(Path.GetDirectoryName(pngpath));
					b.Save(pngpath);
					return pngpath;
				}
				catch (Exception)
				{
					return "";
				}
			}
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
