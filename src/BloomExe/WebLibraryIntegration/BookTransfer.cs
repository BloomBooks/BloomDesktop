using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Bloom.Book;
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

		public string UploadBook(string bookFolder, Action<String> notifier = null)
		{
			var metaDataText = MetaDataText(bookFolder);
			// If it already exists we should update the data.
			if (notifier != null)
				notifier("Uploading book record");
			_parseClient.SetBookRecord(metaDataText);
			var s3BookId = S3BookId(metaDataText);
			_s3Client.UploadBook(s3BookId, bookFolder, notifier);
			return s3BookId;
		}

		private static string MetaDataText(string bookFolder)
		{
			return File.ReadAllText(bookFolder.CombineForPath(BookInfo.MetaDataFileName));
		}

		private static string S3BookId(string metaDataText)
		{
			var metadata = BookMetaData.FromString(metaDataText);
			// It's tempting to use '/' so that S3 tools will treat all the books with the same ID as a folder.
			// But this complicates things because that character is taken as a path separator (even in Windows),
			// which gives us an extra level of folder in our temp folder...too much trouble for now, anyway.
			// So use a different separator.
			var s3BookId = metadata.Id + "$" + metadata.UploadedBy;
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
			var s3Id = S3BookId(MetaDataText(bookPath));
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
