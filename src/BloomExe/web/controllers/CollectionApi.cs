using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionCreating;
using Bloom.CollectionTab;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.Utils;
using Bloom.Workspace;
using L10NSharp;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Linq;

namespace Bloom.web.controllers
{

	public class CollectionApi
	{
		private readonly CollectionSettings _settings;
		private readonly CollectionModel _collectionModel;
		public const string kApiUrlPart = "collections/";
		private readonly BookSelection _bookSelection;
		private BookThumbNailer _thumbNailer;
		private BloomWebSocketServer _webSocketServer;
		private readonly EditBookCommand _editBookCommand;

		private int _thumbnailEventsToWaitFor = -1;

		private object _thumbnailEventsLock = new object();
		// public so that WorkspaceView can set it in constructor.
		// We'd prefer to just let the WorkspaceView be a constructor arg passed to this by Autofac,
		// but that throws an exception, probably there is some circularity.
		public WorkspaceView WorkspaceView;
		public 	 CollectionApi(CollectionSettings settings, CollectionModel collectionModel, BookSelection bookSelection, EditBookCommand editBookCommand, BookThumbNailer thumbNailer, BloomWebSocketServer webSocketServer)
		{
			_settings = settings;
			_collectionModel = collectionModel;
			_bookSelection = bookSelection;
			_editBookCommand = editBookCommand;
			_thumbNailer = thumbNailer;
			_webSocketServer = webSocketServer;
		}


		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "list", HandleListRequest, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "name", request =>
			{
				// always null? request.ReplyWithText(_collection.Name);
				request.ReplyWithText(_settings.CollectionName);
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "books", HandleBooksRequest, false, false);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "book/thumbnail", HandleThumbnailRequest, false, false);

			// Note: the get part of this doesn't need to run on the UI thread, or even requiresSync. If it gets called a lot, consider
			// using different patterns for get and set so we can not use the uI thread for get.
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "selected-book-id", request =>
			{
				switch (request.HttpMethod)
				{
					case HttpMethods.Get:
						request.ReplyWithText("" + _collectionModel.GetSelectedBookOrNull()?.ID);
						break;
					case HttpMethods.Post:
						// We're selecting the book, make sure everything is up to date.
						// This first method does minimal processing to come up with the right collection and BookInfo object
						// without actually loading all the files. We need the title for the Performance Measurement and later
						// we'll use the BookInfo object to get the fully updated book.
						var newBookInfo = GetBookInfoFromPost(request);
						var titleString = newBookInfo.QuickTitleUserDisplay;
						using (PerformanceMeasurement.Global?.Measure("select book", titleString))
						{
							// We could just put the PerformanceMeasurement in the CollectionModel.SelectBook() method,
							// but this GetUpdatedBookObjectFromBookInfo() actually does a non-trivial amount of work,
							// because it asks the CollectionModel to update the book files (including BringBookUpToDate).
							var book = GetUpdatedBookObjectFromBookInfo(newBookInfo);
							if (book.FolderPath != _bookSelection?.CurrentSelection?.FolderPath)
							{
								_collectionModel.SelectBook(book);
							}
						}

						request.PostSucceeded();
						break;
				}
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "selectAndEditBook", request =>
			{
				var book = GetBookObjectFromPost(request, true);
				if (book.FolderPath != _bookSelection?.CurrentSelection?.FolderPath)
				{
					_collectionModel.SelectBook(book);
				}
				if (book.IsSaveable && GetCollectionOfRequest(request).Type == BookCollection.CollectionType.TheOneEditableCollection)
				{
					_editBookCommand.Raise(_bookSelection.CurrentSelection);
				}

				request.PostSucceeded();
			}, true);


			apiHandler.RegisterEndpointHandler(kApiUrlPart + "duplicateBook/", (request) =>
			{
				_collectionModel.DuplicateBook(GetBookObjectFromPost(request));
				request.PostSucceeded();
			}, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "deleteBook/", (request) =>
			{
				var collection = GetCollectionOfRequest(request);
				if (_collectionModel.DeleteBook(GetBookObjectFromPost(request), collection))
					request.PostSucceeded();
				else
					request.Failed();
			}, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "collectionProps/", HandleCollectionProps, false, false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "makeShellBooksBloompack/", (request) =>
				{
					_collectionModel.MakeBloomPack(false);
					request.PostSucceeded();
				}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "makeBloompack/", (request) =>
				{
					_collectionModel.MakeReaderTemplateBloompack();
					request.PostSucceeded();
				},
				true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "doChecksOfAllBooks/", (request) =>
				{
					_collectionModel.DoChecksOfAllBooks();
					request.PostSucceeded();
				},
				true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "rescueMissingImages/", (request) =>
				{
					_collectionModel.RescueMissingImages();
					request.PostSucceeded();
				},
				true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "doUpdatesOfAllBooks/", (request) =>
				{
					_collectionModel.DoUpdatesOfAllBooks();
					request.PostSucceeded();
				},
				true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "removeSourceCollection", HandleRemoveSourceCollection, false);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "addSourceCollection", HandleAddSourceCollection, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "removeSourceFolder", HandleRemoveSourceFolder, true);
		}

		private void HandleRemoveSourceCollection(ApiRequest request)
		{
			var collectionFolderPath = request.RequiredPostString();
			var filename = Path.GetFileName(collectionFolderPath);
			var collectionsFolder = ProjectContext.GetInstalledCollectionsDirectory();
			var linkFile = Path.Combine(collectionsFolder, filename + ".lnk");
			if (RobustFile.Exists(linkFile))
			{
				RobustFile.Delete(linkFile);
				_collectionModel.ReloadCollections();
				request.PostSucceeded();
			}
			else
			{
				request.Failed();
			}
		}

		bool _updateAfterExplorerOpened;
		private void HandleRemoveSourceFolder(ApiRequest request)
		{
			var collectionFolderPath = request.RequiredPostString();
			if (Directory.Exists(collectionFolderPath))
			{
				request.PostSucceeded();
				var startInfo = new ProcessStartInfo
				{
					Arguments = $"/select, \"{collectionFolderPath}\"",
					FileName = "explorer.exe"
				};
				_updateAfterExplorerOpened = true;
				Process.Start(startInfo);
			}
			else
			{
				request.Failed();
				return;
			}
		}

		internal void CheckForCollectionUpdates()
		{
			if (_updateAfterExplorerOpened)
			{
				// trigger a list request?.
				_collectionModel.ReloadCollections();
				dynamic result = new DynamicJson();
				result.success = true;
				result.list = GetCollectionList();
				_webSocketServer.SendBundle("collections", "updateCollectionList", result);
			}
		}

		internal void ResetUpdatingList()
		{
			_updateAfterExplorerOpened = false;
		}

		private void HandleAddSourceCollection(ApiRequest request)
		{
			if (!Directory.Exists(NewCollectionWizard.DefaultParentDirectoryForCollections))
			{
				Directory.CreateDirectory(NewCollectionWizard.DefaultParentDirectoryForCollections);
			}
			// We send the result through a websocket rather than simply returning it because
			// if the user is very slow (one site said FF times out after 90s) the browser may
			// abandon the request before it completes. The POST result is ignored and the
			// browser simply listens to the socket.
			request.PostSucceeded();
			var pathToCollectionFile = "";
			using (var dlg = new DialogAdapters.OpenFileDialogAdapter())
			{
				dlg.Title = LocalizationManager.GetString("CollectionTab.ChooseCollection", "Choose Collection",
					"This is the title of the file-open dialog that you use to choose a Bloom collection");
				dlg.Filter = LocalizationManager.GetString("OpenCreateNewCollectionsDialog.Bloom Collections", "Bloom Collections",
					"This shows in the file-open dialog that you use to open a different bloom collection") + @"|*.bloomLibrary;*.bloomCollection";
				dlg.InitialDirectory = NewCollectionWizard.DefaultParentDirectoryForCollections;
				dlg.CheckFileExists = true;
				dlg.CheckPathExists = true;
				if (dlg.ShowDialog() == DialogResult.Cancel)
					return;
				pathToCollectionFile = dlg.FileName;
			}
			var pathToCollectionDirectory = Path.GetDirectoryName(pathToCollectionFile);
			var collectionName = Path.GetFileNameWithoutExtension(pathToCollectionFile);
			var collectionsFolder = ProjectContext.GetInstalledCollectionsDirectory();
			// This overwrites any existing shortcut with the same collectionName.
			ShortcutMaker.CreateDirectoryShortcut(pathToCollectionDirectory, collectionsFolder);
			dynamic result = new DynamicJson();
			result.success = !String.IsNullOrEmpty(pathToCollectionFile);
			result.collection = new DynamicJson();
			result.collection.id = pathToCollectionDirectory;
			result.collection.name = collectionName;
			result.collection.isSourceCollection = false; // nothing is a "source collection" any longer.  but everything can be.
			result.collection.shouldLocalizeName = false;
			result.collection.isLink = true;
			result.collection.isRemovableFolder = false;
			_collectionModel.ReloadCollections();
			_webSocketServer.SendBundle("collections", "addSourceCollection-results", result);
		}

		// needs to be thread-safe
		private void HandleCollectionProps(ApiRequest request)
		{
			var collection = GetCollectionOfRequest(request);
			if (collection == null)
				return;	// request.Failed() has already been signaled.
			dynamic props = new ExpandoObject();
			props.isFactoryInstalled = collection.IsFactoryInstalled;
			props.containsDownloadedBooks = collection.ContainsDownloadedBooks;
			request.ReplyWithJson(JsonConvert.SerializeObject(props));
		}

		// List out all the collections we have loaded
		public void HandleListRequest(ApiRequest request)
		{
			dynamic output = GetCollectionList();
			request.ReplyWithJson(JsonConvert.SerializeObject(output));
		}

		private dynamic GetCollectionList()
		{
			dynamic output = new List<dynamic>();
			_collectionModel.GetBookCollections().ForEach(c =>
			{
				Debug.WriteLine($"collection: {c.Name}-->{c.PathToDirectory}");
				// For this purpose there's no point in returning empty collections
				// (except the editable one and books from bloom library, which might add items later),
				// and in particular this filters out our xmatter folders which aren't really
				// collections.
				if (c.Type == BookCollection.CollectionType.TheOneEditableCollection || c.ContainsDownloadedBooks || c.GetBookInfos().Any())
				{
					output.Add(
						new
						{
							id = c.PathToDirectory,
							name = c.Name,
							isSourceCollection = _collectionModel.IsSourceCollection,
							shouldLocalizeName = c.PathToDirectory.StartsWith(BloomFileLocator.FactoryCollectionsDirectory) || c.ContainsDownloadedBooks,
							isLink = c.Type != BookCollection.CollectionType.TheOneEditableCollection && IsFromLinkFile(c.PathToDirectory),
							isRemovableFolder = c.Type != BookCollection.CollectionType.TheOneEditableCollection && !IsFromLinkFile(c.PathToDirectory) &&
								!c.ContainsDownloadedBooks && !c.PathToDirectory.StartsWith(BloomFileLocator.FactoryCollectionsDirectory)
						});
				}
			});
			return output;
		}

		private bool IsFromLinkFile(string collectionFolderPath)
		{
			var collectionsFolder = ProjectContext.GetInstalledCollectionsDirectory();
			if (collectionFolderPath.StartsWith(collectionsFolder))
				return false;
			var linkFile = Path.Combine(collectionsFolder, Path.GetFileName(collectionFolderPath) + ".lnk");
			return RobustFile.Exists(linkFile);
		}

		public void HandleBooksRequest(ApiRequest request)
		{
			var collection = GetCollectionOfRequest(request);
			if (collection == null)
			{
				return; // have already called request failed at this point
			}

			// Note: the winforms version used ImproveAndRefreshBookButtons(), which may load the whole book.

			var bookInfos = collection.GetBookInfos();
			var jsonInfos = bookInfos
				.Where(info => collection.Type == BookCollection.CollectionType.TheOneEditableCollection || info.ShowThisBookAsSource())
				.Select(info =>
				{
					var title = info.QuickTitleUserDisplay;
					if (collection.IsFactoryInstalled)
						title = LocalizationManager.GetDynamicString("Bloom", "TemplateBooks.BookName." + title, title);
					return new
						{id = info.Id, title, collectionId = collection.PathToDirectory, folderPath = info.FolderPath, isFactory = collection.IsFactoryInstalled };
				}).ToArray();

			// The goal here is to draw the book buttons before we tie up the UI thread for a long time loading
			// the previously selected book and showing it. So we're going to wait until we get a few requests
			// for the thumbnails used in book buttons. But we need to be sure we will actually get those requests,
			// otherwise, doing the selected book stuff and hiding the splash screen and so on could be permanently
			// prevented. Unfortunately, the buttons are lazy, and in a pathological case the user might drag the
			// splitters and size the window so only one of these buttons is visible. Even in such a case, there
			// should be at least one more visible in the templates area. So I think we can be confident of
			// getting at least two requests. Fortunately, waiting for those seems to be enough to make it look
			// as if we're prioritizing the whole primary collection.
			// We need the count to be at least one, even if the main collection is empty, so that the milestone
			// will always be reported. 
			lock (_thumbnailEventsLock)
			{
				if (collection.Type == BookCollection.CollectionType.TheOneEditableCollection)
					_thumbnailEventsToWaitFor = Math.Max(Math.Min(jsonInfos.Length, 2), 1);
			}

			var json = DynamicJson.Serialize(jsonInfos);
			request.ReplyWithJson(json);
		}

		// This needs to be thread-safe.
		private BookCollection GetCollectionOfRequest(ApiRequest request)
		{
			var id = request.RequiredParam("collection-id").Trim();
			var collection = _collectionModel.GetBookCollections().FirstOrDefault(c => c.PathToDirectory == id);
			if (collection == null)
			{
				request.Failed($"Collection named '{id}' was not found.");
			}

			return collection;
		}

		public void HandleThumbnailRequest(ApiRequest request)
		{
			var bookInfo = GetBookInfoFromRequestParam(request);
			lock (_thumbnailEventsLock)
			{
				if (_thumbnailEventsToWaitFor > 0)
				{
					_thumbnailEventsToWaitFor--;
					if (_thumbnailEventsToWaitFor == 0)
						StartupScreenManager.StartupMilestoneReached("collectionButtonsDrawn");
				}
			}

			string path = Path.Combine(bookInfo.FolderPath, "thumbnail.png");
			if (!RobustFile.Exists(path))
			{
				// This is rarely if ever needed. Bloom already does this when selecting a book
				// or after editing it. One case (but it won't succeed) is when the book doesn't
				// HAVE an image on the cover.
				_thumbNailer.MakeThumbnailOfCover(_collectionModel.GetBookFromBookInfo(bookInfo));
			}
			if (RobustFile.Exists(path))
				request.ReplyWithImage(path);
			else
			{
				var errorImg = Resources.placeHolderBookThumbnail;
				if (_collectionModel.GetBookFromBookInfo(bookInfo).HasFatalError)
					errorImg = Resources.Error70x70;
				var stream = new MemoryStream();
				errorImg.Save(stream, ImageFormat.Png);
				stream.Seek(0L, SeekOrigin.Begin);
				request.ReplyWithStreamContent(stream, "image/png");
				//request.Failed("Thumbnail doesn't exist, and making a new thumbnail is not yet implemented.");
			}
		}

		private BookInfo GetBookInfoFromRequestParam(ApiRequest request)
		{
			var bookId = request.RequiredParam("book-id");
			return GetCollectionOfRequest(request).GetBookInfos().FirstOrDefault(info => info.Id == bookId);
		}

		// Needs to be thread-safe
		private BookInfo GetBookInfoFromPost(ApiRequest request)
		{
			var bookId = request.RequiredPostString();
			return GetCollectionOfRequest(request).GetBookInfos().FirstOrDefault(info => info.Id == bookId);
		}

		private Book.Book GetBookObjectFromPost(ApiRequest request, bool fullyUpdateBookFiles = false)
		{
			var info = GetBookInfoFromPost(request);
			return _collectionModel.GetBookFromBookInfo(info, fullyUpdateBookFiles);

		}

		private Book.Book GetUpdatedBookObjectFromBookInfo(BookInfo info)
		{
			return _collectionModel.GetBookFromBookInfo(info, true);
		}
	}
}
