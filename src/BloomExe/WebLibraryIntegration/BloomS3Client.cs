using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Bloom.Book;
using Bloom.Publish;
using Bloom.web.controllers;
using BloomTemp;
using L10NSharp;
using SIL.Code;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;

namespace Bloom.WebLibraryIntegration
{
    public class AmazonS3Credentials
    {
        public string AccessKey;
        public string SecretAccessKey;
        public string SessionToken;
    }

    /// <summary>
    /// Handles Bloom file/folder operations on Amazon Web Services S3 service.
    /// </summary>
    /// <remarks>Take caution when refactoring. bloom-harvester extends this class.</remarks>
    public class BloomS3Client : IDisposable
    {
        // An S3 client which uses the newest (Oct 2023) way of handling upload credentials.
        // Namely, it gets temporary permission from our API to upload book files.
        private IAmazonS3 _amazonS3ForBookUpload;

        // An S3 client which uses IAM user AccessKeys. Used for:
        // 1. Listing book files during download.
        // 2. Uploading problem books to YouTrack.
        // 3. Unit tests; for deleting test books.
        // Also used in a couple places for downloading public read files because it is simpler to
        // reuse this than create another IAmazonS3 object.
        // As of Nov 2023, these access keys still provide write/delete, but in a couple versions,
        // we will remove that. Only the temporary credentials will be allowed to write/delete at that point.
        private IAmazonS3 _amazonS3WithAccessKey;

        private AmazonS3Config _s3Config;
        private string _previousBucketName;
        protected string _bucketName;

        public const string kDirectoryDelimeterForS3 = "/";

        public const string UnitTestBucketName = "BloomLibraryBooks-UnitTests";
        public const string SandboxBucketName = "BloomLibraryBooks-Sandbox";
        public const string ProductionBucketName = "BloomLibraryBooks";
        public const string ProblemBookUploadsBucketName = "bloom-problem-books";
        public const string BloomDesktopFiles = "bloom-desktop-files";

        public BloomS3Client(string bucketName)
        {
            _bucketName = bucketName;
            _s3Config = new AmazonS3Config { ServiceURL = "https://s3.amazonaws.com" };
            var proxy = new ProxyManager();
            if (!string.IsNullOrEmpty(proxy.Hostname))
            {
                _s3Config.ProxyHost = proxy.Hostname;
                _s3Config.ProxyPort = proxy.Port;
                if (!string.IsNullOrEmpty(proxy.Username))
                    _s3Config.ProxyCredentials = new NetworkCredential(
                        proxy.Username,
                        proxy.Password
                    );
            }
        }

        public void SetTemporaryCredentialsForBookUpload(AmazonS3Credentials credentials)
        {
            if (_amazonS3ForBookUpload != null)
                _amazonS3ForBookUpload.Dispose();
            _amazonS3ForBookUpload = CreateAmazonS3Client(_s3Config, credentials);
        }

        protected IAmazonS3 GetAmazonS3WithAccessKey(string bucketName)
        {
            //Note, it would probably be fine to just generate this each time,
            //but this was the more conservative approach when refactoring
            //to allow a single client to access arbitrary buckets, thus requiring
            //appropriate change of access keys, thus requiring changing AmazonS3Client objects.
            if (bucketName != _previousBucketName)
            {
                if (_amazonS3WithAccessKey != null)
                    _amazonS3WithAccessKey.Dispose();
                _amazonS3WithAccessKey = CreateAmazonS3Client(
                    _s3Config,
                    GetAccessKeyCredentials(bucketName)
                );

                _previousBucketName = bucketName;
            }
            return _amazonS3WithAccessKey; // we keep this so that we can dispose of it later.
        }

        // Overriden in bloom-harvester as of Nov 2023
        protected virtual AmazonS3Credentials GetAccessKeyCredentials(string bucketName)
        {
            var accessKeys = AccessKeys.GetAccessKeys(bucketName);
            return new AmazonS3Credentials
            {
                AccessKey = accessKeys.S3AccessKey,
                SecretAccessKey = accessKeys.S3SecretAccessKey
            };
        }

        // Overriden in bloom-harvester as of Nov 2023
        protected virtual IAmazonS3 CreateAmazonS3Client(
            AmazonS3Config s3Config,
            AmazonS3Credentials credentials
        )
        {
            if (!string.IsNullOrEmpty(credentials.SessionToken))
                return new AmazonS3Client(
                    credentials.AccessKey,
                    credentials.SecretAccessKey,
                    credentials.SessionToken,
                    s3Config
                );

            return new AmazonS3Client(credentials.AccessKey, credentials.SecretAccessKey, s3Config);
        }

        /// <summary>
        /// Gets or sets the request timeout.
        /// </summary>
        public TimeSpan? Timeout
        {
            get { return _s3Config.Timeout; }
            set { _s3Config.Timeout = value; }
        }

        /// <summary>
        /// Gets or sets the timeout for socket read or write operations.
        /// </summary>
        public TimeSpan? ReadWriteTimeout
        {
            get { return _s3Config.ReadWriteTimeout; }
            set { _s3Config.ReadWriteTimeout = value; }
        }

        /// <summary>
        /// Gets or sets the maximum number of times to retry when errors occur.
        /// </summary>
        public int MaxErrorRetry
        {
            get { return _s3Config.MaxErrorRetry; }
            set { _s3Config.MaxErrorRetry = value; }
        }

        internal List<S3Object> GetMatchingItems(string bucketName, string key)
        {
            return GetAmazonS3WithAccessKey(bucketName)
                .ListAllObjects(
                    new ListObjectsV2Request() { BucketName = bucketName, Prefix = key }
                );
        }

        /// <summary>
        /// Allows a file to be put into the root of the bucket.
        /// Could be enhanced to specify a sub folder path, but I don't need that for the current use.
        /// (Current use is to upload problem books to YouTrack reports.)
        /// </summary>
        /// <returns>url to the uploaded file</returns>
        public string UploadSingleFile(string pathToFile)
        {
            using (
                var transferUtility =
                    (BookUpload.IsDryRun)
                        ? null
                        : new TransferUtility(GetAmazonS3WithAccessKey(_bucketName))
            )
            {
                var request = new TransferUtilityUploadRequest
                {
                    BucketName = _bucketName,
                    FilePath = pathToFile,
                    Key = Path.GetFileName(pathToFile),
                    CannedACL = S3CannedACL.PublicRead // Allows any browser to download it.
                };
                // no-cache means "The response may be stored by any cache, even if the response is normally non-cacheable. However,
                // the stored response MUST always go through validation with the origin server first before using it..."
                request.Headers.CacheControl = "no-cache";
                if (!BookUpload.IsDryRun)
                    transferUtility.Upload(request);
                return "https://s3.amazonaws.com/"
                    + _bucketName
                    + "/"
                    + HttpUtility.UrlEncode(request.Key);
            }
        }

        /// <summary>
        /// The thing here is that we need to guarantee unique names at the top level, so we wrap the books inside a folder
        /// with some unique name. As this involves copying the folder it is also a convenient place to omit any PDF files
        /// except the one we want.
        /// </summary>
        public void UploadBook(
            string storageKeyOfBookFolderParent,
            string pathToBloomBookDirectory,
            IProgress progress,
            string pdfToInclude,
            bool includeNarrationAudio,
            bool includeMusic,
            string[] textLanguagesToInclude,
            string[] audioLanguagesToInclude,
            string metadataLang1Code,
            string metadataLang2Code,
            bool isNewBook,
            string collectionSettingsPath = null,
            bool isForBulkUpload = false
        )
        {
            // This currently (unfortunately) enforces a single upload at a time.
            // We considered modifying it now, but decided we would discover the problem
            // if and when we try to implement parallel uploads.
            using (var stagingParentDirectory = new TemporaryFolder("BloomUploadStaging"))
            {
                var stagingDirectory = Path.Combine(
                    stagingParentDirectory.FolderPath,
                    Path.GetFileName(pathToBloomBookDirectory)
                );
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

                SyncBookFiles(
                    isNewBook,
                    storageKeyOfBookFolderParent,
                    stagingParentDirectory.FolderPath,
                    progress
                );
            }
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
                .Cast<XmlElement>();
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
            var settingsText = RobustFile.ReadAllText(settingsPath);
            var doc = new XmlDocument();
            doc.PreserveWhitespace = true;
            doc.LoadXml(settingsText);
            var subscriptionNode = doc.SelectSingleNode("/Collection/SubscriptionCode");
            if (subscriptionNode != null)
                subscriptionNode.InnerText = "";
            Directory.CreateDirectory(Path.Combine(tempBookFolder, "collectionFiles"));
            doc.Save(
                Path.Combine(tempBookFolder, "collectionFiles", "book.uploadCollectionSettings")
            );
        }

        public void RemoveUnwantedLanguageData(
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
            metadata.WriteToFolder(destDirName);
        }

        // At this point, the API has created an S3 folder for us to work with. If the book aready exists, it
        // has a copy of the existing book files.
        // We work with that copy, deleting, adding, and updating files as needed.
        // If we never finish for some reason, we never tell the API we finished, the book record is never updated to point
        // to the new files, and a cleanup function will eventually reset the book record and delete the copy of the book files.
        //Enhance: This is currently synchronous. Research how we can make use of the async SDK methods to enable parallel uploads
        // or whatever other performance enhancements we can gain.
        // When/if we do this, the logic would be something like: queue up all the deletes, run them in parallel. Then queue up
        // all the puts (ensuring we don't put the same file twice) and run those in parallel.
        public void SyncBookFiles(
            bool isNewBook,
            string storageKeyOfBookFolderParent,
            string stagingDirectory,
            IProgress progress
        )
        {
            Guard.Against(BookUpload.IsDryRun, "We shouldn't try to sync files on S3 in a dry run");

            if (!Directory.Exists(stagingDirectory))
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: " + stagingDirectory
                );

            var existingObjectsOnS3 = new List<S3Object>();
            if (!isNewBook)
            {
                // sync all files which are already on S3
                var listObjectRequest = new ListObjectsV2Request
                {
                    BucketName = _bucketName,
                    Prefix = storageKeyOfBookFolderParent
                };
                existingObjectsOnS3 = _amazonS3ForBookUpload.ListAllObjects(listObjectRequest);
                foreach (S3Object s3Object in existingObjectsOnS3)
                {
                    // If the user cancels at this point, we don't tell the API, but there is a cleanup function which runs each day.
                    // It will find the parse record which never finished uploading, reset it, and delete the copy of the book files on S3.
                    if (progress.CancelRequested)
                        return;

                    string localFilePath = Path.Combine(
                        stagingDirectory,
                        s3Object.Key.Substring(storageKeyOfBookFolderParent.Length)
                    );
                    if (
                        IsExcludedFromUploadByExtension(localFilePath)
                        || !RobustFile.Exists(localFilePath)
                    )
                    {
                        _amazonS3ForBookUpload.DeleteObject(_bucketName, s3Object.Key);
                    }
                    else
                    {
                        string localFileEtag = GetEtag(localFilePath);
                        if (localFileEtag != s3Object.ETag)
                        {
                            PutFileOnS3(localFilePath, s3Object.Key, progress);
                        }
                        else
                        {
                            progress.WriteStatus(
                                LocalizationManager.GetString(
                                    "PublishTab.Upload.FileIsUnchanged",
                                    "{0} is unchanged",
                                    "{0} is a file name; this message indicates we are not uploading the file because the contents are the same they were when it was uploaded previously"
                                ),
                                Path.GetFileName(localFilePath)
                            );
                        }
                    }
                }
            }

            // We've synced all the existing files on S3.
            // Now upload any files which are not on S3.
            foreach (
                var localFilePath in Directory.GetFiles(
                    stagingDirectory,
                    "*.*",
                    SearchOption.AllDirectories
                )
            )
            {
                // See cancel comment above.
                if (progress.CancelRequested)
                    return;

                if (IsExcludedFromUploadByExtension(localFilePath))
                    continue; // BL-2246: skip uploading this one

                string key =
                    storageKeyOfBookFolderParent
                    + localFilePath.Substring(stagingDirectory.Length).Replace("\\", "/");
                key = key.Replace("//", "/"); // safety net
                if (isNewBook || !existingObjectsOnS3.Any(o => o.Key == key))
                {
                    PutFileOnS3(localFilePath, key, progress);
                }
            }
        }

        //Note: there is a similar list for BloomPacks, but it is not identical, so don't just copy/paste
        private static readonly string[] excludedFileExtensionsLowerCase =
        {
            ".db",
            ".bloompack",
            ".bak",
            ".userprefs",
            ".md",
            ".map"
        };

        // Argh! Why isn't this handled by the BookFileFilter??
        private bool IsExcludedFromUploadByExtension(string localFilePath)
        {
            return excludedFileExtensionsLowerCase.Contains(
                Path.GetExtension(localFilePath).ToLowerInvariant()
            );
        }

        private void PutFileOnS3(string localFilePath, string s3Key, IProgress progress)
        {
            Guard.Against(BookUpload.IsDryRun, "We shouldn't try to put files on S3 in a dry run");

            var request = new PutObjectRequest
            {
                FilePath = localFilePath,
                BucketName = _bucketName,
                Key = s3Key,
                // Allows any browser to download it.
                // Purposefully, this is the only ACL our temporary permissions allow us to set.
                CannedACL = S3CannedACL.PublicRead
            };

            SetContentDisposition(request, localFilePath);

            // no-cache means "The response may be stored by any cache, even if the response is normally non-cacheable. However,
            // the stored response MUST always go through validation with the origin server first before using it..."
            request.Headers.CacheControl = "no-cache";

            var uploadMsgFmt = LocalizationManager.GetString(
                "PublishTab.Upload.UploadingStatus",
                "Uploading {0}"
            );
            progress.WriteStatus(uploadMsgFmt, Path.GetFileName(localFilePath));

            _amazonS3ForBookUpload.PutObject(request);
        }

        private string GetEtag(string filePath)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = RobustFile.OpenRead(filePath))
                {
                    byte[] hash = md5.ComputeHash(stream);
                    return "\"" + BitConverter.ToString(hash).Replace("-", "").ToLower() + "\"";
                }
            }
        }

        private void SetContentDisposition(PutObjectRequest request, string localFilePath)
        {
            // The effect of this is that navigating to the file's URL is always treated as an attempt to download the file.
            // This is definitely not desirable for the PDF (typically a preview) which we want to navigate to in the Preview button
            // of BloomLibrary.
            // I'm not sure whether there is still any reason to do it for other files.
            // It was temporarily important for the BookOrder file when the Open In Bloom button just downloaded it.
            // However, now the download link uses the bloom: prefix to get the URL passed directly to Bloom,
            // it may not be needed for anything. Still, at least for the files a browser would not know how to
            // open, it seems desirable to download them with their original names, if such a thing should ever happen.
            // So I'm leaving the code in for now except in cases where we know we don't want it.
            // It is possible to also set the filename ( after attachment, put ; filename='" + Path.GetFileName(file) + "').
            // In principle this would be a good thing, since the massive AWS filenames are not useful.
            // However, AWSSDK can't cope with setting this for files with non-ascii names.
            // It seems that the header we insert here eventually becomes a header for a web request, and these allow only ascii.
            // There may be some way to encode non-ascii filenames to get the effect, if we ever want it again. Or AWS may fix the problem.
            // If you put setting the filename back in without such a workaround, be sure to test with a non-ascii book title.
            if (Path.GetExtension(localFilePath).ToLowerInvariant() != ".pdf")
                request.Headers.ContentDisposition = "attachment";
        }

        /// <summary>
        /// copy directory and all subdirectories
        /// </summary>
        /// <param name="sourceDirName"></param>
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

        public string DownloadFile(string bucketName, string storageKeyOfFile)
        {
            var request = new GetObjectRequest()
            {
                BucketName = bucketName,
                Key = storageKeyOfFile
            };
            // We actually don't need any credentials to download the files we are dealing with because they are public read,
            // but it simplifies the code a bit to just reuse the existing IAmazonS3 object.
            using (var response = GetAmazonS3WithAccessKey(bucketName).GetObject(request))
            using (var stream = response.ResponseStream)
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        internal static bool AvoidThisFile(string objectKey)
        {
            // Note that Amazon S3 regards "/" as the directory delimiter for directory oriented
            // displays of object keys.
            string[] endsToAvoid = { "/thumbs.db", ".pdf", ".map" };
            if (endsToAvoid.Any(end => objectKey.ToLowerInvariant().EndsWith(end)))
                return true;
            // The harvester needs the collection settings file, but we don't want to download it
            // when users are downloading books.  (See BL-12583.)
            if (!Program.RunningHarvesterMode)
            {
                string[] foldersToAvoid = { "collectionfiles/" };
                if (foldersToAvoid.Any(folder => objectKey.ToLowerInvariant().Contains(folder)))
                    return true;
            }

            // Removing this restriction on downloading narration per BL-9652
            //if (!Program.RunningHarvesterMode)
            //{
            //	// Except when harvesting, we only want to download audio for "music", not narration.
            //	// The way we determine the difference is that narration audio files are guids. (but also see comment on the regex)
            //	// This isn't 100% accurate because, in theory, someone could choose a music file which has a guid file name.
            //	// But we are living with that possibility for now.
            //	if (AudioProcessor.MusicFileExtensions.Any(end => objectKey.ToLowerInvariant().EndsWith(end)))
            //	{
            //		var match = NarrationFileNameRegex.Match(objectKey);
            //		if (match.Success)
            //		{
            //			Guid dummy;
            //			return Guid.TryParse(match.Groups[2].Value, out dummy);
            //		}
            //	}
            //}

            return false;
        }

        private int CountDesiredFiles(List<S3Object> matching)
        {
            int totalItems = 0;
            for (int i = 0; i < matching.Count; ++i)
            {
                if (AvoidThisFile(matching[i].Key))
                    continue;
                ++totalItems;
            }
            return totalItems;
        }

        /// <summary>
        /// Warning, if the book already exists in the location, this is going to delete it and over-write it. So it's up to the caller to check the sanity of that.
        /// </summary>
        public string DownloadBook(
            string bucketName,
            string storageKeyOfBookFolderParent,
            string pathToDestinationParentDirectory,
            IProgressDialog downloadProgress = null
        )
        {
            //review: should we instead save to a newly created folder so that we don't have to worry about the
            //other folder existing already? Todo: add a test for that first.

            // We need to download individual files to avoid downloading unwanted files (PDFs and thumbs.db to
            // be specific).  See https://silbloom.myjetbrains.com/youtrack/issue/BL-2312.  So we need the list
            // of items, not just the count.
            var matching = GetMatchingItems(bucketName, storageKeyOfBookFolderParent);
            var totalItems = CountDesiredFiles(matching);
            if (totalItems == 0)
                throw new DirectoryNotFoundException(
                    "The book we tried to download is no longer in the BloomLibrary"
                );

            if (!storageKeyOfBookFolderParent.EndsWith("/"))
                storageKeyOfBookFolderParent += '/';

            Debug.Assert(
                matching[0].Key.StartsWith(storageKeyOfBookFolderParent),
                "Matched object does not start with storageKey"
            );

            // Get the top-level directory name of the book from the first object prefix.
            var bookFolderName = matching[0].Key.Substring(storageKeyOfBookFolderParent.Length);
            while (bookFolderName.Contains("/") || bookFolderName.Contains("\\"))
            {
                // Note: Path.GetDirectoryName may replace "/" (URL format) with "\" (Windows format),
                // which can be problematic when examining bookFolderName in a loop.
                // Need to check both / and \
                bookFolderName = Path.GetDirectoryName(bookFolderName);
            }

            // Amazon.S3 appears to truncate titles at 50 characters when building directory and filenames.  This means
            // that relative paths can be as long as 117 characters (2 * 50 + 2 for slashes + 15 for .BloomBookOrder).
            // So our temporary folder must be no more than 140 characters (allow some margin) since paths can be a
            // maximum of 260 characters in Windows.  (More margin than that may be needed because there's no guarantee
            // that image filenames are no longer than 65 characters.)  See https://jira.sil.org/browse/BL-1160.
            // https://issues.bloomlibrary.org/youtrack/issue/BH-5988 has a book with an image file whose name is 167 characters long.
            // So, 50 + 2 for slashes + 167 = 219. This means the temporary folder should be no more than 40 characters long.
            // "C:\Users\steve\AppData\Local\Temp\" is already 35 characters long, and usernames can certainly be longer
            // than 5 characters.  So we can't really afford much randomness in the folder name.
            using (var tempDestination = new TemporaryFolder(GetMinimalRandomFolderName()))
            {
                var tempDirectory = Path.Combine(tempDestination.FolderPath, bookFolderName);
                float progressStep = 1.0F;
                float progress = 0.0F;
                if (downloadProgress != null)
                    downloadProgress.Invoke(
                        (Action)(
                            () =>
                            {
                                // We cannot change ProgressRangeMaximum here because the worker thread is already active.
                                // So we calculate a step value instead.  See https://issues.bloomlibrary.org/youtrack/issue/BL-5443.
                                progressStep =
                                    (float)(
                                        downloadProgress.ProgressRangeMaximum
                                        - downloadProgress.Progress
                                    ) / (float)totalItems;
                                progress = (float)downloadProgress.Progress;
                            }
                        )
                    );
                // We actually don't need any credentials to download the files we are dealing with because they are public read,
                // but it simplifies the code a bit to just reuse the existing IAmazonS3 object.
                using (
                    var transferUtility = new TransferUtility(GetAmazonS3WithAccessKey(bucketName))
                )
                {
                    for (int i = 0; i < matching.Count; ++i)
                    {
                        var objKey = matching[i].Key;
                        if (AvoidThisFile(objKey))
                            continue;
                        // Removing the book's prefix from the object prefix, then using the remainder of the prefix
                        // in the filepath allows for nested subdirectories.
                        var filepath = objKey.Substring(storageKeyOfBookFolderParent.Length);
                        // Download this file then bump progress.
                        var req = new TransferUtilityDownloadRequest()
                        {
                            BucketName = bucketName,
                            Key = objKey,
                            FilePath = Path.Combine(tempDestination.FolderPath, filepath)
                        };
                        transferUtility.Download(req);
                        if (downloadProgress != null)
                            downloadProgress.Invoke(
                                (Action)(
                                    () =>
                                    {
                                        progress += progressStep;
                                        downloadProgress.Progress = (int)progress;
                                    }
                                )
                            );
                    }
                    var destinationPath = Path.Combine(
                        pathToDestinationParentDirectory,
                        bookFolderName
                    );

                    //clear out anything existing on our target
                    var didDelete = false;
                    if (Directory.Exists(destinationPath))
                    {
                        try
                        {
                            SIL.IO.RobustIO.DeleteDirectory(destinationPath, true);
                            didDelete = true;
                        }
                        catch (IOException)
                        {
                            // can't delete it...see if we can copy into it.
                        }
                    }

                    //if we're on the same volume, we can just move it. Else copy it.
                    // It's important that books appear as nearly complete as possible, because a file watcher will very soon add the new
                    // book to the list of downloaded books the user can make new ones from, once it appears in the target directory.
                    bool done = false;
                    if (
                        didDelete
                        && PathHelper.AreOnSameVolume(
                            pathToDestinationParentDirectory,
                            tempDirectory
                        )
                    )
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
                        ErrorReport.NotifyUserOfProblem(
                            new ApplicationException("File Copy problem"),
                            msg
                        );
                    }
                    return destinationPath;
                }
            }
        }

        private string GetMinimalRandomFolderName()
        {
            if (SIL.PlatformUtilities.Platform.IsLinux)
                return "BDS_" + Guid.NewGuid(); // no path length limits on Linux
            string name = "BDS_"; // won't get used, but prevent compiler from squawking...
            for (int i = 0; i < 100; ++i)
            {
                var randomID = Convert
                    .ToBase64String(Guid.NewGuid().ToByteArray())
                    .Replace("/", "_")
                    .Replace("+", "-")
                    .Trim(new[] { '=' });
                name = "BDS" + randomID.Substring(0, 2); // 2 chars of randomness = 64 ** 2 = 4096 possibilities.
                var tempPath = Path.Combine(Path.GetTempPath(), name);
                if (!RobustFile.Exists(tempPath) && !Directory.Exists(tempPath))
                    return name;
                // We're finding a lot of names in use.  To improve our chances, and since this is probably garbage left behind by other
                // downloads, start trying to clean them up.
                if (i > 10)
                {
                    try
                    {
                        if (RobustFile.Exists(tempPath))
                            RobustFile.Delete(tempPath);
                        else if (Directory.Exists(tempPath))
                            SIL.IO.RobustIO.DeleteDirectory(tempPath, true);
                        return name;
                    }
                    catch (Exception)
                    {
                        // can't delete it... hope we can the next one.  or copy into the last one...
                    }
                }
            }
            // Take a chance...
            return name;
        }

        public string GetBaseUrl(string storageKeyOfBookFolder)
        {
            // Yes, this encoding means the slashes are converted to %2f.
            // That's unfortunate, but that's how we've always done it.
            // And that's how old Blooms expect to receive it, so we're stuck with it.
            return $"https://s3.amazonaws.com/{_bucketName}/{HttpUtility.UrlEncode(storageKeyOfBookFolder)}";
        }

        // We get the full URL from the server, like https://s3.amazonaws.com/BloomLibraryBooks/5b4b4b4b/1234567890/
        // We want the part after the bucket name, like 5b4b4b4b/1234567890/
        public static string GetStorageKeyOfBookFolderParentFromUrl(string url)
        {
            Regex s3UrlRegex = new Regex(@"^https?://s3\.amazonaws\.com/[^/]+/(.*)$");
            var match = s3UrlRegex.Match(url);
            if (!match.Success)
                throw new ArgumentException("Not an S3 URL", nameof(url));
            return match.Groups[1].Value;
        }

        // baseUrl looks like https://s3.amazonaws.com/BloomLibraryBooks/5b4b4b4b%2f1234567890%2fBook+Title%2f
        // We want the part after the bucket name, without encoding, like 5b4b4b4b/1234567890/Book Title
        public static string GetStorageKeyOfBookFolder(string baseUrl)
        {
            Regex baseUrlRegex = new Regex(@"^https?://s3\.amazonaws\.com/[^/]+/(.*)$");
            var match = baseUrlRegex.Match(baseUrl.Replace("%2f", "/"));
            if (!match.Success)
                throw new ArgumentException("Not a valid base URL", nameof(baseUrl));
            return HttpUtility.UrlDecode(match.Groups[1].Value);
        }

        public void Dispose()
        {
            if (_amazonS3ForBookUpload != null)
            {
                _amazonS3ForBookUpload.Dispose();
                _amazonS3ForBookUpload = null;
            }
            if (_amazonS3WithAccessKey != null)
            {
                _amazonS3WithAccessKey.Dispose();
                _amazonS3WithAccessKey = null;
            }

            GC.SuppressFinalize(this);
        }
    }
}
