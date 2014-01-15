using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom.Book;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// Class for event args of BookTransfer.BookDownloaded
	/// </summary>
	public class BookDownloadedEventArgs : EventArgs
	{
		public BookInfo BookDetails { get; set; }
	}
}
