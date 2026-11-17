using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Bloom;
using Bloom.Book;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Book
{
    /// <summary>
    /// Tests of the front/back matter pack descriptions, which for the packs that ship with Bloom
    /// live in the English XLF files rather than in description-en.txt files in the pack folders.
    /// </summary>
    [TestFixture]
    public class XMatterInfoTests
    {
        /// <summary>
        /// Every id in the three English XLF files.
        /// </summary>
        private static HashSet<string> GetEnglishXlfIds()
        {
            var englishFolder = Path.Combine(
                FileLocationUtilities.GetDirectoryDistributedWithApplication("localization"),
                "en"
            );
            XNamespace xliff = "urn:oasis:names:tc:xliff:document:1.2";
            var ids = new HashSet<string>();
            foreach (
                var file in new[] { "Bloom.xlf", "BloomMediumPriority.xlf", "BloomLowPriority.xlf" }
            )
            {
                var doc = XDocument.Load(Path.Combine(englishFolder, file));
                foreach (var unit in doc.Descendants(xliff + "trans-unit"))
                    ids.Add(unit.Attribute("id").Value);
            }
            return ids;
        }

        [Test]
        public void DescriptionL10nId_IsNextToTheNameId()
        {
            var info = new XMatterInfo(Path.Combine("somewhere", "Factory-XMatter"));

            Assert.That(
                info.NameL10nId,
                Is.EqualTo(
                    "CollectionSettingsDialog.BookMakingTab.Front/BackMatterPack.Paper Saver"
                ),
                "Test setup: the name id should match the existing name entries in Bloom.xlf"
            );
            Assert.That(info.DescriptionL10nId, Is.EqualTo(info.NameL10nId + ".Description"));
        }

        [Test]
        public void EveryShippedPack_HasItsDescriptionInTheXlf()
        {
            var finder = new XMatterPackFinder(
                new[] { BloomFileLocator.GetFactoryXMatterDirectory() }
            );
            // The packs a user can choose, plus the project-specific ones a branding can choose.
            var packs = finder
                .GetXMattersToOfferInSettings(null)
                .Concat(
                    finder.Factory.Where(p =>
                        p.PathToFolder.Contains("project-specific")
                        && !p.Key.StartsWith("unit-test", StringComparison.Ordinal)
                    )
                )
                .ToArray();
            Assert.That(
                packs.Select(p => p.Key),
                Does.Contain("Traditional").And.Contain("Factory").And.Contain("MXBBook"),
                "Test setup: the finder should see both standard and project-specific packs"
            );

            var ids = GetEnglishXlfIds();
            var missing = packs
                .Where(p => !ids.Contains(p.DescriptionL10nId))
                .Select(p => p.DescriptionL10nId)
                .ToArray();

            Assert.That(missing, Is.Empty, "These pack descriptions are missing from the XLF");
        }
    }
}
