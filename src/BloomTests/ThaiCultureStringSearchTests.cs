using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Publish.BloomPub;
using Bloom.ToPalaso;
using Bloom.Workspace;
using NUnit.Framework;
using SIL.WritingSystems;

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
            ClearLibPalasoLanguageNameCaches();
        }

        [TearDown]
        public void TearDown()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            ClearLibPalasoLanguageNameCaches();
        }

        /// <summary>
        /// IetfLanguageTag caches language names process-wide, keyed by tag and not by culture.
        /// Without clearing them, a name another test looked up earlier would make a test here
        /// pass without the fix, and a name wrongly looked up here would leak into later tests.
        /// </summary>
        private static void ClearLibPalasoLanguageNameCaches()
        {
            foreach (
                var fieldName in new[]
                {
                    "MapIsoCodesToLanguageName",
                    "MapIsoCodeToSubtitledLanguageName",
                }
            )
            {
                var field = typeof(IetfLanguageTag).GetField(
                    fieldName,
                    BindingFlags.Static | BindingFlags.NonPublic
                );
                Assert.That(
                    field,
                    Is.Not.Null,
                    $"libpalaso no longer has the cache IetfLanguageTag.{fieldName}; update this test"
                );
                ((IDictionary)field.GetValue(null)).Clear();
            }
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

        [TestCase("fr", "fr")]
        [TestCase("pt-BR", "pt")]
        [TestCase("zh-CN", "zh-CN")]
        public void GetGeneralCode_IsTheLanguageSubtag(string code, string expected)
        {
            Assert.That(IetfLanguageTagExtra.GetGeneralCode(code), Is.EqualTo(expected));
        }

        [Test]
        public void CreateLanguageItem_IsNamedForTheLanguage_NotInvariantLanguage()
        {
            // BL-16945: every item in the UI language menu was named "Invariant Language".
            var item = WorkspaceView.CreateLanguageItem("de");

            Assert.That(item.MenuText, Is.EqualTo("Deutsch"));
            // EnglishName comes from CultureInfo.DisplayName, which follows the machine's UI
            // language, so check only that it is not the invariant culture's.
            Assert.That(item.EnglishName, Does.Not.StartWith("Invariant"));
        }

        [Test]
        public void WritingSystem_NamedFromItsTag_IsNotTheInvariantCultureName()
        {
            // BL-16945: this is how a new collection's default Language2 got saved as
            // "Invariant Language (Invariant Country)".
            var writingSystem = new WritingSystem(() => "en") { Tag = "en" };

            Assert.That(writingSystem.Name, Is.Not.Empty);
            Assert.That(
                writingSystem.Name,
                Is.Not.EqualTo(CultureInfo.InvariantCulture.EnglishName)
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
    }
}
