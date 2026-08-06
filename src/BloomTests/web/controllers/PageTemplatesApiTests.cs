using System.Collections.Generic;
using System.IO;
using Bloom.web.controllers;
using BloomTemp;
using NUnit.Framework;
using SIL.PlatformUtilities;

namespace BloomTests.web.controllers
{
    [TestFixture]
    public class PageTemplatesApiTests
    {
        [Test]
        public void GetBookTemplatePaths_NoOtherTemplates_ReturnsJustSourceTemplate()
        {
            var pathToCurrentTemplateHtml = "c:\\some\\templates\\here\\\\basic book.htm";
            var sourceBookPaths = new[] { "c:\\some\\templates\\here\\\\basic book.htm" };
            var result = PageTemplatesApi.GetBookTemplatePaths(
                pathToCurrentTemplateHtml,
                sourceBookPaths
            );
            Assert.AreEqual(0, result.IndexOf(pathToCurrentTemplateHtml));
            Assert.AreEqual(1, result.Count);
        }

        [Test]
        public void GetBookTemplatePaths_NonBasicBookOriginal_BasicBookOfferedSecond()
        {
            using (var temp = new TemporaryFolder("NonBasicBookOriginal"))
            {
                var original = new TemplateBookTestFolder(temp.FolderPath, "originalTemplate");
                var basic = new TemplateBookTestFolder(temp.FolderPath, "basic book");
                var alphabet = new TemplateBookTestFolder(temp.FolderPath, "alphabet");
                var zebra = new TemplateBookTestFolder(temp.FolderPath, "zebra");
                var pathToCurrentTemplateHtml = original.HtmlPath;
                var pathToBasicBook = basic.HtmlPath;
                var pathToAlphabet = alphabet.HtmlPath;
                var pathToZebra = zebra.HtmlPath;
                var sourceBookPaths = new[]
                {
                    pathToAlphabet,
                    "c:\\installation dir\\templates\\some book that is not a template at all.html",
                    pathToBasicBook,
                    pathToCurrentTemplateHtml,
                    pathToZebra,
                };
                var result = PageTemplatesApi.GetBookTemplatePaths(
                    pathToCurrentTemplateHtml,
                    sourceBookPaths
                );
                Assert.AreEqual(4, result.Count);
                Assert.That(
                    result[0].ToLowerInvariant(),
                    Is.EqualTo(pathToCurrentTemplateHtml.ToLowerInvariant()),
                    "Template used to make the book should be first in the list."
                );
                Assert.That(
                    result[1].ToLowerInvariant(),
                    Is.EqualTo(pathToBasicBook.ToLowerInvariant()),
                    "Basic Book should move ahead of Alphabet to be second in list when it is not first."
                );
                Assert.That(
                    result[2].ToLowerInvariant(),
                    Is.EqualTo(pathToAlphabet.ToLowerInvariant()),
                    "Alphabet should be third."
                );
                Assert.That(
                    result[3].ToLowerInvariant(),
                    Is.EqualTo(pathToZebra.ToLowerInvariant()),
                    "Zebra should be last."
                );
                if (!Platform.IsWindows)
                    Assert.That(
                        result[0],
                        Is.EqualTo(pathToCurrentTemplateHtml),
                        "Should not change case on Linux"
                    );
            }
        }

        /// <summary>
        /// Sets up a folder that Bloom will recognize as a template book folder with the specified name.
        /// </summary>
        class TemplateBookTestFolder
        {
            public string HtmlPath;

            public TemplateBookTestFolder(string rootFolderName, string name)
            {
                var templatePath = Path.Combine(
                    rootFolderName,
                    name,
                    PageTemplatesApi.TemplateFolderName
                );
                Directory.CreateDirectory(templatePath);
                HtmlPath = Path.Combine(rootFolderName, name, name + ".htm");
            }
        }

        [Test]
        public void GetBookTemplatePaths_TemplateInThisCollection_ReturnsTemplate()
        {
            using (var temp = new TemporaryFolder("TemplateInThisCollection_ReturnsTemplate"))
            {
                var current = new TemplateBookTestFolder(temp.FolderPath, "basic book");
                var pathToCurrentTemplateHtml = current.HtmlPath;
                var other = new TemplateBookTestFolder(temp.FolderPath, "my template");
                var sourceBookPaths = new[] { other.HtmlPath };
                var result = PageTemplatesApi.GetBookTemplatePaths(
                    pathToCurrentTemplateHtml,
                    sourceBookPaths
                );
                Assert.That(
                    result[1].ToLowerInvariant(),
                    Is.EqualTo(sourceBookPaths[0].ToLowerInvariant())
                );
            }
        }

        [Test]
        public void GetBookTemplatePaths_TemplateInThisCollectionAndSourceBooks_ReturnsItOnlyOnce()
        {
            using (
                var temp = new TemporaryFolder(
                    "TemplateInThisCollectionAndSourceBooks_ReturnsItOnlyOnce"
                )
            )
            {
                var current = new TemplateBookTestFolder(temp.FolderPath, "basic book");
                var pathToCurrentTemplateHtml = current.HtmlPath;
                var other = new TemplateBookTestFolder(temp.FolderPath, "my template");
                var sourceBookPaths = new[] { other.HtmlPath, other.HtmlPath };
                var result = PageTemplatesApi.GetBookTemplatePaths(
                    pathToCurrentTemplateHtml,
                    sourceBookPaths
                );
                Assert.AreEqual(2, result.Count, "Should only list my template once.");
            }
        }

        [Test]
        public void GetBookTemplatePaths_TwoTemplatesWithSameName_ListsBoth()
        {
            using (var temp = new TemporaryFolder("TwoTemplatesWithSameName_ListsBoth1"))
            using (var temp2 = new TemporaryFolder("TwoTemplatesWithSameName_ListsBoth2"))
            {
                var current = new TemplateBookTestFolder(temp.FolderPath, "basic book");
                var pathToCurrentTemplateHtml = current.HtmlPath;
                var other = new TemplateBookTestFolder(temp.FolderPath, "my template");
                var other2 = new TemplateBookTestFolder(temp2.FolderPath, "my template");
                var sourceBookPaths = new[] { other.HtmlPath, other2.HtmlPath };
                var result = PageTemplatesApi.GetBookTemplatePaths(
                    pathToCurrentTemplateHtml,
                    sourceBookPaths
                );
                Assert.AreEqual(3, result.Count, "Should list each unique path, not name.");
            }
        }

        /// <summary>
        /// A book folder can be deleted while we are part way through scanning its collection --
        /// by a sync client, an antivirus tool, or the user. That must not abort the whole scan.
        /// See BL-16661, where one folder disappearing took out a whole test fixture.
        /// </summary>
        [Test]
        public void GetBooksInCollectionDirectories_FolderDeletedPartWayThrough_SkipsItAndKeepsGoing()
        {
            using (var collection = new TemporaryFolder("FolderDeletedPartWayThrough"))
            {
                // Two books. "a book" is read first; "b book" is the one we will delete after the
                // scan has started but before it reaches that folder.
                var stays = new TemplateBookTestFolder(collection.FolderPath, "a book");
                File.WriteAllText(stays.HtmlPath, "<html></html>");
                var vanishes = new TemplateBookTestFolder(collection.FolderPath, "b book");
                File.WriteAllText(vanishes.HtmlPath, "<html></html>");

                // Sanity check: both books are really there, and findable, before we start.
                Assert.That(File.Exists(stays.HtmlPath), Is.True);
                Assert.That(File.Exists(vanishes.HtmlPath), Is.True);
                Assert.That(
                    Directory.GetDirectories(collection.FolderPath).Length,
                    Is.EqualTo(2),
                    "Setup sanity check: the collection should contain exactly the two book folders."
                );

                var found = new List<string>();
                // The result is lazy, so pulling one item at a time lets us delete the second
                // book folder mid-scan -- exactly the race this guards against.
                using (
                    var scan = PageTemplatesApi
                        .GetBooksInCollectionDirectories(new[] { collection.FolderPath })
                        .GetEnumerator()
                )
                {
                    Assert.That(scan.MoveNext(), Is.True, "Should have found the first book.");
                    found.Add(scan.Current);

                    SIL.IO.RobustIO.DeleteDirectoryAndContents(
                        Path.GetDirectoryName(vanishes.HtmlPath)
                    );
                    Assert.That(
                        Directory.Exists(Path.GetDirectoryName(vanishes.HtmlPath)),
                        Is.False,
                        "Sanity check: the second book folder should now be gone."
                    );

                    // Before the fix this threw, killing the whole scan.
                    while (scan.MoveNext())
                        found.Add(scan.Current);
                }

                Assert.That(
                    found,
                    Does.Contain(stays.HtmlPath),
                    "The book that was still there should have been found."
                );
                Assert.That(
                    found.Count,
                    Is.EqualTo(1),
                    "The deleted folder should have been skipped, not reported as a book."
                );
            }
        }
    }
}
