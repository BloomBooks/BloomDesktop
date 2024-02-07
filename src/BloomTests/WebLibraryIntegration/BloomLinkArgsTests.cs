using System.Web;
using Bloom.WebLibraryIntegration;
using NUnit.Framework;

namespace BloomTests.WebLibraryIntegration
{
    [TestFixture]
    public class BloomLinkArgsTests
    {
        [Test]
        public void LinkArgs_WithTitle_GetsCorrectOrderAndTitle()
        {
            var order = HttpUtility.UrlEncode("whatever=&?!<>'\"");
            var title = HttpUtility.UrlEncode("title=&?!<>'\"");
            var input =
                BloomLinkArgs.kBloomUrlPrefix
                + BloomLinkArgs.kOrderFile
                + "="
                + order
                + "&title="
                + title;
            var args = new BloomLinkArgs(input);
            Assert.That(args.OrderUrl, Is.EqualTo("whatever=&?!<>'\""));
            Assert.That(args.Title, Is.EqualTo("title=&?!<>'\""));
        }

        //BL-5419
        [Test]
        [Platform(
            Exclude = "Linux",
            Reason = "The Linux Firefox apparently URL-encodes non-ASCII characters.  The Mono HttpUtility.UrlDecode() method mangles non-ASCII characters."
        )]
        public void BloomLinkArgs_WithSpanishFromFirefox_DoesNotMangle()
        {
            var url =
                "bloom://localhost/order?orderFile=BloomLibraryBooks/chris_hurst%40sil.org%2f2bc65ef6-9c84-4b3a-af69-fcc6ed2682b4&title=Como%20la%20hormigita%20salvó%20al%20pájaro%20blanco%0A";
            var args = new BloomLinkArgs(url);
            Assert.True(args.Title.Contains("pájaro"));
        }

        [Test]
        public void BloomLinkArgs_ForEdit_SetCorrectly()
        {
            var url =
                "bloom://localhost/order?orderFile=BloomLibraryBooks-Sandbox/OOgCG25FoW%2f1707149386486%2f&title=hello%20world&minVersion=4.8&forEdit=true";
            var args = new BloomLinkArgs(url);
            Assert.That(args.ForEdit, Is.True);
        }

        //BL-5419. Chrome was ok, becuase it encoded the accented characters
        [Test]
        public void BloomLinkArgs_WithSpanishFromChrome_DoesNotMangle()
        {
            var url =
                "bloom://localhost/order?orderFile=BloomLibraryBooks/chris_hurst%40sil.org%2f2bc65ef6-9c84-4b3a-af69-fcc6ed2682b4&title=Como%20la%20hormigita%20salv%C3%B3%20al%20p%C3%A1jaro%20blanco%0A";
            var args = new BloomLinkArgs(url);
            Assert.True(args.Title.Contains("pájaro"));
        }
    }
}
