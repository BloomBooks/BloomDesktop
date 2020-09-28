using Bloom.Api;
using Bloom.Book;
using Bloom.web.controllers;
using NUnit.Framework;
using SIL.TestUtilities;
using System.IO;

namespace BloomTests.web.controllers
{
	[TestFixture]
	class FileIOApiTests
	{
		private BloomServer _server;
		private TemporaryFolder _testFolder;

		private string GetTestFolderName() => "FileIOApiTests";

		[OneTimeSetUp]
		public void InitialSetup()
		{
			var bookSelection = new BookSelection();
			bookSelection.SelectBook(new Bloom.Book.Book());
			_server = new BloomServer(bookSelection);

			var controller = new FileIOApi(bookSelection);
			controller.RegisterWithApiHandler(_server.ApiHandler);
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

		private (string sourcePath, string destDirPath) SetupCopyFileTests(string sourceFolderName, string sourceFileName, string collectionName, string bookTitle)
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

			(string sourceFilePath, string destDirPath) = SetupCopyFileTests(sourceFolderName, sourceFileName, collectionName, bookTitle);
			string encodedSourceFilePath = EncodeFilenameForHttpRequest(sourceFilePath);

			string destFilePath = Path.Combine(destDirPath, "fa22704c-6f27-4c1f-9f1b-7eb62baf2fde.mp3");
			string encodedDestFilePath = EncodeFilenameForHttpRequest(destFilePath);
			
			string jsonData = $"{{ \"from\": \"{encodedSourceFilePath}\", \"to\": \"{encodedDestFilePath}\" }}";

			// System Under Test //

			// Debugging tip: If debugging with breakpoints, set the timeout to some high value
			int? timeoutInMilliseconds = null;	
			var result = ApiTest.PostString(_server, "fileIO/copyFile", jsonData, ApiTest.ContentType.JSON, timeoutInMilliseconds: timeoutInMilliseconds);

			// Verification
			Assert.That(result, Is.EqualTo("OK"), "Request should reply \"OK\" if successful");
			Assert.That(File.ReadAllText(destFilePath), Is.EqualTo("fakeSourceFileContents"), "Contents of copied file were not as expected.");
		}
		
		// This string contains all the pucntuation on a standard US QWERTY keyboard that are valid in Windows filenames
		private const string _kCopyFileSpecialCharBaseName = "A`B~C!D@E#F$G%H^I&J(K)L-M_N=O+P[Q{R]S}T;U'V,W.X";
		private const string _kCopyFileSpecialCharEncodedBaseName = "A%60B~C!D%40E%23F%24G%25H%5EI%26J(K)L-M_N%3DO%2BP%5BQ%7BR%5DS%7DT%3BU'V%2CW.X";

		private static string EncodeFilenameForHttpRequest(string filename)
		{
			var encoded = filename
				.Replace(_kCopyFileSpecialCharBaseName, _kCopyFileSpecialCharEncodedBaseName)
				.Replace(" ", "%20")
				.Replace('\\', '/');	// Convert any Windows-style Directory separators into URL-style ones.
			return encoded;
		}
	}
}
