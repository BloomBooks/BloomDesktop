using System.IO;
using System.Linq;
using Bloom.Book;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;

namespace BloomTests.Book
{
	class BookCompressorTests : BookTestsBase
	{
		[Test]
		public void CompressBookForDevice_FileNameIsCorrect()
		{
			var testBook = CreateBook();

			var pathToCompressedBook = BookCompressor.CompressBookForDevice(testBook);
			Assert.AreEqual(testBook.Title + BookCompressor.ExtensionForDeviceBloomBook, Path.GetFileName(pathToCompressedBook));
		}

		[Test]
		public void CompressBookForDevice_ContainsCorrectNumberOfFiles()
		{
			var testBook = CreateBook();
			var bookDirInfo = new DirectoryInfo(testBook.FolderPath);

			ZipFile zip = new ZipFile(BookCompressor.CompressBookForDevice(testBook));
			Assert.True(zip.Count > 0);
			Assert.AreEqual(bookDirInfo.EnumerateFiles().Count(), zip.Count);
		}
	}
}
