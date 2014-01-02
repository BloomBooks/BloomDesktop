using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Amazon;
using Amazon.EC2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using BloomTemp;
using L10NSharp;
using RestSharp.Contrib;

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
        public const string ProductionBucketName = "BloomLibraryBooks-Production";

        public BloomS3Client(string bucketName)
        {
            _bucketName = bucketName; 
            _amazonS3 = AWSClientFactory.CreateAmazonS3Client(KeyManager.S3AccessKey,
                KeyManager.S3SecretAccessKey, new AmazonS3Config { ServiceURL = "https://s3.amazonaws.com" });
            _transferUtility = new TransferUtility(_amazonS3);
        }

		/// <summary>
		/// This is set during UploadBook if the book has a thumbnail.png file in the book's folder, to the URL
		/// that will retrieve that file from S3.
		/// It only contains useful information after UploadBook.
		/// </summary>
		public string ThumbnailUrl { get; private set; }

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
        public void UploadBook(string storageKeyOfBookFolder, string pathToBloomBookDirectory, Action<string> notifier = null)
        {
	        ThumbnailUrl = null;
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
            Directory.CreateDirectory(wrapperPath);

            CopyDirectory(pathToBloomBookDirectory, Path.Combine(wrapperPath, Path.GetFileName(pathToBloomBookDirectory)));
			UploadDirectory(prefix, wrapperPath, notifier);

            Directory.Delete(wrapperPath, true);
        }


		/// <summary>
		/// THe weird thing here is that S3 doesn't really have folders, but you can give it a key like "collection/book2/file3.htm"
		/// and it will name it that, and gui client apps then treat that like a folder structure, so you feel like there are folders.
		/// </summary>
		private void UploadDirectory(string prefix, string directoryPath, Action<string> notifier = null)
        {
            if (!Directory.Exists(directoryPath))
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + directoryPath);
            }
            prefix = prefix + Path.GetFileName(directoryPath)+kDirectoryDelimeterForS3;

            foreach (string file in Directory.GetFiles(directoryPath))
            {
	            string fileName = Path.GetFileName(file);
				var request = new TransferUtilityUploadRequest()
                {
                    BucketName = _bucketName,
                    FilePath = file,
                    Key = prefix+ fileName,
                };
				request.CannedACL = S3CannedACL.PublicRead; // Allows any browser to download it.

	            if (notifier != null)
	            {
		            string uploading = LocalizationManager.GetString("PublishWeb.Uploading","Uploading {0}");
		            notifier(string.Format(uploading, fileName));
	            }
	            try
	            {
					_transferUtility.Upload(request);

	            }
	            catch (Exception e)
	            {
		            throw;
	            }
                //var response =_amazonS3.UploadPart(request);
	            if (fileName == "thumbnail.png")
	            {
		            // Remember the url that can be used to download the thumbnail. This seems to work but I wish
					// I could find a way to get a definitive URL from the response to UploadPart or some similar way.
		            ThumbnailUrl = "https://s3.amazonaws.com/" + _bucketName + "/" + HttpUtility.UrlEncode(prefix + fileName);
	            }
            }

            foreach (string subdir in Directory.GetDirectories(directoryPath))
            {
                UploadDirectory(prefix, subdir, notifier);
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

        /// <summary>
        /// Warning, if the book already exists in the location, this is going to delete it an over-write it. So it's up to the caller to check the sanity of that.
        /// </summary>
        /// <param name="storageKeyOfBookFolder"></param>
        public string DownloadBook(string storageKeyOfBookFolder, string pathToDestinationParentDirectory)
        {
            //TODO tell it not to download pdfs. Those are just in there for previewing purposes, we don't need to get them now that we're getting the real thing

            var matchingFilesResponse = _amazonS3.ListObjects(new ListObjectsRequest()
            {
                BucketName = _bucketName,
                Delimiter = kDirectoryDelimeterForS3,
                Prefix = storageKeyOfBookFolder
            });

            foreach (var s3Object in matchingFilesResponse.S3Objects)
            {
                _amazonS3.BeginGetObject(new GetObjectRequest() {BucketName = _bucketName, Key = s3Object.Key},
                    OnDownloadCallback, s3Object);
            }

            //review: should we instead save to a newly created folder so that we don't have to worry about the
            //other folder existing already? Todo: add a test for that first.

            if (!GetBookExists(storageKeyOfBookFolder))
                throw new DirectoryNotFoundException("The book we tried to download is no longer in the BloomLibrary");

            using (var tempDestination =
                    new TemporaryFolder("BloomDownloadStaging " + storageKeyOfBookFolder + " " + Guid.NewGuid()))
            {
                var token = _transferUtility.BeginDownloadDirectory(_bucketName, storageKeyOfBookFolder,
                    tempDestination.FolderPath, OnDownloadProgress, storageKeyOfBookFolder);

                _transferUtility.EndDownloadDirectory(token);

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

        private void OnDownloadCallback(IAsyncResult ar)
        {
            
        }


        private void OnDownloadProgress(IAsyncResult ar)
        {
            
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
