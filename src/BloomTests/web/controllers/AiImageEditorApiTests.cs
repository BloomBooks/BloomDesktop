using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.web.controllers;
using NUnit.Framework;
using SIL.Code;
using SIL.IO;
using SIL.TestUtilities;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;

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

        // ------------------------------------------------------------------
        // TryResolveServedUrlToBookFile: the path-traversal guard that stops a
        // reused-image URL from resolving to anything outside the book folder.
        // servedUrl is passed as a plain book-folder path (FromLocalhost leaves a
        // non-server URL unchanged), so these run without a live server.
        // ------------------------------------------------------------------

        // Builds the kind of URL EnumerateBookImages hands the editor: the book folder
        // path plus a relative part, forward-slashed.
        private string BookUrl(string relative) =>
            _bookFolder.Path.Replace('\\', '/') + "/" + relative;

        [Test]
        public void TryResolveServedUrlToBookFile_InBookImage_Resolves()
        {
            var picPath = MakeFile("pic.png");
            // Sanity: the file we expect to resolve to really exists first.
            Assert.That(File.Exists(picPath), Is.True, "setup");

            var ok = AiImageEditorApi.TryResolveServedUrlToBookFile(
                _bookFolder.Path,
                BookUrl("pic.png"),
                out var resolved
            );

            Assert.That(ok, Is.True, "an existing in-book image should resolve");
            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(picPath)));
        }

        [Test]
        public void TryResolveServedUrlToBookFile_NonExistentInBookFile_Fails()
        {
            var ok = AiImageEditorApi.TryResolveServedUrlToBookFile(
                _bookFolder.Path,
                BookUrl("nope.png"),
                out var resolved
            );

            Assert.That(ok, Is.False, "a file that doesn't exist must not resolve");
            Assert.That(resolved, Is.Null);
        }

        [Test]
        public void TryResolveServedUrlToBookFile_DisallowedExtension_Fails()
        {
            MakeFile("note.txt"); // exists, in-book, but not an image type

            var ok = AiImageEditorApi.TryResolveServedUrlToBookFile(
                _bookFolder.Path,
                BookUrl("note.txt"),
                out _
            );

            Assert.That(
                ok,
                Is.False,
                "a non-image file must not resolve even if it exists in-book"
            );
        }

        [Test]
        public void TryResolveServedUrlToBookFile_NonEditableImageFormat_Fails()
        {
            // An svg is a real image, but the AI editor can't edit it, so it must not
            // resolve as a reusable source (see AllowedImageExtensions).
            var svgPath = MakeFile("drawing.svg");
            // Sanity: the file exists, so a False result is due to the format check, not absence.
            Assert.That(File.Exists(svgPath), Is.True, "setup");

            var ok = AiImageEditorApi.TryResolveServedUrlToBookFile(
                _bookFolder.Path,
                BookUrl("drawing.svg"),
                out var resolved
            );

            Assert.That(
                ok,
                Is.False,
                "an svg (a format the editor cannot edit) must not resolve as a reusable source"
            );
            Assert.That(resolved, Is.Null);
        }

        [Test]
        public void TryResolveServedUrlToBookFile_PathTraversalOutsideBook_Fails()
        {
            // A real file just outside the book folder, reached via "..".
            var outsideDir = Directory.GetParent(_bookFolder.Path).FullName;
            var outsidePath = Path.Combine(outsideDir, "outside-secret.png");
            File.WriteAllText(outsidePath, "x");
            try
            {
                // Sanity: the target exists, so a False result is due to the guard, not absence.
                Assert.That(File.Exists(outsidePath), Is.True, "setup");

                var ok = AiImageEditorApi.TryResolveServedUrlToBookFile(
                    _bookFolder.Path,
                    BookUrl("../outside-secret.png"),
                    out var resolved
                );

                Assert.That(ok, Is.False, "a '..' escape out of the book folder must be rejected");
                Assert.That(resolved, Is.Null);
            }
            finally
            {
                File.Delete(outsidePath);
            }
        }

        [Test]
        public void TryResolveServedUrlToBookFile_SiblingFolderSharingNamePrefix_Fails()
        {
            // A sibling folder whose path has the book folder path as a string prefix
            // ("...book" vs "...book-evil"): the separator in the guard must stop this.
            var evilDir = _bookFolder.Path + "-evil";
            Directory.CreateDirectory(evilDir);
            var evilPic = Path.Combine(evilDir, "pic.png");
            File.WriteAllText(evilPic, "x");
            try
            {
                var ok = AiImageEditorApi.TryResolveServedUrlToBookFile(
                    _bookFolder.Path,
                    evilDir.Replace('\\', '/') + "/pic.png",
                    out var resolved
                );

                Assert.That(
                    ok,
                    Is.False,
                    "a sibling folder that merely shares a name prefix must not be treated as in-book"
                );
                Assert.That(resolved, Is.Null);
            }
            finally
            {
                Directory.Delete(evilDir, true);
            }
        }

        // ------------------------------------------------------------------
        // TryParseIncomingId: parsing/validation of the "{pageId}:{ordinal}" slot id
        // that the editor echoes back on commit (and which we interpolate into an XPath).
        // ------------------------------------------------------------------

        [Test]
        public void TryParseIncomingId_WellFormed_ParsesPageIdAndOrdinal()
        {
            var ok = AiImageEditorApi.TryParseIncomingId(
                "page1:12",
                out var pageId,
                out var ordinal
            );

            Assert.That(ok, Is.True);
            Assert.That(pageId, Is.EqualTo("page1"));
            Assert.That(ordinal, Is.EqualTo(12));
        }

        [Test]
        public void TryParseIncomingId_PageIdWithHyphenAndUnderscore_Allowed()
        {
            var ok = AiImageEditorApi.TryParseIncomingId(
                "my-page_2:3",
                out var pageId,
                out var ordinal
            );

            Assert.That(ok, Is.True);
            Assert.That(pageId, Is.EqualTo("my-page_2"));
            Assert.That(ordinal, Is.EqualTo(3));
        }

        [TestCase("", TestName = "TryParseIncomingId_Empty_Fails")]
        [TestCase(null, TestName = "TryParseIncomingId_Null_Fails")]
        [TestCase("page1", TestName = "TryParseIncomingId_NoColon_Fails")]
        [TestCase(":3", TestName = "TryParseIncomingId_LeadingColonNoPageId_Fails")]
        [TestCase("page1:x", TestName = "TryParseIncomingId_NonIntegerOrdinal_Fails")]
        [TestCase("page1:", TestName = "TryParseIncomingId_MissingOrdinal_Fails")]
        [TestCase("bad page:0", TestName = "TryParseIncomingId_PageIdWithSpace_Fails")]
        [TestCase("bad/page:0", TestName = "TryParseIncomingId_PageIdWithSlash_Fails")]
        [TestCase("page:1:2", TestName = "TryParseIncomingId_ColonInsidePageId_Fails")]
        public void TryParseIncomingId_Malformed_Fails(string incomingId)
        {
            var ok = AiImageEditorApi.TryParseIncomingId(
                incomingId,
                out var pageId,
                out var ordinal
            );

            Assert.That(ok, Is.False, $"'{incomingId}' should be rejected");
            Assert.That(pageId, Is.Null, "a rejected id must not yield a page id");
            Assert.That(ordinal, Is.EqualTo(-1), "a rejected id must not yield an ordinal");
        }

        // ------------------------------------------------------------------
        // IsUserChangeableImageElement: branding/license/QR images are off-limits.
        // ------------------------------------------------------------------

        private static Bloom.SafeXml.SafeXmlElement MakeImgWithClass(string className)
        {
            var classAttr = className == null ? "" : $" class='{className}'";
            var dom = new HtmlDom(
                $@"<html><head></head><body>
                    <div class='bloom-page' id='page1'><div class='marginBox'>
                        <img src='pic.png'{classAttr}/>
                    </div></div>
                  </body></html>"
            );
            return (Bloom.SafeXml.SafeXmlElement)dom.RawDom.SelectSingleNode("//img");
        }

        [Test]
        public void IsUserChangeableImageElement_PlainImage_IsChangeable()
        {
            Assert.That(
                AiImageEditorApi.IsUserChangeableImageElement(MakeImgWithClass(null)),
                Is.True
            );
        }

        [TestCase("branding")]
        [TestCase("licenseImage")]
        [TestCase("bloom-qrcode")]
        public void IsUserChangeableImageElement_ProtectedImage_IsNotChangeable(string className)
        {
            Assert.That(
                AiImageEditorApi.IsUserChangeableImageElement(MakeImgWithClass(className)),
                Is.False,
                $"an image with class '{className}' must not be user-changeable"
            );
        }

        [Test]
        public void IsUserChangeableImageElement_ProtectedClassAmongOthers_IsNotChangeable()
        {
            // The class check must find the protected class even when combined with others.
            Assert.That(
                AiImageEditorApi.IsUserChangeableImageElement(
                    MakeImgWithClass("bloom-imageContainer branding")
                ),
                Is.False
            );
        }

        // ------------------------------------------------------------------
        // CarryCreditsToNewImageFile: an AI-generated result file has no metadata of its
        // own, and Bloom rebuilds the data-copyright/creator/license attributes from the
        // file's metadata, so the replaced image's credits must be written into the new
        // file or they are lost on the next sync.
        // ------------------------------------------------------------------

        // Writes a tiny PNG into the book folder with the given embedded credits, and
        // returns its file name (not full path).
        private string MakePngWithCredits(string name, string creator, string copyright)
        {
            var path = Path.Combine(_bookFolder.Path, name);
            using (var bitmap = new Bitmap(10, 10))
            {
                RobustImageIO.SaveImage(bitmap, path, ImageFormat.Png);
            }
            using (var img = PalasoImage.FromFileRobustly(path))
            {
                img.Metadata.Creator = creator;
                img.Metadata.CopyrightNotice = copyright;
                img.Metadata.License = new CreativeCommonsLicense(
                    true,
                    true,
                    CreativeCommonsLicense.DerivativeRules.Derivatives
                );
                RetryUtility.Retry(() => img.SaveUpdatedMetadataIfItMakesSense());
            }
            return name;
        }

        // Writes a tiny PNG with no embedded IP metadata (as an AI-generated result would
        // arrive), and returns its file name.
        private string MakePlainPng(string name)
        {
            var path = Path.Combine(_bookFolder.Path, name);
            using (var bitmap = new Bitmap(10, 10))
            {
                RobustImageIO.SaveImage(bitmap, path, ImageFormat.Png);
            }
            return name;
        }

        [Test]
        public void CarryCreditsToNewImageFile_CopiesReplacedImageCreditsIntoNewFile()
        {
            var oldName = MakePngWithCredits("old.png", "Jane Doe", "Copyright 2020 Jane Doe");
            var newName = MakePlainPng("ai-image1.png");

            // Sanity: the new file starts with no credits, so a non-empty result below is
            // due to the call and not something already in the file.
            var before = Metadata.FromFile(Path.Combine(_bookFolder.Path, newName));
            Assert.That(
                before.Creator,
                Is.Null.Or.Empty,
                "setup: the generated file should start with no creator"
            );

            AiImageEditorApi.CarryCreditsToNewImageFile(_bookFolder.Path, oldName, newName);

            var after = Metadata.FromFile(Path.Combine(_bookFolder.Path, newName));
            Assert.That(after.Creator, Is.EqualTo("Jane Doe"));
            Assert.That(after.CopyrightNotice, Is.EqualTo("Copyright 2020 Jane Doe"));
            Assert.That(
                after.License,
                Is.InstanceOf<CreativeCommonsLicense>(),
                "the license should be carried over too"
            );
        }

        [Test]
        public void CarryCreditsToNewImageFile_ReplacedImageMissing_LeavesNewFileUnchanged()
        {
            var newName = MakePlainPng("ai-image1.png");

            // Must not throw when the replaced image can't be found; the new file keeps
            // whatever (empty) metadata it had.
            Assert.DoesNotThrow(() =>
                AiImageEditorApi.CarryCreditsToNewImageFile(
                    _bookFolder.Path,
                    "does-not-exist.png",
                    newName
                )
            );

            var after = Metadata.FromFile(Path.Combine(_bookFolder.Path, newName));
            Assert.That(after.Creator, Is.Null.Or.Empty);
        }
    }
}
