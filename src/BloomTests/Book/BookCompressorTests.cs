using System.IO;
using System.Linq;
using Bloom.Book;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using SIL.IO;
using SIL.Windows.Forms.ClearShare;

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

		[Test]
		public void CompressBookForDevice_OmitsUnwantedFiles()
		{
			var testBook = CreateBook();
			// before we add the ones we want excluded note the ones we want.
			// Enhance: not very thorough, since mocking BookStorage prevents CreateBook from creating most of the
			// interesting files.
			var expectedFiles = Directory.GetFiles(testBook.FolderPath).ToList();
			expectedFiles.Add("license.png"); // Something in the compression process adds this.
			expectedFiles.Add("thumbnail.png"); // We should NOT eliminate thumbnail.png, which we eventually want for the reader book chooser UI.

			// This unwanted file has to be real; just putting some text in it leads to out-of-memory failues when Bloom
			// tries to make its background transparent.
			File.Copy(SIL.IO.FileLocator.GetFileDistributedWithApplication(_pathToTestImages, "shirt.png"), Path.Combine(testBook.FolderPath, "thumbnail.png"));
			File.Copy(SIL.IO.FileLocator.GetFileDistributedWithApplication(_pathToTestImages, "shirt.png"), Path.Combine(testBook.FolderPath, "thumbnail-256.png"));
			File.Copy(SIL.IO.FileLocator.GetFileDistributedWithApplication(_pathToTestImages, "shirt.png"), Path.Combine(testBook.FolderPath, "thumbnail-70.png"));
			File.WriteAllText(Path.Combine(testBook.FolderPath, "book.BloomBookOrder"), @"This is unwanted");
			File.WriteAllText(Path.Combine(testBook.FolderPath, "book.pdf"), @"This is unwanted");
			File.WriteAllText(Path.Combine(testBook.FolderPath, "previewMode.css"), @"This is unwanted");
			File.WriteAllText(Path.Combine(testBook.FolderPath, "meta.json"), @"This is unwanted");

			ZipFile zip = new ZipFile(BookCompressor.CompressBookForDevice(testBook));
			Assert.AreEqual(expectedFiles.Count, zip.Count);
			foreach (var file in expectedFiles)
			{
				Assert.That(zip.FindEntry(Path.GetFileName(file), true), Is.Not.Null);
			}
		}

		// re-use the images from another test (added LakePendOreille.jpg for these tests)
		private const string _pathToTestImages = "src/BloomTests/ImageProcessing/images";

		[Test]
		public void GetBytesOfReducedImage_SmallPngImageStaysSame()
		{
			// bird.png:                   PNG image data, 274 x 300, 8-bit/color RGBA, non-interlaced

			var path = SIL.IO.FileLocator.GetFileDistributedWithApplication(_pathToTestImages, "bird.png");
			byte[] originalBytes = File.ReadAllBytes(path);
			byte[] reducedBytes = BookCompressor.GetBytesOfReducedImage(path);
			Assert.AreEqual(originalBytes, reducedBytes, "bird.png is already small enough (274x300)");
			using (var tempFile = TempFile.WithExtension(Path.GetExtension(path)))
			{
				var oldMetadata = Metadata.FromFile(path);
				RobustFile.WriteAllBytes(tempFile.Path, reducedBytes);
				var newMetadata = Metadata.FromFile(tempFile.Path);
				if (oldMetadata.IsEmpty)
				{
					Assert.IsTrue(newMetadata.IsEmpty);
				}
				else
				{
					Assert.IsFalse(newMetadata.IsEmpty);
					Assert.AreEqual(oldMetadata.CopyrightNotice, newMetadata.CopyrightNotice, "copyright preserved for bird.png");
					Assert.AreEqual(oldMetadata.Creator, newMetadata.Creator, "creator preserved for bird.png");
					Assert.AreEqual(oldMetadata.License.ToString(), newMetadata.License.ToString(), "license preserved for bird.png");
				}
			}
		}

		[Test]
		public void GetBytesOfReducedImage_SmallJpgImageStaysSame()
		{
			// man.jpg:                    JPEG image data, JFIF standard 1.01, ..., precision 8, 118x154, frames 3

			var path = SIL.IO.FileLocator.GetFileDistributedWithApplication(_pathToTestImages, "man.jpg");
			var originalBytes = File.ReadAllBytes(path);
			var reducedBytes = BookCompressor.GetBytesOfReducedImage(path);
			Assert.AreEqual(originalBytes, reducedBytes, "man.jpg is already small enough (118x154)");
			using (var tempFile = TempFile.WithExtension(Path.GetExtension(path)))
			{
				var oldMetadata = Metadata.FromFile(path);
				RobustFile.WriteAllBytes(tempFile.Path, reducedBytes);
				var newMetadata = Metadata.FromFile(tempFile.Path);
				if (oldMetadata.IsEmpty)
				{
					Assert.IsTrue(newMetadata.IsEmpty);
				}
				else
				{
					Assert.IsFalse(newMetadata.IsEmpty);
					Assert.AreEqual(oldMetadata.CopyrightNotice, newMetadata.CopyrightNotice, "copyright preserved for man.jpg");
					Assert.AreEqual(oldMetadata.Creator, newMetadata.Creator, "creator preserved for man.jpg");
					Assert.AreEqual(oldMetadata.License.ToString(), newMetadata.License.ToString(), "license preserved for man.jpg");
				}
			}
		}

		[Test]
		public void GetBytesOfReducedImage_LargePngImageReduced()
		{
			// shirtWithTransparentBg.png: PNG image data, 2208 x 2400, 8-bit/color RGBA, non-interlaced

			var path = SIL.IO.FileLocator.GetFileDistributedWithApplication(_pathToTestImages, "shirt.png");
			var originalBytes = File.ReadAllBytes(path);
			var reducedBytes = BookCompressor.GetBytesOfReducedImage(path);
			Assert.Greater(originalBytes.Length, reducedBytes.Length, "shirt.png is reduced from 2208x2400");
			using (var tempFile = TempFile.WithExtension(Path.GetExtension(path)))
			{
				var oldMetadata = Metadata.FromFile(path);
				RobustFile.WriteAllBytes(tempFile.Path, reducedBytes);
				var newMetadata = Metadata.FromFile(tempFile.Path);
				if (oldMetadata.IsEmpty)
				{
					Assert.IsTrue(newMetadata.IsEmpty);
				}
				else
				{
					Assert.IsFalse(newMetadata.IsEmpty);
					Assert.AreEqual(oldMetadata.CopyrightNotice, newMetadata.CopyrightNotice, "copyright preserved for shirt.png");
					Assert.AreEqual(oldMetadata.Creator, newMetadata.Creator, "creator preserved for shirt.png");
					Assert.AreEqual(oldMetadata.License.ToString(), newMetadata.License.ToString(), "license preserved for shirt.png");
				}
			}
		}

		[Test]
		public void GetBytesOfReducedImage_LargeJpgImageReduced()
		{
			// LakePendOreille.jpg:        JPEG image data, JFIF standard 1.01, ... precision 8, 3264x2448, frames 3

			var path = SIL.IO.FileLocator.GetFileDistributedWithApplication(_pathToTestImages, "LakePendOreille.jpg");
			var originalBytes = File.ReadAllBytes(path);
			var reducedBytes = BookCompressor.GetBytesOfReducedImage(path);
			Assert.Greater(originalBytes.Length, reducedBytes.Length, "LakePendOreille.jpg is reduced from 3264x2448");
			using (var tempFile = TempFile.WithExtension(Path.GetExtension(path)))
			{
				var oldMetadata = Metadata.FromFile(path);
				RobustFile.WriteAllBytes(tempFile.Path, reducedBytes);
				var newMetadata = Metadata.FromFile(tempFile.Path);
				if (oldMetadata.IsEmpty)
				{
					Assert.IsTrue(newMetadata.IsEmpty);
				}
				else
				{
					Assert.IsFalse(newMetadata.IsEmpty);
					Assert.AreEqual(oldMetadata.CopyrightNotice, newMetadata.CopyrightNotice, "copyright preserved for LakePendOreille.jpg");
					Assert.AreEqual(oldMetadata.Creator, newMetadata.Creator, "creator preserved for LakePendOreille.jpg");
					Assert.AreEqual(oldMetadata.License.ToString(), newMetadata.License.ToString(), "license preserved for LakePendOreille.jpg");
				}
			}
		}
	}
}
