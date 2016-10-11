using System.IO;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using ICSharpCode.SharpZipLib.Zip;
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

		private int GetDirNameOffset
		{
			get
			{
				var rootName = Path.GetFileName(TestFolderPath);
				return TestFolderPath.Length - rootName.Length;
			}
		}

		public void RunCompressDirectoryTest(ZipOutputStream zipStream, bool forReaderTools = false)
		{
			CompressDirectory(TestFolderPath, zipStream, GetDirNameOffset, forReaderTools);
		}
	}
}
