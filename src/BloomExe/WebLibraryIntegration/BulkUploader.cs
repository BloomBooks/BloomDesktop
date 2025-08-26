using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Publish;
using Bloom.Publish.BloomLibrary;
using Bloom.Publish.PDF;
using Bloom.SubscriptionAndFeatures;
using BloomTemp;
using L10NSharp;
using SIL.IO;
using SIL.Progress;

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

        public const string HashInfoFromLastUpload = ".lastUploadInfo"; // this filename must begin with a period
        public bool LoggedIn => _singleBookUploader.BloomLibraryBookApiClient.LoggedIn;

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
        public async Task BulkUpload(ApplicationContainer container, UploadParameters options)
        {
            BookUpload.Destination = options.Dest;

            using (var progress = new MultiProgress())
            {
                var logFilePath = Path.Combine(options.Path, "BloomBulkUploadLog.txt");

                progress.Add(new Bloom.Utils.ConsoleProgress());

                progress.Add(new FileLogProgress(logFilePath));

                Debug.Assert(!String.IsNullOrWhiteSpace(options.UploadUser));

                if (
                    !_singleBookUploader.BloomLibraryBookApiClient.AttemptSignInAgainForCommandLine(
                        options.UploadUser,
                        options.Dest,
                        progress
                    )
                )
                {
                    progress.WriteError("Problem logging in. See messages above.");
                    System.Environment.Exit(1);
                }

                progress.WriteMessage("Uploading books as user {0}", options.UploadUser);

                var bookParams = new BookUploadParameters(options);

                BulkRepairInstanceIds(
                    options.Path,
                    path =>
                    {
                        // If we find duplicate IDs, we need to evaluate whether the books involved can have their IDs changed safely.
                        var parent = Path.GetDirectoryName(path);
                        if (
                            !CollectionSettings.TryGetSettingsFilePath(
                                parent,
                                out var collectionPath
                            )
                        )
                        {
                            return true; // weird situation, but it's not in a TC so we can update the ID if we want.
                        }

                        using (
                            ProjectContext testContext = container.CreateProjectContext(
                                collectionPath
                            )
                        )
                        {
                            var tc = testContext.TeamCollectionManager?.CurrentCollection;
                            if (tc == null)
                                return true; // not in a TC, we can fix ID
                            return !tc.IsBookPresentInRepo(Path.GetFileName(path));
                        }
                    }
                );
                ProjectContext context = null; // Expensive to create; hold each one we make until we find a book that needs a different one.
                try
                {
                    _collectionFoldersUploaded = new HashSet<string>();
                    _newBooksUploaded = 0;
                    _booksUpdated = 0;
                    _booksSkipped = 0;
                    _booksWithErrors = 0;

                    progress.WriteMessageWithColor(
                        "green",
                        $"\n\nStarting upload at {DateTime.Now.ToString()}\n"
                    );

                    progress.WriteMessageWithColor(
                        "Magenta",
                        $"Looking in '{bookParams.Folder}'..."
                    );
                    context = await UploadCollectionOrKeepLookingDeeper(
                        progress,
                        container,
                        bookParams,
                        context
                    );

                    if (_collectionFoldersUploaded.Count > 0)
                    {
                        progress.WriteMessageWithColor("green", "\n\nAll finished!");
                        progress.WriteMessage(
                            "Processed {0} collection folders.",
                            _collectionFoldersUploaded.Count
                        );
                    }
                    else
                    {
                        progress.WriteError("Did not find any collections to upload.");
                    }

                    progress.WriteMessage("Uploaded {0} new books.", _newBooksUploaded);
                    progress.WriteMessage("Updated {0} books that had changed.", _booksUpdated);
                    progress.WriteMessage("Skipped {0} books that had not changed.", _booksSkipped);
                    if (_booksSkipped > 0)
                    {
                        progress.WriteMessage(
                            "(If you don't want Bloom to skip books it thinks have not changed, you can use the --force argument to force all books to re-upload, or just use the Bloom UI to force upload this one book)."
                        );
                    }

                    if (_booksWithErrors > 0)
                    {
                        progress.WriteError(
                            "Failed to upload {0} books. See \"{1}\" for details.",
                            _booksWithErrors,
                            logFilePath
                        );
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
            return lastFolderPart != null
                && lastFolderPart.StartsWith(".", StringComparison.Ordinal);
        }

        /// <summary>
        /// Handles the recursion through directories: if a folder looks like a Bloom book upload it; otherwise, try its children.
        /// Invisible folders like .hg are ignored.
        /// </summary>
        private async Task<ProjectContext> UploadCollectionOrKeepLookingDeeper(
            IProgress progress,
            ApplicationContainer container,
            BookUploadParameters uploadParams,
            ProjectContext context
        )
        {
            if (IsPrivateFolder(uploadParams.Folder))
                return context;

            if (
                CollectionSettings.TryGetSettingsFilePath(
                    uploadParams.Folder,
                    out var collectionPath
                )
            )
            {
                var settings = new CollectionSettings(collectionPath);
                if (string.IsNullOrEmpty(settings.DefaultBookshelf))
                {
                    // My thinking here is that if we are bothering to do a bulk upload, they should have set a
                    // default bookshelf. If this expectation proves false, then we can just add an argument
                    // to disable it. For Kyrgyzstan, missing bookshelves was a problem I needed to catch.
                    progress.WriteError(
                        $"Skipping {uploadParams.Folder} because there is no default bookshelf."
                    );
                    return context;
                }
                var featureStatus = FeatureStatus.GetFeatureStatus(
                    settings.Subscription,
                    FeatureName.BulkUpload
                );
                if (!featureStatus.Enabled)
                {
                    progress.WriteError(
                        $"Skipping {uploadParams.Folder} because bulk upload requires a Bloom subscription tier of at least \"{featureStatus.SubscriptionTier}\". "
                    );
                    return context;
                }
                context = await BulkUploadBooksOfOneCollection(
                    progress,
                    container,
                    uploadParams,
                    context
                );
                return context;
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
                        context = await UploadCollectionOrKeepLookingDeeper(
                            progress,
                            container,
                            childParams,
                            context
                        );
                    }
                }
            }

            return context;
        }

        private async Task<ProjectContext> BulkUploadBooksOfOneCollection(
            IProgress progress,
            ApplicationContainer container,
            BookUploadParameters uploadParams,
            // If passed a context for the right collection, we return it; otherwise, we may create and return a new one.
            ProjectContext context
        )
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
                        context = await UploadBookInternal(
                            progress,
                            container,
                            bookParams,
                            context
                        );
                    }
                    catch (Exception e)
                    {
                        var msg = String.Format(
                            "{0} was not uploaded due to error: {1}",
                            sub,
                            e.Message
                        );
                        progress.WriteError(msg);
                        progress.WriteException(e);
                        ++_booksWithErrors;
                    }
                }
                else
                {
                    if (htmlFileCount > 1)
                    {
                        progress.WriteError(
                            $"{sub} has {htmlFileCount} html files. One of them should be removed."
                        );
                        ++_booksWithErrors;
                    }
                    else
                    {
                        ReportSuspiciousFilesInFolderLackingHtml(progress, sub);
                    }
                }
            }

            return context;
        }

        private void ReportSuspiciousFilesInFolderLackingHtml(IProgress progress, string folder)
        {
            if (Directory.GetFiles(folder, "origami.css").Length > 0)
            {
                progress.WriteWarning(
                    $"{folder} has no html but has origami.css. This is highly suspicious."
                );
            }

            if (Directory.GetFiles(folder, "origami.css").Length > 0)
            {
                progress.WriteWarning(
                    $"{folder} has no html but has origami.css. This is highly suspicious."
                );
            }

            if (Directory.GetFiles(folder, "*.png").Length > 0)
            {
                progress.WriteWarning(
                    $"{folder} has no html but has a png. This is highly suspicious."
                );
            }

            if (Directory.GetFiles(folder, "*.jpg").Length > 0)
            {
                progress.WriteWarning(
                    $"{folder} has no html but has a jpg. This is highly suspicious."
                );
            }
        }

        private async Task<ProjectContext> UploadBookInternal(
            IProgress progress,
            ApplicationContainer container,
            BookUploadParameters uploadParams,
            // If passed a context for the right collection, we return it; otherwise, we may create and return a new one.
            // We want a ref param, but async methods can't do that.
            ProjectContext context
        )
        {
            progress.WriteMessageWithColor("Cyan", "Starting to upload " + uploadParams.Folder);
            // Make sure the files we want to upload are up to date.
            // Unfortunately this requires making a book object, which requires making a ProjectContext, which must be created with the
            // proper parent book collection if possible.
            var parent = Path.GetDirectoryName(uploadParams.Folder);
            if (!CollectionSettings.TryGetSettingsFilePath(parent, out var collectionPath))
            {
                progress.WriteError(
                    "Skipping book because no collection file was found in its parent directory."
                );
                return context;
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
            var bookInfo = new BookInfo(
                uploadParams.Folder,
                true,
                context.TeamCollectionManager.CurrentCollectionEvenIfDisconnected
                    ?? new AlwaysEditSaveContext() as ISaveContext
            );
            var book = server.GetBookFromBookInfo(bookInfo);
            book.BringBookUpToDate(new NullProgress());
            uploadParams.Folder = book.FolderPath; // BringBookUpToDate can change the title and folder (see BL-10330)
            book.Storage.CleanupUnusedSupportFiles(isForPublish: false); // we are publishing, but this is the real folder not a copy, so play safe.

            var existingBook = _singleBookUploader.GetBookOnServer(
                book.BookInfo.Id,
                out bool haveCollidingBooks
            );
            if (haveCollidingBooks)
            {
                // We will allow bulk upload to replace the (presumably only) book with this ID that has already been
                // uploaded, even if there are colliding books. We already have the problem on Blorg, and are making it
                // no worse. However, we won't allow a collision situation to be created or made worse by adding an
                // additional book with the same ID.
                if (existingBook == null)
                {
                    progress.WriteError(
                        $"Did not upload '{Path.GetFileName(uploadParams.Folder)}' because there is already at least one book with the same ID ('{book.BookInfo.Id}') in BloomLibrary. You can get more information by uploading it individually."
                    );
                    return context;
                }
                progress.WriteMessageWithColor(
                    "orange",
                    $"Warning: there are other books on BloomLibrary with the same id ('{book.BookInfo.Id}') as '{Path.GetFileName(uploadParams.Folder)}'. (Bloom replaced the one you uploaded anyway.)"
                );
            }

            // Compute the book hash file and compare it to the existing one for bulk upload.
            var currentHashes = BookUpload.HashBookFolder(uploadParams.Folder);
            progress.WriteMessage(currentHashes);
            var pathToLocalHashInfoFromLastUpload = Path.Combine(
                uploadParams.Folder,
                HashInfoFromLastUpload
            );
            if (!uploadParams.ForceUpload)
            {
                var canSkip = false;
                if (Program.RunningUnitTests)
                {
                    canSkip = _singleBookUploader.CheckAgainstLocalHashfile(
                        currentHashes,
                        pathToLocalHashInfoFromLastUpload
                    );
                }
                else if (existingBook != null)
                {
                    var s3Prefix = BloomS3Client.GetPrefixFromBaseUrl(existingBook.baseUrl.Value);
                    canSkip = _singleBookUploader.CheckAgainstHashFileOnS3(
                        currentHashes,
                        uploadParams.Folder,
                        s3Prefix,
                        progress
                    );
                    RobustFile.WriteAllText(pathToLocalHashInfoFromLastUpload, currentHashes); // ensure local copy is saved
                }
                if (canSkip)
                {
                    // local copy of hashes file is identical or has been saved
                    progress.WriteMessageWithColor(
                        "green",
                        $"Skipping '{Path.GetFileName(uploadParams.Folder)}' because it has not changed since being uploaded."
                    );
                    ++_booksSkipped;
                    return context; // skip this one; we already uploaded it earlier.
                }
            }
            // save local copy of hashes file: it will be uploaded with the other book files
            RobustFile.WriteAllText(pathToLocalHashInfoFromLastUpload, currentHashes);

            bookInfo.Bookshelf = book.CollectionSettings.DefaultBookshelf;
            var bookshelfName = String.IsNullOrWhiteSpace(book.CollectionSettings.DefaultBookshelf)
                ? "(none)"
                : book.CollectionSettings.DefaultBookshelf;
            progress.WriteMessage($"Bookshelf is '{bookshelfName}'");

            // Assemble the various arguments needed to make the objects normally involved in an upload.
            // We leave some constructor arguments not actually needed for this purpose null.
            var bookSelection = new BookSelection();
            bookSelection.SelectBook(book);
            var currentEditableCollectionSelection = new CurrentEditableCollectionSelection();

            var collection = new BookCollection(
                collectionPath,
                BookCollection.CollectionType.SourceCollection,
                bookSelection,
                context.TeamCollectionManager
            );
            currentEditableCollectionSelection.SelectCollection(collection);

            var publishModel = new PublishModel(
                bookSelection,
                new PdfMaker(),
                currentEditableCollectionSelection,
                context.Settings,
                server,
                _thumbnailer
            );
            var blPublishModel = new BloomLibraryPublishModel(
                _singleBookUploader,
                book,
                publishModel
            );

            if (book.BookInfo.PublishSettings.BloomLibrary.TextLangs.Count == 0)
            {
                BloomLibraryPublishModel.InitializeLanguages(book);
            }

            var hasAtLeastOneLanguageToUpload = book
                .BookInfo.PublishSettings.BloomLibrary.TextLangs.IncludedLanguages()
                .Any();
            if (!hasAtLeastOneLanguageToUpload && BookUpload.GetVideoFilesToInclude(book).Any())
            {
                hasAtLeastOneLanguageToUpload = book
                    .BookInfo.PublishSettings.BloomLibrary.SignLangs.IncludedLanguages()
                    .Any();
            }

            if (
                blPublishModel.MetadataIsReadyToPublish
                && (hasAtLeastOneLanguageToUpload || blPublishModel.OkToUploadWithNoLanguages)
            )
            {
                bool updatingBook = existingBook != null; // this is a live value, so make local copy.
                if (updatingBook)
                {
                    var msg = $"Overwriting the copy of {uploadParams.Folder} on the server...";
                    progress.WriteWarning(msg);
                }
                var uploadResult = "";
                using (
                    var tempFolder = new TemporaryFolder(
                        Path.Combine("BloomUpload", Path.GetFileName(book.FolderPath))
                    )
                )
                {
                    BookUpload.PrepareBookForUpload(
                        ref book,
                        server,
                        tempFolder.FolderPath,
                        progress
                    );

                    // On success, returns the book objectId; on failure, returns empty string
                    uploadResult = await _singleBookUploader.FullUpload(
                        book,
                        progress,
                        publishModel,
                        uploadParams,
                        existingBook?.id.Value
                    );
                }

                if (string.IsNullOrEmpty(uploadResult) || uploadResult == "quiet")
                {
                    progress.WriteError("{0} was not uploaded.", uploadParams.Folder);
                    ++_booksWithErrors;
                }
                else
                {
                    progress.WriteMessageWithColor(
                        "Green",
                        "{0} has been uploaded",
                        uploadParams.Folder
                    );
                    if (updatingBook)
                        ++_booksUpdated;
                    else
                        ++_newBooksUploaded;
                }
            }
            else
            {
                // report to the user why we are not uploading their book
                var reason = blPublishModel.GetReasonForNotUploadingBook();
                progress.WriteError("{0} was not uploaded.  {1}", uploadParams.Folder, reason);
                ++_booksWithErrors;
            }

            return context;
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
        internal static void BulkRepairInstanceIds(
            string rootFolderPath,
            Func<string, bool> okToChangeId
        )
        {
            BookInfo.CheckForDuplicateInstanceIdsAndRepair(rootFolderPath, okToChangeId);
        }
    }
}
