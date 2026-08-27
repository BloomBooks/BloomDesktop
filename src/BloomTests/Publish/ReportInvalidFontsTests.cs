using System.IO;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ImageProcessing;
using Bloom.Publish;
using Bloom.web;
using BloomTemp;
using NUnit.Framework;
using SIL.Progress;

namespace BloomTests.Publish
{
    /// <summary>
    /// Regression test for BL-16767. Bulk upload runs PublishHelper.ReportInvalidFonts once per
    /// book, in one process. The pre-fix implementation created an inline WebView2Browser on the
    /// calling thread for each book; after the first book, each new inline WebView2 failed to
    /// finish initializing, and every following book failed to upload with "The instance of
    /// CoreWebView2 is uninitialized". The fix drives the font check through an OffScreenBrowser
    /// (a WebView2 on its own dedicated thread). This test proves the check survives a series of
    /// books in one process, which is exactly what bulk upload needs.
    /// </summary>
    [TestFixture]
    public class ReportInvalidFontsTests
    {
        private BloomServer _server;
        private TemporaryFolder _workFolder;

        /// <summary>
        /// A running BloomServer is required: ReportInvalidFonts navigates a real browser to the
        /// book (served from memory) so the stylesheets actually resolve.
        /// </summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var settings = new CollectionSettings();
            var locator = new BloomFileLocator(
                settings,
                new XMatterPackFinder(new[] { BloomFileLocator.GetFactoryXMatterDirectory() }),
                ProjectContext.GetFactoryFileLocations(),
                ProjectContext.GetFoundFileLocations(),
                ProjectContext.GetAfterXMatterFileLocations()
            );
            _server = new BloomServer(
                new RuntimeImageProcessor(new BookRenamedEvent()),
                new BookSelection(),
                locator
            );
            _server.EnsureListening();
            _workFolder = new TemporaryFolder("ReportInvalidFontsTests");
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _workFolder.Dispose();
            RetiredTestServers.Retire(_server);
        }

        /// <summary>
        /// Make a minimal book folder: one page with one editable div, enough for the font scan
        /// to have something to compute a font-family for.
        /// </summary>
        private string MakeBook(string name)
        {
            var folder = Path.Combine(_workFolder.FolderPath, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(
                Path.Combine(folder, name + ".htm"),
                XmlHtmlConverter.CreateHtmlString(
                    "<div class='bloom-page'><div class='bloom-editable' lang='fr'>Bonjour</div></div>"
                )
            );
            return folder;
        }

        [Test]
        public void ReportInvalidFonts_FiveBooksInARow_DoesNotThrow()
        {
            // Five is comfortably past the point where the pre-fix code started failing
            // (the second book).
            for (int i = 1; i <= 5; i++)
            {
                var folder = MakeBook("book" + i);
                var progress = new StringBuilderProgress();
                Assert.DoesNotThrow(
                    () => PublishHelper.ReportInvalidFonts(folder, progress),
                    $"The font check threw for book {i} of the series."
                );
            }
        }
    }
}
