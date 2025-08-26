using System;
using System.IO;
using Bloom.Utils;
using BloomTemp;
using Moq;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

namespace BloomTests.Utils
{
    [TestFixture]
    internal class FilePathMemoryTests
    {
        Mock<Bloom.Book.Book> _mockBook1;
        Mock<Bloom.Book.Book> _mockBook2;
        Mock<Bloom.Book.Book> _mockBook3;
        string _userDocumentFolder;

        [SetUp]
        public void SetUp()
        {
            FilePathMemory.ResetFilePathMemory();
            _userDocumentFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _mockBook1 = CreateMockBook(
                "536ff975-1bd7-49ca-bcc8-937f8e4b8c9f",
                Path.Combine(_userDocumentFolder, "Bloom", "Plants")
            );
            _mockBook2 = CreateMockBook(
                "536ff975-1bd7-49ca-bcc8-937f8e4b8cA0",
                Path.Combine(_userDocumentFolder, "Bloom", "Animals")
            );
            _mockBook3 = CreateMockBook(
                "536ff975-1bd7-49ca-bcc8-937f8e4b8cA1",
                Path.Combine(_userDocumentFolder, "Bloom", "Minerals")
            );
        }

        [Test]
        public void SimplestUseCaseWorks()
        {
            // Test the code with none of the optional parameters in use.

            // The standard default folder is used for output.
            var pdfFile = FilePathMemory.GetOutputFilePath(_mockBook1.Object, ".epub");
            Assert.That(pdfFile, Is.EqualTo(Path.Combine(_userDocumentFolder, "Plants.epub")));
            pdfFile = FilePathMemory.GetOutputFilePath(_mockBook2.Object, ".epub");
            Assert.That(pdfFile, Is.EqualTo(Path.Combine(_userDocumentFolder, "Animals.epub")));
            pdfFile = FilePathMemory.GetOutputFilePath(_mockBook3.Object, ".epub");
            Assert.That(pdfFile, Is.EqualTo(Path.Combine(_userDocumentFolder, "Minerals.epub")));

            // Explicitly remember a different path for two of these books.  (These folders/files don't need to exist.)
            FilePathMemory.RememberOutputFilePath(
                _mockBook1.Object,
                ".epub",
                @"D:\output\epub\Eat Your Vegetables.epub"
            );
            FilePathMemory.RememberOutputFilePath(
                _mockBook2.Object,
                ".epub",
                @"D:\output\epub-2\Cute Furry Animals.epub"
            );

            // The explicit paths are remembered for these books.
            pdfFile = FilePathMemory.GetOutputFilePath(_mockBook1.Object, ".epub");
            Assert.That(pdfFile, Is.EqualTo(@"D:\output\epub\Eat Your Vegetables.epub"));
            pdfFile = FilePathMemory.GetOutputFilePath(_mockBook2.Object, ".epub");
            Assert.That(pdfFile, Is.EqualTo(@"D:\output\epub-2\Cute Furry Animals.epub"));

            // The most recent folder used for a remembered book is used for the next book
            // that has not been remembered.
            pdfFile = FilePathMemory.GetOutputFilePath(_mockBook3.Object, ".epub");
            Assert.That(pdfFile, Is.EqualTo(@"D:\output\epub-2\Minerals.epub"));

            // But it is not remembered for a different type of book.
            pdfFile = FilePathMemory.GetOutputFilePath(_mockBook3.Object, ".bloompub");
            Assert.That(
                pdfFile,
                Is.EqualTo(Path.Combine(_userDocumentFolder, "Minerals.bloompub"))
            );
        }

        [Test]
        public void UsingADefaultFolderWorks()
        {
            // Test the code with the optional folder parameter in use, but none of the other optional parameters being used.

            using (
                var tempFolder = new TemporaryFolder(
                    Path.Combine(Path.GetTempPath(), "DefaultFor_UsingADefaultFolderWorks")
                )
            )
            {
                // The provided default folder is used (if it exists).
                var pdfFile = FilePathMemory.GetOutputFilePath(
                    _mockBook1.Object,
                    ".pdf",
                    proposedFolder: tempFolder.FolderPath
                );
                Assert.That(pdfFile, Is.EqualTo(Path.Combine(tempFolder.FolderPath, "Plants.pdf")));
                // The provided default folder is not remembered unless RememberOutputFilePath is called.
                var pdfFile2 = FilePathMemory.GetOutputFilePath(_mockBook2.Object, ".pdf");
                Assert.That(pdfFile2, Is.EqualTo(Path.Combine(_userDocumentFolder, "Animals.pdf")));

                // Remembering the first output path saves its folder for the next book.
                FilePathMemory.RememberOutputFilePath(_mockBook1.Object, ".pdf", pdfFile);
                pdfFile2 = FilePathMemory.GetOutputFilePath(_mockBook2.Object, ".pdf");
                Assert.That(
                    pdfFile2,
                    Is.EqualTo(Path.Combine(tempFolder.FolderPath, "Animals.pdf"))
                );

                // Remember a different path for a book.  (This path isn't actually checked for existence.)
                FilePathMemory.RememberOutputFilePath(
                    _mockBook1.Object,
                    ".pdf",
                    @"D:\tmp\pdf\Plants-All.pdf"
                );

                // The explicit path for the book is remembered despite a different default folder value being provided.
                pdfFile = FilePathMemory.GetOutputFilePath(
                    _mockBook1.Object,
                    ".pdf",
                    proposedFolder: tempFolder.FolderPath
                );
                Assert.That(pdfFile, Is.EqualTo(@"D:\tmp\pdf\Plants-All.pdf"));

                // The folder from the explicit path is remembered and used for the other book as well.  Note that the
                // RememberOutputFilePath() has to be explicitly called to remember the previous setting.
                pdfFile = FilePathMemory.GetOutputFilePath(_mockBook2.Object, ".pdf");
                Assert.That(pdfFile, Is.EqualTo(@"D:\tmp\pdf\Animals.pdf"));
            }
        }

        [Test]
        public void ComplexUsesWork()
        {
            // Test the code with all three optional parameters being used: folder, name, and extra tag.

            using (
                var tempFolder = new TemporaryFolder(
                    Path.Combine(Path.GetTempPath(), "DefaultFor_ComplexUsesWork")
                )
            )
            {
                // The provided default folder is used (if it exists).
                var pdfFile = FilePathMemory.GetOutputFilePath(
                    _mockBook1.Object,
                    ".pdf",
                    "Plants-English-Pages-printshop",
                    "English-Pages-printshop",
                    tempFolder.FolderPath
                );
                Assert.That(
                    pdfFile,
                    Is.EqualTo(
                        Path.Combine(tempFolder.FolderPath, "Plants-English-Pages-printshop.pdf")
                    )
                );
                // The provided default folder is not remembered unless RememberOutputFilePath is called.
                var pdfFile2 = FilePathMemory.GetOutputFilePath(
                    _mockBook2.Object,
                    ".pdf",
                    "Animals-French-Cover",
                    "French-Cover"
                );
                Assert.That(
                    pdfFile2,
                    Is.EqualTo(Path.Combine(_userDocumentFolder, "Animals-French-Cover.pdf"))
                );

                // Remembering the first output path saves its folder for the next book.
                FilePathMemory.RememberOutputFilePath(
                    _mockBook1.Object,
                    ".pdf",
                    pdfFile,
                    "English-Pages-printshop"
                );
                pdfFile2 = FilePathMemory.GetOutputFilePath(
                    _mockBook2.Object,
                    ".pdf",
                    "Animals-Spanish-Inside",
                    "Spanish-Inside"
                );
                Assert.That(
                    pdfFile2,
                    Is.EqualTo(Path.Combine(tempFolder.FolderPath, "Animals-Spanish-Inside.pdf"))
                );

                // Remember a different path for a book.  (This path isn't actually checked for existence.)
                FilePathMemory.RememberOutputFilePath(
                    _mockBook1.Object,
                    ".pdf",
                    @"D:\tmp\pdf\Plants-All.pdf",
                    "English-Pages-printshop"
                );

                // The explicit path for the book is remembered despite different default name and folder values being provided.
                pdfFile = FilePathMemory.GetOutputFilePath(
                    _mockBook1.Object,
                    ".pdf",
                    "Plants-AllPages",
                    "English-Pages-printshop",
                    tempFolder.FolderPath
                );
                Assert.That(pdfFile, Is.EqualTo(@"D:\tmp\pdf\Plants-All.pdf"));

                // Changing the extraTag value uses the new provided name, but not the folder since the stored value overrides.
                pdfFile = FilePathMemory.GetOutputFilePath(
                    _mockBook1.Object,
                    ".pdf",
                    "Plants-English-Pages",
                    "English-Pages",
                    tempFolder.FolderPath
                );
                Assert.That(pdfFile, Is.EqualTo(@"D:\tmp\pdf\Plants-English-Pages.pdf"));
            }
        }

        /// <summary>
        /// Create a mock book with only the book's id and folder path instantiated.
        /// </summary>
        /// <remarks>
        /// Creating this method required adding a parameterless contructor for BookStorage and
        /// adding the virtual keyword to the Book.Storage, BookInfo.Id, and BookStorage.BookInfo
        /// properties.
        /// </remarks>
        Mock<Bloom.Book.Book> CreateMockBook(string bookId, string bookFolder)
        {
            var mockBook = new Mock<Bloom.Book.Book>();
            var mockBookInfo = new Mock<Bloom.Book.BookInfo>();
            var mockStorage = new Mock<Bloom.Book.BookStorage>();
            mockBook.Setup(book => book.Storage).Returns(mockStorage.Object);
            mockBook.Setup(book => book.FolderPath).Returns(bookFolder);
            mockBookInfo.Setup(info => info.Id).Returns(bookId);
            mockStorage.Setup(storage => storage.BookInfo).Returns(mockBookInfo.Object);
            return mockBook;
        }
    }
}
