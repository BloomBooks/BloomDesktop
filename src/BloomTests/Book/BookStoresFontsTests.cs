using System.IO;
using System.Linq;
using Bloom.FontProcessing;
using Bloom.web.controllers;
using BloomTests.FontProcessing;
using NUnit.Framework;
using SIL.Progress;

namespace BloomTests.Book
{
    /// <summary>
    /// Tests that bringing a book up to date puts a copy of each font the book uses into the
    /// fonts folder of the collection that holds the book.
    /// </summary>
    [TestFixture]
    public class BookStoresFontsTests : BookTestsBase
    {
        [TearDown]
        public void TearDownFontMetadata()
        {
            FontsApi.AvailableFontMetadataDictionary.Clear();
        }

        [Test]
        public void BringBookUpToDate_StoresTheUsedFontButNotOneBloomServes()
        {
            var installedGroup = InstalledTestFonts.FindFamilyWithGoodLicense(out var family);
            if (installedGroup == null)
                Assert.Ignore("No font with a known-good license is installed on this computer.");
            // Bloom serves Andika itself, so a book that uses it needs no stored copy.
            Assert.That(
                FontServe.GetInstance().HasFamily("Andika"),
                Is.True,
                "this test needs Andika to be one of the fonts Bloom serves"
            );
            FontsApi.AvailableFontMetadataDictionary[family] = new FontMetadata(
                family,
                installedGroup
            );

            var book = CreateBookWithPhysicalFile("<div class='bloom-page'></div>");
            File.WriteAllText(
                Path.Combine(book.FolderPath, "customBookStyles.css"),
                ".a { font-family: '" + family + "'; }\n.b { font-family: 'Andika'; }\n"
            );
            var collectionFontsFolder = EmbeddedFonts.GetCollectionFontsFolder(book.FolderPath);
            // Sanity check: nothing is stored before the book is brought up to date.
            Assert.That(Directory.Exists(collectionFontsFolder), Is.False);

            book.BringBookUpToDate(new NullProgress());

            var expectedFile = Path.Combine(
                collectionFontsFolder,
                family + Path.GetExtension(installedGroup.Normal).ToLowerInvariant()
            );
            Assert.That(File.Exists(expectedFile), Is.True, expectedFile + " should exist");
            // The rule points out of the book folder, because the file belongs to the collection.
            var css = File.ReadAllText(Path.Combine(book.FolderPath, "defaultLangStyles.css"));
            Assert.That(
                css,
                Does.Contain("url('../fonts/" + Path.GetFileName(expectedFile) + "')")
            );
            Assert.That(
                Directory.EnumerateFiles(collectionFontsFolder, "Andika*").ToList(),
                Is.Empty,
                "a font Bloom serves must not be stored"
            );
        }
    }
}
