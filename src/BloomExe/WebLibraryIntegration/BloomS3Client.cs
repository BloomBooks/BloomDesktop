using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
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
                _amazonS3WithAccessKey = CreateAmazonS3Client(_s3Config, bucketName);

                _previousBucketName = bucketName;
            }
            return _amazonS3WithAccessKey; // we keep this so that we can dispose of it later.
        }

        // Overriden in bloom-harvester as of March 2024
        protected virtual AmazonS3Credentials GetAccessKeyCredentials(string bucketName)
        {
            var accessKeys = AccessKeys.GetAccessKeys(bucketName);
            return new AmazonS3Credentials
            {
                AccessKey = accessKeys.S3AccessKey,
                SecretAccessKey = accessKeys.S3SecretAccessKey
            };
        }

        protected virtual IAmazonS3 CreateAmazonS3Client(AmazonS3Config s3Config, string bucketName)
        {
            var credentials = GetAccessKeyCredentials(bucketName);
            return CreateAmazonS3Client(s3Config, credentials);
        }

        protected IAmazonS3 CreateAmazonS3Client(
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

        internal List<S3Object> GetMatchingItems(string bucketName, string prefix)
        {
            return GetAmazonS3WithAccessKey(bucketName)
                .ListAllObjects(
                    new ListObjectsV2Request() { BucketName = bucketName, Prefix = prefix }
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

        // The API has determined which files are new or modified, and we need to upload those.
        // Unmodified files are copied by the Azure function. Obsolete files don't need to be copied or uploaded.
        //
        // If we never finish our upload for some reason, we never tell the API we finished, the book record is never updated to point
        // to the new files, and a cleanup function will eventually reset the book record and delete the copy of the book files.
        //
        //Enhance: This is currently synchronous. Research how we can make use of the async SDK methods to enable parallel uploads
        // or whatever other performance enhancements we can gain.
        public void UploadBook(
            string s3PrefixToUploadTo,
            string stagingDirectory,
            string[] filesToUpload,
            IProgress progress
        )
        {
            Guard.Against(BookUpload.IsDryRun, "We shouldn't try to sync files on S3 in a dry run");

            if (filesToUpload == null)
                return;

            if (!Directory.Exists(stagingDirectory))
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: " + stagingDirectory
                );

            foreach (var fileToUpload in filesToUpload)
            {
                // If the user cancels at this point, we don't tell the API, but there is a cleanup function which runs each day.
                // It will find the database book record of the incomplete upload, reset it, and delete the copy of the book files on S3.
                if (progress.CancelRequested)
                    return;

                var localFilePath = Path.Combine(stagingDirectory, fileToUpload);
                if (!RobustFile.Exists(localFilePath))
                    throw new FileNotFoundException(
                        "File to upload does not exist or could not be found: " + localFilePath
                    );
                string key = s3PrefixToUploadTo + fileToUpload;
                PutFileOnS3(localFilePath, key, progress);
            }
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

        // Usually, the S3 ETag is just the md5 hash of the file contents.
        // Technically, there are cases where it can be something else such as multi-part uploads.
        // See https://teppen.io/2018/06/23/aws_s3_etags/.
        // But we are just doing simple uploads, so it should always match.
        // Even if it doesn't match for some reason, the worst that will happen is a performance loss;
        // we use this to determine that we can just copy the old version of the file instead of upload it again.
        public static string GetProbableEtag(string filePath)
        {
            return "\"" + Utils.MiscUtils.GetMd5HashOfFile(filePath) + "\"";
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

        internal static bool AvoidThisFile(string objectKey, bool includeCollectionFiles)
        {
            // Note that Amazon S3 regards "/" as the directory delimiter for directory oriented
            // displays of object keys.
            string[] endsToAvoid = { "/thumbs.db", ".pdf", ".map" };
            if (endsToAvoid.Any(end => objectKey.ToLowerInvariant().EndsWith(end)))
                return true;
            // The harvester needs the collection settings file, but we don't want to download it
            // when users are downloading books.  (See BL-12583.)
            // Exception: when we are downloading a book and making a new collection in which
            // to edit it, we do want the collection settings file.
            if (!Program.RunningHarvesterMode && !includeCollectionFiles)
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

        private int CountDesiredFiles(List<S3Object> matching, bool includeCollectionFiles)
        {
            int totalItems = 0;
            for (int i = 0; i < matching.Count; ++i)
            {
                if (AvoidThisFile(matching[i].Key, includeCollectionFiles))
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
            IProgressDialog downloadProgress = null,
            bool forEdit = false
        )
        {
            //review: should we instead save to a newly created folder so that we don't have to worry about the
            //other folder existing already? Todo: add a test for that first.

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
                var bookFolderName = DownLoadBookDirect(
                    bucketName,
                    storageKeyOfBookFolderParent,
                    downloadProgress,
                    forEdit,
                    tempDestination.FolderPath
                );
                if (bookFolderName == null)
                {
                    // cancelled
                    return null;
                }
                var tempDirectory = Path.Combine(tempDestination.FolderPath, bookFolderName);

                var destinationPath = Path.Combine(
                    pathToDestinationParentDirectory,
                    bookFolderName
                );

                BookDownload.MoveOrCopyDirectory(tempDirectory, destinationPath);

                return destinationPath;
            }
        }

        /// <summary>
        /// This variant of downloading a book assumes that the caller has already created a temporary directory and
        /// we can safely make a folder inside it using the natural name for the book's folder
        /// </summary>
        public string DownLoadBookDirect(
            string bucketName,
            string storageKeyOfBookFolderParent,
            IProgressDialog downloadProgress,
            bool forEdit,
            string tempDestPath
        )
        {
            // We need to download individual files to avoid downloading unwanted files (PDFs and thumbs.db to
            // be specific).  See https://silbloom.myjetbrains.com/youtrack/issue/BL-2312.  So we need the list
            // of items, not just the count.
            var matching = GetMatchingItems(bucketName, storageKeyOfBookFolderParent);
            var totalItems = CountDesiredFiles(matching, forEdit);
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
            using (var transferUtility = new TransferUtility(GetAmazonS3WithAccessKey(bucketName)))
            {
                for (int i = 0; i < matching.Count; ++i)
                {
                    var objKey = matching[i].Key;
                    if (AvoidThisFile(objKey, forEdit))
                        continue;
                    // Removing the book's prefix from the object prefix, then using the remainder of the prefix
                    // in the filepath allows for nested subdirectories.
                    var filepath = objKey.Substring(storageKeyOfBookFolderParent.Length);
                    // Download this file then bump progress.
                    var req = new TransferUtilityDownloadRequest()
                    {
                        BucketName = bucketName,
                        Key = objKey,
                        FilePath = Path.Combine(tempDestPath, filepath)
                    };
                    transferUtility.Download(req);
                    if (downloadProgress != null)
                    {
                        // This implements the cancellation as well as guarding against sending progress
                        // updates to a disposed progress dialog.
                        if (downloadProgress.CancellationPending())
                            return null;
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
                }
            }

            return bookFolderName;
        }

        public string GetMinimalRandomFolderName()
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

        // Given the S3 prefix where book files are uploaded, determine the full baseUrl.
        public string GetBaseUrl(string prefixForBookFiles)
        {
            // Yes, this encoding means the slashes are converted to %2f.
            // That's unfortunate, but that's how we've always done it.
            // And that's how old Blooms expect to receive it, so we're stuck with it.
            return $"https://s3.amazonaws.com/{_bucketName}/{HttpUtility.UrlEncode(prefixForBookFiles)}";
        }

        private static Regex s_s3UrlRegex = new Regex(
            @"^https?://s3\.amazonaws\.com/[^/]+/(.*)$",
            RegexOptions.Compiled
        );

        // We get the full URL where we are supposed to upload the book files
        // from the server, like https://s3.amazonaws.com/BloomLibraryBooks/5b4b4b4b/1234567890/title/
        // We want the part after the bucket name, like 5b4b4b4b/1234567890/title/
        // This is what the S3 SDK refers to as the prefix.
        public static string GetPrefixFromBookFileUploadUrl(string bookFileUploadUrl)
        {
            var match = s_s3UrlRegex.Match(bookFileUploadUrl);
            if (!match.Success)
                throw new ArgumentException("Not an S3 URL", nameof(bookFileUploadUrl));
            return match.Groups[1].Value;
        }

        // baseUrl is a database field which we use to point to various book resources.
        // It looks like
        // old style:
        // https://s3.amazonaws.com/BloomLibraryBooks/bob@example.com%2f8d0d9043-a1bb-422d-aa5b-29726cdcd96a%2fBook+Title%2f
        // new style:
        // https://s3.amazonaws.com/BloomLibraryBooks/5b4b4b4b%2f1234567890%2fBook+Title%2f
        // We want the part after the bucket name, without encoding, like 5b4b4b4b/1234567890/Book Title
        // This is what the S3 SDK refers to as the prefix.
        // Note that baseUrl is url-encoded, including (who knows why?...) the slashes (%2f). Older Blooms expect the %2f.
        public static string GetPrefixFromBaseUrl(string baseUrl)
        {
            var match = s_s3UrlRegex.Match(baseUrl.Replace("%2f", "/"));
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
