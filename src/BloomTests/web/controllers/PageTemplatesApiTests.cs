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
    }
}
