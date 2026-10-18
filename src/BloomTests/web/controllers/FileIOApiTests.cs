using System;
using System.IO;
using Bloom.Api;
using Bloom.Book;
using Bloom.web.controllers;
using Moq;
using NUnit.Framework;
using SIL.TestUtilities;

namespace BloomTests.web.controllers
{
    [TestFixture]
    class FileIOApiTests
    {
        private BloomServer _server;
        private FileIOApi _controller;
        private TemporaryFolder _testFolder;

        // The folder of the book that the server's current book reports, set by a test.
        private string _bookFolderPath;

        private string GetTestFolderName() => "FileIOApiTests";

        [OneTimeSetUp]
        public void InitialSetup()
        {
            var bookSelection = new BookSelection();
            var book = new Mock<Bloom.Book.Book>();
            book.Setup(b => b.FolderPath).Returns(() => _bookFolderPath);
            bookSelection.SelectBook(book.Object);
            _server = new BloomServer(bookSelection);

            _controller = new FileIOApi(bookSelection, new BloomWebSocketServer());
            _controller.RegisterWithApiHandler(_server.ApiHandler);
        }

        [OneTimeTearDown]
        public void FinalTearDown()
        {
            // This fixture's tests go through ApiTest, which makes the server listen, but nothing
            // ever released it -- so it held one of EnsureListening's twenty ports for the whole
            // rest of the run. Pre-existing, and harmless until the port budget started to matter;
            // Devin spotted it while reviewing that budget. Retired rather than disposed, like
            // every other fixture that listens (BL-16667).
            RetiredTestServers.Retire(_server);
            _server = null;
        }

        [SetUp]
        public void Setup()
        {
            _testFolder = new TemporaryFolder(GetTestFolderName());
        }

        [TearDown]
        public void TearDown()
        {
            if (_testFolder != null)
            {
                _testFolder.Dispose();
                _testFolder = null;
            }
        }

        private (string sourcePath, string destDirPath) SetupCopyFileTests(
            string sourceFolderName,
            string sourceFileName,
            string collectionName,
            string bookTitle
        )
        {
            string sourcePath = SetupFakeSourceFile(sourceFolderName, sourceFileName);

            TemporaryFolder collectionFolder = new TemporaryFolder(_testFolder, collectionName);
            var bookFolder = new TemporaryFolder(collectionFolder, bookTitle);

            return (sourcePath, bookFolder.Path);
        }

        /// <summary>
        /// Generate a fake file in a temporary folder and returns the path to it.
        /// </summary>
        private string SetupFakeSourceFile(string sourceFolderName, string sourceFileName)
        {
            var sourceFolder = new TemporaryFolder(_testFolder, sourceFolderName);
            var sourceFullPath = Path.Combine(sourceFolder.Path, sourceFileName);
            File.WriteAllText(sourceFullPath, "fakeSourceFileContents");
            return sourceFullPath;
        }

        [Test]
        public void CopyFile_SimpleInput_CompletesSuccessfully()
        {
            string baseName = "Hello World";
            RunCopyFileCompletesSuccessfullyTest(baseName);
        }

        [Test]
        // Also refer to BloomBrowserUI test "importRecording() encodes special characters"
        public void CopyFile_InputWithSpecialChars_CompletesSuccessfully()
        {
            string baseName = _kCopyFileSpecialCharBaseName;
            RunCopyFileCompletesSuccessfullyTest(baseName);
        }

        public void RunCopyFileCompletesSuccessfullyTest(string baseName)
        {
            // Setup
            string sourceFolderName = $"{baseName} Folder";
            string sourceFileName = $"{baseName} audio.mp3";
            string collectionName = $"{baseName} Collection";
            string bookTitle = $"{baseName} Book";

            (string sourceFilePath, string destDirPath) = SetupCopyFileTests(
                sourceFolderName,
                sourceFileName,
                collectionName,
                bookTitle
            );
            string encodedSourceFilePath = EncodeFilenameForHttpRequest(sourceFilePath);

            string destFilePath = Path.Combine(
                destDirPath,
                "fa22704c-6f27-4c1f-9f1b-7eb62baf2fde.mp3"
            );
            string encodedDestFilePath = EncodeFilenameForHttpRequest(destFilePath);

            string jsonData =
                $"{{ \"from\": \"{encodedSourceFilePath}\", \"to\": \"{encodedDestFilePath}\" }}";

            // System Under Test //

            // Debugging tip: If debugging with breakpoints, set the timeout to some high value
            int? timeoutInMilliseconds = null;
            var result = ApiTest.PostString(
                _server,
                "fileIO/copyFile",
                jsonData,
                ApiTest.ContentType.JSON,
                timeoutInMilliseconds: timeoutInMilliseconds
            );

            // Verification
            Assert.That(result, Is.EqualTo("OK"), "Request should reply \"OK\" if successful");
            Assert.That(
                File.ReadAllText(destFilePath),
                Is.EqualTo("fakeSourceFileContents"),
                "Contents of copied file were not as expected."
            );
        }

        /// <summary>
        /// A folder of built-in sounds holding one sound, and an empty book folder, which the
        /// server's current book reports.
        /// </summary>
        private (string soundsFolder, string bookFolder) SetupBuiltInSoundTests(
            string soundFileName
        )
        {
            var soundsFolder = new TemporaryFolder(_testFolder, "sounds").Path;
            File.WriteAllText(Path.Combine(soundsFolder, soundFileName), "builtInSound");
            _bookFolderPath = new TemporaryFolder(_testFolder, "Book").Path;
            return (soundsFolder, _bookFolderPath);
        }

        [Test]
        public void CopyBuiltInSound_CopiesTheSoundIntoTheBooksAudioFolder()
        {
            var (soundsFolder, bookFolder) = SetupBuiltInSoundTests("correct_yay.webm");
            _controller.BuiltInSoundsFolder = soundsFolder;
            var audioFolder = Path.Combine(bookFolder, "audio");
            Assert.That(Directory.Exists(audioFolder), Is.False, "test setup");

            var result = ApiTest.PostString(
                _server,
                "fileIO/copyBuiltInSound",
                "correct_yay.webm",
                ApiTest.ContentType.Text
            );

            Assert.That(result, Is.EqualTo("OK"));
            Assert.That(
                File.ReadAllText(Path.Combine(audioFolder, "correct_yay.webm")),
                Is.EqualTo("builtInSound")
            );
        }

        [Test]
        public void CopyBuiltInSoundToBook_LeavesAFileOfTheSameNameInTheBookAsItIs()
        {
            var (soundsFolder, bookFolder) = SetupBuiltInSoundTests("correct_yay.webm");
            var audioFolder = Directory.CreateDirectory(Path.Combine(bookFolder, "audio")).FullName;
            var existing = Path.Combine(audioFolder, "correct_yay.webm");
            File.WriteAllText(existing, "theUsersOwnSound");

            FileIOApi.CopyBuiltInSoundToBook("correct_yay.webm", soundsFolder, bookFolder);

            Assert.That(File.ReadAllText(existing), Is.EqualTo("theUsersOwnSound"));
        }

        [TestCase("../correct_yay.webm")]
        [TestCase(@"sounds\correct_yay.webm")]
        [TestCase(@"C:\Windows\win.ini")]
        [TestCase("")]
        public void CopyBuiltInSoundToBook_RefusesAnythingButAFileName(string fileName)
        {
            var (soundsFolder, bookFolder) = SetupBuiltInSoundTests("correct_yay.webm");

            Assert.Throws<ArgumentException>(() =>
                FileIOApi.CopyBuiltInSoundToBook(fileName, soundsFolder, bookFolder)
            );
            Assert.That(Directory.Exists(Path.Combine(bookFolder, "audio")), Is.False);
        }

        [Test]
        public void CopyBuiltInSoundToBook_RefusesASoundThatBloomDoesNotHave()
        {
            var (soundsFolder, bookFolder) = SetupBuiltInSoundTests("correct_yay.webm");

            Assert.Throws<FileNotFoundException>(() =>
                FileIOApi.CopyBuiltInSoundToBook("wrong_boo.webm", soundsFolder, bookFolder)
            );
        }

        // This string contains all the pucntuation on a standard US QWERTY keyboard that are valid in Windows filenames
        private const string _kCopyFileSpecialCharBaseName =
            "A`B~C!D@E#F$G%H^I&J(K)L-M_N=O+P[Q{R]S}T;U'V,W.X";
        private const string _kCopyFileSpecialCharEncodedBaseName =
            "A%60B~C!D%40E%23F%24G%25H%5EI%26J(K)L-M_N%3DO%2BP%5BQ%7BR%5DS%7DT%3BU'V%2CW.X";

        private static string EncodeFilenameForHttpRequest(string filename)
        {
            var encoded = filename
                .Replace(_kCopyFileSpecialCharBaseName, _kCopyFileSpecialCharEncodedBaseName)
                .Replace(" ", "%20")
                .Replace('\\', '/'); // Convert any Windows-style Directory separators into URL-style ones.
            return encoded;
        }
    }
}
