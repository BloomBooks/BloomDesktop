using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using NUnit.Framework;
using SIL.TestUtilities;

using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using BloomTests.TestDoubles.CollectionTab;
using Bloom.TeamCollection;


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
			_view = new LibraryListView(new FakeLibraryModel(collectionFolder), bookSelection, new SelectedTabChangedEvent(), new LocalizationChangedEvent(), new BookStatusChangeEvent(), null);

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
			_view = new LibraryListView(new FakeLibraryModel(collectionFolder), bookSelection, new SelectedTabChangedEvent(), new LocalizationChangedEvent(), new BookStatusChangeEvent(), null);

			Bloom.Properties.Settings.Default.CurrentBookPath = Path.Combine(collectionFolder.Path, "book1");
			var expectedBookPath = Bloom.Properties.Settings.Default.CurrentBookPath;

			// System Under Test //
			var obj = new Microsoft.VisualStudio.TestTools.UnitTesting.PrivateObject(_view);
			obj.Invoke("LoadOneCollection", collection, new System.Windows.Forms.FlowLayoutPanel());

			// Verification //
			Assert.IsNotNull(bookSelection?.CurrentSelection);
			Assert.AreEqual(expectedBookPath, bookSelection.CurrentSelection.FolderPath);
		}

		[Test]
		public void OnTeamCollectionBookStatusChange_TeamCollection_CheckedOutBySelf()
		{
			// Setup //
			var collectionFolder = new TemporaryFolder("LibraryListViewTests");
			Book.BookCollectionTests.AddBook(collectionFolder, "book1");

			_view = new LibraryListView(new FakeLibraryModel(collectionFolder), new BookSelection(), new SelectedTabChangedEvent(), new LocalizationChangedEvent(), new BookStatusChangeEvent(), null);

			var primaryCollectionFlow = new FlowLayoutPanel();
			var obj = new Microsoft.VisualStudio.TestTools.UnitTesting.PrivateObject(_view);
			obj.SetField("_primaryCollectionFlow", primaryCollectionFlow);
			_view.LoadPrimaryCollectionButtons();

			// System Under Test //
			_view.OnTeamCollectionBookStatusChange(new BookStatusChangeEventArgs("book1", CheckedOutBy.Self));

			// Verification //
			var button = primaryCollectionFlow.Controls.OfType<Button>().First();
			var labelOfButton = button.Controls.OfType<Label>().First();
			AssertImageCenterIsColor(labelOfButton.Image, Palette.BloomYellow);
		}

		[Test]
		public void OnTeamCollectionBookStatusChange_TeamCollection_CheckedOutByOther()
		{
			// Setup //
			var collectionFolder = new TemporaryFolder("LibraryListViewTests");
			Book.BookCollectionTests.AddBook(collectionFolder, "book1");

			_view = new LibraryListView(new FakeLibraryModel(collectionFolder), new BookSelection(), new SelectedTabChangedEvent(), new LocalizationChangedEvent(), new BookStatusChangeEvent(), null);

			var primaryCollectionFlow = new FlowLayoutPanel();
			var obj = new Microsoft.VisualStudio.TestTools.UnitTesting.PrivateObject(_view);
			obj.SetField("_primaryCollectionFlow", primaryCollectionFlow);
			_view.LoadPrimaryCollectionButtons();

			// System Under Test //
			_view.OnTeamCollectionBookStatusChange(new BookStatusChangeEventArgs("book1", CheckedOutBy.Other));

			// Verification //
			var button = primaryCollectionFlow.Controls.OfType<Button>().First();
			var labelOfButton = button.Controls.OfType<Label>().First();
			AssertImageCenterIsColor(labelOfButton.Image, Palette.BloomPurple);
		}

		[Test]
		public void OnTeamCollectionBookStatusChange_TeamCollection_GivenCheckedOutByOther_WhenCheckedOutByNone_RemovesIcon()
		{
			// Setup //
			var collectionFolder = new TemporaryFolder("LibraryListViewTests");
			Book.BookCollectionTests.AddBook(collectionFolder, "book1");

			_view = new LibraryListView(new FakeLibraryModel(collectionFolder), new BookSelection(), new SelectedTabChangedEvent(), new LocalizationChangedEvent(), new BookStatusChangeEvent(), null);

			var primaryCollectionFlow = new FlowLayoutPanel();
			var obj = new Microsoft.VisualStudio.TestTools.UnitTesting.PrivateObject(_view);
			obj.SetField("_primaryCollectionFlow", primaryCollectionFlow);
			_view.LoadPrimaryCollectionButtons();
			_view.OnTeamCollectionBookStatusChange(new BookStatusChangeEventArgs("book1", CheckedOutBy.Other));
			var button = primaryCollectionFlow.Controls.OfType<Button>().First();
			Assert.AreEqual(1, button.Controls.OfType<Label>().Count(), "Test was not set up properly. Wrong number of labels.");

			// System Under Test //
			_view.OnTeamCollectionBookStatusChange(new BookStatusChangeEventArgs("book1", CheckedOutBy.None));

			// Verification //			
			var labelOfButton = button.Controls.OfType<Label>().FirstOrDefault();
			Assert.IsNull(labelOfButton);
		}

		internal static void AssertImageCenterIsColor(Image image, Color expectedColor)
		{
			using (var bitmap = new Bitmap(image))
			{
				var centerX = image.Size.Width / 2;
				var centerY = image.Size.Height / 2;
				var centerPixel = bitmap.GetPixel(centerX, centerY);
				var resultColor = centerPixel;
				Assert.AreEqual(expectedColor.ToString(), resultColor.ToString());
			}
		}
	}

}
