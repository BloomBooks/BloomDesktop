#if !__MonoCS__
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Bloom;
using Bloom.Book;
using Bloom.web;
using BloomTests.Book;
using BloomTests.web;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Publish.BloomPub.usb
{
    [TestFixture]
    public class UsbPublisherTests : BookTestsBase
    {
        private static BookSelection s_bookSelection;
        private BookServer _bookServer;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SetupTestBookSelection();
        }

        public override void Setup()
        {
            base.Setup();
            _bookServer = CreateBookServer();
            s_bookSelection.SelectBook(CreateBook());
        }

        private static void SetupTestBookSelection()
        {
            s_bookSelection = new BookSelection();
        }

        private static WebSocketProgress CreateWebSocketProgress(out WebSocketServerSpy spy)
        {
            spy = new WebSocketServerSpy();
            spy.Init("webSocketServerSpy");
            return new WebSocketProgress(spy, "ummm...");
        }

        [Test]
        public void SendBookAsync_HandlesDiskFullException()
        {
            // Setup
            WebSocketServerSpy spy;
            var progress = CreateWebSocketProgress(out spy);
            var testUsbPublisher = new MockUsbPublisher(progress, _bookServer);
            testUsbPublisher.SetExceptionToThrow(MockUsbPublisher.ExceptionToThrow.DeviceFull);

            // SUT
            testUsbPublisher.SendBookAsync(s_bookSelection.CurrentSelection, Color.Aqua);

            // Allow async method to complete
            HangoutAwhile();

            // Unfortunately, using the MockUsbPublisher to throw our Disk Full exception in SendBookDoWork also
            // means we aren't testing the code that figures out the size of the book. At least it's predictable!
            const string message =
                "The device reported that it does not have enough space for this book. The book is of unknown MB.";
            Assert.AreEqual(message, spy.Events.First().Value.Item1);
            Assert.That(spy.Events.First().Value.Item3, Is.EqualTo(ProgressKind.Error));
        }

        [Test]
        public void SendBookAsync_HandlesDeviceHungException()
        {
            // Setup
            WebSocketServerSpy spy;
            var progress = CreateWebSocketProgress(out spy);
            var testUsbPublisher = new MockUsbPublisher(progress, _bookServer);
            testUsbPublisher.SetExceptionToThrow(MockUsbPublisher.ExceptionToThrow.DeviceHung);

            // SUT
            testUsbPublisher.SendBookAsync(s_bookSelection.CurrentSelection, Color.Aqua);

            // Allow async method to complete
            HangoutAwhile();

            // Unfortunately, using the MockUsbPublisher to throw our exception in SendBookDoWork also
            // means we aren't testing the code that figures out the size of the book. At least it's predictable!
            const string message =
                "The device reported that it does not have enough space for this book. The book is of unknown MB.";
            Assert.AreEqual(message, spy.Events.First().Value.Item1);
            Assert.That(spy.Events.First().Value.Item3, Is.EqualTo(ProgressKind.Error));
        }

        [Test]
        public void GetSizeOfBloomdFile_Works()
        {
            // Setup
            WebSocketServerSpy spy;
            var progress = CreateWebSocketProgress(out spy);
            var testUsbPublisher = new MockUsbPublisher(progress, _bookServer);
            testUsbPublisher.SetExceptionToThrow(
                MockUsbPublisher.ExceptionToThrow.HandleDeviceFull
            );

            var book = CreateBookWithPhysicalFile(kThreePageBookBodyContent);
            var bloomdPath = MakeFakeBloomdFile(book);
            testUsbPublisher.SetLastBloomPubFileSize(bloomdPath);

            // SUT
            var size = testUsbPublisher.GetStoredBloomPubFileSize();

            // 'size' is a culture-specific string; convert it to the invariant culture for test
            size = double.Parse(size).ToString(CultureInfo.InvariantCulture);

            Assert.AreEqual("0.1", size);
        }

        private static string MakeFakeBloomdFile(Bloom.Book.Book book)
        {
            var srcFile = Path.Combine(book.FolderPath, "book.htm");
            var filePath = new TempFileForSafeWriting(
                Path.Combine(book.FolderPath, "book.bloompub")
            ).TempFilePath;
            var fileContents = File.ReadAllLines(srcFile);

            for (var i = 0; i < 100; i++)
                File.AppendAllLines(filePath, fileContents);

            return filePath;
        }

        private static void HangoutAwhile()
        {
            Application.DoEvents();
            Thread.Sleep(1000);
            Application.DoEvents();
            Thread.Sleep(1000);
            Application.DoEvents();
        }
    }
}
#endif
