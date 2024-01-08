using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Amazon.Runtime;
using Amazon.S3;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Publish;
using Bloom.web;
using DesktopAnalytics;
using L10NSharp;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;

namespace Bloom.WebLibraryIntegration
{
    /// <summary>
    /// Currently pushes a book's metadata to Parse.com (a mongodb service) and files to Amazon S3.
    /// We are using both because Parse offers a more structured, query-able data organization
    /// that is useful for metadata, but does not allow large enough files for some of what we need.
    /// </summary>
    public class BookUpload
    {
        public BloomLibraryBookApiClient BloomLibraryBookApiClient;
        private BloomS3Client _s3Client;
        internal readonly BookThumbNailer _thumbnailer;
        private string _storageKeyOfBookFolderParentOnS3 = "";
        public IProgress Progress;

        public const string UploadHashesFilename = ".lastUploadInfo"; // this filename must begin with a period

        static string _destination;

        public BookUpload(
            BloomLibraryBookApiClient bloomLibraryBookApiClient,
            BloomS3Client bloomS3Client,
            BookThumbNailer htmlThumbnailer
        )
        {
            this.BloomLibraryBookApiClient = bloomLibraryBookApiClient;
            this._s3Client = bloomS3Client;
            _thumbnailer = htmlThumbnailer;
        }

        /// <summary>
        /// Implicitly use the sandbox as the destination target.  Can be explicitly overridden
        /// on the command line in upload commands.  See <see cref="Destination"/>.
        /// </summary>
        internal static bool UseSandboxByDefault
        {
            get
            {
#if DEBUG
                return true;
#else
                var temp = Environment.GetEnvironmentVariable("BloomSandbox");
                if (string.IsNullOrWhiteSpace(temp))
                    return false;
                temp = temp.ToLowerInvariant();
                return temp == "yes" || temp == "true" || temp == "y" || temp == "t";
#endif
            }
        }

        /// <summary>
        /// whereas we can *download* from anywhere regardless of production, debug, or unit test,
        /// or the environment variable "BloomSandbox", we currently only allow *uploading*
        /// to only one bucket depending on these things. This also does double duty for selecting
        /// the parse-server keys that are appropriate
        /// </summary>
        public static string UploadBucketNameForCurrentEnvironment
        {
            get
            {
                if (Program.RunningUnitTests)
                {
                    return BloomS3Client.UnitTestBucketName;
                }
                return BookUpload.UseSandbox
                    ? BloomS3Client.SandboxBucketName
                    : BloomS3Client.ProductionBucketName;
            }
        }

        private void DisplayNetworkUploadProblem(Exception e, IProgress progress)
        {
            var msg1 = LocalizationManager.GetString(
                "PublishTab.Upload.GenericUploadProblemNotice",
                "There was a problem uploading your book."
            );
            var msg2 = e.Message.Replace("{", "{{").Replace("}", "}}");
            progress.WriteError(msg1);
            progress.WriteError(msg2);
            progress.WriteVerbose(e.StackTrace);
        }

        private static Form ShellWindow
        {
            get { return Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell); }
        }

        public void Logout()
        {
            BloomLibraryBookApiClient.Logout();
        }

        public bool LoggedIn => BloomLibraryBookApiClient.LoggedIn;

        internal const string BloomS3UrlPrefix = "https://s3.amazonaws.com/";

        private string _uploadedBy;
        private string _accountWhenUploadedByLastSet;

        /// <summary>
        /// The string that should be used to indicate who is uploading books.
        /// When set, this is remembered until someone different logs in; when next
        /// retrieved, it resets to the new account.
        /// </summary>
        public string UploadedBy
        {
            get
            {
                if (_accountWhenUploadedByLastSet == BloomLibraryBookApiClient.Account)
                    return _uploadedBy;
                // If a different login has since occurred, default to uploaded by that account.
                UploadedBy = BloomLibraryBookApiClient.Account;
                return _uploadedBy;
            }
            set
            {
                _accountWhenUploadedByLastSet = BloomLibraryBookApiClient.Account;
                _uploadedBy = value;
            }
        }

        /// <summary>
        /// Only for use in tests
        /// </summary>
        internal string UploadBook_ForUnitTest(string bookFolder)
        {
            return UploadBook(
                bookFolder,
                new NullProgress(),
                null,
                null,
                true,
                true,
                null,
                null,
                null,
                null,
                null
            );
        }

        internal string UploadBook_ForUnitTest(
            string bookFolder,
            out string storageKeyOfBookFolderParentOnS3,
            IProgress progress = null,
            string existingBookObjectId = null
        )
        {
            if (progress == null)
                progress = new NullProgress();
            var result = UploadBook(
                bookFolder,
                progress,
                existingBookObjectId,
                null,
                true,
                true,
                null,
                null,
                null,
                null,
                null
            );

            storageKeyOfBookFolderParentOnS3 = _storageKeyOfBookFolderParentOnS3;
            return result;
        }

        private string UploadBook(
            string bookFolder,
            IProgress progress,
            string existingBookObjectIdOrNull,
            string pdfToInclude,
            bool includeNarrationAudio,
            bool includeMusic,
            string[] textLanguages,
            string[] audioLanguages,
            CollectionSettings collectionSettings,
            string metadataLang1Code,
            string metadataLang2Code,
            bool isForBulkUpload = false
        )
        {
            var htmlFile = BookStorage.FindBookHtmlInFolder(bookFolder);
            // Using this rather than FromFolder because it will throw if we can't get some metadata, which I think is
            // appropriate here...don't want to upload a badly messed-up book.
            var metadata = BookMetaData.FromFile(
                Path.Combine(bookFolder, BookInfo.MetaDataFileName)
            );

            // In case we somehow have a book with no ID, we must have one to upload it.
            if (String.IsNullOrEmpty(metadata.Id))
            {
                metadata.Id = Guid.NewGuid().ToString();
            }
            // And similarly it should have SOME title.
            if (String.IsNullOrEmpty(metadata.Title))
            {
                metadata.Title = Path.GetFileNameWithoutExtension(bookFolder);
            }
            // If the collection has a default bookshelf, make sure the book has that tag.
            // Also make sure it doesn't have any other bookshelf tags (which would typically be
            // from a previous default bookshelf upload), including a duplicate of the one
            // we may be about to add.
            var tags = (metadata.Tags ?? new string[0]).Where(t => !t.StartsWith("bookshelf:"));

            if (!String.IsNullOrEmpty(collectionSettings?.DefaultBookshelf))
            {
                if (collectionSettings.HaveEnterpriseFeatures)
                    tags = tags.Concat(
                        new[] { "bookshelf:" + collectionSettings.DefaultBookshelf }
                    );
                else
                {
                    // At least at this point, we aren't localizing this message, because the people with Enterprise
                    // bookshelves likely know enough English to understand this message.
                    progress.WriteWarning(
                        "This book was not uploaded to the '"
                            + collectionSettings.DefaultBookshelf
                            + "' bookshelf. Uploading to a bookshelf requires a valid Enterprise subscription."
                    );
                }
            }
            metadata.Tags = tags.ToArray();

            // Any updated ID at least needs to become a permanent part of the book.
            // It simplifies unit testing if the metadata file is also updated with the uploadedBy value.
            // Not sure if there is any other reason to do it (or not do it).
            // For example, do we want to send/receive who is the latest person to upload?
            metadata.WriteToFolder(bookFolder);

            // We no longer use these book order files. Delete any remnants.
            foreach (var file in Directory.GetFiles(bookFolder, $"*{BookInfo.BookOrderExtension}"))
                RobustFile.Delete(file);

            string bookObjectId = "";
            try
            {
                if (!IsDryRun)
                {
                    // Do NOT save this change in the book folder!
                    metadata.AllTitles = PublishModel.RemoveUnwantedLanguageDataFromAllTitles(
                        metadata.AllTitles,
                        textLanguages
                    );

                    if (progress.CancelRequested)
                        return "";

                    // The server will create a new folder for our upload or sync. If the book already exists, the new folder gets prepopulated with the existing files.
                    // (after we are done, the server handles making the new folder the current one)
                    (var transactionId, _storageKeyOfBookFolderParentOnS3, var s3Credentials) =
                        BloomLibraryBookApiClient.InitiateBookUpload(
                            progress,
                            existingBookObjectIdOrNull
                        );

#if DEBUG
                    // S3 URL can be reasonably deduced, as long as we have the S3 ID, so print that out in Debug mode.
                    // Format: $"https://s3.amazonaws.com/BloomLibraryBooks{isSandbox}/{storageKeyOfBookFolderParentOnS3}/{title}"
                    // Example: https://s3.amazonaws.com/BloomLibraryBooks-Sandbox/8xhqkxGvg1/1697233000925/AutoSplit+Timings
                    // Example (old format): https://s3.amazonaws.com/BloomLibraryBooks-Sandbox/jeffrey_su@sil.org/8d0d9043-a1bb-422d-aa5b-29726cdcd96a/AutoSplit+Timings
                    var msgBookId =
                        "storageKeyOfBookFolderParentOnS3: " + _storageKeyOfBookFolderParentOnS3;
                    progress.WriteMessage(msgBookId);
#endif

                    if (progress.CancelRequested)
                        return "";

                    _s3Client.SetTemporaryCredentialsForBookUpload(s3Credentials);
                    _s3Client.UploadBook(
                        _storageKeyOfBookFolderParentOnS3,
                        bookFolder,
                        progress,
                        pdfToInclude,
                        includeNarrationAudio,
                        includeMusic,
                        textLanguages,
                        audioLanguages,
                        metadataLang1Code,
                        metadataLang2Code,
                        existingBookObjectIdOrNull == null,
                        collectionSettings?.SettingsFilePath,
                        isForBulkUpload
                    );
                    metadata.BaseUrl = _s3Client.GetBaseUrl(
                        $"{_storageKeyOfBookFolderParentOnS3}{Path.GetFileName(bookFolder)}/"
                    );

                    if (progress.CancelRequested)
                        return "";

                    // Inform the server we have completed the upload. It will update baseUrl to point to the new files and delete the old ones.
                    BloomLibraryBookApiClient.FinishBookUpload(
                        progress,
                        transactionId,
                        metadata.WebDataJson
                    );

                    bookObjectId = transactionId;

                    //   if (!UseSandbox) // don't make it seem like there are more uploads than their really are if this a tester pushing to the sandbox
                    {
                        Analytics.Track(
                            "UploadBook-Success",
                            new Dictionary<string, string>()
                            {
                                { "url", metadata.BaseUrl },
                                { "title", metadata.Title }
                            }
                        );
                    }
                }
            }
            catch (WebException e)
            {
                DisplayNetworkUploadProblem(e, progress);
                if (IsProductionRun) // don't make it seem like there are more upload failures than their really are if this a tester pushing to the sandbox
                    Analytics.Track(
                        "UploadBook-Failure",
                        new Dictionary<string, string>()
                        {
                            { "url", metadata.BaseUrl },
                            { "title", metadata.Title },
                            { "error", e.Message }
                        }
                    );
                return "";
            }
            catch (AmazonS3Exception e)
            {
                if (
                    e.Message.Contains(
                        "The difference between the request time and the current time is too large"
                    )
                )
                {
                    progress.WriteError(
                        LocalizationManager.GetString(
                            "PublishTab.Upload.TimeProblem",
                            "There was a problem uploading your book. This is probably because your computer is set to use the wrong timezone or your system time is badly wrong. See http://www.di-mgt.com.au/wclock/help/wclo_setsysclock.html for how to fix this."
                        )
                    );
                    if (IsProductionRun)
                        Analytics.Track("UploadBook-Failure-SystemTime");
                }
                else
                {
                    DisplayNetworkUploadProblem(e, progress);
                    if (IsProductionRun)
                        // don't make it seem like there are more upload failures than there really are if this a tester pushing to the sandbox
                        Analytics.Track(
                            "UploadBook-Failure",
                            new Dictionary<string, string>()
                            {
                                { "url", metadata.BaseUrl },
                                { "title", metadata.Title },
                                { "error", e.Message }
                            }
                        );
                }
                return "";
            }
            catch (AmazonServiceException e)
            {
                DisplayNetworkUploadProblem(e, progress);
                if (IsProductionRun) // don't make it seem like there are more upload failures than there really are if this a tester pushing to the sandbox
                    Analytics.Track(
                        "UploadBook-Failure",
                        new Dictionary<string, string>()
                        {
                            { "url", metadata.BaseUrl },
                            { "title", metadata.Title },
                            { "error", e.Message }
                        }
                    );
                return "";
            }
            catch (Exception e)
            {
                var msg1 = LocalizationManager.GetString(
                    "PublishTab.Upload.UploadProblemNotice",
                    "There was a problem uploading your book. You may need to restart Bloom or get technical help."
                );
                var msg2 = e.Message.Replace("{", "{{").Replace("}", "}}");
                progress.WriteError(msg1);
                progress.WriteError(msg2);
                progress.WriteVerbose(e.StackTrace);

                if (IsProductionRun) // don't make it seem like there are more upload failures than there really are if this a tester pushing to the sandbox
                    Analytics.Track(
                        "UploadBook-Failure",
                        new Dictionary<string, string>()
                        {
                            { "url", metadata.BaseUrl },
                            { "title", metadata.Title },
                            { "error", e.Message }
                        }
                    );
                return "";
            }

            return bookObjectId;
        }

        /// <summary>
        /// The upload destination possibly set from the command line.  This must be set even before calling
        /// the constructor of this class because it is used in UploadBucketNameForCurrentEnvironment
        /// which is called by the BloomLibraryBookApiClient constructor.  And the constructor for this class has a
        /// BloomLibraryBookApiClient argument.
        /// </summary>
        /// <remarks>
        /// If not set explicitly before accessing, the destination is set according to <see cref="UseSandboxByDefault"/>.
        /// It can only be set once while the program is running.  Trying to change it will cause an
        /// exception to be thrown.
        /// </remarks>
        internal static string Destination
        {
            get
            {
                if (_destination == null)
                    Destination = UseSandboxByDefault
                        ? UploadDestination.Development
                        : UploadDestination.Production;
                return _destination;
            }
            set
            {
                if (_destination == null && value != null)
                    _destination = value;
                else if (_destination != value)
                    throw new Exception("Cannot change upload destination after setting it!");
            }
        }

        /// <summary>
        /// Is this dry run (regardless of whether we're supposedly targetting the sandbox or production)?
        /// </summary>
        public static bool IsDryRun { get; set; } = false;

        /// <summary>
        /// Are we actually uploading to production (not a dry run)?
        /// </summary>
        public static bool IsProductionRun =>
            Destination == UploadDestination.Production && !IsDryRun;

        /// <summary>
        /// Are we supposed to upload to the sandbox, either explicitly or by default?  (could be a dry run)
        /// </summary>
        public static bool UseSandbox
        {
            get
            {
                switch (Destination)
                {
                    case UploadDestination.Development:
                        return true;
                    case UploadDestination.Production:
                        return false;
                    default:
                        return UseSandboxByDefault; // dry run
                }
            }
        }

        public bool IsBookOnServer(string bookPath)
        {
            var metadata = BookMetaData.FromFile(
                bookPath.CombineForPath(BookInfo.MetaDataFileName)
            );
            return BloomLibraryBookApiClient.GetSingleBookRecord(metadata.Id) != null;
        }

        /// <returns>book record or null</returns>
        public dynamic GetBookOnServer(string bookInstanceId, bool includeLanguageInfo = false)
        {
            return BloomLibraryBookApiClient.GetSingleBookRecord(
                bookInstanceId,
                includeLanguageInfo: includeLanguageInfo
            );
        }

        internal bool CheckAgainstHashFileOnS3(
            string currentHashes,
            string bookFolder,
            string storageKeyOfBookFolder,
            IProgress progress
        )
        {
            string hashInfoOnS3 = null;
            try
            {
                var key = storageKeyOfBookFolder + UploadHashesFilename;
                hashInfoOnS3 = _s3Client.DownloadFile(
                    UseSandbox
                        ? BloomS3Client.SandboxBucketName
                        : BloomS3Client.ProductionBucketName,
                    key
                );
            }
            catch
            {
                hashInfoOnS3 = ""; // probably file doesn't exist because it hasn't yet been uploaded
            }

#if DEBUG
            if (currentHashes != hashInfoOnS3)
            {
                progress.WriteMessage("local hashes:");
                progress.WriteMessage(currentHashes);
                progress.WriteMessage("s3 hashes:");
                progress.WriteMessage(hashInfoOnS3);
            }
#endif
            return currentHashes == hashInfoOnS3;
        }

        internal bool CheckAgainstLocalHashfile(string currentHashes, string uploadInfoPath)
        {
            if (RobustFile.Exists(uploadInfoPath))
            {
                var previousHashes = RobustFile.ReadAllText(uploadInfoPath);
                return currentHashes == previousHashes;
            }
            return false;
        }

        /// <summary>
        /// If we do not have enterprise enabled, copy the book and remove all enterprise level features.
        /// </summary>
        internal static bool PrepareBookForUpload(
            ref Book.Book book,
            BookServer bookServer,
            string tempFolderPath,
            IProgress progress
        )
        {
            if (book.CollectionSettings.HaveEnterpriseFeatures)
                return false;

            // We need to be sure that any in-memory changes have been written to disk
            // before we start copying/loading the new book to/from disk
            book.Save();

            Directory.CreateDirectory(tempFolderPath);
            BookStorage.CopyDirectory(book.FolderPath, tempFolderPath);
            // In the temp folder it's safe to assume we can save changes.
            var bookInfo = new BookInfo(tempFolderPath, true, new AlwaysEditSaveContext());
            var copiedBook = bookServer.GetBookFromBookInfo(bookInfo);
            copiedBook.BringBookUpToDate(new NullProgress(), true);
            var pages = new List<XmlElement>();
            foreach (XmlElement page in copiedBook.GetPageElements())
                pages.Add(page);
            ISet<string> warningMessages = new HashSet<string>();
            PublishHelper.RemoveEnterpriseFeaturesIfNeeded(copiedBook, pages, warningMessages);
            PublishHelper.SendBatchedWarningMessagesToProgress(warningMessages, progress);
            copiedBook.Save();
            copiedBook.UpdateSupportFiles();
            book = copiedBook;
            return true;
        }

        /// <summary>
        /// Common routine used in normal upload and bulk upload.
        /// </summary>
        /// <returns>On success, returns the book objectId; on failure, returns empty string</returns>
        internal string FullUpload(
            Book.Book book,
            IProgress progress,
            PublishModel publishModel,
            BookUploadParameters bookParams,
            string existingBookObjectIdOrNull
        )
        {
            // this (isForPublish:true) is dangerous and the product of much discussion.
            // See "finally" block later to see that we put branding files back
            book.Storage.CleanupUnusedSupportFiles(isForPublish: true);
            try
            {
                var bookFolder = book.FolderPath;

                var languagesToUpload = book.BookInfo.PublishSettings.BloomLibrary.TextLangs
                    .IncludedLanguages()
                    .ToArray();
                var languagesToAdvertiseOnBlorg = book.GetTextLanguagesToAdvertiseOnBloomLibrary(
                        languagesToUpload
                    )
                    .ToArray();

                // When initializing, we may set the collection's sign language to IncludeByDefault so the checkbox on the publish screen
                // gets set by default. Also, videos could have been removed since the user last visited the publish screen (e.g. bulk upload).
                // So we need to make sure we have videos before including the sign language.
                if (book.HasSignLanguageVideos())
                {
                    languagesToUpload = languagesToUpload
                        .Union(
                            book.BookInfo.PublishSettings.BloomLibrary.SignLangs.IncludedLanguages()
                        )
                        .ToArray();
                    languagesToAdvertiseOnBlorg = languagesToAdvertiseOnBlorg
                        .Union(
                            book.BookInfo.PublishSettings.BloomLibrary.SignLangs.IncludedLanguages()
                        )
                        .ToArray();
                }

                // Set this in the metadata so it gets uploaded. Do this in the background task as it can take some time.
                // These bits of data can't easily be set while saving the book because we save one page at a time
                // and they apply to the book as a whole.
                book.BookInfo.LanguageDescriptors = book.BookData.MakeLanguageUploadData(
                    languagesToAdvertiseOnBlorg
                );
                book.BookInfo.PageCount = book.GetPages().Count();
                book.BookInfo.Save();
                // If the caller wants to preserve existing thumbnails, recreate them only if one or more of them do not exist.
                var thumbnailsExist =
                    RobustFile.Exists(Path.Combine(bookFolder, "thumbnail-70.png"))
                    && RobustFile.Exists(Path.Combine(bookFolder, "thumbnail-256.png"))
                    && RobustFile.Exists(Path.Combine(bookFolder, "thumbnail.png"));
                if (!bookParams.PreserveThumbnails || !thumbnailsExist)
                {
                    var thumbnailMsg = LocalizationManager.GetString(
                        "PublishTab.Upload.MakingThumbnail",
                        "Making thumbnail image..."
                    );
                    progress.WriteStatus(thumbnailMsg);
                    //the largest thumbnail I found on Amazon was 300px high. Prathambooks.org about the same.
                    _thumbnailer.MakeThumbnailOfCover(book, 70); // this is a sacrificial one to prime the pump, to fix BL-2673
                    _thumbnailer.MakeThumbnailOfCover(book, 70);
                    if (progress.CancelRequested)
                        return "";
                    _thumbnailer.MakeThumbnailOfCover(book, 256);
                    if (progress.CancelRequested)
                        return "";

                    // It is possible the user never went back to the Collection tab after creating/updating the book, in which case
                    // the 'normal' thumbnail never got created/updating. See http://issues.bloomlibrary.org/youtrack/issue/BL-3469.
                    _thumbnailer.MakeThumbnailOfCover(book);
                    if (progress.CancelRequested)
                        return "";
                }

                var uploadPdfPath = UploadPdfPath(bookFolder);
                var videoFiles = GetVideoFilesToInclude(book);
                bool hasVideo = videoFiles.Any();
                if (hasVideo)
                {
                    var skipPdfMsg = LocalizationManager.GetString(
                        "PublishTab.Upload.SkipMakingPdf",
                        "Skipping PDF because this book has video"
                    );
                    progress.WriteStatus(skipPdfMsg);
                }
                else
                {
                    // If there is not already a locked preview in the book folder
                    // (which we take to mean the user has created a customized one that he prefers),
                    // make sure we have a current correct preview and then copy it to the book folder so it gets uploaded.
                    if (!FileHelper.IsLocked(uploadPdfPath))
                    {
                        var pdfMsg = LocalizationManager.GetString(
                            "PublishTab.Upload.MakingPdf",
                            "Making PDF Preview..."
                        );
                        progress.WriteStatus(pdfMsg);

                        publishModel.MakePDFForUpload(progress);
                        if (RobustFile.Exists(publishModel.PdfFilePath))
                        {
                            RobustFile.Copy(publishModel.PdfFilePath, uploadPdfPath, true);
                        }
                        else
                        {
                            return ""; // no PDF, no upload (See BL-6719)
                        }
                    }
                }

                // Figure out which languages to upload audio for.
                // There's no point in including languages for which we won't have text.
                var audioLanguagesToUpload = book.BookInfo.PublishSettings.BloomLibrary.AudioLangs
                    .IncludedLanguages()
                    .Intersect(
                        book.BookInfo.PublishSettings.BloomLibrary.TextLangs.IncludedLanguages()
                    );

                if (bookParams.IsForBulkUpload)
                {
                    // Update all metadata features, since we are doing bulk upload and the user won't be able to do it
                    // by checking boxes individually. The two 'true' params mean to act like the user checked both Talking Book and
                    // Sign Language boxes. (BL-12553)
                    book.UpdateMetadataFeatures(true, true, audioLanguagesToUpload);
                }

                if (progress.CancelRequested)
                    return "";

                var bookObjectId = UploadBook(
                    bookFolder,
                    progress,
                    existingBookObjectIdOrNull,
                    hasVideo ? null : Path.GetFileName(uploadPdfPath),
                    book.BookInfo.PublishSettings.BloomLibrary.IncludeAudio,
                    !bookParams.ExcludeMusic,
                    languagesToUpload,
                    audioLanguagesToUpload.ToArray(),
                    book.CollectionSettings,
                    book.BookData.MetadataLanguage1Tag,
                    book.BookData.MetadataLanguage2Tag,
                    bookParams.IsForBulkUpload
                );

                Debug.Assert(
                    existingBookObjectIdOrNull == null
                        || string.IsNullOrEmpty(bookObjectId)
                        || bookObjectId == "quiet"
                        || existingBookObjectIdOrNull == bookObjectId,
                    "If existingBookObjectIdOrNull is provided, it better equal bookObjectId"
                );

                var url = BloomLibraryUrls.BloomLibraryDetailPageUrlFromBookId(bookObjectId);
                book.ReportSimplisticFontAnalytics(FontAnalytics.FontEventType.PublishWeb, url);

                return bookObjectId;
            }
            finally
            {
                // Put back all the branding files which we removed above in the call to CleanupUnusedSupportFiles()
                book.UpdateSupportFiles();

                // NB: alternatively, we considered refactoring CleanupUnusedSupportFiles() to give us a list of files
                // to not upload.
            }
        }

        /// <summary>
        /// Figure out if any video files are unused in this book, in case we haven't had them stripped out by opening
        /// the saved book yet (when BookStorage will do it for us).
        /// </summary>
        /// <param name="book"></param>
        /// <returns></returns>
        internal static IEnumerable<string> GetVideoFilesToInclude(Book.Book book)
        {
            return BookStorage.GetVideoPathsRelativeToBook(book.RawDom.DocumentElement);
        }

        /// <summary>
        /// Conditionally exclude .mp3 files for narration and music.
        /// Always exclude .wav files for narration.
        /// </summary>
        private static ISet<string> GetAudioFilesToInclude(Book.Book book, bool excludeMusic)
        {
            HashSet<string> result = new HashSet<string>();
            bool excludeNarrationAudio = !book.BookInfo.PublishSettings.BloomLibrary.IncludeAudio;
            if (!excludeNarrationAudio)
                result.AddRange(book.Storage.GetNarrationAudioFileNamesReferencedInBook(false));
            if (!excludeMusic)
                result.AddRange(book.Storage.GetBackgroundMusicFileNamesReferencedInBook());
            return result;
        }

        internal static string UploadPdfPath(string bookFolder)
        {
            // Do NOT use ChangeExtension here. If the folder name has a period (e.g.: "Look at the sky. What do you see")
            // ChangeExtension will strip of the last sentence, which is not what we want (and not what BloomLibrary expects).
            return Path.Combine(bookFolder, Path.GetFileName(bookFolder) + ".pdf");
        }

        internal bool IsThisVersionAllowedToUpload()
        {
            return BloomLibraryBookApiClient.IsThisVersionAllowedToUpload();
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
        /// <param name="okToChangeId">If this function returns true for a folder path, that book's ID may be changed.</param>
        internal static void BulkRepairInstanceIds(
            string rootFolderPath,
            Func<string, bool> okToChangeId
        )
        {
            BookInfo.RepairDuplicateInstanceIds(rootFolderPath, okToChangeId);
        }

        /// <summary>
        /// Compute a hash for the book in the given directory.  The hash includes the book's HTML file
        /// (suitably pruned to remove insignificant changes such as whitespace between elements), other
        /// relevant files in the directory and its subdirectories (CSS, audio, video, images, etc.) and
        /// the two basic collection level files in the parent directory (customCollectionStyles.css and
        /// *.bloomCollection).
        /// </summary>
        public static string HashBookFolder(string directory)
        {
            var bldr = new StringBuilder();
            Debug.Assert(Directory.Exists(directory));
            var dirInfo = new DirectoryInfo(directory);
            var htmFiles = dirInfo.GetFiles("*.htm", SearchOption.TopDirectoryOnly);
            Debug.Assert(htmFiles.Length == 1);

            var hash = Book.Book.ComputeHashForAllBookRelatedFiles(htmFiles[0].FullName);
            bldr.AppendLineFormat("{0} {1}", htmFiles[0].Name, hash);

            return bldr.ToString().Replace(Environment.NewLine, "\r\n"); // cross-platform line endings for this file
        }
    }

    public class BookUploadParameters
    {
        public string Folder;
        public bool ExcludeMusic;
        public bool PreserveThumbnails;
        public bool ForceUpload;
        public bool IsForBulkUpload;

        public BookUploadParameters() { }

        public BookUploadParameters(UploadParameters options)
        {
            Folder = options.Path;
            ExcludeMusic = options.ExcludeMusicAudio;
            PreserveThumbnails = options.PreserveThumbnails;
            ForceUpload = options.ForceUpload;
            IsForBulkUpload = true;
        }
    }
}
