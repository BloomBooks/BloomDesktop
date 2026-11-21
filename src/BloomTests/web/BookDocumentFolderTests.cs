using Bloom;
using Bloom.Api;
using NUnit.Framework;

namespace BloomTests.web
{
    /// <summary>
    /// Tests for BloomServer.IsBookDocumentOutsideFolder, which tells a page list or page being
    /// edited that was loaded from the book's folder before it was renamed (BL-17055).
    /// </summary>
    [TestFixture]
    public class BookDocumentFolderTests
    {
        private const string kOldFolder =
            @"C:\Users\someone\Documents\Bloom\Odia Books\Book-bbc9bb4d";
        private const string kNewFolder =
            @"C:\Users\someone\Documents\Bloom\Odia Books\ଉଡ଼ାଜାହାଜ କେମିତି ଉଡ଼େ";

        /// <summary>
        /// The address of an in-memory document in <paramref name="folder"/> (a page list unless
        /// <paramref name="source"/> says otherwise), named the way
        /// BloomServer.MakeInMemoryHtmlFileInBookFolder names one, and made an address with
        /// ToLocalhost.
        /// </summary>
        private static string PageListAddressIn(
            string folder,
            InMemoryHtmlFileSource source = InMemoryHtmlFileSource.Pagelist
        )
        {
            var address = (
                folder.Replace('\\', '/')
                + "/d8ed5a8e-df28-4bf0-990f-63a6651c3860"
                + BloomServer.InMemoryHtmlFileNameEnding(source)
            ).ToLocalhost();
            Assert.That(
                address,
                Does.StartWith(BloomServer.ServerUrlWithBloomPrefixEndingInSlash),
                "test setup: the address should be one Bloom's server serves"
            );
            return address;
        }

        [Test]
        public void PageListFromTheOldFolder_IsOutsideTheNewFolder()
        {
            Assert.That(
                BloomServer.IsBookDocumentOutsideFolder(PageListAddressIn(kOldFolder), kNewFolder),
                Is.True
            );
        }

        [Test]
        public void PageListFromTheCurrentFolder_IsNotOutsideIt()
        {
            // A folder name with spaces and non-Latin letters, which the address carries escaped.
            Assert.That(
                BloomServer.IsBookDocumentOutsideFolder(PageListAddressIn(kNewFolder), kNewFolder),
                Is.False
            );
        }

        [Test]
        public void FolderRenamedOnlyInCase_IsOutside()
        {
            // The server matches book paths exactly, so the old-case address no longer works.
            Assert.That(
                BloomServer.IsBookDocumentOutsideFolder(
                    PageListAddressIn(kOldFolder),
                    kOldFolder.Replace("Book-bbc9bb4d", "BOOK-bbc9bb4d")
                ),
                Is.True
            );
        }

        [Test]
        public void AddressWithAQuery_IsJudgedByItsPath()
        {
            Assert.That(
                BloomServer.IsBookDocumentOutsideFolder(
                    PageListAddressIn(kNewFolder) + "?x=1",
                    kNewFolder
                ),
                Is.False
            );
        }

        /// <summary>
        /// The address of the page being edited in <paramref name="folder"/>, as
        /// BloomServer.UrlForCurrentBookPage makes it.
        /// </summary>
        private static string PageBeingEditedAddressIn(string folder) =>
            BloomServer.UrlForCurrentBookPage(folder, "57db084a-94b0-4694-a33e-fbdacb9fc56f");

        [Test]
        public void PageBeingEditedFromTheOldFolder_IsOutsideTheNewFolder()
        {
            Assert.That(
                BloomServer.IsBookDocumentOutsideFolder(
                    PageBeingEditedAddressIn(kOldFolder),
                    kNewFolder
                ),
                Is.True
            );
        }

        [Test]
        public void PageBeingEditedFromTheCurrentFolder_IsNotOutsideIt()
        {
            Assert.That(
                BloomServer.IsBookDocumentOutsideFolder(
                    PageBeingEditedAddressIn(kNewFolder),
                    kNewFolder
                ),
                Is.False
            );
        }

        [Test]
        public void SomeOtherInMemoryDocument_IsNeverOutside()
        {
            // Only the page list and the page being edited are what this is about.
            Assert.That(
                BloomServer.IsBookDocumentOutsideFolder(
                    PageListAddressIn(kOldFolder, InMemoryHtmlFileSource.Normal),
                    kNewFolder
                ),
                Is.False
            );
        }

        [Test]
        public void NoOrForeignReferer_IsNeverOutside()
        {
            Assert.That(BloomServer.IsBookDocumentOutsideFolder(null, kNewFolder), Is.False);
            Assert.That(BloomServer.IsBookDocumentOutsideFolder("", kNewFolder), Is.False);
            Assert.That(
                BloomServer.IsBookDocumentOutsideFolder(
                    "https://example.com/Book-bbc9bb4d/x-memsim-Pagelist.html",
                    kNewFolder
                ),
                Is.False
            );
        }
    }
}
