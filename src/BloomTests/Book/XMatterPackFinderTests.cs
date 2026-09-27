using System.Globalization;
using System.IO;
using System.Linq;
using Bloom.Book;
using NUnit.Framework;
using SIL.TestUtilities;

namespace BloomTests.Book
{
    /// <summary>
    /// Tests that xmatter pack keys and labels do not depend on the user's regional format.
    /// Under th-TH, a culture-sensitive IndexOf("-xmatter") ignores the hyphen, so every key came
    /// out with a trailing "-", no pack matched "Factory", and clicking OK in Collection Settings
    /// crashed in GetValidXmatter (BL-16932).
    /// </summary>
    [TestFixture]
    public class XMatterPackFinderTests
    {
        private CultureInfo _originalCulture;
        private TemporaryFolder _xMatterFolder;
        private XMatterPackFinder _finder;

        [SetUp]
        public void Setup()
        {
            _xMatterFolder = new TemporaryFolder("XMatterPackFinderTests");
            Directory.CreateDirectory(Path.Combine(_xMatterFolder.Path, "Factory-XMatter"));
            Directory.CreateDirectory(Path.Combine(_xMatterFolder.Path, "Traditional-XMatter"));
            _finder = new XMatterPackFinder(new[] { _xMatterFolder.Path });

            _originalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
        }

        [TearDown]
        public void TearDown()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            _xMatterFolder.Dispose();
        }

        [Test]
        public void Key_ThaiCulture_IsTheNameBeforeTheSuffix()
        {
            var info = new XMatterInfo(Path.Combine("somewhere", "Traditional-XMatter"));

            Assert.That(info.Key, Is.EqualTo("Traditional"));
        }

        [Test]
        public void EnglishLabel_ThaiCulture_IsTheSplitName()
        {
            var info = new XMatterInfo(Path.Combine("somewhere", "Factory-XMatter"));

            Assert.That(info.EnglishLabel, Is.EqualTo("Paper Saver"));
        }

        [Test]
        public void GetValidXmatter_ThaiCulture_KeepsTheCollectionsPack()
        {
            Assert.That(
                _finder.All.Count(),
                Is.EqualTo(2),
                "Test setup: the finder should see the two packs made in Setup"
            );

            Assert.That(_finder.GetValidXmatter(null, "Traditional"), Is.EqualTo("Traditional"));
        }

        [Test]
        public void GetValidXmatter_ThaiCulture_UnknownPackFallsBackToFactory()
        {
            Assert.That(
                _finder.All.Count(),
                Is.EqualTo(2),
                "Test setup: the finder should see the two packs made in Setup"
            );

            Assert.That(_finder.GetValidXmatter(null, "NoSuchPack"), Is.EqualTo("Factory"));
        }
    }
}
