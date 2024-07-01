using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using System.Windows.Forms;
using Amazon.Runtime;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionCreating;
using DesktopAnalytics;
using L10NSharp;
using SIL.Extensions;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.Progress;
using Bloom.web.controllers;
using Bloom.MiscUI;
using Bloom.ToPalaso;
using Microsoft.VisualBasic;
using BloomTemp;
using SIL.IO;
using System.Security;
using Bloom.Publish.BloomLibrary;
using Bloom.ToPalaso;

namespace Bloom.WebLibraryIntegration
{
    /// <summary>
    /// Gets book files from Amazon S3.
    /// </summary>
    public class BookDownload
    {
        private readonly BloomS3Client _s3Client;
        public IProgress Progress;

        public event EventHandler<BookDownloadedEventArgs> BookDownLoaded;

        public BookDownload(BloomS3Client bloomS3Client)
        {
            _s3Client = bloomS3Client;
        }

        public string LastBookDownloadedPath { get; set; }

        /// <summary>
        /// Download a book, given a bookOrder URL
        /// </summary>
        /// <param name="orderUrl">bloom://localhost/order?orderFile=BloomLibraryBooks-UnitTests/unittest%40example.com%2fa211f07b-2c9f-4b97-b0b1-71eb24fd%2f</param>
        public string DownloadFromOrderUrl(
            string orderUrl,
            string destPath,
            string bookTitleForAnalytics = "unknown",
            bool forEdit = false
        )
        {
            string storageKeyOfBookFolderParentOnS3 = "unknown";
            try
            {
                var uri = new Uri(orderUrl);
                var order = HttpUtility.ParseQueryString(uri.Query)["orderFile"];

                // Starting in 5.6, we simplified the bookOrder URL to not include the full path to the obsolete .BloomBookOrder file.
                // Instead, the meaningful info includes just the bucket name and the prefix (folder) where we can locate the book folder.
                // (Until these changes for 5.6, that was always the user email address followed by the book instance ID, with a slash between.)
                // Unfortunately, older Blooms assume that the prefix part must have two slashes. So we'll have to maintain 2 slashes in the URL
                // as long as we want to keep download working in older versions.
                //
                // But this code is ready for some day when we may stop enforcing two slashes.
                var index = order.IndexOf('/');
                var bucket = order.Substring(0, index);
                storageKeyOfBookFolderParentOnS3 = order.Substring(index + 1);

                // getting the url is considered step 1. (Note, this used to take longer because we actually downloaded a file.)
                _progressDialog?.Invoke(() =>
                {
                    _progressDialog.Progress = 1;
                });

                // uncomment line below to simulate bad internet connection
                // throw new WebException();

                var destinationPath = DownloadBook(
                    bucket,
                    storageKeyOfBookFolderParentOnS3,
                    destPath,
                    forEdit
                );
                if (string.IsNullOrEmpty(destinationPath))
                    return ""; // user cancelled
                LastBookDownloadedPath = destinationPath;

                Analytics.Track(
                    "DownloadedBook-Success",
                    new Dictionary<string, string>()
                    {
                        { "url", storageKeyOfBookFolderParentOnS3 },
                        { "title", bookTitleForAnalytics }
                    }
                );
                return destinationPath;
            }
            catch (Exception e)
            {
                try
                {
                    // We want to try this before we give a report that may terminate the program. But if something
                    // more goes wrong, ignore it.
                    Analytics.Track(
                        "DownloadedBook-Failure",
                        new Dictionary<string, string>()
                        {
                            { "url", storageKeyOfBookFolderParentOnS3 },
                            { "title", bookTitleForAnalytics }
                        }
                    );
                    Analytics.ReportException(e);
                }
                catch (Exception) { }
                var showSendReport = true;
                // For most types of error, we will set 'showSendReport' to false to avoid an ugly yellow dialog box.
                var message = LocalizationManager.GetString(
                    "Download.ProblemNotice",
                    "There was a problem downloading your book. You may need to restart Bloom or get technical help."
                );
                // BL-1233, we've seen what appear to be timeout exceptions, can't confirm the actual Exception subclass though.
                // It's likely that S3 wraps the original TimeoutException from .net with its own AmazonServiceException.
                if (e is TimeoutException || e.InnerException is TimeoutException)
                {
                    message = LocalizationManager.GetString(
                        "Download.TimeoutProblemNotice",
                        "There was a problem downloading the book: something took too long. You can try again at a different time, or write to us at issues@bloomlibrary.org if you cannot get the download to work from your location."
                    );
                    showSendReport = false;
                }
                if (e is AmazonServiceException || e is WebException || e is IOException) // Network problems, not an internal error, less alarming message called for
                {
                    Logger.WriteError("Bloom had a download problem", e);
                    message = LocalizationManager.GetString(
                        "Download.GenericNetworkProblemNotice",
                        "There was a problem downloading the book.  You can try again at a different time, or write to us at issues@bloomlibrary.org if you cannot get the download to work from your location."
                    );
                    var secondPart = string.Format(
                        LocalizationManager.GetString(
                            "Download.PleaseSendLogFile",
                            "Please also send us the latest log file found at {0}."
                        ),
                        Logger.LogPath
                    );
                    message += Environment.NewLine + Environment.NewLine + secondPart;
                    showSendReport = false;
                }
                DisplayProblem(e, message, showSendReport);
                return "";
            }
        }

        private static void DisplayProblem(Exception e, string message, bool showSendReport = true)
        {
            var action = new Action(
                () =>
                    NonFatalProblem.Report(
                        ModalIf.Alpha,
                        PassiveIf.All,
                        message,
                        null,
                        e,
                        showSendReport
                    )
            );
            var shellWindow = ShellWindow;
            if (shellWindow != null)
                shellWindow.Invoke(action);
            else
                action.Invoke();
        }

        public static string DownloadFolder
        {
            get
            {
                return Environment
                    .GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                    .CombineForPath(
                        ProjectContext.GetInstalledCollectionsDirectory(),
                        BookCollection.DownloadedBooksCollectionNameInEnglish
                    );
            }
        }

        private IProgressDialog _progressDialog;
        private string _bookOrderUrl;

        internal void HandleBloomBookOrder(string bookOrderUrl)
        {
            _bookOrderUrl = bookOrderUrl;
            if (!IsThisVersionAllowedToDownload(bookOrderUrl))
            {
                // We don't need exception handling here because the test above only returns true if it was able to parse the version.
                var minVersionStr = HttpUtility.ParseQueryString(new Uri(bookOrderUrl).Query)[
                    "minVersion"
                ];
                // We can't use a browser here; we haven't gotten that far in the setup.
                MessageBox.Show(
                    string.Format(
                        LocalizationManager.GetString(
                            "Download.OldVersion",
                            "The download you started needs version {0} or later of Bloom. Please install that version if you haven't already, and run it. Then try the download again."
                        ),
                        minVersionStr
                    ),
                    "Bloom "
                        + Application.ProductVersion
                        + " "
                        + ApplicationUpdateSupport.ChannelName
                );
                ProcessExtra.SafeStartInFront("https://bloomlibrary.org/download");
                // Application.Run() hasn't been executed yet, so we can't use Application.Exit().
                Environment.Exit(1);
            }
            using (var progressDialog = new ProgressDialog())
            {
                var progressDialogWrapper = new ProgressDialogWrapper(progressDialog);
                _progressDialog = progressDialogWrapper;
                progressDialog.CanCancel = true;
                progressDialog.Overview = LocalizationManager.GetString(
                    "Download.DownloadingDialogTitle",
                    "Downloading book"
                );
                progressDialog.ProgressRangeMaximum = 14; // a somewhat minimal file count. We will fine-tune it when we know.
                if (IsUrlOrder(bookOrderUrl))
                {
                    var link = new BloomLinkArgs(bookOrderUrl);
                    progressDialog.StatusText = link.Title;
                }
                else
                {
                    // There is no other kind anymore. We used to handle a .BloomBookOrder file.
                }

                // We must do the download in a background thread, even though the whole process is doing nothing else,
                // so we can invoke stuff on the main thread to (e.g.) update the progress bar.
                BackgroundWorker worker = new BackgroundWorker();
                worker.WorkerSupportsCancellation = true;
                worker.DoWork += OnDoDownload;
                progressDialog.BackgroundWorker = worker;
                progressDialogWrapper.CancellationTest = () => worker.CancellationPending;
                progressDialog.ShowDialog(); // hidden automatically when task completes
                if (
                    progressDialog.ProgressStateResult != null
                    && progressDialog.ProgressStateResult.ExceptionThatWasEncountered != null
                )
                {
                    var exc = progressDialog.ProgressStateResult.ExceptionThatWasEncountered;
                    ProblemReportApi.ShowProblemDialog(null, exc, "", "fatal");
                }
            }
        }

        private static bool IsThisVersionAllowedToDownload(string bookOrderUrl)
        {
            // This awkwardness is so we can unit test the main logic without getting messed up by a "real" Application.ProductVersion.
            return IsThisVersionAllowedToDownloadInner(bookOrderUrl, Application.ProductVersion);
        }

        internal static bool IsThisVersionAllowedToDownloadInner(
            string bookOrderUrl,
            string appVersion
        )
        {
            int requiredMajorVersion;
            int requiredMinorVersion;
            try
            {
                var minVersionStr = HttpUtility.ParseQueryString(new Uri(bookOrderUrl).Query)[
                    "minVersion"
                ];
                Version requiredVersion = Version.Parse(minVersionStr);
                requiredMajorVersion = requiredVersion.Major;
                requiredMinorVersion = requiredVersion.Minor;
            }
            catch
            {
                // Three possibilities:
                // 1. minVersion is missing.
                //    Allow download.
                // 2. minVersion is invalid.
                //    Allow download. (This could be argued either way, but this is easier. Since we control the other end, we don't expect this to happen.)
                // 3. Something is wrong with the url itself.
                //    Likely, something else will go wrong, but we don't want to put up a message about the version needing to be updated.
                return true;
            }
            var ourVersion = Version.Parse(appVersion);
            var ourMajorVersion = ourVersion.Major;
            var ourMinorVersion = ourVersion.Minor;

            if (ourMajorVersion == requiredMajorVersion)
                return ourMinorVersion >= requiredMinorVersion;
            return ourMajorVersion >= requiredMajorVersion;
        }

        /// <summary>
        /// url is typically something like https://s3.amazonaws.com/BloomLibraryBooks/somebody@example.com/0a2745dd-ca98-47ea-8ba4-2cabc67022e
        /// It is harmless if there are more elements in it (e.g. address to a particular file in the folder)
        /// Note: if you copy the url from part of the link to a file in the folder from AWS,
        /// you typically need to change %40 to @ in the uploader's email.
        /// </summary>
        internal string HandleDownloadWithoutProgress(string url, string destRoot)
        {
            const string BloomS3UrlPrefix = "https://s3.amazonaws.com/";

            _progressDialog = new ConsoleProgress();
            if (!url.StartsWith(BloomS3UrlPrefix))
            {
                Console.WriteLine($"Url unexpectedly does not start with {BloomS3UrlPrefix}");
                return "";
            }
            var bookOrder = url.Substring(BloomS3UrlPrefix.Length);
            var index = bookOrder.IndexOf('/');
            var bucket = bookOrder.Substring(0, index);
            var folder = bookOrder.Substring(index + 1);

            return DownloadBook(bucket, folder, destRoot);
        }

        /// <summary>
        /// this runs in a worker thread
        /// </summary>
        private void OnDoDownload(object sender, DoWorkEventArgs args)
        {
            // If we are passed a bloom book order URL, download the corresponding book and open it.
            if (IsUrlOrder(_bookOrderUrl))
            {
                var link = new BloomLinkArgs(_bookOrderUrl);
                DownloadFromOrderUrl(
                    _bookOrderUrl,
                    link.ForEdit
                        ? NewCollectionWizard.DefaultParentDirectoryForCollections
                        : DownloadFolder,
                    link.Title,
                    link.ForEdit
                );
                if (link.ForEdit && link.DatabaseId != null && LastBookDownloadedPath != null)
                {
                    // Write a collection-level file that is used when re-uploading the book
                    // so we know exactly which book this collection was made for.
                    var pathToForEditDataFile = Path.Combine(
                        Path.GetDirectoryName(LastBookDownloadedPath), // collection folder
                        BloomLibraryPublishModel.kNameOfDownloadForEditFile
                    );
                    var id = BookMetaData.FromFolder(LastBookDownloadedPath).Id;
                    var editData = new ExpandoObject() as IDictionary<string, object>;
                    // When we look for a 'matching' book to re-upload, it has to match on all three of these.
                    editData["databaseId"] = link.DatabaseId;
                    editData["instanceId"] = id;
                    editData["bookFolder"] = LastBookDownloadedPath.Replace("\\", "/");
                    // We can't create an instance and read the branding, because load will wipe it out when it sees no code.
                    var branding = CollectionSettings.ReadBrandingNameFromCollectionFile(
                        CollectionCreatedForLastDownload
                    );
                    editData["branding"] = branding;
                    RobustFile.WriteAllText(
                        pathToForEditDataFile,
                        Newtonsoft.Json.JsonConvert.SerializeObject(editData)
                    );
                }
            }
            else
            {
                // There is no other kind anymore. We used to handle a .BloomBookOrder file.
            }
        }

        private static Form ShellWindow
        {
            get { return Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell); }
        }

        private static bool IsUrlOrder(string argument)
        {
            return argument.ToLowerInvariant().StartsWith(BloomLinkArgs.kBloomUrlPrefix);
        }

        public string CollectionCreatedForLastDownload { get; private set; }

        // Internal for testing
        internal string DownloadBook(
            string bucket,
            string storageKeyOfBookFolderParentOnS3,
            string dest,
            bool forEdit = false
        )
        {
            string destinationPath;
            if (forEdit)
            {
                using (
                    var tempDestination = new TemporaryFolder(
                        _s3Client.GetMinimalRandomFolderName()
                    )
                )
                {
                    var tempDirectory = tempDestination.FolderPath;

                    var bookFolderName = _s3Client.DownLoadBookDirect(
                        bucket,
                        storageKeyOfBookFolderParentOnS3,
                        _progressDialog,
                        forEdit,
                        tempDirectory
                    );

                    var bookFolderPathTemp = Path.Combine(tempDirectory, bookFolderName);
                    var settingsPath = Path.Combine(
                        bookFolderPathTemp,
                        "collectionFiles",
                        "book.uploadCollectionSettings"
                    );
                    string bookFolder = "";

                    var htmlPath = BookStorage.FindBookHtmlInFolder(bookFolderPathTemp);
                    if (string.IsNullOrEmpty(htmlPath))
                    {
                        throw new ApplicationException(
                            "Downloaded book does not contain an html file"
                        );
                    }
                    if (!RobustFile.Exists(settingsPath))
                    {
                        var metadataPath = BookMetaData.MetaDataPath(bookFolderPathTemp);
                        var reconstructor = new CollectionSettingsReconstructor(
                            RobustFile.ReadAllText(htmlPath, Encoding.UTF8),
                            RobustFile.ReadAllText(metadataPath, Encoding.UTF8),
                            bookFolderPathTemp
                        );
                        var settingsContent = reconstructor.BloomCollection;
                        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                        RobustFile.WriteAllText(settingsPath, settingsContent);
                    }
                    //var collectionName = NewCollectionWizard.GetNewCollectionName(langName);
                    var bookName = Path.GetFileNameWithoutExtension(htmlPath);
                    var nameTemplate = LocalizationManager.GetString(
                        "Download.FromBloomLibrary",
                        "From Bloom Library"
                    );
                    // Include the template in the sanitization, in case a translator put in a colon or something.
                    var collectionName = BookStorage.SanitizeNameForFileSystem(
                        nameTemplate + " - " + bookName
                    );
                    // This is somewhat arbitrary, but helps avoid exceeding path name limits.
                    collectionName = collectionName.Substring(
                        0,
                        Math.Min(50, collectionName.Length)
                    );
                    var collectionPath = BookStorage.GetUniqueFolderPath(
                        dest,
                        collectionName,
                        collectionName + " {0}"
                    );
                    Debug.Assert(!Directory.Exists(collectionPath));
                    Directory.CreateDirectory(collectionPath);
                    bookFolder = Path.Combine(collectionPath, bookFolderName);
                    MoveOrCopyDirectory(bookFolderPathTemp, bookFolder);
                    var collectionFilePath = Path.Combine(
                        collectionPath,
                        collectionName + ".bloomCollection"
                    );
                    RobustFile.Move(
                        Path.Combine(
                            bookFolder,
                            "collectionFiles",
                            "book.uploadCollectionSettings"
                        ),
                        collectionFilePath
                    );
                    CollectionCreatedForLastDownload = collectionFilePath;

                    RobustIO.DeleteDirectory(Path.Combine(bookFolder, "collectionFiles"));

                    destinationPath = bookFolder;
                }
            }
            else
            {
                CollectionCreatedForLastDownload = null;
                destinationPath = _s3Client.DownloadBook(
                    bucket,
                    storageKeyOfBookFolderParentOnS3,
                    dest,
                    _progressDialog,
                    forEdit
                );
            }

            if (destinationPath == null)
                return null; // user cancelled

            if (BookDownLoaded != null)
            {
                var bookInfo = new BookInfo(destinationPath, false); // A downloaded book is a template, so never editable.
                BookDownLoaded(this, new BookDownloadedEventArgs() { BookDetails = bookInfo });
            }
            var htmlFile = BookStorage.FindBookHtmlInFolder(destinationPath);
            if (htmlFile == "")
                return destinationPath; //argh! it didn't really download the book!
            var xmlDomFromHtmlFile = XmlHtmlConverter.GetXmlDomFromHtmlFile(htmlFile, false);
            var dom = new HtmlDom(xmlDomFromHtmlFile);
            bool needToSave = false;
            // If the book is downloaded from Bloom Library, we don't want to treat it as though
            // it were directly created from a Reader bloomPack.  So relax the formatting lock.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-9996.
            if (dom.HasMetaElement("lockFormatting"))
            {
                dom.RemoveMetaElement("lockFormatting");
                needToSave = true;
            }
            if (needToSave)
                XmlHtmlConverter.SaveDOMAsHtml5(dom.RawDom, htmlFile);

            return destinationPath;
        }

        /// <summary>
        /// Most of this wants to be somewhere more general, but the error reporting is specific to downloading books.
        /// </summary>
        public static void MoveOrCopyDirectory(string tempDirectory, string destinationPath)
        {
            //clear out anything existing on our target
            var destIsClear = true;
            if (Directory.Exists(destinationPath))
            {
                try
                {
                    SIL.IO.RobustIO.DeleteDirectory(destinationPath, true);
                }
                catch (IOException)
                {
                    // can't delete it...see if we can copy into it.
                    destIsClear = false;
                }
            }

            //if we're on the same volume, we can just move it. Else copy it.
            // It's important that books appear as nearly complete as possible, because a file watcher will very soon add the new
            // book to the list of downloaded books the user can make new ones from, once it appears in the target directory.
            bool done = false;
            if (destIsClear && PathHelper.AreOnSameVolume(destinationPath, tempDirectory))
            {
                try
                {
                    SIL.IO.RobustIO.MoveDirectory(tempDirectory, destinationPath);
                    done = true;
                }
                catch (IOException)
                {
                    // If moving didn't work we'll just try copying
                }
                catch (UnauthorizedAccessException) { }
            }

            if (!done)
                done = CopyDirectory(tempDirectory, destinationPath);
            if (!done)
            {
                var msg = LocalizationManager.GetString(
                    "Download.CopyFailed",
                    "Bloom downloaded the book but had problems making it available in Bloom. Please restart your computer and try again. If you get this message again, please report the problem to us."
                );
                // The exception doesn't add much useful information but it triggers a version of the dialog with a Details button
                // that leads to the yellow box and an easy way to send the report.
                ErrorReport.NotifyUserOfProblem(new ApplicationException("File Copy problem"), msg);
            }
        }

        /// <summary>
        /// copy directory and all subdirectories. Catches most likely exceptions and returns false if any occur,
        /// unless it can read both files and they are the same.
        /// This is similar to several routines in Libpalaso DirectoryUtilities and DirectoryHelper,
        /// but I can't find one that is exactly what we need.
        /// </summary>
        /// <param name="destDirName">Note, this is not the *parent*; this is the actual name you want, e.g. CopyDirectory("c:/foo", "c:/temp/foo") </param>
        /// <returns>true if no exception occurred</returns>
        private static bool CopyDirectory(string sourceDirName, string destDirName)
        {
            bool success = true;
            var sourceDirectory = new DirectoryInfo(sourceDirName);

            if (!sourceDirectory.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: " + sourceDirName
                );
            }

            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            foreach (FileInfo file in sourceDirectory.GetFiles())
            {
                var destFileName = Path.Combine(destDirName, file.Name);
                try
                {
                    file.CopyTo(destFileName, true);
                }
                catch (Exception ex)
                {
                    if (
                        !(
                            ex is IOException
                            || ex is UnauthorizedAccessException
                            || ex is SecurityException
                        )
                    )
                        throw;
                    // Maybe we don't need to write it...it hasn't changed since a previous download?
                    if (!SameFileContent(destFileName, file.FullName))
                        success = false;
                }
            }

            foreach (DirectoryInfo subdir in sourceDirectory.GetDirectories())
            {
                success =
                    CopyDirectory(subdir.FullName, Path.Combine(destDirName, subdir.Name))
                    && success;
            }
            return success;
        }

        // Return true if both files exist, are readable, and have the same content.
        static bool SameFileContent(string path1, string path2)
        {
            if (!RobustFile.Exists(path1))
                return false;
            if (!RobustFile.Exists(path2))
                return false;
            try
            {
                var first = RobustFile.ReadAllBytes(path1);
                var second = RobustFile.ReadAllBytes(path2);
                if (first.Length != second.Length)
                    return false;
                for (int i = 0; i < first.Length; i++)
                    if (first[i] != second[i])
                        return false;
                return true;
            }
            catch (IOException)
            {
                return false; // can't even read
            }
        }
    }
}
