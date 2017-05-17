using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using SIL.TestUtilities;

namespace BloomTests.CollectionTab
{
	class TestLibraryModel: LibraryModel
	{
		public readonly string TestFolderPath;

		public TestLibraryModel(TemporaryFolder testFolder)
			: base(testFolder.Path, new CollectionSettings(), new BookSelection(), new SourceCollectionsList(),
			null, null, new CreateFromSourceBookCommand(), null, null, null)
		{
			TestFolderPath = testFolder.Path;
		}

		public void RunCompressDirectoryTest(string outputPath, bool forReaderTools = false)
		{
			BookCompressor.CompressDirectory(outputPath, TestFolderPath, "", forReaderTools);
		}
	}
}
