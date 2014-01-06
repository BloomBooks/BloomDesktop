using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Autofac.Features.Metadata;
using Bloom.Book;
using L10NSharp;
using Palaso.Extensions;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// Currently pushes a book's metadata to Parse.com (a mongodb service) and files to Amazon S3.
	/// We are using both because Parse offers a more structured, query-able data organization
	/// that is useful for metadata, but does not allow large enough files for some of what we need.
	/// </summary>
	public class BookTransfer
	{
		private BloomParseClient _parseClient;
		private BloomS3Client _s3Client;

		public BookTransfer(BloomParseClient bloomParseClient, BloomS3Client bloomS3Client)
		{
			this._parseClient = bloomParseClient;
			this._s3Client = bloomS3Client;
		}


		public bool LogIn(string account, string password)
		{
			return _parseClient.LogIn(account, password);
		}

		public bool LoggedIn
		{
			get { return _parseClient.LoggedIn; }
		}

		public const string BookOrderExtension = ".BloomBookOrder";

		public string UploadBook(string bookFolder, Action<String> notifier = null)
		{
			var metaDataText = MetaDataText(bookFolder);
			var metadata = BookMetaData.FromString(metaDataText);
			// In case we somehow have a book with no ID, we must have one to upload it.
			if (string.IsNullOrEmpty(metadata.Id))
			{
				metadata.Id = Guid.NewGuid().ToString();
			}
			// And similarly it should have SOME title.
			if (string.IsNullOrEmpty(metadata.Title))
			{
				metadata.Title = Path.GetFileNameWithoutExtension(bookFolder);
			}
			metadata.UploadedBy = _parseClient.Account;
			var s3BookId = S3BookId(metadata);
			metadata.DownloadSource = s3BookId;
			// Any updated ID at least needs to become a permanent part of the book.
			// The file uploaded must also contain the correct DownloadSource data, so that it can be used
			// as an 'order' to download the book.
			// It simplifies unit testing if the metadata file is also updated with the uploadedBy value.
			// Not sure if there is any other reason to do it (or not do it).
			// For example, do we want to send/receive who is the latest person to upload?
			metadata.WriteToFolder(bookFolder);
			// The metadata is also a book order...but we need it on the server with the desired file name,
			// because we can't rename on download. The extension must be the one Bloom knows about,
			// and we want the file name to indicate which book, so use the name of the book folder.
			var metadataPath = BookMetaData.MetaDataPath(bookFolder);
			var orderPath = Path.Combine(bookFolder, Path.GetFileName(bookFolder) + BookOrderExtension);
			File.Copy(metadataPath, orderPath, true);

			_s3Client.UploadBook(s3BookId, bookFolder, notifier);
			metadata.Thumbnail = _s3Client.ThumbnailUrl;
			metadata.BookOrder = _s3Client.BookOrderUrl;
			if (notifier != null)
				notifier(LocalizationManager.GetString("PublishWeb.UploadingBook","Uploading book record"));
			// Do this after uploading the books, since the ThumbnailUrl is generated in the course of the upload.
			_parseClient.SetBookRecord(metadata.Json);
			return s3BookId;
		}

		private static string MetaDataText(string bookFolder)
		{
			return File.ReadAllText(bookFolder.CombineForPath(BookInfo.MetaDataFileName));
		}

		private string S3BookId(BookMetaData metadata)
		{
			// It's tempting to use '/' so that S3 tools will treat all the books with the same ID as a folder.
			// But this complicates things because that character is taken as a path separator (even in Windows),
			// which gives us an extra level of folder in our temp folder...too much trouble for now, anyway.
			// So use a different separator.
			var s3BookId = _parseClient.Account + "/" + metadata.Id;
			return s3BookId;
		}

		/// <summary>
		/// Internal for testing because it's not yet clear this is the appropriate public routine.
		/// Probably some API gets a list of BloomInfo objects from the parse.com data, and we pass one of
		/// them as the argument for the public method.
		/// </summary>
		/// <param name="s3BookId"></param>
		/// <param name="dest"></param>
		/// <returns></returns>
		internal string DownloadBook(string s3BookId, string dest)
		{
			return _s3Client.DownloadBook(s3BookId, dest);
		}

		public void HandleBookOrder(string bookOrderPath, string projectPath)
		{
			var metadata = BookMetaData.FromString(File.ReadAllText(bookOrderPath));
			var s3BookId = metadata.DownloadSource;
			_s3Client.DownloadBook(s3BookId, Path.GetDirectoryName(projectPath));
		}

		public bool IsBookOnServer(string bookPath)
		{
			var metadata = BookMetaData.FromString(File.ReadAllText(bookPath.CombineForPath(BookInfo.MetaDataFileName)));
			return _parseClient.GetSingleBookRecord(metadata.Id, metadata.UploadedBy) != null;
		}

		// Wait (up to three seconds) for data uploaded to become available.
		// Currently only used in unit testing.
		// I have no idea whether 3s is an adequate time to wait for 'eventual consistency'. So far it seems to work.
		internal void WaitUntilS3DataIsOnServer(string bookPath)
		{
			var s3Id = S3BookId(BookMetaData.FromFolder(bookPath));
			var count = Directory.GetFiles(bookPath).Length;
			for (int i = 0; i < 30; i++)
			{
				var uploaded = _s3Client.GetBookFileCount(s3Id);
				if (uploaded >= count)
					return;
				Thread.Sleep(100);
			}
			throw new ApplicationException("S3 is very slow today");
		}
	}
}
