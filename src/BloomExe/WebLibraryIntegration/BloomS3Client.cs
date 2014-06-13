using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Amazon;
using Amazon.EC2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using BloomTemp;
using L10NSharp;
using Palaso.Progress;
using Palaso.UI.WindowsForms.Progress;
using RestSharp.Contrib;
using Segmentio;

namespace Bloom.WebLibraryIntegration
{
    /// <summary>
    /// Handles Bloom file/folder operations on Amazon Web Services S3 service.
    /// </summary>
    public class BloomS3Client:IDisposable
    {
        private IAmazonS3 _amazonS3;
        private TransferUtility _transferUtility;
        private readonly string _bucketName;
        public const string kDirectoryDelimeterForS3 = "/";
        public const string UnitTestBucketName = "BloomLibraryBooks-UnitTests";
        public const string SandboxBucketName = "BloomLibraryBooks-Sandbox";
        public const string ProductionBucketName = "BloomLibraryBooks";

        public BloomS3Client(string bucketName)
        {
            _bucketName = bucketName; 
            _amazonS3 = AWSClientFactory.CreateAmazonS3Client(KeyManager.S3AccessKey,
                KeyManager.S3SecretAccessKey, new AmazonS3Config { ServiceURL = "https://s3.amazonaws.com" });
            _transferUtility = new TransferUtility(_amazonS3);
        }

		/// <summary>
		/// This is set during UploadBook to the URL holding files like various thumbnails, preview, etc.
		/// It ends up in a parse.com column named "baseUrl", and the angular appends things like "/thumbnail256.png" to it.
		/// It only contains useful information after UploadBook.
		/// </summary>
		public string BaseUrl { get; private set; }

		// Similarly for the book order file.
		public string BookOrderUrl { get; private set; }

		internal string BucketName {get { return _bucketName; }}

        public bool GetBookExists(string key)
        {
	        return GetBookFileCount(key) > 0;
        }

	    internal int GetBookFileCount(string key)
	    {
		    var matchingFilesResponse = _amazonS3.ListObjects(new ListObjectsRequest()
		    {
			    BucketName = _bucketName,
			    Prefix = key
		    });
		    var count = matchingFilesResponse.S3Objects.Count;
		    return count;
	    }

	    public int GetCountOfAllFilesInBucket()
        {
            var matchingFilesResponse = _amazonS3.ListObjects(new ListObjectsRequest()
            {
                BucketName = _bucketName
            });
            return matchingFilesResponse.S3Objects.Count;
        }


        public IEnumerable<string> GetFilePaths()
        {
            var matchingFilesResponse = _amazonS3.ListObjects(new ListObjectsRequest()
            {
                BucketName = _bucketName
            });
            return from x in matchingFilesResponse.S3Objects select x.Key;
        }

	    public void DeleteBookData(string key)
	    {
			var matchingFilesResponse = _amazonS3.ListObjects(new ListObjectsRequest()
			{
				BucketName = _bucketName,
				Prefix = key
			});
			if (matchingFilesResponse.S3Objects.Count == 0)
				return;

			var deleteObjectsRequest = new DeleteObjectsRequest()
			{
				BucketName = UnitTestBucketName,
				Objects = matchingFilesResponse.S3Objects.Select(s3Object => new KeyVersion() { Key = s3Object.Key }).ToList()
			};

			var response = _amazonS3.DeleteObjects(deleteObjectsRequest);
			Debug.Assert(response.DeleteErrors.Count == 0);
		    
	    }


        public bool FileExists(params string[] parts)
        {
            var request = new ListObjectsRequest()
            {
                BucketName = _bucketName,
                Prefix = String.Join(kDirectoryDelimeterForS3,parts)
            };
            return _amazonS3.ListObjects(request).S3Objects.Count>0;
        }

        public void EmptyUnitTestBucket()
        {
            var matchingFilesResponse = _amazonS3.ListObjects(new ListObjectsRequest()
            {
                //NB: this one intentionally hard-codes the folder it can delete, to protect from accidents
                BucketName = UnitTestBucketName,
            });
            if (matchingFilesResponse.S3Objects.Count == 0)
                return;

            var deleteObjectsRequest = new DeleteObjectsRequest()
            {
                BucketName = UnitTestBucketName,
                Objects = matchingFilesResponse.S3Objects.Select(s3Object => new KeyVersion() {Key = s3Object.Key}).ToList()
            };
            
            var response = _amazonS3.DeleteObjects(deleteObjectsRequest);
            Debug.Assert(response.DeleteErrors.Count == 0);
        }

        /// <summary>
        /// The thing here is that we need to guarantee unique names at the top level, so we wrap the books inside a folder
        /// with some unique name
        /// </summary>
        /// <param name="storageKeyOfBookFolder"></param>
        /// <param name="pathToBloomBookDirectory"></param>
        public void UploadBook(string storageKeyOfBookFolder, string pathToBloomBookDirectory, IProgress progress)
        {
	        BaseUrl = null;
	        BookOrderUrl = null;
	        DeleteBookData(storageKeyOfBookFolder); // In case we're overwriting, get rid of any deleted files.
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

            CopyDirectory(pathToBloomBookDirectory, Path.Combine(wrapperPath, Path.GetFileName(pathToBloomBookDirectory)));
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

            // Remember the url that can be used to download files like thumbnails and preview.pdf. This seems to work but I wish
            // I could find a way to get a definitive URL from the response to UploadPart or some similar way.
            BaseUrl = "https://s3.amazonaws.com/" + _bucketName + "/" + HttpUtility.UrlEncode(prefix);

            foreach (string file in Directory.GetFiles(directoryPath))
            {
                string fileName = Path.GetFileName(file);
                var request = new TransferUtilityUploadRequest()
                {
                    BucketName = _bucketName,
                    FilePath = file,
                    Key = prefix + fileName
                };
                // The effect of this is that navigating to the file's URL is always treated as an attempt to download the file,
                // and the file is downloaded with the specified name (rather than a name which includes the full path from the S3 bucket root).
                // This is definitely not desirable for the PDF (typically a preview) which we want to navigate to in the Preview button
                // of BloomLibrary.
                // I'm not sure whether there is still any reason to do it for other files.
                // It was temporarily important for the BookOrder file when the Open In Bloom button just downloaded it.
                // However, now the download link uses the bloom: prefix to get the URL passed directly to Bloom,
                // it may not be needed for anything. Still, at least for the files a browser would not know how to
                // open, it seems desirable to download them with their original names, if such a thing should ever happen.
                // So I'm leaving the code in for now except in cases where we know we don't want it.
                if (Path.GetExtension(file).ToLowerInvariant() != ".pdf")
                    request.Headers.ContentDisposition = "attachment; filename='" + Path.GetFileName(file) + "'";
                request.CannedACL = S3CannedACL.PublicRead; // Allows any browser to download it.

                progress.WriteStatus(LocalizationManager.GetString("Publish.Upload.UploadingStatus", "Uploading {0}"),
                    fileName);

                try
                {
                    _transferUtility.Upload(request);

                }
                catch (Exception e)
                {
                    throw;
                }
                if (fileName.EndsWith(BookTransfer.BookOrderExtension))
                {
                    // Remember the url that can be used to download the book. This seems to work but I wish
                    // I could find a way to get a definitive URL from the response to UploadPart or some similar way.
                    BookOrderUrl = BloomLinkArgs.kBloomUrlPrefix + BloomLinkArgs.kOrderFile + "=" + _bucketName + "/" +
                                   HttpUtility.UrlEncode(prefix + fileName);
                }
            }

            foreach (string subdir in Directory.GetDirectories(directoryPath))
            {
                UploadDirectory(prefix, subdir, progress);
            }
        }

        /// <summary>
        /// copy directory and all subdirectories
        /// </summary>
        /// <param name="sourceDirName"></param>
        /// <param name="destDirName">Note, this is not the *parent*; this is the actual name you want, e.g. CopyDirectory("c:/foo", "c:/temp/foo") </param>
        private static void CopyDirectory(string sourceDirName, string destDirName)
        {
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
                file.CopyTo(Path.Combine(destDirName, file.Name), true);
            }

            foreach (DirectoryInfo subdir in sourceDirectory.GetDirectories())
            {
                CopyDirectory(subdir.FullName, Path.Combine(destDirName, subdir.Name));
            }
        }

	    public string DownloadFile(string storageKeyOfFile)
	    {
		    var request = new GetObjectRequest() {BucketName = _bucketName, Key = storageKeyOfFile};
			using (var response = _amazonS3.GetObject(request))
			using (var stream = response.ResponseStream)
			using (var reader = new StreamReader(stream, Encoding.UTF8))
			{
				return reader.ReadToEnd();
			}
	    }

        /// <summary>
        /// Warning, if the book already exists in the location, this is going to delete it an over-write it. So it's up to the caller to check the sanity of that.
        /// </summary>
        /// <param name="storageKeyOfBookFolder"></param>
		public string DownloadBook(string storageKeyOfBookFolder, string pathToDestinationParentDirectory, ProgressDialog downloadProgress = null)
        {
            //TODO tell it not to download pdfs. Those are just in there for previewing purposes, we don't need to get them now that we're getting the real thing

            //review: should we instead save to a newly created folder so that we don't have to worry about the
            //other folder existing already? Todo: add a test for that first.

            if (!GetBookExists(storageKeyOfBookFolder))
                throw new DirectoryNotFoundException("The book we tried to download is no longer in the BloomLibrary");

            using (var tempDestination =
                    new TemporaryFolder("BloomDownloadStaging " + storageKeyOfBookFolder + " " + Guid.NewGuid()))
            {
	            var request = new TransferUtilityDownloadDirectoryRequest()
	            {
		            BucketName = _bucketName,
		            S3Directory = storageKeyOfBookFolder,
		            LocalDirectory = tempDestination.FolderPath
	            };
				int downloaded = 0;
	            int initialProgress = 0;
				if (downloadProgress != null)
				{
					downloadProgress.Invoke((Action)(() =>
					{
						downloadProgress.Progress++; // count getting set up as one step.
						initialProgress = downloadProgress.Progress; // might be one more step done, downloading order
					}));
				}
				int total = 14; // arbitrary (typical minimum files in project)
				request.DownloadedDirectoryProgressEvent += delegate(object sender, DownloadDirectoryProgressArgs args)
				{
					int progressMax = initialProgress + args.TotalNumberOfFiles;
					int currentProgress = initialProgress + args.NumberOfFilesDownloaded;
					if (downloadProgress != null && (progressMax != total || currentProgress != downloaded))
					{
						total = progressMax;
						downloaded = currentProgress;
						// We only want to invoke if something really changed.
						downloadProgress.Invoke((Action)(() =>
						{
							downloadProgress.ProgressRangeMaximum = progressMax; // probably only changes the first time
							downloadProgress.Progress = currentProgress;
						}));
					}
				};
				_transferUtility.DownloadDirectory(request);

                //look inside the wrapper that we got

                var children = Directory.GetDirectories(tempDestination.FolderPath);
                if (children.Length != 1)
                {
                    throw new ApplicationException(
                        string.Format("Bloom expected to find a single directory in {0}, but instead there were {1}",
                            tempDestination.FolderPath, children.Length));
                }
                var destinationPath = Path.Combine(pathToDestinationParentDirectory, Path.GetFileName(children[0]));

                //clear out anything exisitng on our target
                if (Directory.Exists(destinationPath))
                {
                    Directory.Delete(destinationPath, true);
                }

                //if we're on the same volume, we can just move it. Else copy it.
                if (Directory.GetDirectoryRoot(pathToDestinationParentDirectory) == Directory.GetDirectoryRoot(tempDestination.FolderPath))
                {
                    Directory.Move(children[0], destinationPath);
                }
                else
                {
                    CopyDirectory(children[0], destinationPath);
                }
	            return destinationPath;
            }
        }

        public void Dispose()
        {
            if (_transferUtility != null)
            {
                _transferUtility.Dispose();
                _transferUtility = null;
            }
            if (_amazonS3 != null)
            {
                _amazonS3.Dispose();
                _amazonS3 = null;
            }
        }

    }
}
