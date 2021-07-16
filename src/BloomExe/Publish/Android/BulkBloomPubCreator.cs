using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.web;

namespace Bloom.Publish.Android
{
	public class BulkBloomPubCreator
	{
		private readonly BookServer _bookServer;
		private readonly LibraryModel _libraryModel;

		public delegate BulkBloomPubCreator Factory(BookServer bookServer, LibraryModel collectionModel);//autofac uses this

		public BulkBloomPubCreator(BookServer bookServer, LibraryModel libraryModel)
		{
			_bookServer = bookServer;
			_libraryModel = libraryModel;
		}
		public void SaveAll(WebSocketProgress progress)
		{
			foreach (var bookInfo in _libraryModel.TheOneEditableCollection.GetBookInfos())
			{
				progress.MessageWithoutLocalizing(bookInfo.FolderPath);
			}
			progress.ShowButtons();
			progress.Finished();
		}
	}
}
