using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Publish;
using Bloom.Publish.BloomLibrary;
using Bloom.Publish.PDF;
using L10NSharp;
using SIL.IO;
using SIL.Progress;
using BloomTemp;

namespace Bloom.WebLibraryIntegration
{
	public class BulkUploader
	{
		private readonly BookUpload _singleBookUploader;
		
		private readonly BookThumbNailer _thumbnailer;
		public IProgress Progress;

		private HashSet<string> _collectionFoldersUploaded;
		private int _newBooksUploaded;
		private int _booksUpdated;
		private int _booksSkipped;
		private int _booksWithErrors;

		public const string HashInfoFromLastUpload = ".lastUploadInfo";   // this filename must begin with a period
		public bool LoggedIn => _singleBookUploader.ParseClient.LoggedIn;
		

		public BulkUploader(BookUpload singleBookUploader)
		{
			_singleBookUploader = singleBookUploader;
			_thumbnailer = singleBookUploader._thumbnailer;
		}

		/// <summary>
		/// Upload bloom books in the specified folder to the bloom library.
		/// Folders that contain exactly one .htm file are interpreted as books and uploaded.
		/// Other folders are searched recursively for children that appear to be bloom books.
		/// The parent folder of a bloom book is searched for a .bloomCollection file and, if one is found,
		/// the book is treated as part of that collection (e.g., for determining vernacular language).
		/// If the .bloomCollection file is not found, the book is not uploaded.
		/// N.B. The bulk upload process will go ahead and upload templates and books that are already on the server
		/// (over-writing the existing book) without informing the user.
		/// </summary>
		/// <remarks>This method is triggered by starting Bloom with "upload" on the cmd line.</remarks>
		public void BulkUpload(ApplicationContainer container, UploadParameters options)
		{
			BookUpload.Destination = options.Dest;

			using (var progress = new MultiProgress())
			{
				var logFilePath = Path.Combine(options.Path, "BloomBulkUploadLog.txt");

				progress.Add(new Bloom.Utils.ConsoleProgress());
				
				progress.Add(new FileLogProgress(logFilePath));

				if (!_singleBookUploader.IsThisVersionAllowedToUpload())
				{
					var oldVersionMsg = LocalizationManager.GetString("PublishTab.Upload.OldVersion",
						"Sorry, this version of Bloom Desktop is not compatible with the current version of BloomLibrary.org. Please upgrade to a newer version.");
					progress.WriteMessage(oldVersionMsg);
					return;
				}

				Debug.Assert(!String.IsNullOrWhiteSpace(options.UploadUser));

				if (!_singleBookUploader.ParseClient.AttemptSignInAgainForCommandLine(options.UploadUser, options.Dest, progress))
				{
					progress.WriteError("Problem logging in. See messages above.");
					System.Environment.Exit(1); 
				}

				progress.WriteMessage("Uploading books as user {0}", options.UploadUser);
				
				var bookParams = new BookUploadParameters(options);

				BulkRepairInstanceIds(options.Path, path =>
				{
					// If we find duplicate IDs, we need to evaluate whether the books involved can have their IDs changed safely.
					var parent = Path.GetDirectoryName(path);
					var collectionPath = Directory.GetFiles(parent, "*.bloomCollection").FirstOrDefault();
					if (collectionPath == null || !RobustFile.Exists(collectionPath))
					{
						return true; // weird situation, but it's not in a TC so we can update the ID if we want.
					}

					using (ProjectContext testContext = container.CreateProjectContext(collectionPath))
					{
						var tc = testContext.TeamCollectionManager?.CurrentCollection;
						if (tc == null)
							return true; // not in a TC, we can fix ID
						return !tc.IsBookPresentInRepo(Path.GetFileName(path));
					}
				}); 
				ProjectContext
					context = null; // Expensive to create; hold each one we make until we find a book that needs a different one.
				try
				{
					_collectionFoldersUploaded = new HashSet<string>();
					_newBooksUploaded = 0;
					_booksUpdated = 0;
					_booksSkipped = 0;
					_booksWithErrors = 0;

					progress.WriteMessageWithColor("green", $"\n\nStarting upload at {DateTime.Now.ToString()}\n");

					progress.WriteMessageWithColor("Magenta", $"Looking in '{bookParams.Folder}'...");
					UploadCollectionOrKeepLookingDeeper(progress, container, bookParams, ref context);

					if (_collectionFoldersUploaded.Count > 0)
					{
						progress.WriteMessageWithColor("green", "\n\nAll finished!");
						progress.WriteMessage("Processed {0} collection folders.", _collectionFoldersUploaded.Count);
					}
					else
					{
						progress.WriteError("Did not find any collections to upload.");
					}

					progress.WriteMessage("Uploaded {0} new books.", _newBooksUploaded);
					progress.WriteMessage("Updated {0} books that had changed.", _booksUpdated);
					progress.WriteMessage("Skipped {0} books that had not changed.", _booksSkipped);
					if (_booksSkipped>0)
					{
						progress.WriteMessage("(If you don't want Bloom to skip books it thinks have not changed, you can use the --force argument to force all books to re-upload, or just use the Bloom UI to force upload this one book).");
					}

					if (_booksWithErrors > 0)
					{
						progress.WriteError("Failed to upload {0} books. See \"{1}\" for details.", _booksWithErrors,
							logFilePath);
					}
				}
				finally
				{
					context?.Dispose();
				}
			}
		}

		// identify folder or files like probably .hg or .lastUploadInfo
		private bool IsPrivateFolder(string path)
		{
			var lastFolderPart = Path.GetFileName(path);
			return lastFolderPart != null && lastFolderPart.StartsWith(".", StringComparison.Ordinal);
		}

		/// <summary>
		/// Handles the recursion through directories: if a folder looks like a Bloom book upload it; otherwise, try its children.
		/// Invisible folders like .hg are ignored.
		/// </summary>
		private void UploadCollectionOrKeepLookingDeeper(IProgress progress, ApplicationContainer container, BookUploadParameters uploadParams,
			ref ProjectContext context)
		{
			if (IsPrivateFolder(uploadParams.Folder))
				return;

			var collectionPath = Directory.GetFiles(uploadParams.Folder, "*.bloomCollection").FirstOrDefault();
			if (collectionPath != null)
			{
				var settings = new CollectionSettings(collectionPath);
				if (string.IsNullOrEmpty(settings.DefaultBookshelf))
				{
					// My thinking here is that if we are bothering to do a bulk upload, they should have set a
					// default bookshelf. If this expectation proves false, then we can just add an argument
					// to disable it. For Kyrgyzstan, missing bookshelves was a problem I needed to catch.
					progress.WriteError($"Skipping {uploadParams.Folder} because there is no default bookshelf.");
					return;
				}
				if (!settings.HaveEnterpriseFeatures)
				{
					progress.WriteError($"Skipping {uploadParams.Folder} because bulk upload is an Enterprise-only feature.");
					return;
				}
				BulkUploadBooksOfOneCollection(progress, container, uploadParams, ref context);
				return;
			}
			else // go looking deeper for collection folders
			{
		
				foreach (var sub in Directory.GetDirectories(uploadParams.Folder))
				{
					if (!IsPrivateFolder(uploadParams.Folder))
					{
						var childParams = uploadParams;
						childParams.Folder = sub;
						progress.WriteMessageWithColor("Magenta", $"\nLooking in '{sub}'...");
						UploadCollectionOrKeepLookingDeeper(progress, container, childParams, ref context);
					}
				}
			}
		}

		private void BulkUploadBooksOfOneCollection(IProgress progress, ApplicationContainer container,
			BookUploadParameters uploadParams,
			ref ProjectContext context)
		{
			foreach (var sub in Directory.GetDirectories(uploadParams.Folder))
			{
				var htmlFileCount = Directory.GetFiles(sub, "*.htm").Length;
				if (htmlFileCount == 1)
				{
					// Our (perhaps insufficient) definition of a book folder is that it has exactly 1 htm file.
					try
					{
						var bookParams = uploadParams;
						bookParams.Folder = sub;
						UploadBookInternal(progress, container, bookParams, ref context);
					}
					catch (Exception e)
					{
						var msg = String.Format("{0} was not uploaded due to error: {1}", sub, e.Message);
						progress.WriteError(msg);
						progress.WriteException(e);
						++_booksWithErrors;
					}
				}
				else
				{
					if (htmlFileCount > 1)
					{
						progress.WriteError($"{sub} has {htmlFileCount} html files. One of them should be removed.");
						++_booksWithErrors;
					}
					else
					{
						ReportSuspiciousFilesInFolderLackingHtml(progress, sub);
					}
				}
			}
		}

		private void ReportSuspiciousFilesInFolderLackingHtml(IProgress progress, string folder)
		{
			if (Directory.GetFiles(folder, "origami.css").Length > 0)
			{
				progress.WriteWarning(
					$"{folder} has no html but has origami.css. This is highly suspicious.");
			}

			if (Directory.GetFiles(folder, "origami.css").Length > 0)
			{
				progress.WriteWarning(
					$"{folder} has no html but has origami.css. This is highly suspicious.");
			}

			if (Directory.GetFiles(folder, "*.png").Length > 0)
			{
				progress.WriteWarning(
					$"{folder} has no html but has a png. This is highly suspicious.");
			}

			if (Directory.GetFiles(folder, "*.jpg").Length > 0)
			{
				progress.WriteWarning(
					$"{folder} has no html but has a jpg. This is highly suspicious.");
			}
		}

		private void UploadBookInternal(IProgress progress, ApplicationContainer container, BookUploadParameters uploadParams,
	ref ProjectContext context)
		{
			progress.WriteMessageWithColor("Cyan", "Starting to upload " + uploadParams.Folder);
			// Make sure the files we want to upload are up to date.
			// Unfortunately this requires making a book object, which requires making a ProjectContext, which must be created with the
			// proper parent book collection if possible.
			var parent = Path.GetDirectoryName(uploadParams.Folder);
			var collectionPath = Directory.GetFiles(parent, "*.bloomCollection").FirstOrDefault();
			if (collectionPath == null || !RobustFile.Exists(collectionPath))
			{
				progress.WriteError("Skipping book because no collection file was found in its parent directory.");
				return;
			}
			_collectionFoldersUploaded.Add(collectionPath);

			// Get the book content as up to date as possible, without any unused files so that
			// we can compute an accurate hash value.
			if (context == null || context.SettingsPath != collectionPath)
			{
				context?.Dispose();
				// optimise: creating a context seems to be quite expensive. Probably the only thing we need to change is
				// the collection. If we could update that in place...despite autofac being told it has lifetime scope...we would save some time.
				// Note however that it's not good enough to just store it in the project context. The one that is actually in
				// the autofac object (_scope in the ProjectContext) is used by autofac to create various objects, in particular, books.
				context = container.CreateProjectContext(collectionPath);
				Program.SetProjectContext(context);
			}
			var server = context.BookServer;
			var bookInfo = new BookInfo(uploadParams.Folder, true, context.TeamCollectionManager.CurrentCollectionEvenIfDisconnected ?? new AlwaysEditSaveContext() as ISaveContext);
			var book = server.GetBookFromBookInfo(bookInfo, fullyUpdateBookFiles: true);
			book.BringBookUpToDate(new NullProgress());
			uploadParams.Folder = book.FolderPath;	// BringBookUpToDate can change the title and folder (see BL-10330)
			book.Storage.CleanupUnusedSupportFiles(isForPublish: false); // we are publishing, but this is the real folder not a copy, so play safe.

			// Compute the book hash file and compare it to the existing one for bulk upload.
			var currentHashes = BookUpload.HashBookFolder(uploadParams.Folder);
			progress.WriteMessage(currentHashes);
			var pathToLocalHashInfoFromLastUpload = Path.Combine(uploadParams.Folder, HashInfoFromLastUpload);
			if (!uploadParams.ForceUpload)
			{
				var canSkip = false;
				if (Program.RunningUnitTests)
				{
					canSkip = _singleBookUploader.CheckAgainstLocalHashfile(currentHashes, pathToLocalHashInfoFromLastUpload);
				}
				else
				{
					canSkip = _singleBookUploader.CheckAgainstHashFileOnS3(currentHashes, uploadParams.Folder, progress);
					RobustFile.WriteAllText(pathToLocalHashInfoFromLastUpload, currentHashes); // ensure local copy is saved
				}
				if (canSkip)
				{
					// local copy of hashes file is identical or has been saved
					progress.WriteMessageWithColor("green", $"Skipping '{Path.GetFileName(uploadParams.Folder)}' because it has not changed since being uploaded.");
					++_booksSkipped;
					return; // skip this one; we already uploaded it earlier.
				}
			}
			// save local copy of hashes file: it will be uploaded with the other book files
			RobustFile.WriteAllText(pathToLocalHashInfoFromLastUpload, currentHashes);

			bookInfo.Bookshelf = book.CollectionSettings.DefaultBookshelf;
			var bookshelfName = String.IsNullOrWhiteSpace(book.CollectionSettings.DefaultBookshelf) ? "(none)" : book.CollectionSettings.DefaultBookshelf;
			progress.WriteMessage($"Bookshelf is '{bookshelfName}'");

			// Assemble the various arguments needed to make the objects normally involved in an upload.
			// We leave some constructor arguments not actually needed for this purpose null.
			var bookSelection = new BookSelection();
			bookSelection.SelectBook(book);
			var currentEditableCollectionSelection = new CurrentEditableCollectionSelection();

			var collection = new BookCollection(collectionPath, BookCollection.CollectionType.SourceCollection, bookSelection, context.TeamCollectionManager);
			currentEditableCollectionSelection.SelectCollection(collection);

			var publishModel = new PublishModel(bookSelection, new PdfMaker(), currentEditableCollectionSelection, context.Settings, server, _thumbnailer);
			var blPublishModel = new BloomLibraryPublishModel(_singleBookUploader, book, publishModel);

			if (book.BookInfo.PublishSettings.BloomLibrary.TextLangs.Count == 0)
			{
				BloomLibraryPublishModel.InitializeLanguages(book);
			}

			var hasAtLeastOneLanguageToUpload = book.BookInfo.PublishSettings.BloomLibrary.TextLangs.IncludedLanguages().Any();
			if (!hasAtLeastOneLanguageToUpload && BookUpload.GetVideoFilesToInclude(book).Any())
			{
				hasAtLeastOneLanguageToUpload = book.BookInfo.PublishSettings.BloomLibrary.SignLangs.IncludedLanguages().Any();
			}

			if (blPublishModel.MetadataIsReadyToPublish && (hasAtLeastOneLanguageToUpload || blPublishModel.OkToUploadWithNoLanguages))
			{
				bool updatingBook = blPublishModel.BookIsAlreadyOnServer;   // this is a live value, so make local copy.
				if (updatingBook)
				{
					var msg = $"Overwriting the copy of {uploadParams.Folder} on the server...";
					progress.WriteWarning(msg);
				}
				using (var tempFolder = new TemporaryFolder(Path.Combine("BloomUpload", Path.GetFileName(book.FolderPath))))
				{
					BookUpload.PrepareBookForUpload(ref book, server, tempFolder.FolderPath, progress);
					_singleBookUploader.FullUpload(book, progress, publishModel, uploadParams, out var _);
				}

				progress.WriteMessageWithColor("Green", "{0} has been uploaded", uploadParams.Folder);
				if (updatingBook)
					++_booksUpdated;
				else
					++_newBooksUploaded;
			}
			else
			{
				// report to the user why we are not uploading their book
				var reason = blPublishModel.GetReasonForNotUploadingBook();
				progress.WriteError("{0} was not uploaded.  {1}", uploadParams.Folder, reason);
				++_booksWithErrors;
			}
		}

		/// <summary>
		/// In the past we've had problems with users copying folders manually and creating derivative books with
		/// the same bookInstanceId guids. Then we try to bulk upload a folder structure with books like this and the
		/// duplicates overwrite whichever book got uploaded first.
		/// This method recurses through the folders under 'rootFolderPath' and keeps track of all the unique bookInstanceId
		/// guids. When a duplicate is found, we will call BookInfo.InstallFreshInstanceGuid().
		/// </summary>
		/// <remarks>Internal for testing.</remarks>
		/// <param name="rootFolderPath"></param>
		internal static void BulkRepairInstanceIds(string rootFolderPath, Func<string, bool> okToChangeId)
		{
			BookInfo.RepairDuplicateInstanceIds(rootFolderPath, okToChangeId);
		}
	}
}
