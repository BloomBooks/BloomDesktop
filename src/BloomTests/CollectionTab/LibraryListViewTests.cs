using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SIL.TestUtilities;

using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using BloomTests.TestDoubles.CollectionTab;


namespace BloomTests.CollectionTab
{
	[TestFixture]
	public class LibraryListViewTests
	{
		private LibraryListView _view;

		[TearDown]
		public void TestTearDown()
		{
			if (_view != null)
			{
				// Important to dispose this. Otherwise it may hang around and unexpected attempt to process during idle events.
				// The following tests were previously found to be problematic in conjunction with failing to dispose the view:
				//   CompressBookForDevice_FileNameIsCorrect, ...HandlesVideosAndModifiesSrcAttribute, ...ImgInImageContainer_ConvertedToBackground, and ...IncludesVersionFileAndStyleSheet
				_view.Dispose();
			}
		}

		[Test]
		public void LoadOneCollection_NonEditableCollection_BookNotSelected()
		{
			// Setup //
			var collectionFolder = new TemporaryFolder("LibraryListViewTests");
			var collection = new BookCollection(collectionFolder.Path, BookCollection.CollectionType.SourceCollection, new BookSelection());
			BloomTests.Book.BookCollectionTests.AddBook(collectionFolder, "book1");

			BookSelection bookSelection = new BookSelection();
			_view = new LibraryListView(new FakeLibraryModel(collectionFolder), bookSelection, new SelectedTabChangedEvent(), new LocalizationChangedEvent());

			Bloom.Properties.Settings.Default.CurrentBookPath = Path.Combine(collectionFolder.Path, "book1");

			// System Under Test //
			var obj = new Microsoft.VisualStudio.TestTools.UnitTesting.PrivateObject(_view);
			obj.Invoke("LoadOneCollection", collection, new System.Windows.Forms.FlowLayoutPanel());

			// Verification //
			Assert.IsNull(bookSelection.CurrentSelection);
		}

		[Test]
		public void LoadOneCollection_EditableCollection_BookSelected()
		{
			// Setup //
			var collectionFolder = new TemporaryFolder("LibraryListViewTests");
			var collection = new BookCollection(collectionFolder.Path, BookCollection.CollectionType.TheOneEditableCollection, new BookSelection());
			BloomTests.Book.BookCollectionTests.AddBook(collectionFolder, "book1");

			BookSelection bookSelection = new BookSelection();
			_view = new LibraryListView(new FakeLibraryModel(collectionFolder), bookSelection, new SelectedTabChangedEvent(), new LocalizationChangedEvent());

			Bloom.Properties.Settings.Default.CurrentBookPath = Path.Combine(collectionFolder.Path, "book1");
			var expectedBookPath = Bloom.Properties.Settings.Default.CurrentBookPath;

			// System Under Test //
			var obj = new Microsoft.VisualStudio.TestTools.UnitTesting.PrivateObject(_view);
			obj.Invoke("LoadOneCollection", collection, new System.Windows.Forms.FlowLayoutPanel());

			// Verification //
			Assert.IsNotNull(bookSelection?.CurrentSelection);
			Assert.AreEqual(expectedBookPath, bookSelection.CurrentSelection.FolderPath);
		}
	}

}
