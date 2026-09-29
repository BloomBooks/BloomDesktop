using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Bloom;
using Bloom.Book;
using Bloom.Publish.BloomPub;
using Bloom.Workspace;
using NUnit.Framework;
using SIL.TestUtilities;

namespace BloomTests
{
    /// <summary>
    /// Under th-TH, a culture-sensitive IndexOf ignores punctuation and spaces: a search string that
    /// starts with punctuation matches one character late, and one made only of punctuation "matches"
    /// at the start of any string, even one that does not contain it (BL-16932). These tests run code
    /// that searches for such strings with the current culture set to th-TH.
    /// </summary>
    [TestFixture]
    public class ThaiCultureStringSearchTests
    {
        private CultureInfo _originalCulture;

        [SetUp]
        public void Setup()
        {
            _originalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
        }

        [TearDown]
        public void TearDown()
        {
            CultureInfo.CurrentCulture = _originalCulture;
        }

        [Test]
        public void UrlPathString_QueryOnly_NoQuery_IsEmpty()
        {
            var url = UrlPathString.CreateFromUnencodedString("images/cover.jpg");

            Assert.That(url.QueryOnly.NotEncoded, Is.EqualTo(""));
        }

        [Test]
        public void UrlPathString_QueryOnly_WithQuery_IsTheQuery()
        {
            var url = UrlPathString.CreateFromUnencodedString("images/cover.jpg?optional=true");

            Assert.That(url.QueryOnly.NotEncoded, Is.EqualTo("?optional=true"));
        }

        [Test]
        public void RemoveCommentsFromCss_RemovesOnlyTheComments()
        {
            var css =
                "/* heading */\n.a { font-family: Andika; }\n// line comment\n.b { color: red; }";

            var result = HtmlDom.RemoveCommentsFromCss(css);

            Assert.That(result, Does.Contain(".a { font-family: Andika; }"));
            Assert.That(result, Does.Contain(".b { color: red; }"));
            Assert.That(result, Does.Not.Contain("heading"));
            Assert.That(result, Does.Not.Contain("line comment"));
        }

        [Test]
        public void RemoveCommentsFromCss_NoComments_IsUnchanged()
        {
            var css = ".a { font-family: Andika; }";

            Assert.That(HtmlDom.RemoveCommentsFromCss(css), Is.EqualTo(css));
        }

        [Test]
        public void GetXMatterFromStyleSheetFileName_IsTheNameBeforeTheSuffix()
        {
            Assert.That(
                XMatterHelper.GetXMatterFromStyleSheetFileName("Traditional-XMatter.css"),
                Is.EqualTo("Traditional")
            );
        }

        [Test]
        public void GetShortenedLanguageName_DropsTheCountry()
        {
            Assert.That(
                WorkspaceView.GetShortenedLanguageName("Français (France)"),
                Is.EqualTo("Français")
            );
        }

        [Test]
        public void ExtractFilenameFromBackgroundImageStyleUrl_IsTheNameInTheQuotes()
        {
            Assert.That(
                BloomPubMaker.ExtractFilenameFromBackgroundImageStyleUrl(
                    "background-image:url('cover.jpg')"
                ),
                Is.EqualTo("cover.jpg")
            );
        }

        [Test]
        public void ExtractFilenameFromBackgroundImageStyleUrl_DecodesTheName()
        {
            Assert.That(
                BloomPubMaker.ExtractFilenameFromBackgroundImageStyleUrl(
                    "background-image:url('%E0%B8%9B%E0%B8%811111.jpg')"
                ),
                Is.EqualTo("\u0e1b\u0e011111.jpg")
            );
        }

        [Test]
        public void TrimEnd_RemovesTrailingSeparators_AndReturns()
        {
            var trim = Task.Run(() => BookData.TrimEnd("Accra, Ghana, ", ", "));

            Assert.That(
                trim.Wait(5000),
                Is.True,
                "TrimEnd did not return; it is looping on a separator it cannot remove"
            );
            Assert.That(trim.Result, Is.EqualTo("Accra, Ghana"));
        }

        /// <summary>
        /// Unlike IndexOf, ICU's IsSuffix/IsPrefix -- which back EndsWith/StartsWith -- do not
        /// report a match for an all-ignorable needle, so under th-TH the searches on this path
        /// answer the same whether or not they pass a StringComparison (measured on .NET 8 / ICU;
        /// see the notes on BL-16934). This test therefore cannot fail if those arguments are
        /// dropped again: it is coverage that a Thai-named book is still found, not a guard
        /// against regressing the culture-sensitivity fix. The tests above, which exercise
        /// IndexOf, are the ones that do fail without it.
        /// </summary>
        [Test]
        public void FindBookHtmlInFolder_ThaiNamedBook_ChoosesTheHtmFile()
        {
            Assert.That(CultureInfo.CurrentCulture.Name, Is.EqualTo("th-TH"));
            using (var outerFolder = new TemporaryFolder("FindBookHtmlInFolder_ThaiNamedBook"))
            {
                // The folder name differs from the book's file name, so the candidates are found
                // by filtering the folder's files on their extension.
                using (var folder = new TemporaryFolder(outerFolder, "นิทานใหม่"))
                {
                    File.WriteAllText(folder.Combine("นิทาน.htm"), "");
                    File.WriteAllText(folder.Combine("นิทาน.htm.bak"), "");
                    File.WriteAllText(folder.Combine("นิทาน.htmbak"), "");
                    File.WriteAllText(folder.Combine("notes.txt"), "");

                    var path = BookStorage.FindBookHtmlInFolder(folder.Path);

                    Assert.That(Path.GetFileName(path), Is.EqualTo("นิทาน.htm"));
                }
            }
        }
    }
}
