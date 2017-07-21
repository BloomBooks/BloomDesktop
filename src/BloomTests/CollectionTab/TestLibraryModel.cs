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
		public bool ThrowCOMError { private get; set; }

		public TestLibraryModel(TemporaryFolder testFolder)
			: base(testFolder.Path, new CollectionSettings(), new BookSelection(), new SourceCollectionsList(),
			null, null, new CreateFromSourceBookCommand(), null, null, null)
		{
			TestFolderPath = testFolder.Path;
			ThrowCOMError = false;
		}

		public void RunCompressDirectoryTest(string outputPath, bool forReaderTools = false)
		{
			BookCompressor.CompressDirectory(outputPath, TestFolderPath, "", forReaderTools);
		}

		internal override void SelectFileInExplorer(string path)
		{
			if (ThrowCOMError)
			{
				throw new System.Runtime.InteropServices.COMException("Purposefully thrown by test.");
			}
			base.SelectFileInExplorer(path);
		}
	}
}
