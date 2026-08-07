using System.Linq;
using Bloom;
using Bloom.Edit;
using Bloom.SafeXml;
using NUnit.Framework;

namespace BloomTests.Edit
{
    [TestFixture]
    public class EditingModelTests
    {
        /// <summary>
        /// Build a page containing one img per given src, and return the src of the img that
        /// UpdateMetaData's XPath would pick for <paramref name="wantedFileName"/>, or null.
        /// </summary>
        private static string FindSrcMatchedFor(string wantedFileName, params string[] srcsOnPage)
        {
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(
                "<div class='bloom-page'>"
                    + string.Join("", srcsOnPage.Select(s => $"<img src=\"{s}\"/>"))
                    + "</div>"
            );
            // Sanity check: the page really does hold the images we think it does, so a null
            // result below means the XPath missed, not that the fixture was empty.
            Assert.That(
                doc.DocumentElement.SafeSelectNodes(".//img").Length,
                Is.EqualTo(srcsOnPage.Length),
                "test setup: expected every img to be in the page"
            );

            var match = UrlPathString
                .CreateFromUnencodedString(wantedFileName, strictlyTreatAsUnencoded: true)
                .UrlEncoded;
            var found = doc
                .DocumentElement.SafeSelectNodes(EditingModel.MakeImgWithSrcXPath(match))
                .Cast<SafeXmlElement>()
                .FirstOrDefault();
            return found?.GetAttribute("src");
        }

        [Test]
        public void ImgWithSrcXPath_PlainSrc_Matches()
        {
            Assert.That(FindSrcMatchedFor("cat.png", "cat.png"), Is.EqualTo("cat.png"));
        }

        /// <summary>
        /// The reason this XPath was widened: a src usually carries a query string as well as the
        /// file name, and matching the src exactly found nothing on exactly those pages, so
        /// editing the image's copyright silently did nothing.
        /// </summary>
        [TestCase("cat.png?transparent=yes")]
        [TestCase("cat.png?thumbnail=1")]
        [TestCase("cat.png?12345")]
        public void ImgWithSrcXPath_SrcHasQueryString_StillMatches(string srcOnPage)
        {
            Assert.That(FindSrcMatchedFor("cat.png", srcOnPage), Is.EqualTo(srcOnPage));
        }

        /// <summary>
        /// Requiring the '?' is what stops the widened match from grabbing a different file whose
        /// name merely begins with the same characters.
        /// </summary>
        [Test]
        public void ImgWithSrcXPath_DifferentFileWithSamePrefix_DoesNotMatch()
        {
            Assert.That(FindSrcMatchedFor("cat.png", "cat2.png"), Is.Null);
            Assert.That(
                FindSrcMatchedFor("cat.png", "cat2.png", "cat.png?transparent=yes"),
                Is.EqualTo("cat.png?transparent=yes"),
                "it should skip the look-alike and find the real one"
            );
        }

        /// <summary>
        /// BL-16669: the name we search for is the encoded form, so a file whose name contains a
        /// literal '%' has to match the encoded src that ChangePicture wrote for it.
        /// </summary>
        [Test]
        public void ImgWithSrcXPath_FileNameContainsPercentEscape_Matches()
        {
            Assert.That(
                FindSrcMatchedFor("photo%41.png", "photo%2541.png?transparent=yes"),
                Is.EqualTo("photo%2541.png?transparent=yes")
            );
            // ...and must not match the name the old bug produced.
            Assert.That(FindSrcMatchedFor("photo%41.png", "photoA.png"), Is.Null);
        }

        /// <summary>
        /// An apostrophe would end the XPath string literal if it reached it unescaped; UrlEncoded
        /// turns it into %27 first, so the query stays well-formed.
        /// </summary>
        [Test]
        public void ImgWithSrcXPath_FileNameContainsApostrophe_DoesNotBreakTheQuery()
        {
            Assert.That(
                FindSrcMatchedFor("bob's cat.png", "bob%27s%20cat.png"),
                Is.EqualTo("bob%27s%20cat.png")
            );
        }
    }
}
