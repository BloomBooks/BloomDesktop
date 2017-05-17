using System.IO;
using System.Linq;
using Bloom.Book;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;

namespace BloomTests.Book
{
	class BookCompactorTests : BookTestsBase
	{
		[Test]
		public void CompactBookForDevice_FileNameIsCorrect()
		{
			var testBook = CreateBook();

			var pathToCompactedBook = BookCompactor.CompactBookForDevice(testBook);
			Assert.AreEqual(testBook.Title + BookCompactor.ExtensionForDeviceBloomBook, Path.GetFileName(pathToCompactedBook));
		}

		[Test]
		public void CompactBookForDevice_ContainsCorrectNumberOfFiles()
		{
			var testBook = CreateBook();
			var bookDirInfo = new DirectoryInfo(testBook.FolderPath);

			ZipFile zip = new ZipFile(BookCompactor.CompactBookForDevice(testBook));
			Assert.True(zip.Count > 0);
			Assert.AreEqual(bookDirInfo.EnumerateFiles().Count(), zip.Count);
		}
	}
}
