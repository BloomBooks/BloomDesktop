using Bloom;
using Bloom.Api;
using NUnit.Framework;

namespace BloomTests.web
{
    /// <summary>
    /// Tests for BloomServer.IsPageListDocumentOutsideFolder, which lets the page list's content
    /// requests tell a page list left over from before the book's folder was renamed (BL-17055).
    /// </summary>
    [TestFixture]
    public class PageListDocumentFolderTests
    {
        private const string kOldFolder =
            @"C:\Users\someone\Documents\Bloom\Odia Books\Book-bbc9bb4d";
        private const string kNewFolder =
            @"C:\Users\someone\Documents\Bloom\Odia Books\ଉଡ଼ାଜାହାଜ କେମିତି ଉଡ଼େ";

        /// <summary>
        /// The address of a page list document in <paramref name="folder"/>, the way Bloom makes
        /// one (BloomServer.MakeInMemoryHtmlFileInBookFolder, then ToLocalhost).
        /// </summary>
        private static string PageListAddressIn(
            string folder,
            string fileEnding = "-memsim-Pagelist.html"
        )
        {
            var address = (
                folder.Replace('\\', '/') + "/d8ed5a8e-df28-4bf0-990f-63a6651c3860" + fileEnding
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
                BloomServer.IsPageListDocumentOutsideFolder(
                    PageListAddressIn(kOldFolder),
                    kNewFolder
                ),
                Is.True
            );
        }

        [Test]
        public void PageListFromTheCurrentFolder_IsNotOutsideIt()
        {
            // A folder name with spaces and non-Latin letters, which the address carries escaped.
            Assert.That(
                BloomServer.IsPageListDocumentOutsideFolder(
                    PageListAddressIn(kNewFolder),
                    kNewFolder
                ),
                Is.False
            );
        }

        [Test]
        public void FolderRenamedOnlyInCase_IsOutside()
        {
            // The server matches book paths exactly, so the old-case address no longer works.
            Assert.That(
                BloomServer.IsPageListDocumentOutsideFolder(
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
                BloomServer.IsPageListDocumentOutsideFolder(
                    PageListAddressIn(kNewFolder) + "?x=1",
                    kNewFolder
                ),
                Is.False
            );
        }

        [Test]
        public void SomethingOtherThanAPageList_IsNeverOutside()
        {
            // The page being edited, or any other document, is not what this is about.
            Assert.That(
                BloomServer.IsPageListDocumentOutsideFolder(
                    PageListAddressIn(kOldFolder, "-memsim-Normal.html"),
                    kNewFolder
                ),
                Is.False
            );
        }

        [Test]
        public void NoOrForeignReferer_IsNeverOutside()
        {
            Assert.That(BloomServer.IsPageListDocumentOutsideFolder(null, kNewFolder), Is.False);
            Assert.That(BloomServer.IsPageListDocumentOutsideFolder("", kNewFolder), Is.False);
            Assert.That(
                BloomServer.IsPageListDocumentOutsideFolder(
                    "https://example.com/Book-bbc9bb4d/x-memsim-Pagelist.html",
                    kNewFolder
                ),
                Is.False
            );
        }
    }
}
