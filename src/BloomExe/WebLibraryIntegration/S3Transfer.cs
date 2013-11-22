using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace Bloom.WebLibraryIntegration
{
	public class S3Transfer:IDisposable
	{
		private const string _bucketName = "BloomLibraryBooksSandbox";
		private IAmazonS3 _client;
		private TransferUtility _transferUtility;

		public S3Transfer()
		{
			_client = AWSClientFactory.CreateAmazonS3Client("AKIAJEKSYRFYYQQFJ6VQ",
				"8sMcTTUkA2GlqeJDDD9QZWRmYMmjVxrAckocnB5r", new AmazonS3Config { ServiceURL = "https://s3.amazonaws.com" });
			_transferUtility = new TransferUtility(_client);
		}

		/// <summary>
		/// The thing here is that we need to guarantee unique names at the top level, so we wrap the books inside a folder
		/// with some unique name
		/// </summary>
		/// <param name="storageKeyOfBookFolder"></param>
		/// <param name="pathToBloomBookDirectory"></param>
		public void UploadBook(string storageKeyOfBookFolder, string pathToBloomBookDirectory)
		{
			//first, let's copy to temp so that we don't have to worry about changes to the original while we're uploading,
			//and at the same time introduce a wrapper with the unique key for this person+book

			var wrapperPath = Path.Combine(Path.GetTempPath(), storageKeyOfBookFolder);
			Directory.CreateDirectory(wrapperPath);

			CopyDirectory(pathToBloomBookDirectory, Path.Combine(wrapperPath, Path.GetFileName(pathToBloomBookDirectory)));
			UploadDirectory(wrapperPath);

			Directory.Delete(wrapperPath, true);
		}


		/// <summary>
		/// THe weird thing here is that S3 doesn't really have folders, but you can give it a key like "collection/book2/file3.htm"
		/// and it will name it that, and gui client apps then treat that like a folder structure, so you feel like there are folders.
		/// </summary>
		private void UploadDirectory(string directoryPath)
		{
			UploadDirectory("", directoryPath);
		}

		private void UploadDirectory(string prefix, string directoryPath)
		{
			if (!Directory.Exists(directoryPath))
			{
				throw new DirectoryNotFoundException(
					"Source directory does not exist or could not be found: "
					+ directoryPath);
			}
			prefix = prefix + Path.GetFileName(directoryPath)+"/";

			foreach (string file in Directory.GetFiles(directoryPath))
			{
				var request = new UploadPartRequest()
				{
					BucketName = _bucketName,
					FilePath = file,
					IsLastPart = true,
					Key = prefix+ Path.GetFileName(file)
				};

				_client.UploadPart(request);
			}

			foreach (string subdir in Directory.GetDirectories(directoryPath))
			{
				UploadDirectory(prefix, subdir);
			}
		}

		/// <summary>
		/// copy directory and all subdirectories
		/// </summary>
		/// <param name="sourceDirName"></param>
		/// <param name="destDirName">NOte, this is not the *parent*; this is the actual name you want, e.g. CopyDirectory("c:/foo", "c:/temp/foo") </param>
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
		public void DownloadBook(string storageKeyOfBookFolder, string pathToDestinationParentDirectory)
		{
			//TODO tell it not to download pdfs. Those are just in there for previewing purposes, we don't need to get them now that we're getting the real thing

			var token = _transferUtility.BeginDownloadDirectory(_bucketName, storageKeyOfBookFolder, Path.GetTempPath(),
				OnProgress, storageKeyOfBookFolder);

			_transferUtility.EndDownloadDirectory(token);

			//look inside the wrapper that we got

			var downloadedDirPath = Path.Combine(Path.GetTempPath(), storageKeyOfBookFolder);
			var children = Directory.GetDirectories(downloadedDirPath);
			if (children.Length != 1)
			{
				throw new ApplicationException(string.Format("Bloom expected to find a single directory in {0}, but insted there were {1}", downloadedDirPath, children.Length));
			}
			var destinationPath = Path.Combine(pathToDestinationParentDirectory, Path.GetFileName(children[0]));

			//clear out anything exisitng on our target
			if (Directory.Exists(destinationPath))
			{
				Directory.Delete(destinationPath,true);
			}

			//if we're on the same volumne, we can just move it. Else copy it.
			if (Directory.GetDirectoryRoot(pathToDestinationParentDirectory) ==
				Directory.GetDirectoryRoot(downloadedDirPath))
			{
				Directory.Move(children[0], destinationPath);
			}
			else
			{
				CopyDirectory(children[0],destinationPath);
			}

			Directory.Delete(downloadedDirPath);
		}


		private void OnProgress(IAsyncResult ar)
		{

		}

		public void Dispose()
		{
			if (_transferUtility != null)
			{
				_transferUtility.Dispose();
				_transferUtility = null;
			}
			if (_client != null)
			{
				_client.Dispose();
				_client = null;
			}
		}
	}
}
