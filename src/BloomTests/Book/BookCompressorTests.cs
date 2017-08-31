using System;
using System.IO;
using System.Linq;
using Bloom.Api;
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
			var testBook = CreateBook(bringBookUpToDate: true);

			using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(testBook.Title + BookCompressor.ExtensionForDeviceBloomBook))
			{
				BookCompressor.CompressBookForDevice(bloomdTempFile.Path, testBook);
				Assert.AreEqual(testBook.Title + BookCompressor.ExtensionForDeviceBloomBook,
					Path.GetFileName(bloomdTempFile.Path));
			}
		}

		[Test]
		public void CompressBookForDevice_ContainsCorrectNumberOfFiles()
		{
			var testBook = CreateBook(bringBookUpToDate: true);
			var bookDirInfo = new DirectoryInfo(testBook.FolderPath);

			using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(testBook.Title + BookCompressor.ExtensionForDeviceBloomBook))
			{
				BookCompressor.CompressBookForDevice(bloomdTempFile.Path, testBook);
				ZipFile zip = new ZipFile(bloomdTempFile.Path);
				Assert.True(zip.Count > 0);
				Assert.AreEqual(bookDirInfo.EnumerateFiles().Count(), zip.Count);
			}
		}

		[Test]
		public void CompressBookForDevice_OmitsUnwantedFiles()
		{
			var testBook = CreateBook(bringBookUpToDate: true);
			// before we add the ones we want excluded note the ones we want.
			// Enhance: not very thorough, since mocking BookStorage prevents CreateBook from creating most of the
			// interesting files.
			var expectedFiles = Directory.GetFiles(testBook.FolderPath).ToList();
			expectedFiles.Add("thumbnail.png"); // We should NOT eliminate thumbnail.png, which we eventually want for the reader book chooser UI.
			expectedFiles.Add("previewMode.css"); // Somewhat sadly, we need this, e.g., for rules that hide the page labels.

			// This unwanted file has to be real; just putting some text in it leads to out-of-memory failures when Bloom
			// tries to make its background transparent.
			File.Copy(SIL.IO.FileLocator.GetFileDistributedWithApplication(_pathToTestImages, "shirt.png"), Path.Combine(testBook.FolderPath, "thumbnail.png"));
			File.Copy(SIL.IO.FileLocator.GetFileDistributedWithApplication(_pathToTestImages, "shirt.png"), Path.Combine(testBook.FolderPath, "thumbnail-256.png"));
			File.Copy(SIL.IO.FileLocator.GetFileDistributedWithApplication(_pathToTestImages, "shirt.png"), Path.Combine(testBook.FolderPath, "thumbnail-70.png"));
			File.WriteAllText(Path.Combine(testBook.FolderPath, "book.BloomBookOrder"), @"This is unwanted");
			File.WriteAllText(Path.Combine(testBook.FolderPath, "book.pdf"), @"This is unwanted");
			File.WriteAllText(Path.Combine(testBook.FolderPath, "previewMode.css"), @"This is wanted");
			File.WriteAllText(Path.Combine(testBook.FolderPath, "meta.json"), @"This is unwanted");

			using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(testBook.Title + BookCompressor.ExtensionForDeviceBloomBook))
			{
				BookCompressor.CompressBookForDevice(bloomdTempFile.Path, testBook);
				ZipFile zip = new ZipFile(bloomdTempFile.Path);
				Assert.AreEqual(expectedFiles.Count, zip.Count);
				foreach (var file in expectedFiles)
				{
					Assert.That(zip.FindEntry(Path.GetFileName(file), true), Is.Not.Null);
				}
			}
		}

		// Also verifies that images that DO exist are NOT removed (even if src attr includes params like ?optional=true)
		// Since this is one of the few tests that makes a real HTML file we use it also to check
		// the the HTML file is at the root of the zip.
		[Test]
		public void CompressBookForDevice_RemovesImgElementsWithMissingSrc_AndContentEditable()
		{
			var testBook = CreateBook(bringBookUpToDate: true);
			// This requires a real book file (which a mocked book usually doesn't have).
			var imgsToRemove =
				"<img class='branding branding-wide' src='back-cover-outside-wide.svg' type='image/svg' onerror='this.style.display='none''></img><img src = 'nonsence.svg'/><img src=\"rubbish\"> </img  >";
			var positiveCe = " contenteditable=\"true\"";
			var negativeCe = " contenteditable='false'";
			var htmlTemplate = @"<!DOCTYPE html>
<html>
<body>
    <div class='bloom-page cover coverColor outsideBackCover bloom-backMatter A5Portrait' data-page='required singleton' data-export='back-matter-back-cover' id='b1b3129a-7675-44c4-bc1e-8265bd1dfb08'>
        <div class='pageLabel' lang='en'>
            Outside Back Cover
        </div>
        <div class='pageDescription' lang='en'></div>

        <div class='marginBox'>
        <div class='bloom-translationGroup' data-default-languages='N1'>
            <div class='bloom-editable Outside-Back-Cover-style bloom-copyFromOtherLanguageIfNecessary bloom-contentNational1 bloom-visibility-code-on' lang='fr'{1} data-book='outsideBackCover'>
                <label class='bubble'>If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover.</label>
            </div>

            <div class='bloom-editable Outside-Back-Cover-style bloom-copyFromOtherLanguageIfNecessary bloom-contentNational2' lang='de'{1} data-book='outsideBackCover'></div>

            <div class='bloom-editable Outside-Back-Cover-style bloom-copyFromOtherLanguageIfNecessary bloom-content1' lang='ksf'{1} data-book='outsideBackCover'></div>
        </div{2}>{0} <img class='branding' src='back-cover-outside.svg?optional=true' type='image/svg' onerror='this.style.display='none''></img></div>
    </div>
</body>
</html>";
			var htmlOriginal = string.Format(htmlTemplate, imgsToRemove, positiveCe, negativeCe);
			var bookFileName = Path.GetFileName(testBook.FolderPath) + ".htm";
			var bookPath = Path.Combine(testBook.FolderPath, bookFileName);
			File.WriteAllText(bookPath, htmlOriginal);
			// Simulate the typical situation where we have the regular but not the wide svg
			File.WriteAllText(Path.Combine(testBook.FolderPath, "back-cover-outside.svg"), @"this is a fake for testing");

			using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(testBook.Title + BookCompressor.ExtensionForDeviceBloomBook))
			{
				BookCompressor.CompressBookForDevice(bloomdTempFile.Path, testBook);
				ZipFile zip = new ZipFile(bloomdTempFile.Path);

				var newHtml = GetEntryContents(zip, bookFileName, true);
				var assertThatNewHtml = AssertThatXmlIn.String(newHtml);
				assertThatNewHtml.HasSpecifiedNumberOfMatchesForXpath("//img",1); // only back-cover-outside.svg survives
				assertThatNewHtml.HasSpecifiedNumberOfMatchesForXpath("//img[@src='back-cover-outside.svg?optional=true']", 1); // only back-cover-outside.svg survives
				assertThatNewHtml.HasNoMatchForXpath("//div[@contenteditable]");
			}
		}

		[Test]
		public void CompressBookForDevice_ImgInImgContainer_ConvertedToBackground()
		{
			var testBook = CreateBook(bringBookUpToDate: true);
			// This requires a real book file (which a mocked book usually doesn't have).
			var oldImg =
				"<div style=\"\" class=\"bloom-imageContainer bloom-leadingElement\">"
				+
				"<img data-creator=\"Anis Ka'abu\" data-license=\"cc-by\" data-copyright='1996 \"SIL\" PNG' style=\"width: 408px; height: 261px; margin-left: 0px; margin-top: 18px;\" alt=\"This picture, HL0014-1.svg, is missing or was loading too slowly.\" src=\"HL00'14 1.svg\" height=\"261\" width=\"408\"></img>"
				+ "</div>";
			// Above should be replaced with something like this: "<div class=\"bloom-imageContainer bloom-leadingElement bloom-backgroundImage\" style=\"background-image:url('HL00%2714%201.svg')\" data-creator=\"Anis Ka'abu\" data-license=\"cc-by\" data-copyright='1996 \"SIL\" PNG'></div>";
			// {0} is in twice as a crude way of checking that it can replace more than one image.
			var htmlTemplate = @"<!DOCTYPE html>
<html>
<body>
    <div class='bloom-page cover coverColor outsideBackCover bloom-backMatter A5Portrait' data-page='required singleton' data-export='back-matter-back-cover' id='b1b3129a-7675-44c4-bc1e-8265bd1dfb08'>
        <div class='pageDescription' lang='en'></div>

        <div class='marginBox'>
    {0}
    {0}
    </div>
</body>
</html>";
			var htmlOriginal = string.Format(htmlTemplate, oldImg);
			var bookFileName = Path.GetFileName(testBook.FolderPath) + ".htm";
			var bookPath = Path.Combine(testBook.FolderPath, bookFileName);
			File.WriteAllText(bookPath, htmlOriginal);
			// The image file has to exist or we will delete instead of converting. Use svg because compression will
			// actually try to draw, and fake pngs and jpgs cause exceptions.
			File.WriteAllText(Path.Combine(testBook.FolderPath, "HL00'14 1.svg"), @"this is a fake for testing");

			using (var bloomdTempFile =
				TempFile.WithFilenameInTempFolder(testBook.Title + BookCompressor.ExtensionForDeviceBloomBook))
			{
				BookCompressor.CompressBookForDevice(bloomdTempFile.Path, testBook);
				ZipFile zip = new ZipFile(bloomdTempFile.Path);
				// Technically this is too strong. We'd be happy with any equivalent HTML file, e.g., whitespace could
				// have changed. But this is the easiest to test and works with the current implementation.
				var newHtml = GetEntryContents(zip, bookFileName, true);
				var assertThatNewHtml = AssertThatXmlIn.String(newHtml);
				assertThatNewHtml.HasNoMatchForXpath("//img"); // should be merged into parent
				assertThatNewHtml.HasSpecifiedNumberOfMatchesForXpath("//div[@data-creator=\"Anis Ka'abu\" and @data-license='cc-by' and @data-copyright='1996 \"SIL\" PNG' and @class='bloom-imageContainer bloom-leadingElement bloom-backgroundImage' and @style=\"background-image:url('HL00%2714%201.svg')\"]", 2);
			}
		}

		[Test]
		public void CompressBookForDevice_IncludesVersionFile_AndAddsStylesheet()
		{
			var testBook = CreateBook(bringBookUpToDate: true);
			// This requires a real book file (which a mocked book usually doesn't have).
			// It's also important that the book contains something like contenteditable that will be removed when
			// sending the book. The sha is based on the actual file contents of the book, not the
			// content actually embedded in the bloomd.
			var html = @"<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'></meta>
    <link rel='stylesheet' href='../settingsCollectionStyles.css' type='text/css'></link>
    <link rel='stylesheet' href='../customCollectionStyles.css' type='text/css'></link>
</head>
<body>
    <div class='bloom-page cover coverColor outsideBackCover bloom-backMatter A5Portrait' data-page='required singleton' data-export='back-matter-back-cover' id='b1b3129a-7675-44c4-bc1e-8265bd1dfb08'>
		<div  contenteditable='true'>something</div>
    </div>
</body>
</html>";
			var bookFileName = Path.GetFileName(testBook.FolderPath) + ".htm";
			var bookPath = Path.Combine(testBook.FolderPath, bookFileName);
			File.WriteAllText(bookPath, html);

			using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(testBook.Title + BookCompressor.ExtensionForDeviceBloomBook))
			{
				BookCompressor.CompressBookForDevice(bloomdTempFile.Path, testBook);
				ZipFile zip = new ZipFile(bloomdTempFile.Path);
				var newHtml = GetEntryContents(zip, bookFileName);
				AssertThatXmlIn.String(newHtml).HasSpecifiedNumberOfMatchesForXpath("//html/head/link[@rel='stylesheet' and @href='readerStyles.css' and @type='text/css']", 1);
				Assert.That(GetEntryContents(zip, "version.txt"), Is.EqualTo(Bloom.Book.Book.MakeVersionCode(html)));
				GetEntryContents(zip,"readerStyles.css");
			}
		}

		private string GetEntryContents(ZipFile zip, string name, bool exact = false)
		{
			var buffer = new byte[4096];

			Func<ZipEntry, bool> predicate;
			if (exact)
				predicate = n => n.Name.Equals(name);
			else
				predicate = n => n.Name.EndsWith(name);

			var ze = (from ZipEntry entry in zip select entry).FirstOrDefault(predicate);
			Assert.That(ze, Is.Not.Null);

			using (var instream = zip.GetInputStream(ze))
			using (var writer = new MemoryStream())
			{
				ICSharpCode.SharpZipLib.Core.StreamUtils.Copy(instream, writer, buffer);
				writer.Position = 0;
				using (var reader = new StreamReader(writer))
				{
					return reader.ReadToEnd();
				}
			}
		}

		// re-use the images from another test (added LakePendOreille.jpg for these tests)
		private const string _pathToTestImages = "src/BloomTests/ImageProcessing/images";

		[Test]
		public void GetBytesOfReducedImage_SmallPngImageMadeTransparent()
		{
			// bird.png:                   PNG image data, 274 x 300, 8-bit/color RGBA, non-interlaced

			var path = SIL.IO.FileLocator.GetFileDistributedWithApplication(_pathToTestImages, "bird.png");
			byte[] originalBytes = File.ReadAllBytes(path);
			byte[] reducedBytes = BookCompressor.GetBytesOfReducedImage(path);
			Assert.That(reducedBytes, Is.Not.EqualTo(originalBytes)); // no easy way to check it was made transparent, but should be changed.
			// Size should not change much.
			Assert.That(reducedBytes.Length, Is.LessThan(originalBytes.Length * 11/10));
			Assert.That(reducedBytes.Length, Is.GreaterThan(originalBytes.Length * 9 / 10));
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
