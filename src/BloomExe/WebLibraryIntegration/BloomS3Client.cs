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
using System.Xml;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Publish;
using Bloom.web.controllers;
using BloomTemp;
using L10NSharp;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Xml;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// Handles Bloom file/folder operations on Amazon Web Services S3 service.
	/// </summary>
	public class BloomS3Client:IDisposable
	{
		private IAmazonS3 _amazonS3;
		public const string kDirectoryDelimeterForS3 = "/";
		public const string UnitTestBucketName = "BloomLibraryBooks-UnitTests";
		public const string SandboxBucketName = "BloomLibraryBooks-Sandbox";
		public const string ProductionBucketName = "BloomLibraryBooks";
		public const string ProblemBookUploadsBucketName = "bloom-problem-books";
		public const string BloomDesktopFiles = "bloom-desktop-files";

		// Notice the optional "i".
		// These file names are guids, and if the guid starts with a number, we prepend "i".
		// But really we just want to extract the possible guid part to see if it is, in fact, a guid.
		private static readonly Regex NarrationFileNameRegex = new Regex("^(.*[\\/\\\\])*i?(.+?)\\.[^.]*$", RegexOptions.Compiled);

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
					_s3Config.ProxyCredentials = new NetworkCredential(proxy.Username, proxy.Password);
			}
		}

		protected IAmazonS3 GetAmazonS3(string bucketName)
		{
			//Note, it would probably be fine to just generate this each time,
			//but this was the more conservative approach when refactoring
			//to allow a single client to access arbitrary buckets, thus requiring
			//appropriate change of access keys, thus requiring changing AmazonS3Client objects.
			if(bucketName != _previousBucketName)
			{
				if (_amazonS3 != null)
					_amazonS3.Dispose();
				_amazonS3 = CreateAmazonS3Client(bucketName, _s3Config);

				_previousBucketName = bucketName;
			}
			return _amazonS3; // we keep this so that we can dispose of it later.
		}

		protected virtual IAmazonS3 CreateAmazonS3Client(string bucketName, AmazonS3Config s3Config)
		{
			var accessKeys = AccessKeys.GetAccessKeys(bucketName);
			return new AmazonS3Client(accessKeys.S3AccessKey,
				accessKeys.S3SecretAccessKey, s3Config);
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

		/// <summary>
		/// This is set during UploadBook to the URL holding files like various thumbnails, preview, etc.
		/// It ends up in a parse.com column named "baseUrl", and the angular appends things like "/thumbnail256.png" to it.
		/// It only contains useful information after UploadBook.
		/// </summary>
		public string BaseUrl { get; private set; }

		// Similarly for the book order file.
		public string BookOrderUrlOfRecentUpload { get; private set; }

		internal List<S3Object> GetMatchingItems(string bucketName, string key)
		{
			return GetAmazonS3(bucketName).ListAllObjects(new ListObjectsRequest()
				{
					BucketName = bucketName,
					Prefix = key
				});
		}

		internal int GetBookFileCount(string key, string bucketName)
		{
			var count = GetMatchingItems(key, bucketName).Count;
			return count;
		}

		public void DeleteBookData(string bucketName, string key)
		{
			if (BookUpload.IsDryRun)
				return;

			var listMatchingObjectsRequest = new ListObjectsRequest()
			{
				BucketName = bucketName,
				Prefix = key
			};

			ListObjectsResponse matchingFilesResponse;
			do
			{
				// Note: ListObjects can only return 1,000 objects at a time,
				//       and DeleteObjects can only delete 1,000 objects at a time.
				//       So a loop is needed if the book contains 1,001+ objects.
				matchingFilesResponse = GetAmazonS3(bucketName).ListObjects(listMatchingObjectsRequest);
				if (matchingFilesResponse.S3Objects.Count == 0)
					return;

				var deleteObjectsRequest = new DeleteObjectsRequest()
				{
					BucketName = bucketName,
					Objects = matchingFilesResponse.S3Objects.Select(s3Object => new KeyVersion() { Key = s3Object.Key }).ToList()
				};

				var response = GetAmazonS3(bucketName).DeleteObjects(deleteObjectsRequest);
				Debug.Assert(response.DeleteErrors.Count == 0);

				// Prep the next request (if needed)
				listMatchingObjectsRequest.Marker = matchingFilesResponse.NextMarker;
			}
			while (matchingFilesResponse.IsTruncated);	// Returns true if haven't reached the end yet
		}

		public void EmptyUnitTestBucket(string prefix)
		{
			DeleteBookData(BloomS3Client.UnitTestBucketName, prefix);
		}

		/// <summary>
		/// Allows a file to be put into the root of the bucket.
		/// Could be enhanced to specify a sub folder path, but I don't need that for the current use.
		/// </summary>
		/// <returns>url to the uploaded file</returns>
		public string UploadSingleFile(string pathToFile, IProgress progress)
		{
			using (var transferUtility =  (BookUpload.IsDryRun) ? null : new TransferUtility(GetAmazonS3(_bucketName)))
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
				progress.WriteStatus("Uploading book to Bloom Support...");
				Console.WriteLine("Uploading book to Bloom Support...");
				if (!BookUpload.IsDryRun)
					transferUtility.Upload(request);
				return "https://s3.amazonaws.com/" + _bucketName + "/" + HttpUtility.UrlEncode(request.Key);
			}
		}


		/// <summary>
		/// The thing here is that we need to guarantee unique names at the top level, so we wrap the books inside a folder
		/// with some unique name. As this involves copying the folder it is also a convenient place to omit any PDF files
		/// except the one we want.
		/// </summary>
		public void UploadBook(string storageKeyOfBookFolder, string pathToBloomBookDirectory, IProgress progress,
			string pdfToInclude, bool includeNarrationAudio, bool includeMusic,
			string[] languagesToInclude, string metadataLang1Code, string metadataLang2Code, bool isForBulkUpload = false)
		{
			BaseUrl = null;
			BookOrderUrlOfRecentUpload = null;
			DeleteBookData(_bucketName, storageKeyOfBookFolder); // In case we're overwriting, get rid of any deleted files.

			//first, let's copy to temp so that we don't have to worry about changes to the original while we're uploading,
			//and at the same time introduce a wrapper with the last part of the unique key for this person+book
			string prefix = ""; // storageKey up to last slash (or empty)
			string tempFolderName = storageKeyOfBookFolder; // storage key after last slash (or all of it)

			// storageKeyOfBookFolder typically has a slash in it, email/id.
			// We only want the id as the temp folder name.
			// If there is anything before it, though, we want that as a prefix to make a parent 'folder' on parse.com.
			int index = storageKeyOfBookFolder.LastIndexOf('/');
			if (index >= 0)
			{
				prefix = storageKeyOfBookFolder.Substring(0, index + 1); // must include the slash
				tempFolderName = storageKeyOfBookFolder.Substring(index + 1);
			}

			var wrapperPath = Path.Combine(Path.GetTempPath(), tempFolderName);

			//If we previously uploaded the book, but then had a problem, this directory could still be on our harddrive. Clear it out.
			if (Directory.Exists(wrapperPath))
			{
				DeleteFileSystemInfo(new DirectoryInfo(wrapperPath));
			}

			Directory.CreateDirectory(wrapperPath);

			var destDirName = Path.Combine(wrapperPath, Path.GetFileName(pathToBloomBookDirectory));
			var filter = new BookFileFilter(pathToBloomBookDirectory) { IncludeFilesForContinuedEditing = true,
				NarrationLanguages = (includeNarrationAudio? languagesToInclude : Array.Empty<string>()),
				WantVideo = true,
				WantMusic = includeMusic
			};
			if (pdfToInclude != null)
				filter.AlwaysAccept(pdfToInclude);
			filter.AlwaysAccept(Path.GetFileNameWithoutExtension(pathToBloomBookDirectory) + BookInfo.BookOrderExtension);
			if (isForBulkUpload)
				filter.AlwaysAccept(".lastUploadInfo");
			filter.CopyBookFolderFiltered(destDirName);

			ProcessVideosInTempDirectory(destDirName);

			if (languagesToInclude != null && languagesToInclude.Count() > 0)
				RemoveUnwantedLanguageData(destDirName, languagesToInclude, metadataLang1Code, metadataLang2Code);

			PublishHelper.ReportInvalidFonts(destDirName, progress);

			UploadDirectory(prefix, wrapperPath, progress);

			DeleteFileSystemInfo(new DirectoryInfo(wrapperPath));
		}

		private readonly static string[] validSubFolders = { "audio", "video", "activities", "template" };
		/// <summary>
		/// Remove any subdirectories that are not in the our whitelist.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-7616 and
		/// https://issues.bloomlibrary.org/youtrack/issue/BL-11429.
		/// </remarks>
		private static void RemoveUnwantedSubdirectories(string directoryPath)
		{
			foreach (string subdir in Directory.GetDirectories(directoryPath))
			{
				var name = Path.GetFileName(subdir);
				if (validSubFolders.Contains(name))
					continue;
				DeleteFileSystemInfo(new DirectoryInfo(subdir));	// This does a recursive delete.
			}
		}

		private void ProcessVideosInTempDirectory(string destDirName)
		{
			var htmlFilePath = BookStorage.FindBookHtmlInFolder(destDirName);
			if (string.IsNullOrEmpty(htmlFilePath))
				return;
			var xmlDomFromHtmlFile = XmlHtmlConverter.GetXmlDomFromHtmlFile(htmlFilePath);
			var domForVideoProcessing = new HtmlDom(xmlDomFromHtmlFile);
			var videoContainerElements = HtmlDom.SelectChildVideoElements(domForVideoProcessing.RawDom.DocumentElement).Cast<XmlElement>();
			if (!videoContainerElements.Any())
				return;
			SignLanguageApi.ProcessVideos(videoContainerElements, destDirName);
			XmlHtmlConverter.SaveDOMAsHtml5(domForVideoProcessing.RawDom, htmlFilePath);
		}

		private static void RemoveUnwantedVideoFiles(string destDirName, IEnumerable<string> videoFilesToInclude)
		{
			// If videoFilesToInclude is null or empty, ALL video files in the video subdirectory will be deleted.
			// This likely means that they aren't referenced in the Book's .htm file, although someday there could be
			// other reasons to not include them.
			var videoDir = BookStorage.GetVideoFolderPath(destDirName);
			if (!Directory.Exists(videoDir))
				return;

			foreach (var videoFilePath in Directory.EnumerateFiles(videoDir))
			{
				// videoFilesToInclude strings do not include timings, but do include "video/" prefix,
				// so include the prefix in our Contains() test.
				var fileName = BookStorage.GetNormalizedPathForOS(Path.GetFileName(videoFilePath));
				if (videoFilesToInclude == null || !videoFilesToInclude.Contains(BookStorage.GetVideoFolderName + fileName))
					RobustFile.Delete(videoFilePath);
			}
		}

		private static void RemoveUnwantedAudioFiles(string destDirName, ISet<string> audioFilesToInclude)
		{
			// If audioFilesToInclude is null or empty, ALL audio files in the audio subdirectory will be deleted.
			// This could mean that none of them are referenced in the Book's .htm file, but the user can also choose
			// not to upload audio files.
			string audioDir = AudioProcessor.GetAudioFolderPath(destDirName);
			if (!Directory.Exists(audioDir))
				return;

			foreach (var audioFile in Directory.EnumerateFiles(audioDir))
			{
				var fileName = BookStorage.GetNormalizedPathForOS(Path.GetFileName(audioFile));
				if (audioFilesToInclude == null || !audioFilesToInclude.Contains(fileName))
					RobustFile.Delete(Path.Combine(audioDir, fileName));
			}
		}

		private static void DeleteFileSystemInfo(FileSystemInfo fileSystemInfo)
		{
			var directoryInfo = fileSystemInfo as DirectoryInfo;
			if (directoryInfo != null)
			{
				foreach (var childInfo in directoryInfo.GetFileSystemInfos())
				{
					DeleteFileSystemInfo(childInfo);
				}
			}

			fileSystemInfo.Attributes = FileAttributes.Normal; // thumbnails can be intentionally readonly (when they are created by hand)
			fileSystemInfo.Delete();
		}

		public void RemoveUnwantedLanguageData(string destDirName, IEnumerable<string> languagesToInclude,
			string metadataLang1Code, string metadataLang2Code)
		{
			// There should be only one html file with the same name as the directory it's in, but let's
			// not make any assumptions here.
			foreach (var filepath in Directory.EnumerateFiles(destDirName, "*.htm"))
			{
				var xmlDomFromHtmlFile = XmlHtmlConverter.GetXmlDomFromHtmlFile(filepath, false);
				var dom = new HtmlDom(xmlDomFromHtmlFile);
				PublishModel.RemoveUnwantedLanguageData(dom, languagesToInclude, false, metadataLang1Code, metadataLang2Code);
				XmlHtmlConverter.SaveDOMAsHtml5(dom.RawDom, filepath);
			}
			// Remove language specific style settings from all CSS files for unwanted languages.
			// For 5.3, we wholesale keep all L2/L3 rules even though this might result in incorrect error messages about fonts. (BL-11357)
			// In 5.4, we hope to clean up all this font determination stuff by using a real browser to determine what is used.
			PublishModel.RemoveUnwantedLanguageRulesFromCssFiles(destDirName, languagesToInclude.Append(metadataLang1Code).Append(metadataLang2Code));
			var metadata = BookMetaData.FromFolder(destDirName);
			metadata.AllTitles =
				PublishModel.RemoveUnwantedLanguageDataFromAllTitles(metadata.AllTitles, languagesToInclude.ToArray());
			metadata.WriteToFolder(destDirName);
		}

		//Note: there is a similar list for BloomPacks, but it is not identical, so don't just copy/paste
		private static readonly string[] excludedFileExtensionsLowerCase = { ".db", ".bloompack", ".bak", ".userprefs", ".md", ".map" };
		private AmazonS3Config _s3Config;
		private string _previousBucketName;
		protected string _bucketName;

		/// <summary>
		/// THe weird thing here is that S3 doesn't really have folders, but you can give it a key like "collection/book2/file3.htm"
		/// and it will name it that, and gui client apps then treat that like a folder structure, so you feel like there are folders.
		/// </summary>
		private void UploadDirectory(string prefix, string directoryPath, IProgress progress)
		{
			if (!Directory.Exists(directoryPath))
			{
				throw new DirectoryNotFoundException(
					"Source directory does not exist or could not be found: "
					+ directoryPath);
			}
			prefix = prefix + Path.GetFileName(directoryPath) + kDirectoryDelimeterForS3;

			var filesToUpload = Directory.GetFiles(directoryPath);

			// Remember the url that can be used to download files like thumbnails and preview.pdf. This seems to work but I wish
			// I could find a way to get a definitive URL from the response to UploadPart or some similar way.
			// This method gets called for the root directory (ending in guid), the main directory (ending in book name), and subdirectories.
			// We want to keep the one that ends in the book name...the main root directory.
			// This should be the first non-empty directory we are passed (the root only has a folder in it)
			if (BaseUrl == null && filesToUpload.Length > 0)
				BaseUrl = "https://s3.amazonaws.com/" + _bucketName + "/" + HttpUtility.UrlEncode(prefix);;

			using (var transferUtility = (BookUpload.IsDryRun) ? null : new TransferUtility(_amazonS3))
			{
				foreach(string file in filesToUpload)
				{
					var fileName = Path.GetFileName(file);
					if(excludedFileExtensionsLowerCase.Contains(Path.GetExtension(fileName.ToLowerInvariant())))
						continue; // BL-2246: skip uploading this one

					var request = new TransferUtilityUploadRequest()
					{
						BucketName = _bucketName,
						FilePath = file,
						Key = prefix + fileName
					};
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
					if(Path.GetExtension(file).ToLowerInvariant() != ".pdf")
						request.Headers.ContentDisposition = "attachment";
					// no-cache means "The response may be stored by any cache, even if the response is normally non-cacheable. However,
					// the stored response MUST always go through validation with the origin server first before using it..."
					request.Headers.CacheControl = "no-cache";
					request.CannedACL = S3CannedACL.PublicRead; // Allows any browser to download it.
					var uploadMsgFmt = LocalizationManager.GetString("PublishTab.Upload.UploadingStatus", "Uploading {0}");
					if (BookUpload.IsDryRun)
						uploadMsgFmt = "(Dry run) Would upload {0}";	// TODO: localize?
					progress.WriteStatus(uploadMsgFmt, fileName);
					if (progress.CancelRequested)
						return;

					try
					{
						if (!BookUpload.IsDryRun)
							transferUtility.Upload(request);
					}
					catch (Exception e)
					{
						Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
						throw;
					}
					if(fileName.EndsWith(BookInfo.BookOrderExtension))
					{
						// Remember the url that can be used to download the book. This seems to work but I wish
						// I could find a way to get a definitive URL from the response to UploadPart or some similar way.
						BookOrderUrlOfRecentUpload = BloomLinkArgs.kBloomUrlPrefix + BloomLinkArgs.kOrderFile + "=" + _bucketName + "/" +
						                             HttpUtility.UrlEncode(prefix + fileName);
					}
				}

				foreach(string subdir in Directory.GetDirectories(directoryPath))
				{
					UploadDirectory(prefix, subdir, progress);
					if (progress.CancelRequested)
						return;
				}
			}
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
					"Source directory does not exist or could not be found: "
					+ sourceDirName);
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
					if (!(ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException))
						throw;
					// Maybe we don't need to write it...it hasn't changed since a previous download?
					if (!SameFileContent(destFileName, file.FullName))
						success = false;
				}
			}

			foreach (DirectoryInfo subdir in sourceDirectory.GetDirectories())
			{
				success = CopyDirectory(subdir.FullName, Path.Combine(destDirName, subdir.Name)) && success;
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
			var request = new GetObjectRequest() {BucketName = bucketName, Key = storageKeyOfFile};
			using (var response = GetAmazonS3(bucketName).GetObject(request))
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
			string[] endsToAvoid = { "/thumbs.db", ".pdf", ".map"};
			if (endsToAvoid.Any(end => objectKey.ToLowerInvariant().EndsWith(end)))
				return true;

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
		/// Warning, if the book already exists in the location, this is going to delete it an over-write it. So it's up to the caller to check the sanity of that.
		/// </summary>
		/// <param name="storageKeyOfBookFolder"></param>
		public string DownloadBook(string bucketName, string storageKeyOfBookFolder, string pathToDestinationParentDirectory,
			IProgressDialog downloadProgress = null)
		{
			//review: should we instead save to a newly created folder so that we don't have to worry about the
			//other folder existing already? Todo: add a test for that first.

			// We need to download individual files to avoid downloading unwanted files (PDFs and thumbs.db to
			// be specific).  See https://silbloom.myjetbrains.com/youtrack/issue/BL-2312.  So we need the list
			// of items, not just the count.
			var matching = GetMatchingItems(bucketName, storageKeyOfBookFolder);
			var totalItems = CountDesiredFiles(matching);
			if(totalItems == 0)
				throw new DirectoryNotFoundException("The book we tried to download is no longer in the BloomLibrary");

			if (!storageKeyOfBookFolder.EndsWith("/"))
				storageKeyOfBookFolder += '/';
			
			Debug.Assert(matching[0].Key.StartsWith(storageKeyOfBookFolder), "Matched object does not start with storageKey");

			// Get the top-level directory name of the book from the first object key.
			var bookFolderName = matching[0].Key.Substring(storageKeyOfBookFolder.Length);
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
				if(downloadProgress != null)
					downloadProgress.Invoke((Action) (() => {
						// We cannot change ProgressRangeMaximum here because the worker thread is already active.
						// So we calculate a step value instead.  See https://issues.bloomlibrary.org/youtrack/issue/BL-5443.
						progressStep = (float)(downloadProgress.ProgressRangeMaximum - downloadProgress.Progress) / (float)totalItems;
						progress = (float)downloadProgress.Progress;
					}));
				using(var transferUtility = new TransferUtility(_amazonS3))
				{
					for(int i = 0; i < matching.Count; ++i)
					{
						var objKey = matching[i].Key;
						if(AvoidThisFile(objKey))
							continue;
						// Removing the book's prefix from the object key, then using the remainder of the key
						// in the filepath allows for nested subdirectories.
						var filepath = objKey.Substring(storageKeyOfBookFolder.Length);
						// Download this file then bump progress.
						var req = new TransferUtilityDownloadRequest()
						{
							BucketName = bucketName,
							Key = objKey,
							FilePath = Path.Combine(tempDestination.FolderPath, filepath)
						};
						transferUtility.Download(req);
						if(downloadProgress != null)
							downloadProgress.Invoke((Action) (() => {
								progress += progressStep;
								downloadProgress.Progress = (int)progress;
							}));
					}
					var destinationPath = Path.Combine(pathToDestinationParentDirectory, bookFolderName);

					//clear out anything existing on our target
					var didDelete = false;
					if(Directory.Exists(destinationPath))
					{
						try
						{
							SIL.IO.RobustIO.DeleteDirectory(destinationPath, true);
							didDelete = true;
						}
						catch(IOException)
						{
							// can't delete it...see if we can copy into it.
						}
					}

					//if we're on the same volume, we can just move it. Else copy it.
					// It's important that books appear as nearly complete as possible, because a file watcher will very soon add the new
					// book to the list of downloaded books the user can make new ones from, once it appears in the target directory.
					bool done = false;
					if(didDelete && PathHelper.AreOnSameVolume(pathToDestinationParentDirectory, tempDirectory))
					{
						try
						{
							SIL.IO.RobustIO.MoveDirectory(tempDirectory, destinationPath);
							done = true;
						}
						catch(IOException)
						{
							// If moving didn't work we'll just try copying
						}
						catch(UnauthorizedAccessException)
						{
						}
					}
					if(!done)
						done = CopyDirectory(tempDirectory, destinationPath);
					if(!done)
					{
						var msg = LocalizationManager.GetString("Download.CopyFailed",
							"Bloom downloaded the book but had problems making it available in Bloom. Please restart your computer and try again. If you get this message again, please report the problem to us.");
						// The exception doesn't add much useful information but it triggers a version of the dialog with a Details button
						// that leads to the yellow box and an easy way to send the report.
						ErrorReport.NotifyUserOfProblem(new ApplicationException("File Copy problem"), msg);
					}
					return destinationPath;
				}
			}
		}

		private string GetMinimalRandomFolderName()
		{
			if (SIL.PlatformUtilities.Platform.IsLinux)
				return "BDS_" + Guid.NewGuid();		// no path length limits on Linux
			string name = "BDS_";	// won't get used, but prevent compiler from squawking...
			for (int i = 0; i < 100; ++i)
			{
				var randomID = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("/","_").Replace("+","-").Trim(new []{'='});
				name = "BDS" + randomID.Substring(0,2);	// 2 chars of randomness = 64 ** 2 = 4096 possibilities.
				var tempPath = Path.Combine(Path.GetTempPath(), name);
				if (!File.Exists(tempPath) && !Directory.Exists(tempPath))
					return name;
				// We're finding a lot of names in use.  To improve our chances, and since this is probably garbage left behind by other
				// downloads, start trying to clean them up.
				if (i > 10)
				{
					try
					{
						if (File.Exists(tempPath))
							File.Delete(tempPath);
						else if (Directory.Exists(tempPath))
							Directory.Delete(tempPath, true);
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

		public void Dispose()
		{
			if (_amazonS3 != null)
			{
				_amazonS3.Dispose();
				_amazonS3 = null;
			}

			GC.SuppressFinalize(this);
		}

	}
}
