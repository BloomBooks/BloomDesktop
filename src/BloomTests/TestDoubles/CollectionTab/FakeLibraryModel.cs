using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using SIL.TestUtilities;

using BloomTests.TestDoubles.Book;

namespace BloomTests.TestDoubles.CollectionTab
{
	class FakeLibraryModel: LibraryModel
	{
		public readonly string TestFolderPath;

		public FakeLibraryModel(TemporaryFolder testFolder)
			: base(testFolder.Path, new CollectionSettings(), new BookSelection(), GetDefaultSourceCollectionsList(),
			BookCollectionFactory, null, new CreateFromSourceBookCommand(), new FakeBookServer(), new CurrentEditableCollectionSelection(), null)
		{
			TestFolderPath = testFolder.Path;
		}

		public void RunCompressDirectoryTest(string outputPath, bool forReaderTools = false)
		{
			BookCompressor.CompressDirectory(outputPath, TestFolderPath, "", forReaderTools);
		}

		protected static SourceCollectionsList GetDefaultSourceCollectionsList()
		{
			var parentFolder = new TemporaryFolder("Parent");
			var templateCollectionFolder = new TemporaryFolder(parentFolder, "Templates");

			return new SourceCollectionsList(null, null, null, new string[] { parentFolder.Path });
		}

		public static BookCollection BookCollectionFactory(string path, BookCollection.CollectionType collectionType)
		{
			return new BookCollection(path, collectionType, null);
		}
	}
}
