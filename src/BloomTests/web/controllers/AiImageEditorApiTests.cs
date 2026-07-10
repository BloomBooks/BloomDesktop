using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.web.controllers;
using NUnit.Framework;
using SIL.TestUtilities;

namespace BloomTests.web.controllers
{
    /// <summary>
    /// Tests for the AI Image Editor commit-time cleanup of orphaned generated image files
    /// (see <see cref="AiImageEditorApi.DeleteSupersededAiImageFiles"/>). These exercise the
    /// two testable helpers directly rather than the whole commit flow, which needs a live
    /// server/session.
    /// </summary>
    [TestFixture]
    public class AiImageEditorApiTests
    {
        private TemporaryFolder _bookFolder;

        [SetUp]
        public void Setup()
        {
            _bookFolder = new TemporaryFolder("AiImageEditorApiTests");
        }

        [TearDown]
        public void TearDown()
        {
            _bookFolder.Dispose();
        }

        // Writes a tiny (non-empty) file into the book folder and returns its full path.
        private string MakeFile(string name)
        {
            var path = Path.Combine(_bookFolder.Path, name);
            File.WriteAllText(path, "x");
            return path;
        }

        // A minimal book DOM: a data-div, one content page, and (optionally) extra image
        // markup. The images referenced here are what CollectReferencedImageFileNames should
        // find, and therefore what DeleteSupersededAiImageFiles must refuse to delete.
        private static HtmlDom MakeDom(string pageAndDataDivImages)
        {
            return new HtmlDom(
                @"<html><head></head><body>
                    <div id='bloomDataDiv'></div>
                    <div class='bloom-page' id='page1'><div class='marginBox'>"
                    + pageAndDataDivImages
                    + @"</div></div>
                  </body></html>"
            );
        }

        [Test]
        public void CollectReferencedImageFileNames_FindsImgBackgroundAndDataDivImages()
        {
            var dom = new HtmlDom(
                @"<html><head></head><body>
                    <div id='bloomDataDiv'><div data-book='coverImage'><img src='cover-ai-image.png'/></div></div>
                    <div class='bloom-page' id='page1'><div class='marginBox'>
                        <img src='on-page.png'/>
                        <div class='bloom-backgroundImage' style=""background-image:url('bg.jpg')""></div>
                    </div></div>
                  </body></html>"
            );

            var referenced = AiImageEditorApi.CollectReferencedImageFileNames(dom);

            // Sanity: it found something, so a later "not referenced" result is meaningful.
            Assert.That(referenced, Is.Not.Empty, "setup: DOM should contain image references");
            Assert.That(referenced, Does.Contain("on-page.png"), "img on a page");
            Assert.That(referenced, Does.Contain("bg.jpg"), "background-image url");
            Assert.That(referenced, Does.Contain("cover-ai-image.png"), "img inside the data-div");
            Assert.That(
                referenced,
                Does.Not.Contain("never-referenced.png"),
                "a file no element points at must not appear"
            );
        }

        [Test]
        public void DeleteSupersededAiImageFiles_DeletesOnlyUnreferencedAiImageFiles()
        {
            // Four candidate old files displaced by a commit:
            var orphan = MakeFile("ai-image.png"); // ours, no longer referenced -> delete
            var stillUsed = MakeFile("ai-image1.png"); // ours, another slot still uses it -> keep
            var coverUsed = MakeFile("ai-image2.png"); // ours, referenced from the data-div -> keep
            var userOriginal = MakeFile("photo.jpg"); // not ours (no ai-image prefix) -> keep

            // The DOM still references ai-image1.png (on a page) and ai-image2.png (data-div),
            // but nothing references ai-image.png. photo.jpg is unreferenced too, but it isn't
            // one of our generated files so it must be left alone.
            var dom = new HtmlDom(
                @"<html><head></head><body>
                    <div id='bloomDataDiv'><div data-book='coverImage'><img src='ai-image2.png'/></div></div>
                    <div class='bloom-page' id='page1'><div class='marginBox'><img src='ai-image1.png'/></div></div>
                  </body></html>"
            );

            // Sanity: everything exists before the call.
            Assert.That(File.Exists(orphan), Is.True, "setup");
            Assert.That(File.Exists(stillUsed), Is.True, "setup");
            Assert.That(File.Exists(coverUsed), Is.True, "setup");
            Assert.That(File.Exists(userOriginal), Is.True, "setup");

            AiImageEditorApi.DeleteSupersededAiImageFiles(
                _bookFolder.Path,
                dom,
                new List<string> { "ai-image.png", "ai-image1.png", "ai-image2.png", "photo.jpg" }
            );

            Assert.That(
                File.Exists(orphan),
                Is.False,
                "the unreferenced ai-image file should be deleted"
            );
            Assert.That(
                File.Exists(stillUsed),
                Is.True,
                "an ai-image file another slot still references must be kept"
            );
            Assert.That(
                File.Exists(coverUsed),
                Is.True,
                "an ai-image file the data-div references must be kept"
            );
            Assert.That(
                File.Exists(userOriginal),
                Is.True,
                "a non-ai-image file (user's original) must never be deleted"
            );
        }

        [Test]
        public void DeleteSupersededAiImageFiles_EmptyCandidates_DoesNothing()
        {
            var keep = MakeFile("ai-image.png");
            var dom = MakeDom(""); // references nothing

            AiImageEditorApi.DeleteSupersededAiImageFiles(
                _bookFolder.Path,
                dom,
                Enumerable.Empty<string>()
            );

            Assert.That(
                File.Exists(keep),
                Is.True,
                "with no candidates, no file should be touched even if unreferenced"
            );
        }
    }
}
