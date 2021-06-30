using System;
using Bloom.Book;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// Class for event args of BookDownload.BookDownloaded
	/// </summary>
	public class BookDownloadedEventArgs : EventArgs
	{
		public BookInfo BookDetails { get; set; }
	}
}
