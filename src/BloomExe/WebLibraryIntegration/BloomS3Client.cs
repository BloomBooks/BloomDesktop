﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using BloomTemp;
using L10NSharp;
using SIL.Code;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.Progress;
using RestSharp.Contrib;

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

		public BloomS3Client()
		{
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

		private IAmazonS3 GetAmazonS3(string bucketName)
		{ 
			//Note, it would probably be fine to just generate this each time,
			//but this was the more conservative approach when refactoring
			//to allow a single client to access arbitrary buckets, thus requiring
			//appropriate change of access keys, thus requiring changing AmazonS3Client objects.
			if(bucketName != _previousBucketName)
			{
				var accessKeys = AccessKeys.GetAccessKeys(bucketName);
				if (_amazonS3 != null)
					_amazonS3.Dispose();
				_amazonS3 = new AmazonS3Client(accessKeys.S3AccessKey,
					accessKeys.S3SecretAccessKey, _s3Config);

				_previousBucketName = bucketName;
			}
			return _amazonS3; // we keep this so that we can dispose of it later.
		}

		/// <summary>
		/// This is set during UploadBook to the URL holding files like various thumbnails, preview, etc.
		/// It ends up in a parse.com column named "baseUrl", and the angular appends things like "/thumbnail256.png" to it.
		/// It only contains useful information after UploadBook.
		/// </summary>
		public string BaseUrl { get; private set; }

		// Similarly for the book order file.
		public string BookOrderUrlOfRecentUpload { get; private set; }

		internal ListObjectsResponse GetMatchingItems(string bucketName, string key)
		{
			var matchingItemsResponse = GetAmazonS3(bucketName).ListObjects(new ListObjectsRequest()
			{
				BucketName = bucketName,
				Prefix = key
			});
			return matchingItemsResponse;
		}

		internal int GetBookFileCount(string key, string bucketName)
		{
			var count = GetMatchingItems(key, bucketName).S3Objects.Count;
			return count;
		}

		public void DeleteBookData(string bucketName, string key)
		{
			var matchingFilesResponse = GetAmazonS3(bucketName).ListObjects(new ListObjectsRequest()
			{
				BucketName = bucketName,
				Prefix = key
			});
			if (matchingFilesResponse.S3Objects.Count == 0)
				return;

			var deleteObjectsRequest = new DeleteObjectsRequest()
			{
				BucketName = bucketName,
				Objects = matchingFilesResponse.S3Objects.Select(s3Object => new KeyVersion() { Key = s3Object.Key }).ToList()
			};

			var response = GetAmazonS3(bucketName).DeleteObjects(deleteObjectsRequest);
			Debug.Assert(response.DeleteErrors.Count == 0);

		}

		public void EmptyUnitTestBucket(string prefix)
		{
			var matchingFilesResponse = GetAmazonS3(BloomS3Client.UnitTestBucketName).ListObjects(new ListObjectsRequest()
			{
				//NB: this one intentionally hard-codes the folder it can delete, to protect from accidents
				BucketName = UnitTestBucketName,
				Prefix = prefix,
			});
			if (matchingFilesResponse.S3Objects.Count == 0)
				return;

			var deleteObjectsRequest = new DeleteObjectsRequest()
			{
				BucketName = UnitTestBucketName,
				Objects = matchingFilesResponse.S3Objects.Select(s3Object => new KeyVersion() {Key = s3Object.Key}).ToList()
			};

			var response = GetAmazonS3(BloomS3Client.UnitTestBucketName).DeleteObjects(deleteObjectsRequest);
			Debug.Assert(response.DeleteErrors.Count == 0);
		}

		/// <summary>
		/// The thing here is that we need to guarantee unique names at the top level, so we wrap the books inside a folder
		/// with some unique name. As this involves copying the folder it is also a convenient place to omit any PDF files
		/// except the one we want.
		/// </summary>
		/// <param name="storageKeyOfBookFolder"></param>
		/// <param name="pathToBloomBookDirectory"></param>
		public void UploadBook(string storageKeyOfBookFolder, string pathToBloomBookDirectory, IProgress progress, string pdfToInclude = null)
		{
			BaseUrl = null;
			BookOrderUrlOfRecentUpload = null;
			DeleteBookData(BookTransfer.UploadBucketNameForCurrentEnvironment, storageKeyOfBookFolder); // In case we're overwriting, get rid of any deleted files.
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
			CopyDirectory(pathToBloomBookDirectory, destDirName);
			// Don't upload audio (todo: test).
			string audioDir = Path.Combine(destDirName, "audio");
			if (Directory.Exists(audioDir))
				Directory.Delete(audioDir, true);
			var unwantedPdfs = Directory.EnumerateFiles(destDirName, "*.pdf").Where(x => Path.GetFileName(x) != pdfToInclude);
			foreach (var file in unwantedPdfs)
				File.Delete(file);
			UploadDirectory(prefix, wrapperPath, progress);

			DeleteFileSystemInfo(new DirectoryInfo(wrapperPath));
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

		private readonly string[] excludedFileNames = { "thumbs.db", "book.userprefs" }; // these files (if encountered) won't be uploaded
		private AmazonS3Config _s3Config;
		private string _previousBucketName;

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
			var bucketName = BookTransfer.UploadBucketNameForCurrentEnvironment;

			// Remember the url that can be used to download files like thumbnails and preview.pdf. This seems to work but I wish
			// I could find a way to get a definitive URL from the response to UploadPart or some similar way.
			// This method gets called for the root directory (ending in guid), the main directory (ending in book name), and subdirectories.
			// We want to keep the one that ends in the book name...the main root directory.
			// This should be the first non-empty directory we are passed (the root only has a folder in it)
			if (BaseUrl == null && filesToUpload.Length > 0)
				BaseUrl = "https://s3.amazonaws.com/" + bucketName + "/" + HttpUtility.UrlEncode(prefix);;

			using(var transferUtility = new TransferUtility(_amazonS3))
			{
				foreach(string file in filesToUpload)
				{
					var fileName = Path.GetFileName(file);
					if(excludedFileNames.Contains(fileName.ToLowerInvariant()))
						continue; // BL-2246: skip uploading this one

					var request = new TransferUtilityUploadRequest()
					{
						BucketName = bucketName,
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
					request.CannedACL = S3CannedACL.PublicRead; // Allows any browser to download it.

					progress.WriteStatus(LocalizationManager.GetString("PublishTab.Upload.UploadingStatus", "Uploading {0}"),
						fileName);

					try
					{
						transferUtility.Upload(request);

					}
					catch(Exception e)
					{
						throw;
					}
					if(fileName.EndsWith(BookTransfer.BookOrderExtension))
					{
						// Remember the url that can be used to download the book. This seems to work but I wish
						// I could find a way to get a definitive URL from the response to UploadPart or some similar way.
						BookOrderUrlOfRecentUpload = BloomLinkArgs.kBloomUrlPrefix + BloomLinkArgs.kOrderFile + "=" + bucketName + "/" +
						                             HttpUtility.UrlEncode(prefix + fileName);
					}
				}

				foreach(string subdir in Directory.GetDirectories(directoryPath))
				{
					UploadDirectory(prefix, subdir, progress);
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
			if (!File.Exists(path1))
				return false;
			if (!File.Exists(path2))
				return false;
			try
			{
				var first = File.ReadAllBytes(path1);
				var second = File.ReadAllBytes(path2);
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

		private bool AvoidThisFile(string objectKey)
		{
			// Note that Amazon S3 regards "/" as the directory delimiter for directory oriented
			// displays of object keys.
			return objectKey.ToLowerInvariant().EndsWith("/thumbs.db") || objectKey.ToLowerInvariant().EndsWith(".pdf");
		}

		private int CountDesiredFiles(ListObjectsResponse matching)
		{
			int totalItems = 0;
			for (int i = 0; i < matching.S3Objects.Count; ++i)
			{
				if (AvoidThisFile(matching.S3Objects[i].Key))
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
			ProgressDialog downloadProgress = null)
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

			Debug.Assert(matching.S3Objects[0].Key.StartsWith(storageKeyOfBookFolder + "/"));

			// Get the top-level directory name of the book from the first object key.
			var bookFolderName = matching.S3Objects[0].Key.Substring(storageKeyOfBookFolder.Length + 1);
			while(bookFolderName.Contains("/"))
				bookFolderName = Path.GetDirectoryName(bookFolderName);

			// Amazon.S3 appears to truncate titles at 50 characters when building directory and filenames.  This means
			// that relative paths can be as long as 117 characters (2 * 50 + 2 for slashes + 15 for .BloomBookOrder).
			// So our temporary folder must be no more than 140 characters (allow some margin) since paths can be a
			// maximum of 260 characters in Windows.  (More margin than that may be needed because there's no guarantee
			// that image filenames are no longer than 65 characters.)  See https://jira.sil.org/browse/BL-1160.
			using(var tempDestination = new TemporaryFolder("BDS_" + Guid.NewGuid()))
			{
				var tempDirectory = Path.Combine(tempDestination.FolderPath, bookFolderName);
				if(downloadProgress != null)
					downloadProgress.Invoke((Action) (() => { downloadProgress.ProgressRangeMaximum = totalItems; }));
				int booksDownloaded = 0;
				using(var transferUtility = new TransferUtility(_amazonS3))
				{
					for(int i = 0; i < matching.S3Objects.Count; ++i)
					{
						var objKey = matching.S3Objects[i].Key;
						if(AvoidThisFile(objKey))
							continue;
						// Removing the book's prefix from the object key, then using the remainder of the key
						// in the filepath allows for nested subdirectories.
						var filepath = objKey.Substring(storageKeyOfBookFolder.Length + 1);
						// Download this file then bump progress.
						var req = new TransferUtilityDownloadRequest()
						{
							BucketName = bucketName,
							Key = objKey,
							FilePath = Path.Combine(tempDestination.FolderPath, filepath)
						};
						transferUtility.Download(req);
						++booksDownloaded;
						if(downloadProgress != null)
							downloadProgress.Invoke((Action) (() => { downloadProgress.Progress = booksDownloaded; }));
					}
					var destinationPath = Path.Combine(pathToDestinationParentDirectory, bookFolderName);

					//clear out anything existing on our target
					var didDelete = false;
					if(Directory.Exists(destinationPath))
					{
						try
						{
							Directory.Delete(destinationPath, true);
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
					if(didDelete && PathUtilities.PathsAreOnSameVolume(pathToDestinationParentDirectory, tempDirectory))
					{
						try
						{
							Directory.Move(tempDirectory, destinationPath);
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
							"Bloom downloaded the book but had problems making it available in Bloom. Please restart your computer and try again. If you get this message again, please click the 'Details' button and report the problem to the Bloom developers");
						// The exception doesn't add much useful information but it triggers a version of the dialog with a Details button
						// that leads to the yellow box and an easy way to send the report.
						ErrorReport.NotifyUserOfProblem(new ApplicationException("File Copy problem"), msg);
					}
					return destinationPath;
				}
			}
		}

		public void Dispose()
		{
//			if (_transferUtility != null)
//			{
//				_transferUtility.Dispose();
//				_transferUtility = null;
//			}
			if (_amazonS3 != null)
			{
				_amazonS3.Dispose();
				_amazonS3 = null;
			}

			GC.SuppressFinalize(this);
		}

	}
}
