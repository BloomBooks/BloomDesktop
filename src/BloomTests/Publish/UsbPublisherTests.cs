#if !__MonoCS__
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom;
using Bloom.Book;
using Bloom.web;
using BloomTests.Book;
using BloomTests.web;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Publish
{
	[TestFixture]
	class UsbPublisherTests : BookTestsBase
	{
		private static BookSelection s_bookSelection;
		private BookServer _bookServer;
		private WebSocketProgress _progress;
		private MockUsbPublisher _testUsbPublisher;
		private WebSocketServerSpy _spy;

		[OneTimeSetUp]
		public void OneTimeSetup()
		{
			GetTestBookSelection();
			_spy = new WebSocketServerSpy();
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			_spy.Dispose();
		}

		public override void Setup()
		{
			base.Setup();
			Program.SetUpLocalization(new ApplicationContainer());
			_bookServer = CreateBookServer();
			s_bookSelection.SelectBook(CreateBook());
			_progress = CreateWebSocketProgress();
			_testUsbPublisher = new MockUsbPublisher(_progress, _bookServer);
		}

		public override void TearDown()
		{
			_spy.Reset();
			base.TearDown();
		}

		private WebSocketProgress CreateWebSocketProgress()
		{
			_spy.Init("webSocketServerSpy");
			return new WebSocketProgress(_spy);
		}

		private static BookSelection GetTestBookSelection()
		{
			s_bookSelection = new BookSelection();
			return s_bookSelection;
		}

		[Test]
		public void SendBookAsync_HandlesDiskFullException()
		{
			_testUsbPublisher.SendBookAsync(s_bookSelection.CurrentSelection, Color.Aqua);
			// Allow async method to complete
			Application.DoEvents();

			// Unfortunately, using the MockUsbPublisher to throw our Disk Full exception in SendBookDoWork also
			// means we aren't testing the code that figures out the size of the book. At least it's predictable!
			const string message =
				"<span style='color:red'>The device reported that it does not have enough space for this book. The book is 0.0 MB.</span>";
			Assert.AreEqual(message, _spy.Events.First().Value.Item1);
		}

		[Test]
		public void GetSizeOfBloomdFile_Works()
		{
			var book = CreateBookWithPhysicalFile(ThreePageHtml);
			var bloomdPath = MakeFakeBloomdFile(book);
			_testUsbPublisher.SetLastBloomdFilePath(bloomdPath);
			var size = _testUsbPublisher.GetBloomdFileSize();
			Assert.AreEqual("0.1", size);
		}

		private string MakeFakeBloomdFile(Bloom.Book.Book book)
		{
			var srcFile = Path.Combine(book.FolderPath, "book.htm");
			var filePath = new TempFileForSafeWriting(Path.Combine(book.FolderPath, "book.bloomd")).TempFilePath;
			var fileContents = File.ReadAllLines(srcFile);

			for (var i = 0; i < 100; i++)
				File.AppendAllLines(filePath, fileContents);

			return filePath;
		}
	}
}
#endif
