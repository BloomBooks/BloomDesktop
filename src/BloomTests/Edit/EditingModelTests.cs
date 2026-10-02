using System.Linq;
using System.Reflection;
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

            var match = UrlPathString.CreateFromUnencodedString(wantedFileName).UrlEncoded;
            var found = doc
                .DocumentElement.SafeSelectNodes(EditingModel.MakeImgWithSrcXPath(match))
                .Cast<SafeXmlElement>()
                .FirstOrDefault();
            return found?.GetAttribute("src");
        }

        /// <summary>
        /// MergeCurrentPageThenSave cannot see inside the action it is given, so the action has to
        /// declare whether it changes the book -- and the DEFAULT has to be "yes".
        ///
        /// This is not a style preference. Getting it wrong in the "yes" direction costs a write
        /// that was not needed. Getting it wrong in the "no" direction means the action runs, the
        /// screen shows the result, and nothing is written: the merge marks the book dirty only
        /// when the page's own content changed, so a command used on a page the user never typed on
        /// leaves the book looking clean at the moment the action makes it dirty. That is how
        /// changing the page size, choosing a different layout and setting the copyright quietly
        /// failed to reach disk. Flipping this default would bring all of that back at once, in
        /// every caller, silently -- hence a test on the default itself.
        /// </summary>
        [Test]
        public void MergeCurrentPageThenSave_ActionChangesTheBook_DefaultsToTrue()
        {
            var parameter = typeof(EditingModel)
                .GetMethod(nameof(EditingModel.MergeCurrentPageThenSave))
                .GetParameters()
                .SingleOrDefault(p => p.Name == "actionChangesTheBook");

            Assert.That(
                parameter,
                Is.Not.Null,
                "test setup: MergeCurrentPageThenSave should still have an actionChangesTheBook parameter"
            );
            Assert.That(
                parameter.HasDefaultValue,
                Is.True,
                "actionChangesTheBook should be optional, so that a new caller gets the safe answer without thinking about it"
            );
            Assert.That(
                parameter.DefaultValue,
                Is.True,
                "actionChangesTheBook must default to TRUE: a caller that changes the book and does not say so has its change written nowhere"
            );
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
