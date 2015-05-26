using System;
using System.IO;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using ICSharpCode.SharpZipLib.Zip;
using Palaso.TestUtilities;

namespace BloomTests.CollectionTab
{
	class TestLibraryModel: LibraryModel
	{
		public readonly string TestFolderPath;

		public TestLibraryModel(TemporaryFolder testFolder)
			: base(testFolder.Path, new CollectionSettings(), null, new BookSelection(), new SourceCollectionsList(),
			null, null, new CreateFromSourceBookCommand(), null, null)
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

		public void RunCompressDirectoryTest(ZipOutputStream zipStream)
		{
			CompressDirectory(TestFolderPath, zipStream, GetDirNameOffset, false);
		}
	}
}
