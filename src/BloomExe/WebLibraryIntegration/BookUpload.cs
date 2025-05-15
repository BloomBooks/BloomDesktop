using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Forms;
using Amazon.Runtime;
using Amazon.S3;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Properties;
using Bloom.Publish;
using Bloom.SafeXml;
using Bloom.SubscriptionAndFeatures;
using Bloom.web;
using Bloom.web.controllers;
using BloomTemp;
using DesktopAnalytics;
using L10NSharp;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;

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
        private string _s3PrefixToUploadTo = "";
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
            out string s3PrefixUploadedTo,
            IProgress progress = null,
            string existingBookObjectId = null,
            CollectionSettings collectionSettings = null
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
                collectionSettings,
                null,
                null
            );

            s3PrefixUploadedTo = _s3PrefixToUploadTo;
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
            bool isForBulkUpload = false,
            bool changeUploader = false
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
                if (collectionSettings.Subscription.HaveActiveSubscription)
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
            bool isNewBook = existingBookObjectIdOrNull == null;
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

                    // This currently (unfortunately) enforces a single upload at a time.
                    // If we want to change that in the future, we would need different folder names,
                    // perhaps appending an ID or even just a GUID.
                    using (
                        var stagingDirectoryTempFolder = new TemporaryFolder("BloomUploadStaging")
                    )
                    {
                        var stagingDirectory = stagingDirectoryTempFolder.FolderPath;
                        SetUpStaging(
                            bookFolder,
                            stagingDirectory,
                            progress,
                            pdfToInclude,
                            includeNarrationAudio,
                            includeMusic,
                            textLanguages,
                            audioLanguages,
                            metadataLang1Code,
                            metadataLang2Code,
                            collectionSettings?.SettingsFilePath,
                            isForBulkUpload
                        );

                        string[] filesToUpload = null;
                        List<FilePathAndHash> bookFiles = GetStagedFilesAndHashes(stagingDirectory);

                        if (progress.CancelRequested)
                            return "";

                        // The server determines our upload location (_s3PrefixToUploadTo)
                        // and, for an existing book, copies the files which haven't been modified.
                        // It also reports which files should be uploaded (new and modified).
                        // After we are done uploading those files, the server points the book
                        // database record at the new S3 location.
                        (
                            var transactionId,
                            var s3Credentials,
                            _s3PrefixToUploadTo,
                            filesToUpload
                        ) = BloomLibraryBookApiClient.InitiateBookUpload(
                            progress,
                            bookFiles,
                            Path.GetFileName(bookFolder), // This last part of the path is the book title
                            existingBookObjectIdOrNull
                        );

#if DEBUG
                        // S3 URL can be reasonably deduced, as long as we have the S3 prefix, so print that out in Debug mode.
                        // Format: $"https://s3.amazonaws.com/BloomLibraryBooks{isSandbox}/{s3PrefixToUploadTo}"
                        // Example: https://s3.amazonaws.com/BloomLibraryBooks-Sandbox/8xhqkxGvg1/1697233000925/AutoSplit+Timings
                        // Example (old format): https://s3.amazonaws.com/BloomLibraryBooks-Sandbox/bob_example@sil.org/8d0d9043-a1bb-422d-aa5b-29726cdcd96a/AutoSplit+Timings
                        var msgBookId = "s3PrefixToUploadTo: " + _s3PrefixToUploadTo;
                        progress.WriteMessage(msgBookId);
#endif

                        if (progress.CancelRequested)
                            return "";

                        _s3Client.SetTemporaryCredentialsForBookUpload(s3Credentials);
                        _s3Client.UploadBook(
                            _s3PrefixToUploadTo,
                            stagingDirectory,
                            filesToUpload,
                            progress
                        );
                        metadata.BaseUrl = _s3Client.GetBaseUrl($"{_s3PrefixToUploadTo}");

                        if (progress.CancelRequested)
                            return "";

                        // Inform the server we have completed the upload.
                        // It will update the database record's baseUrl to point to the new files and delete the old ones.
                        BloomLibraryBookApiClient.FinishBookUpload(
                            progress,
                            transactionId,
                            metadata.WebDataJson,
                            changeUploader
                        );

                        bookObjectId = transactionId;
                    }

                    if (IsProductionRun) // don't make it seem like there are more uploads than there really are if this is just a tester pushing to the sandbox
                    {
                        Analytics.Track(
                            "UploadBook-Success",
                            new Dictionary<string, string>()
                            {
                                { "url", metadata.BaseUrl },
                                { "title", metadata.Title },
                                { "uploader", UploadedBy },
                                { "BookId", metadata.Id },
                                { "isNewBook", isNewBook.ToString() },
                            }
                        );
                    }
                }
            }
            catch (WebException e)
            {
                DisplayNetworkUploadProblem(e, progress);
                ReportFailureToAnalytics(metadata, isNewBook, e);
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
                    ReportFailureToAnalytics(metadata, isNewBook, e);
                }
                return "";
            }
            catch (AmazonServiceException e)
            {
                DisplayNetworkUploadProblem(e, progress);
                ReportFailureToAnalytics(metadata, isNewBook, e);
                return "";
            }
            catch (VersionCannotUploadException e)
            {
                var errorMessage = e.Message.Replace("{", "{{").Replace("}", "}}");
                progress.WriteError(errorMessage);
                string cancelledMessage = LocalizationManager.GetString(
                    "PublishTab.Upload.Cancelled",
                    "Upload was cancelled"
                );
                progress.WriteMessage(cancelledMessage);
                return "quiet";
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

                ReportFailureToAnalytics(metadata, isNewBook, e);
                return "";
            }

            return bookObjectId;
        }

        private void ReportFailureToAnalytics(BookMetaData metadata, bool isNewBook, Exception e)
        {
            if (IsProductionRun) // don't make it seem like there are more upload failures than there really are if this is just a tester pushing to the sandbox
                Analytics.Track(
                    "UploadBook-Failure",
                    new Dictionary<string, string>()
                    {
                        { "url", metadata.BaseUrl },
                        { "title", metadata.Title },
                        { "error", e.Message },
                        { "uploader", UploadedBy },
                        { "BookId", metadata.Id },
                        { "isNewBook", isNewBook.ToString() },
                    }
                );
        }

        private List<FilePathAndHash> GetStagedFilesAndHashes(string stagingDirectory)
        {
            var fileInfos = new List<FilePathAndHash>();
            foreach (
                var fullFilePath in Directory.GetFiles(
                    stagingDirectory,
                    "*.*",
                    SearchOption.AllDirectories
                )
            )
            {
                fileInfos.Add(
                    new FilePathAndHash()
                    {
                        Path = fullFilePath
                            .Substring(stagingDirectory.Length + 1)
                            .Replace("\\", "/"),
                        Hash = BloomS3Client.GetProbableEtag(fullFilePath)
                    }
                );
            }
            return fileInfos;
        }

        // Copy the needed files to the staging directory and make any modifications needed before upload.
        private void SetUpStaging(
            string pathToBloomBookDirectory,
            string stagingDirectory,
            IProgress progress,
            string pdfToInclude,
            bool includeNarrationAudio,
            bool includeMusic,
            string[] textLanguagesToInclude,
            string[] audioLanguagesToInclude,
            string metadataLang1Code,
            string metadataLang2Code,
            string collectionSettingsPath = null,
            bool isForBulkUpload = false
        )
        {
            var filter = new BookFileFilter(pathToBloomBookDirectory)
            {
                IncludeFilesForContinuedEditing = true,
                NarrationLanguages = (
                    includeNarrationAudio ? audioLanguagesToInclude : Array.Empty<string>()
                ),
                WantVideo = true,
                WantMusic = includeMusic
            };
            if (pdfToInclude != null)
                filter.AlwaysAccept(pdfToInclude);
            if (isForBulkUpload)
                filter.AlwaysAccept(".lastUploadInfo");
            filter.CopyBookFolderFiltered(stagingDirectory);

            ProcessVideosInTempDirectory(stagingDirectory);
            CopyCollectionSettingsToTempDirectory(collectionSettingsPath, stagingDirectory);

            if (textLanguagesToInclude != null && textLanguagesToInclude.Count() > 0)
                RemoveUnwantedLanguageData(
                    stagingDirectory,
                    textLanguagesToInclude,
                    metadataLang1Code,
                    metadataLang2Code
                );

            PublishHelper.ReportInvalidFonts(stagingDirectory, progress);

            // Really crop images, which allows us to simplify the representation of background images,
            // so the new structure with the background canvas elements doesn't get uploaded.
            // We think it's better if Blorg books don't have this structure until we can migrate all pages
            // to it.
            // Since this is a temp directory and a book that's already up-to-date, I think it's safe to
            // just load a DOM from the file, modify it, and write it out again, without all the
            // overhead of creating a book object.
            var htmlFile = BookStorage.FindBookHtmlInFolder(stagingDirectory);
            var xmlDomFromHtmlFile = XmlHtmlConverter.GetXmlDomFromHtmlFile(htmlFile, false);

            PublishHelper.ReallyCropImages(xmlDomFromHtmlFile, stagingDirectory, stagingDirectory);
            PublishHelper.SimplifyBackgroundImages(xmlDomFromHtmlFile); // after really cropping

            XmlHtmlConverter.SaveDOMAsHtml5(xmlDomFromHtmlFile, htmlFile);
        }

        private void ProcessVideosInTempDirectory(string destDirName)
        {
            var htmlFilePath = BookStorage.FindBookHtmlInFolder(destDirName);
            if (string.IsNullOrEmpty(htmlFilePath))
                return;
            var xmlDomFromHtmlFile = XmlHtmlConverter.GetXmlDomFromHtmlFile(htmlFilePath);
            var domForVideoProcessing = new HtmlDom(xmlDomFromHtmlFile);
            var videoContainerElements = HtmlDom
                .SelectChildVideoElements(domForVideoProcessing.RawDom.DocumentElement)
                .Cast<SafeXmlElement>();
            if (!videoContainerElements.Any())
                return;
            SignLanguageApi.ProcessVideos(videoContainerElements, destDirName);
            XmlHtmlConverter.SaveDOMAsHtml5(domForVideoProcessing.RawDom, htmlFilePath);
        }

        /// <summary>
        /// Copy a sanitized (no subscription code) collection settings file to the temp folder so that
        /// harvester will have access to it.
        /// </summary>
        /// <remarks>
        /// See BL-12583.
        /// </remarks>
        private static void CopyCollectionSettingsToTempDirectory(
            string settingsPath,
            string tempBookFolder
        )
        {
            if (String.IsNullOrEmpty(settingsPath) || !RobustFile.Exists(settingsPath))
                return;
            var doc = SanitizeCollectionSettingsForUpload(settingsPath);
            Directory.CreateDirectory(Path.Combine(tempBookFolder, "collectionFiles"));
            doc.Save(
                Path.Combine(tempBookFolder, "collectionFiles", "book.uploadCollectionSettings")
            );
        }

        /// <summary>
        /// Sanitize collection settings file for upload by redacting subscription code and removing unpublishable languages.
        /// </summary>
        /// <param name="settingsPath">The path to the collection settings file</param>
        /// <returns>A SafeXmlDocument with sanitized collection settings</returns>
        public static SafeXmlDocument SanitizeCollectionSettingsForUpload(string settingsPath)
        {
            var settingsText = RobustFile.ReadAllText(settingsPath);
            var doc = SafeXmlDocument.Create();
            doc.PreserveWhitespace = true;
            doc.LoadXml(settingsText);

            var subscriptionNode = doc.SelectSingleNode("/Collection/SubscriptionCode");
            if (subscriptionNode != null)
            {
                var sub = new Subscription(subscriptionNode.InnerText);
                subscriptionNode.InnerText = sub.GetRedactedCode();
            }
            // we don't publish the "BrandingProjectName" anymore, since we're using a redacted code instead
            var brandingProjectNameNode = doc.SelectSingleNode("/Collection/BrandingProjectName");
            if (brandingProjectNameNode != null)
            {
                brandingProjectNameNode.ParentNode.RemoveChild(brandingProjectNameNode);
            }

            // Remove traces of AI generated data from the collection settings.
            var languages = doc.SafeSelectNodes("/Collection/Languages/Language");

            foreach (SafeXmlElement lang in languages)
            {
                var code = lang.GetChildWithName("languageiso639code")?.InnerText;
                if (PublishHelper.IsUnpublishableLanguage(code))
                    lang.ParentNode.RemoveChild(lang);
            }
            return doc;
        }

        private void RemoveUnwantedLanguageData(
            string destDirName,
            IEnumerable<string> languagesToInclude,
            string metadataLang1Code,
            string metadataLang2Code
        )
        {
            // There should be only one html file with the same name as the directory it's in, but let's
            // not make any assumptions here.
            foreach (var filepath in Directory.EnumerateFiles(destDirName, "*.htm"))
            {
                var xmlDomFromHtmlFile = XmlHtmlConverter.GetXmlDomFromHtmlFile(filepath, false);
                var dom = new HtmlDom(xmlDomFromHtmlFile);
                // Since we're not pruning xmatter, it doesn't matter what we pass for the set of xmatter langs to keep.
                PublishModel.RemoveUnwantedLanguageData(
                    dom,
                    languagesToInclude,
                    false,
                    new HashSet<string>()
                );
                XmlHtmlConverter.SaveDOMAsHtml5(dom.RawDom, filepath);
            }
            // Remove language specific style settings from all CSS files for unwanted languages.
            // For 5.3, we wholesale keep all L2/L3 rules even though this might result in incorrect error messages about fonts. (BL-11357)
            // In 5.4, we hope to clean up all this font determination stuff by using a real browser to determine what is used.
            PublishModel.RemoveUnwantedLanguageRulesFromCssFiles(
                destDirName,
                languagesToInclude.Append(metadataLang1Code).Append(metadataLang2Code)
            );
            var metadata = BookMetaData.FromFolder(destDirName);
            metadata.AllTitles = PublishModel.RemoveUnwantedLanguageDataFromAllTitles(
                metadata.AllTitles,
                languagesToInclude.ToArray()
            );
            // For this use case, we don't want to create a backup (meta.bak) because we never want to upload one.
            metadata.WriteToFolder(destDirName, makeBackup: false);
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

        /// <returns>book record of a book uploaded by the current user with the right ID, or null.
        /// If there are other books with the same ID, haveCollidingBooks is set true.</returns>
        public dynamic GetBookOnServer(string bookInstanceId, out bool haveCollidingBooks)
        {
            var matchingBooks = GetBooksOnServer(bookInstanceId);
            // We are counting on there not being more than one book uploaded by any given user
            // with the same ID; we have prevented that from the earliest days of book uploading.
            var result = matchingBooks.FirstOrDefault(
                b => b.uploader?.email == Settings.Default.WebUserId
            );
            if (result != null)
            {
                // If there is ANY book with the right ID and uploader, we return that as the
                // one that this user should be able to update. That's not considered a collision,
                // unless there are OTHER books with the same ID.
                haveCollidingBooks = matchingBooks.Length > 1;
                return result;
            }
            // If there is no book with the right ID and uploader, then ANY book with that ID is a collision.
            haveCollidingBooks = matchingBooks.Length > 0;
            return null;
        }

        public dynamic GetBookPermissions(string bookObjectId)
        {
            return BloomLibraryBookApiClient.GetBookPermissions(bookObjectId);
        }

        public dynamic[] GetBooksOnServer(string bookInstanceId, bool includeLanguageInfo = false)
        {
            var json = BloomLibraryBookApiClient.GetBookRecords(
                bookInstanceId,
                includeLanguageInfo
            );
            // The json is always an array. But it's a bit easier to work with if we convert it
            // to a regular C# array, even leaving the individual objects as dynamic.
            var result = new dynamic[json.Count];
            for (int i = 0; i < json.Count; i++)
            {
                result[i] = json[i];
            }
            return result;
        }

        internal bool CheckAgainstHashFileOnS3(
            string currentHashes,
            string bookFolder,
            string s3Prefix,
            IProgress progress
        )
        {
            string hashInfoOnS3 = null;
            try
            {
                var key = s3Prefix + UploadHashesFilename;
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
            if (
                book.CollectionSettings.Subscription.HaveActiveSubscription
                && !PublishHelper.BookHasUnpublishableData(book)
            )
                return false; // no need to prune the book data

            // We need to be sure that any in-memory changes have been written to disk
            // before we start copying/loading the new book to/from disk
            book.Save();

            Directory.CreateDirectory(tempFolderPath);
            BookStorage.CopyDirectory(book.FolderPath, tempFolderPath);
            // In the temp folder it's safe to assume we can save changes.
            var bookInfo = new BookInfo(tempFolderPath, true, new AlwaysEditSaveContext());
            var copiedBook = bookServer.GetBookFromBookInfo(bookInfo);
            copiedBook.BringBookUpToDate(new NullProgress(), true);
            var pages = new List<SafeXmlElement>();
            foreach (SafeXmlElement page in copiedBook.GetPageElements())
                pages.Add(page);
            // Retiring this: We now stop you with the UI before you get this far
            // if (!book.CollectionSettings.Subscription.HaveActiveSubscription)
            // {
            //     // Remove enterprise features since they aren't allowed.
            //     ISet<string> warningMessages = new HashSet<string>();
            //     PublishHelper.RemoveFeaturesThatExceedSubscription(
            //         copiedBook,
            //         pages,
            //         warningMessages
            //     );
            //     PublishHelper.SendBatchedWarningMessagesToProgress(warningMessages, progress);
            // }
            // Remove any AI generated content from the book. (BL-14339)
            foreach (var page in pages)
                PublishHelper.RemoveUnpublishableContent(page);
            PublishHelper.RemoveUnpublishableBookData(copiedBook.RawDom);
            PublishHelper.RemoveUnpublishableBookInfo(copiedBook.BookInfo);
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
            string existingBookObjectIdOrNull,
            bool changeUploader = false
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
                    bookParams.IsForBulkUpload,
                    changeUploader
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

                BloomWebSocketServer.Instance.SendEvent("booksOnBlorg", "reload");
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
            BookInfo.CheckForDuplicateInstanceIdsAndRepair(rootFolderPath, okToChangeId);
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

    public class FilePathAndHash
    {
        [JsonProperty("path")]
        public string Path;

        [JsonProperty("hash")]
        public string Hash;
    }
}
