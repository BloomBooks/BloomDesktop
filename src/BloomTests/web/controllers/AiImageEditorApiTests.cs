using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Bloom;
using Bloom.Book;
using Bloom.ImageProcessing;
using Bloom.web.controllers;
using NUnit.Framework;
using SIL.Code;
using SIL.Core.ClearShare;
using SIL.IO;
using SIL.Progress;
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
        // ImportImageIntoBookFolder: brings a committed image into the book folder with
        // Bloom's normal import processing (BL-16645). The "ai-image" name it produces is
        // load-bearing — DeleteSupersededAiImageFiles reclaims old files by that prefix —
        // and the processing must never be able to lose the image.
        // ------------------------------------------------------------------

        // Writes a PNG of the given size into a "source" folder OUTSIDE the book folder,
        // standing in for the AI image editor's history folder, and returns its full path.
        private string MakeSourcePng(string name, int width, int height)
        {
            var sourceFolder = Path.Combine(_bookFolder.Path, "source");
            Directory.CreateDirectory(sourceFolder);
            var path = Path.Combine(sourceFolder, name);
            using (var bitmap = new Bitmap(width, height))
            {
                RobustImageIO.SaveImage(bitmap, path, ImageFormat.Png);
            }
            return path;
        }

        [Test]
        public void ImportImageIntoBookFolder_NamesTheFileAiImage_NotAfterTheSource()
        {
            // The source is named like an AI history result (an opaque id). The old code
            // reserved an "ai-image*" name; ProcessAndSaveImageIntoFolder would instead name
            // the file after the source, silently taking it out of reach of
            // DeleteSupersededAiImageFiles.
            var source = MakeSourcePng("a1b2c3d4.png", 10, 10);
            Assert.That(File.Exists(source), Is.True, "setup: the source image should exist");

            var newName = AiImageEditorApi.ImportImageIntoBookFolder(source, _bookFolder.Path);

            Assert.That(
                newName,
                Does.StartWith("ai-image"),
                "DeleteSupersededAiImageFiles reclaims our files by this prefix"
            );
            Assert.That(
                newName,
                Does.Not.Contain("a1b2c3d4"),
                "the opaque history id must not become the book's file name"
            );
            Assert.That(
                File.Exists(Path.Combine(_bookFolder.Path, newName)),
                Is.True,
                "the bytes must actually land in the book folder"
            );
        }

        [Test]
        public void ImportImageIntoBookFolder_CalledTwice_ProducesDistinctFiles()
        {
            // Two slots committed in one go must not collapse onto one file name.
            var source = MakeSourcePng("result.png", 10, 10);

            var first = AiImageEditorApi.ImportImageIntoBookFolder(source, _bookFolder.Path);
            var second = AiImageEditorApi.ImportImageIntoBookFolder(source, _bookFolder.Path);

            Assert.That(second, Is.Not.EqualTo(first), "each import needs its own file");
            Assert.That(File.Exists(Path.Combine(_bookFolder.Path, first)), Is.True);
            Assert.That(File.Exists(Path.Combine(_bookFolder.Path, second)), Is.True);
        }

        [Test]
        public void ImportImageIntoBookFolder_OversizedImage_IsDownscaled()
        {
            // The point of BL-16645: an AI service can hand back a huge PNG, and copying it
            // verbatim bloats the book folder for every sync, upload and backup.
            var source = MakeSourcePng("huge.png", 5000, 4000);
            using (var before = Image.FromFile(source))
            {
                Assert.That(
                    before.Width,
                    Is.EqualTo(5000),
                    "setup: the source really is oversized"
                );
            }

            var newName = AiImageEditorApi.ImportImageIntoBookFolder(source, _bookFolder.Path);

            using (var after = Image.FromFile(Path.Combine(_bookFolder.Path, newName)))
            {
                Assert.That(
                    after.Width,
                    Is.LessThan(5000),
                    "an oversized image should have been downscaled on the way in"
                );
            }
        }

        [Test]
        public void ImportImageIntoBookFolder_OversizedCreditedImage_KeepsItsCredits()
        {
            // An uploaded result big enough to need resizing is rewritten by GraphicsMagick on the
            // way in, and that drops the iTXt/XMP chunk libpalaso keeps IP metadata in (measured:
            // a plain gm resize loses it, and asking it not to strip profiles doesn't help). The
            // ordinary import path is safe because PageEditingModel.ChangePicture re-writes the
            // metadata straight after ProcessAndSaveImageIntoFolder; this path has to do the same,
            // or an uploaded photo quietly loses its copyright (BL-16645).
            var source = MakeSourcePng("credited-huge.png", 4200, 1000);
            using (var img = PalasoImage.FromFileRobustly(source))
            {
                img.Metadata.Creator = "Jane Doe";
                img.Metadata.CopyrightNotice = "Copyright 2020 Jane Doe";
                img.Metadata.License = new CreativeCommonsLicense(
                    true,
                    true,
                    CreativeCommonsLicense.DerivativeRules.Derivatives
                );
                RetryUtility.Retry(() => img.SaveUpdatedMetadataIfItMakesSense());
            }
            // Sanity: the credits really are in the source, so a match below isn't two blanks
            // agreeing with each other.
            var before = Metadata.FromFile(source);
            Assert.That(before.Creator, Is.EqualTo("Jane Doe"), "setup");
            Assert.That(before.CopyrightNotice, Is.EqualTo("Copyright 2020 Jane Doe"), "setup");

            var newName = AiImageEditorApi.ImportImageIntoBookFolder(source, _bookFolder.Path);

            var newPath = Path.Combine(_bookFolder.Path, newName);
            using (var after = Image.FromFile(newPath))
            {
                // Sanity: it really took the resize path, which is the one that loses metadata.
                Assert.That(
                    after.Width,
                    Is.LessThan(4200),
                    "setup: this image should have been downscaled on the way in"
                );
            }
            var kept = Metadata.FromFile(newPath);
            Assert.That(
                kept.Creator,
                Is.EqualTo("Jane Doe"),
                "the creator must survive the import resize"
            );
            Assert.That(
                kept.CopyrightNotice,
                Is.EqualTo("Copyright 2020 Jane Doe"),
                "the copyright must survive the import resize"
            );
            Assert.That(
                kept.License,
                Is.Not.Null.And.Not.InstanceOf<NullLicense>(),
                "the licence must survive the import resize too"
            );
        }

        [Test]
        public void ImportImageIntoBookFolder_ReusedImage_IsNotResized()
        {
            // A reused book image was already import-processed on its own way in, so we
            // deliberately don't resize it again: it would gain nothing and would cost the image
            // a generation of quality. (Credits used to be part of that reasoning; they no longer
            // are, now that the import re-attaches metadata whichever path it took.)
            var source = MakeSourcePng("already-in-book.png", 5000, 4000);

            var newName = AiImageEditorApi.ImportImageIntoBookFolder(
                source,
                _bookFolder.Path,
                resizeIfNeeded: false
            );

            using (var after = Image.FromFile(Path.Combine(_bookFolder.Path, newName)))
            {
                Assert.That(
                    after.Width,
                    Is.EqualTo(5000),
                    "a reused image must come through at its original size"
                );
                Assert.That(after.Height, Is.EqualTo(4000));
            }
        }

        [Test]
        public void ImportImageIntoBookFolder_ReusedImage_KeepsItsCredits()
        {
            // The credits live inside the image file. Nothing re-writes them on the reuse
            // path, so whatever import processing does to the file has to leave them alone.
            var name = MakePngWithCredits("reused.png", "Jane Doe", "Copyright 2020 Jane Doe");
            var source = Path.Combine(_bookFolder.Path, name);

            // Sanity: the credits really are in the source file, so a match below is not
            // just two empty values agreeing.
            var before = Metadata.FromFile(source);
            Assert.That(before.Creator, Is.EqualTo("Jane Doe"), "setup");

            var newName = AiImageEditorApi.ImportImageIntoBookFolder(
                source,
                _bookFolder.Path,
                resizeIfNeeded: false
            );

            var after = Metadata.FromFile(Path.Combine(_bookFolder.Path, newName));
            Assert.That(after.Creator, Is.EqualTo("Jane Doe"));
            Assert.That(after.CopyrightNotice, Is.EqualTo("Copyright 2020 Jane Doe"));
        }

        [Test]
        public void ImportImageIntoBookFolder_UnprocessableFormat_IsCopiedVerbatim()
        {
            // PalasoImage decodes through GDI+, which has no WebP codec. We must still get
            // the user's image into the book (unprocessed but intact), under our own name.
            var sourceFolder = Path.Combine(_bookFolder.Path, "source");
            Directory.CreateDirectory(sourceFolder);
            var source = Path.Combine(sourceFolder, "generated.webp");
            var bytes = new byte[] { 1, 2, 3, 4, 5 };
            File.WriteAllBytes(source, bytes);

            var newName = AiImageEditorApi.ImportImageIntoBookFolder(source, _bookFolder.Path);

            Assert.That(newName, Does.StartWith("ai-image"));
            Assert.That(
                Path.GetExtension(newName),
                Is.EqualTo(".webp"),
                "a format we can't process keeps its own extension"
            );
            Assert.That(
                File.ReadAllBytes(Path.Combine(_bookFolder.Path, newName)),
                Is.EqualTo(bytes),
                "the bytes must survive unchanged"
            );
        }

        [Test]
        public void ImportImageIntoBookFolder_CorruptImage_StillLandsInTheBookFolder()
        {
            // Processing must never be able to lose the image: a .png that isn't really a PNG
            // throws inside PalasoImage, and we fall back to the plain copy.
            var sourceFolder = Path.Combine(_bookFolder.Path, "source");
            Directory.CreateDirectory(sourceFolder);
            var source = Path.Combine(sourceFolder, "broken.png");
            File.WriteAllText(source, "this is not a png");

            var newName = AiImageEditorApi.ImportImageIntoBookFolder(source, _bookFolder.Path);

            Assert.That(newName, Does.StartWith("ai-image"));
            Assert.That(
                File.Exists(Path.Combine(_bookFolder.Path, newName)),
                Is.True,
                "a corrupt image must still be copied rather than silently dropped"
            );
        }

        // ------------------------------------------------------------------
        // ConvertPngToJpegIfItBloatsTheJpegItReplaces: an AI result often comes back as a PNG
        // for a slot the book held as a JPEG, and a photographic PNG can be several times the
        // size (BL-16645). man.png and man.jpg are the same photo in both formats, so together
        // they stand in for exactly that situation.
        // ------------------------------------------------------------------

        private const string _pathToTestImages = "src/BloomTests/ImageProcessing/images";

        // Copies one of the images distributed with the application into the book folder under
        // the given name, and returns that name (which is what the API deals in).
        private string CopyTestImageIntoBookFolder(string distributedName, string nameInBook)
        {
            var source = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                distributedName
            );
            RobustFile.Copy(source, Path.Combine(_bookFolder.Path, nameInBook), true);
            return nameInBook;
        }

        private long LengthInBookFolder(string name)
        {
            return new FileInfo(Path.Combine(_bookFolder.Path, name)).Length;
        }

        // A real (header-sniffable) but tiny JPEG in the book folder, for cases that need the
        // superseded file to be small enough to make the new PNG look bloated.
        private string MakeSmallJpegInBookFolder(string name)
        {
            var path = Path.Combine(_bookFolder.Path, name);
            using (var bitmap = new Bitmap(4, 4))
            {
                RobustImageIO.SaveImage(bitmap, path, ImageFormat.Jpeg);
            }
            Assert.That(
                ImageUtils.IsJpegFile(path),
                Is.True,
                "setup: the stand-in old file must sniff as a real JPEG"
            );
            return name;
        }

        [Test]
        public void ConvertPngToJpeg_BigPngReplacingSmallJpeg_KeepsTheJpegAndDeletesThePng()
        {
            var oldSrc = CopyTestImageIntoBookFolder("man.jpg", "old-photo.jpg");
            var newName = CopyTestImageIntoBookFolder("man.png", "ai-image1.png");
            var pngLength = LengthInBookFolder(newName);
            // Sanity: this really is the blow-up case, or the method would rightly do nothing
            // and the assertions below would pass for the wrong reason.
            Assert.That(
                pngLength,
                Is.GreaterThan(1.5 * LengthInBookFolder(oldSrc)),
                "setup: the new PNG must be the bloated one"
            );

            var result = AiImageEditorApi.ConvertPngToJpegIfItBloatsTheJpegItReplaces(
                _bookFolder.Path,
                oldSrc,
                newName
            );

            Assert.That(
                Path.GetExtension(result),
                Is.EqualTo(".jpg"),
                "a photo PNG this much bigger than the JPEG it replaces should be re-encoded"
            );
            Assert.That(
                result,
                Does.StartWith("ai-image"),
                "DeleteSupersededAiImageFiles reclaims our files by this prefix"
            );
            Assert.That(
                File.Exists(Path.Combine(_bookFolder.Path, result)),
                Is.True,
                "the file we name has to exist"
            );
            Assert.That(
                LengthInBookFolder(result),
                Is.LessThan(pngLength),
                "the whole point is a smaller file"
            );
            Assert.That(
                File.Exists(Path.Combine(_bookFolder.Path, newName)),
                Is.False,
                "leaving the PNG behind would keep exactly the bulk we converted away from"
            );
        }

        [Test]
        public void ConvertPngToJpeg_JpegOfTheSameBaseNameExists_DoesNotOverwriteIt()
        {
            // ImportImageIntoBookFolder only reserved "ai-image1.png", and GetUnusedFilename
            // checks just that one name — so "ai-image1.jpg" can be another slot's live image,
            // and writing the conversion over it would destroy that image.
            var oldSrc = CopyTestImageIntoBookFolder("man.jpg", "old-photo.jpg");
            var newName = CopyTestImageIntoBookFolder("man.png", "ai-image1.png");
            var otherSlotsImage = Path.Combine(_bookFolder.Path, "ai-image1.jpg");
            File.WriteAllText(otherSlotsImage, "another slot's image");

            var result = AiImageEditorApi.ConvertPngToJpegIfItBloatsTheJpegItReplaces(
                _bookFolder.Path,
                oldSrc,
                newName
            );

            Assert.That(
                Path.GetExtension(result),
                Is.EqualTo(".jpg"),
                "setup: this case should still convert, or it proves nothing about clobbering"
            );
            Assert.That(
                result,
                Is.Not.EqualTo("ai-image1.jpg"),
                "the conversion must claim a name of its own"
            );
            Assert.That(
                File.ReadAllText(otherSlotsImage),
                Is.EqualTo("another slot's image"),
                "the other slot's image must come through untouched"
            );
        }

        [Test]
        public void ConvertPngToJpeg_ReplacingAPng_LeavesTheNewPngAlone()
        {
            // Nothing to gain: the book was already paying PNG prices for this slot, and
            // re-encoding would silently cost quality.
            var oldSrc = CopyTestImageIntoBookFolder("bird.png", "old-art.png");
            var newName = CopyTestImageIntoBookFolder("man.png", "ai-image1.png");

            var result = AiImageEditorApi.ConvertPngToJpegIfItBloatsTheJpegItReplaces(
                _bookFolder.Path,
                oldSrc,
                newName
            );

            Assert.That(result, Is.EqualTo(newName), "only a superseded JPEG justifies converting");
            Assert.That(File.Exists(Path.Combine(_bookFolder.Path, newName)), Is.True);
        }

        [Test]
        public void ConvertPngToJpeg_PngNoBiggerThanTheJpegItReplaces_LeavesItAlone()
        {
            // LakePendOreille.jpg is far bigger than man.png, so there is no blow-up to undo
            // and the lossy re-encode would buy the book nothing.
            var oldSrc = CopyTestImageIntoBookFolder("LakePendOreille.jpg", "old-photo.jpg");
            var newName = CopyTestImageIntoBookFolder("man.png", "ai-image1.png");
            Assert.That(
                LengthInBookFolder(newName),
                Is.LessThan(LengthInBookFolder(oldSrc)),
                "setup: the new PNG is not the bloated one in this case"
            );

            var result = AiImageEditorApi.ConvertPngToJpegIfItBloatsTheJpegItReplaces(
                _bookFolder.Path,
                oldSrc,
                newName
            );

            Assert.That(
                result,
                Is.EqualTo(newName),
                "no conversion when the PNG isn't the problem"
            );
            Assert.That(File.Exists(Path.Combine(_bookFolder.Path, newName)), Is.True);
        }

        [Test]
        public void ConvertPngToJpeg_ConversionDeclined_LeavesNoStrayJpegInTheBookFolder()
        {
            // bird.png is line art: converting it gains little, so the conversion is declined
            // after a name has already been reserved and GraphicsMagick has run. Whatever the
            // reason for declining, nothing unreferenced may be left behind — a stray file
            // would sit in the very folder this method exists to keep small.
            var oldSrc = MakeSmallJpegInBookFolder("old-tiny.jpg");
            var newName = CopyTestImageIntoBookFolder("bird.png", "ai-image1.png");
            // Sanity: the ratio gate must pass, so we really do get as far as converting.
            Assert.That(
                LengthInBookFolder(newName),
                Is.GreaterThan(1.5 * LengthInBookFolder(oldSrc)),
                "setup: big enough to be a conversion candidate"
            );

            var result = AiImageEditorApi.ConvertPngToJpegIfItBloatsTheJpegItReplaces(
                _bookFolder.Path,
                oldSrc,
                newName
            );

            Assert.That(result, Is.EqualTo(newName), "line art is not worth re-encoding");
            Assert.That(
                Directory.GetFiles(_bookFolder.Path, "ai-image*.jpg"),
                Is.Empty,
                "a declined conversion must not leave an unreferenced file in the book folder"
            );
        }

        [Test]
        public void ConvertPngToJpeg_TransparentPng_IsLeftAloneEvenThoughItIsBigger()
        {
            // A JPEG has no alpha channel, so converting would flatten the see-through areas
            // onto a solid background — and since we delete the PNG on success, the
            // transparency would be gone for good. Size is no excuse for that.
            var oldSrc = CopyTestImageIntoBookFolder("man.jpg", "old-photo.jpg");
            var newName = CopyTestImageIntoBookFolder(
                "shirtWithTransparentBg.png",
                "ai-image1.png"
            );
            // Sanity: this is a big PNG that really is transparent, so it would otherwise be
            // converted and the assertion below would be testing nothing.
            Assert.That(
                LengthInBookFolder(newName),
                Is.GreaterThan(1.5 * LengthInBookFolder(oldSrc)),
                "setup: big enough that only the transparency check can stop the conversion"
            );
            using (var image = Image.FromFile(Path.Combine(_bookFolder.Path, newName)))
            {
                Assert.That(
                    ImageUtils.HasTransparency(image),
                    Is.True,
                    "setup: the test image must actually have transparency"
                );
            }

            var result = AiImageEditorApi.ConvertPngToJpegIfItBloatsTheJpegItReplaces(
                _bookFolder.Path,
                oldSrc,
                newName
            );

            Assert.That(
                result,
                Is.EqualTo(newName),
                "a transparent PNG must be kept as a PNG however big it is"
            );
            Assert.That(
                File.Exists(Path.Combine(_bookFolder.Path, newName)),
                Is.True,
                "and the transparent file itself must survive"
            );
        }

        [Test]
        public void ConvertPngToJpeg_Converted_CarriesTheCreditsOntoTheJpeg()
        {
            // GraphicsMagick does not preserve credits when it rewrites a PNG as a JPEG, and an
            // uploaded result can arrive with the user's own credits embedded in it. Nothing
            // downstream would put them back — EmbedCreditsInNewImageFile writes only the credits
            // the AI image editor explicitly sent — so losing them here would quietly strip a
            // photographer's copyright.
            var oldSrc = CopyTestImageIntoBookFolder("man.jpg", "old-photo.jpg");
            var newName = CopyTestImageIntoBookFolder("man.png", "ai-image1.png");
            var newPath = Path.Combine(_bookFolder.Path, newName);
            using (var img = PalasoImage.FromFileRobustly(newPath))
            {
                img.Metadata.Creator = "Jane Doe";
                img.Metadata.CopyrightNotice = "Copyright 2020 Jane Doe";
                img.Metadata.License = new CreativeCommonsLicense(
                    true,
                    true,
                    CreativeCommonsLicense.DerivativeRules.Derivatives
                );
                RetryUtility.Retry(() => img.SaveUpdatedMetadataIfItMakesSense());
            }
            // Sanity: the credits really are in the PNG, so a match below isn't two blanks agreeing.
            var before = Metadata.FromFile(newPath);
            Assert.That(before.Creator, Is.EqualTo("Jane Doe"), "setup");
            Assert.That(before.CopyrightNotice, Is.EqualTo("Copyright 2020 Jane Doe"), "setup");

            var result = AiImageEditorApi.ConvertPngToJpegIfItBloatsTheJpegItReplaces(
                _bookFolder.Path,
                oldSrc,
                newName
            );

            // Sanity: it really did convert, or there would be no re-encode to lose anything.
            Assert.That(
                Path.GetExtension(result),
                Is.EqualTo(".jpg"),
                "setup: this case must convert for the test to mean anything"
            );
            var after = Metadata.FromFile(Path.Combine(_bookFolder.Path, result));
            Assert.That(
                after.Creator,
                Is.EqualTo("Jane Doe"),
                "the creator must survive the re-encode"
            );
            Assert.That(
                after.CopyrightNotice,
                Is.EqualTo("Copyright 2020 Jane Doe"),
                "the copyright must survive the re-encode"
            );
            Assert.That(
                after.License?.Token,
                Is.EqualTo("cc-by"),
                "and so must the licence, which otherwise degrades to 'ask permission'"
            );
        }

        [Test]
        public void ConvertPngToJpeg_PngTransparentOnlyInTheMiddle_IsLeftAlone()
        {
            // The case the corner-only check used to miss (BL-16645): a subject knocked out of an
            // otherwise solid canvas, so the corners are opaque but the middle is see-through.
            // Converting it would flatten the cutout and delete the original.
            var oldSrc = MakeSmallJpegInBookFolder("old-tiny.jpg");
            var newName = "ai-image1.png";
            var newPath = Path.Combine(_bookFolder.Path, newName);
            using (var bitmap = new Bitmap(300, 300, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < 300; ++y)
                for (int x = 0; x < 300; ++x)
                {
                    var inBorder = x < 30 || y < 30 || x >= 270 || y >= 270;
                    // Varied border colors, so the PNG doesn't compress down below the size gate.
                    bitmap.SetPixel(
                        x,
                        y,
                        inBorder
                            ? Color.FromArgb(255, (x * 7) % 256, (y * 13) % 256, (x + y) % 256)
                            : Color.FromArgb(0, 0, 0, 0)
                    );
                }
                RobustImageIO.SaveImage(bitmap, newPath, ImageFormat.Png);
            }
            // Sanity checks, so this can't pass for the wrong reason: it sniffs as a PNG, it is
            // big enough to be a conversion candidate, and the transparency really is invisible to
            // the corner scan yet visible to HasTransparency.
            Assert.That(ImageUtils.IsPngFile(newPath), Is.True, "setup");
            Assert.That(
                LengthInBookFolder(newName),
                Is.GreaterThan(1.5 * LengthInBookFolder(oldSrc)),
                "setup: big enough that only the transparency check can stop the conversion"
            );
            using (var check = Image.FromFile(newPath))
            {
                Assert.That(
                    ((Bitmap)check).GetPixel(5, 5).A,
                    Is.EqualTo(255),
                    "setup: the corner must be opaque, or the old corner-only check would have caught it"
                );
                Assert.That(
                    ImageUtils.HasTransparency(check),
                    Is.True,
                    "setup: the scattered sampling must see the interior cutout"
                );
            }

            var result = AiImageEditorApi.ConvertPngToJpegIfItBloatsTheJpegItReplaces(
                _bookFolder.Path,
                oldSrc,
                newName
            );

            Assert.That(
                result,
                Is.EqualTo(newName),
                "a picture see-through only in its middle must still be kept as a PNG"
            );
            Assert.That(File.Exists(newPath), Is.True, "and the original must survive");
            Assert.That(
                Directory.GetFiles(_bookFolder.Path, "ai-image*.jpg"),
                Is.Empty,
                "no JPEG should have been produced at all"
            );
        }

        [Test]
        public void ConvertPngToJpeg_UndecodableFile_LeavesItAloneRatherThanThrowing()
        {
            // ImportImageIntoBookFolder copies a file it can't process in verbatim, without
            // ever decoding it, so a file with a valid PNG header but junk content really can
            // reach us. Throwing here would abort the whole commit — every replacement in it —
            // over a missed size saving.
            var oldSrc = CopyTestImageIntoBookFolder("man.jpg", "old-photo.jpg");
            var newName = "ai-image1.png";
            var newPath = Path.Combine(_bookFolder.Path, newName);
            var junk = new byte[20000];
            new byte[] { 137, 80, 78, 71 }.CopyTo(junk, 0); // a PNG header, then nothing valid
            File.WriteAllBytes(newPath, junk);
            // Sanity: it sniffs as a PNG and is big enough, so we really do reach the decode.
            Assert.That(ImageUtils.IsPngFile(newPath), Is.True, "setup");
            Assert.That(
                LengthInBookFolder(newName),
                Is.GreaterThan(1.5 * LengthInBookFolder(oldSrc)),
                "setup: big enough to be a conversion candidate"
            );

            var result = AiImageEditorApi.ConvertPngToJpegIfItBloatsTheJpegItReplaces(
                _bookFolder.Path,
                oldSrc,
                newName
            );

            Assert.That(result, Is.EqualTo(newName), "an undecodable file is simply not optimized");
            Assert.That(
                File.Exists(newPath),
                Is.True,
                "and it must still be there for the page to point at"
            );
        }

        [Test]
        public void ConvertPngToJpeg_NoOldFile_LeavesTheNewPngAlone()
        {
            // A slot can point at a file that isn't there (or at nothing at all); with nothing
            // to compare against we must still not lose the image we just imported.
            var newName = CopyTestImageIntoBookFolder("man.png", "ai-image1.png");

            foreach (var missingOldSrc in new[] { "no-such-file.jpg", "", null })
            {
                var result = AiImageEditorApi.ConvertPngToJpegIfItBloatsTheJpegItReplaces(
                    _bookFolder.Path,
                    missingOldSrc,
                    newName
                );

                Assert.That(
                    result,
                    Is.EqualTo(newName),
                    $"oldSrc '{missingOldSrc}' gives us nothing to compare against"
                );
                Assert.That(File.Exists(Path.Combine(_bookFolder.Path, newName)), Is.True);
            }
        }

        // ------------------------------------------------------------------
        // WriteRequestBodyToFile: the /file POST endpoint's save. Two saves of the SAME file
        // at once are ordinary traffic from the AI image editor (BL-16702) — one generated
        // image assigned to two book-image slots makes its commit call putFile once per slot,
        // concurrently — so they must not be able to trip over each other's temp file.
        // ------------------------------------------------------------------

        // The path a history image would be written to, under a fresh .ai-image-editor folder
        // that does not exist yet (so these also cover creating it).
        private string HistoryFilePath(string fileName) =>
            Path.Combine(_bookFolder.Path, ".ai-image-editor", "history", fileName);

        [Test]
        public void WriteRequestBodyToFile_WritesTheBodyAndCreatesTheFolder()
        {
            var path = HistoryFilePath("result.png");
            // Sanity: neither the file nor its folder exists yet, so what we find afterwards
            // was made by the call.
            Assert.That(Directory.Exists(Path.GetDirectoryName(path)), Is.False, "setup");
            var bytes = new byte[] { 1, 2, 3, 4, 5 };

            AiImageEditorApi.WriteRequestBodyToFile(path, new MemoryStream(bytes));

            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
        }

        [Test]
        public void WriteRequestBodyToFile_NoBody_SavesAnEmptyFile()
        {
            // An empty body means "save an empty file", not "leave what's there": the AI image
            // editor must never be told "saved" while stale content survives on disk.
            var path = HistoryFilePath("result.png");
            AiImageEditorApi.WriteRequestBodyToFile(path, new MemoryStream(new byte[] { 1, 2 }));
            Assert.That(new FileInfo(path).Length, Is.EqualTo(2), "setup: the file has content");

            AiImageEditorApi.WriteRequestBodyToFile(path, null);

            Assert.That(new FileInfo(path).Length, Is.EqualTo(0));
        }

        // A body that hands over its first chunk and then fails, standing in for a client that
        // goes away mid-upload. It lets one read through so bytes really do land in the temp
        // file — Stream.CopyTo uses an 80KB buffer, so a body that dies on its first read would
        // leave the temp empty and the test would not be exercising a truncated write at all.
        private class FailingStream : MemoryStream
        {
            public FailingStream(byte[] initialBytes)
                : base(initialBytes) { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (Position > 0)
                    throw new IOException("the client went away");
                return base.Read(buffer, offset, count);
            }
        }

        [Test]
        public void WriteRequestBodyToFile_WriteFails_KeepsTheOldFileAndLeavesNoTempBehind()
        {
            // Writing through a temp file exists so a half-finished upload can't destroy the
            // good file that was there. The temp itself must not survive either: nothing would
            // serve or enumerate it, but it can be multi-MB and would sit in the book's folder
            // for the life of the book.
            var path = HistoryFilePath("result.png");
            var good = new byte[] { 1, 2, 3 };
            AiImageEditorApi.WriteRequestBodyToFile(path, new MemoryStream(good));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(good), "setup: the good file is there");

            // Bigger than CopyTo's 80KB buffer, so the write really is truncated part way
            // rather than failing before it wrote anything.
            Assert.That(
                () =>
                    AiImageEditorApi.WriteRequestBodyToFile(
                        path,
                        new FailingStream(new byte[200000])
                    ),
                Throws.InstanceOf<IOException>(),
                "a failed body read must still fail the request"
            );

            Assert.That(
                File.ReadAllBytes(path),
                Is.EqualTo(good),
                "the file that was there must be untouched"
            );
            Assert.That(
                Directory.EnumerateFiles(Path.GetDirectoryName(path), "*.tmp"),
                Is.Empty,
                "the failed write's temp file must have been cleaned up"
            );
        }

        [Test]
        public void WriteRequestBodyToFile_SwapFails_KeepsTheTempSoTheNewBytesSurvive()
        {
            // The one failure we must NOT tidy up after. Swapping the new file in is a delete
            // of the destination followed by a move, so a failure in there can already have
            // taken the old file with it, leaving the temp holding the only copy of the new
            // bytes. Deleting it then would lose both. (Devin raised this against the first
            // version of the cleanup, which deleted the temp on any failure.)
            var path = HistoryFilePath("result.png");
            AiImageEditorApi.WriteRequestBodyToFile(path, new MemoryStream(new byte[] { 1, 2, 3 }));
            var newBytes = new byte[] { 9, 9, 9, 9 };
            var historyFolder = Path.GetDirectoryName(path);

            // Holding the destination open blocks the delete half of the swap, so the write
            // gets as far as the swap and then fails — which is the case we care about.
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.That(
                    () => AiImageEditorApi.WriteRequestBodyToFile(path, new MemoryStream(newBytes)),
                    Throws.InstanceOf<IOException>(),
                    "setup: holding the file open should make the swap fail"
                );
            }

            var temps = Directory.EnumerateFiles(historyFolder, "*.tmp").ToList();
            Assert.That(
                temps,
                Is.Not.Empty,
                "a failed swap must leave the new bytes behind in the temp file"
            );
            Assert.That(
                File.ReadAllBytes(temps[0]),
                Is.EqualTo(newBytes),
                "and they must be all of the new bytes"
            );
        }

        [Test]
        public void WriteRequestBodyToFile_ConcurrentWritesOfTheSameFile_AllSucceed()
        {
            // BL-16702: the AI image editor POSTs history/<resultId>.png once per slot the
            // result was assigned to, all at the same time. Before this was serialized, the
            // writes collided on the shared "<file>.tmp" path (RobustFile.Create opens with
            // FileShare.None and does not retry), so all but one threw IOException — which the
            // API layer turns into a 503, and which made the AI image editor abandon the whole
            // commit before it ever asked Bloom to replace anything.
            var path = HistoryFilePath("result.png");
            // Big enough that the writes really do overlap rather than finishing one after
            // another, like the multi-MB PNG an AI service returns.
            var bytes = new byte[4 * 1024 * 1024];
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)i;

            const int writerCount = 4;
            var exceptions = new ConcurrentBag<Exception>();
            // The barrier is what makes this a concurrency test: every writer waits there and
            // they are all released together, rather than each starting whenever its thread
            // happens to get going — which could otherwise leave them running one after another,
            // passing without testing anything (see AGENTS.md on falsely passing tests).
            var startLine = new Barrier(writerCount);
            var arrived = new ConcurrentBag<int>();
            var writers = Enumerable
                .Range(0, writerCount)
                .Select(index => new Thread(() =>
                {
                    startLine.SignalAndWait();
                    arrived.Add(index);
                    try
                    {
                        AiImageEditorApi.WriteRequestBodyToFile(path, new MemoryStream(bytes));
                    }
                    catch (Exception e)
                    {
                        exceptions.Add(e);
                    }
                }))
                .ToList();
            writers.ForEach(t => t.Start());
            writers.ForEach(t => t.Join());

            Assert.That(
                exceptions,
                Is.Empty,
                "no concurrent write of the same file should fail: "
                    + string.Join("; ", exceptions.Select(e => e.Message))
            );
            // Sanity check: every writer got past the barrier, so all four really were in
            // flight together and the empty `exceptions` above means something.
            Assert.That(
                arrived,
                Is.EquivalentTo(Enumerable.Range(0, writerCount)),
                "every writer should have been released from the barrier and run"
            );
            // And the survivor must be the whole file, not a partly-written one.
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
            Assert.That(
                Directory.EnumerateFiles(Path.GetDirectoryName(path), "*.tmp"),
                Is.Empty,
                "no temp file should be left behind"
            );
        }

        // ------------------------------------------------------------------
        // TryResolveServedUrlToBookFile: the path-traversal guard that stops a
        // reused-image URL from resolving to anything outside the book folder.
        // servedUrl is passed as a plain book-folder path (FromLocalhost leaves a
        // non-server URL unchanged), so these run without a live server.
        // ------------------------------------------------------------------

        // Builds the kind of URL EnumerateBookImages hands the AI image editor: the book folder
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
            // An svg is a real image, but the AI image editor can't edit it, so it must not
            // resolve as a reusable source (see AllowedImageExtensions).
            var svgPath = MakeFile("drawing.svg");
            // Sanity: the file exists, so a False result is due to the format check, not
            // absence.
            Assert.That(File.Exists(svgPath), Is.True, "setup");

            var ok = AiImageEditorApi.TryResolveServedUrlToBookFile(
                _bookFolder.Path,
                BookUrl("drawing.svg"),
                out var resolved
            );

            Assert.That(
                ok,
                Is.False,
                "an svg (a format the AI image editor cannot edit) must not resolve as a source"
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
                // Sanity: the target exists, so a False result is due to the guard, not
                // absence.
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
        // TryParseIncomingId: parsing/validation of the "{pageId}:{ordinal}" slot id that the
        // AI image editor echoes back on commit (and which we interpolate into an XPath).
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
        // EmbedCreditsInNewImageFile: an AI-generated result file has no metadata of its own,
        // and Bloom rebuilds the data-copyright/creator/license attributes from the file's
        // metadata, so whatever credits the result should have must be written into the new
        // file or they are lost on the next sync. The AI image editor decides what those
        // credits are; when it sends none, the result gets none — Bloom must never reach for
        // the replaced image's credits, because the user may have made an entirely new image.
        // ------------------------------------------------------------------

        // Writes a tiny PNG into the book folder with the given embedded credits, and
        // returns its file name (not full path). When licenseNotes is supplied it becomes the
        // rights statement alongside the CC license — the free-text "license notes" a user can
        // add to a Creative Commons license to spell out extra restrictions.
        private string MakePngWithCredits(
            string name,
            string creator,
            string copyright,
            string licenseNotes = null
        )
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
                if (licenseNotes != null)
                    img.Metadata.License.RightsStatement = licenseNotes;
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
        public void EmbedCreditsInNewImageFile_WritesTheSuppliedCreditsIntoTheFile()
        {
            var newName = MakePlainPng("ai-image1.png");

            // Sanity: the new file starts with no credits, so a non-empty result below is
            // due to the call and not something already in the file.
            var before = Metadata.FromFile(Path.Combine(_bookFolder.Path, newName));
            Assert.That(
                before.Creator,
                Is.Null.Or.Empty,
                "setup: the generated file should start with no creator"
            );

            var credits = new AiImageEditorApi.ImageCredits
            {
                creator = "Jane Doe",
                copyrightNotice = "Copyright 2020 Jane Doe",
                licenseUrl = "http://creativecommons.org/licenses/by/4.0/",
            };

            AiImageEditorApi.EmbedCreditsInNewImageFile(_bookFolder.Path, newName, credits);

            var after = Metadata.FromFile(Path.Combine(_bookFolder.Path, newName));
            Assert.That(after.Creator, Is.EqualTo("Jane Doe"));
            Assert.That(after.CopyrightNotice, Is.EqualTo("Copyright 2020 Jane Doe"));
            Assert.That(
                after.License,
                Is.InstanceOf<CreativeCommonsLicense>(),
                "a creativecommons.org URL should reconstruct a CreativeCommonsLicense"
            );
            Assert.That(after.License.Url, Does.Contain("creativecommons.org/licenses/by"));
        }

        [Test]
        public void EmbedCreditsInNewImageFile_CustomLicense_PreservesRightsStatement()
        {
            // A rights statement with no CC URL must survive as a CustomLicense — losing it
            // would be exactly the kind of silent credit loss this whole change guards against.
            var newName = MakePlainPng("ai-image1.png");
            var credits = new AiImageEditorApi.ImageCredits
            {
                creator = "Someone",
                licenseRightsStatement = "All rights reserved; ask first.",
            };

            AiImageEditorApi.EmbedCreditsInNewImageFile(_bookFolder.Path, newName, credits);

            var after = Metadata.FromFile(Path.Combine(_bookFolder.Path, newName));
            Assert.That(after.Creator, Is.EqualTo("Someone"));
            Assert.That(
                after.License,
                Is.InstanceOf<CustomLicense>(),
                "a rights statement with no CC URL should become a CustomLicense"
            );
            Assert.That(
                after.License.RightsStatement,
                Is.EqualTo("All rights reserved; ask first.")
            );
        }

        [Test]
        public void CreditsRoundTrip_RightsStatementWithNoCcUrl_MatchesWhatTheSourceFileReadsBack()
        {
            // Why the wire carries no license *type* token, only the URL and the rights
            // statement: an image file stores just those two things (cc:license and dc:rights),
            // and ClearShare derives the type from them when reading (LicenseUtils.FromXmp) — a
            // rights statement with no CC URL always comes back as a CustomLicense no matter
            // which type was originally set. So BuildLicense deliberately mirrors FromXmp, which
            // makes the round trip faithful to the file: the copy's license reads back exactly
            // as the source's does. Set the source up the hard way here — a "contact the
            // copyright holder" NullLicense that also has notes — because that is the case a
            // type token would supposedly rescue. If ClearShare ever does start distinguishing
            // more license types in the file, this test fails and the wire needs that token.
            var sourceName = MakePlainPng("source.png");
            var sourcePath = Path.Combine(_bookFolder.Path, sourceName);
            using (var img = PalasoImage.FromFileRobustly(sourcePath))
            {
                img.Metadata.CopyrightNotice = "Copyright 2019 Someone";
                img.Metadata.License = new NullLicense { RightsStatement = "Email us first." };
                RetryUtility.Retry(() => img.SaveUpdatedMetadataIfItMakesSense());
            }

            // What the SOURCE file itself reads back as — the most any trip through a file
            // could preserve, and so the bar our round trip has to meet.
            var sourceAsRead = Metadata.FromFile(sourcePath);
            Assert.That(
                sourceAsRead.License.RightsStatement,
                Is.EqualTo("Email us first."),
                "setup: the source file should have kept its rights statement"
            );

            var newName = MakePlainPng("ai-image1.png");
            var credits = AiImageEditorApi.GetCreditsForImageFile(_bookFolder.Path, sourceName);
            AiImageEditorApi.EmbedCreditsInNewImageFile(_bookFolder.Path, newName, credits);

            var after = Metadata.FromFile(Path.Combine(_bookFolder.Path, newName));
            Assert.That(
                after.License.GetType(),
                Is.EqualTo(sourceAsRead.License.GetType()),
                "the copy's license type should match what the source file reads back as"
            );
            Assert.That(
                after.License.RightsStatement,
                Is.EqualTo(sourceAsRead.License.RightsStatement),
                "and so should its rights statement"
            );
        }

        // Both ways the AI image editor can say "this result has no credits": no credits
        // object at all, and one whose every field is empty.
        [TestCase(false, TestName = "EmbedCreditsInNewImageFile_NullCredits_LeavesFileClean")]
        [TestCase(true, TestName = "EmbedCreditsInNewImageFile_EmptyCredits_LeavesFileClean")]
        public void EmbedCreditsInNewImageFile_NoCredits_LeavesGeneratedFileClean(
            bool emptyRatherThanNull
        )
        {
            // The image being replaced HAS credits, and used to be Bloom's fallback source.
            // It must not be: the user may have edited that illustration for a while and then
            // made an entirely new image, which is not entitled to the old one's credits.
            var oldName = MakePngWithCredits("old.png", "Jane Doe", "Copyright 2020 Jane Doe");
            var newName = MakePlainPng("ai-image1.png");

            // Sanity: the replaced image really does have credits that could leak.
            var oldMeta = Metadata.FromFile(Path.Combine(_bookFolder.Path, oldName));
            Assert.That(oldMeta.Creator, Is.EqualTo("Jane Doe"), "setup");

            AiImageEditorApi.EmbedCreditsInNewImageFile(
                _bookFolder.Path,
                newName,
                emptyRatherThanNull ? new AiImageEditorApi.ImageCredits() : null
            );

            var after = Metadata.FromFile(Path.Combine(_bookFolder.Path, newName));
            Assert.That(
                after.Creator,
                Is.Null.Or.Empty,
                "no credits from the AI image editor means no credits on the result"
            );
            Assert.That(after.CopyrightNotice, Is.Null.Or.Empty);
        }

        [Test]
        public void GetCreditsForImageFile_RoundTripsEmbeddedCreditsForTheAiImageEditor()
        {
            // The outbound half: what EnumerateBookImages hands the AI image editor must
            // reflect the file's embedded credits, so a result derived from the image can
            // carry them.
            var name = MakePngWithCredits("pic.png", "Ada Lovelace", "Copyright 1843 Ada");

            var credits = AiImageEditorApi.GetCreditsForImageFile(_bookFolder.Path, name);

            Assert.That(credits, Is.Not.Null, "an image with metadata should yield credits");
            Assert.That(credits.creator, Is.EqualTo("Ada Lovelace"));
            Assert.That(credits.copyrightNotice, Is.EqualTo("Copyright 1843 Ada"));
            Assert.That(
                credits.licenseUrl,
                Does.Contain("creativecommons.org"),
                "the CC license URL should travel to the AI image editor"
            );
        }

        [Test]
        public void GetCreditsForImageFile_CcLicenseWithNotes_SendsTheNotesAsWellAsTheUrl()
        {
            // A Creative Commons license can carry free-text license notes as well as its URL.
            // We used to flatten the license to a single wire string, which for a CC license
            // meant just the URL — so the notes never reached the AI image editor and could not
            // come back, and the AI-edited copy of the illustration lost them (BL-16603).
            var name = MakePngWithCredits(
                "pic.png",
                "Ada Lovelace",
                "Copyright 1843 Ada",
                "Not to be used in advertising."
            );

            var credits = AiImageEditorApi.GetCreditsForImageFile(_bookFolder.Path, name);

            Assert.That(credits, Is.Not.Null, "an image with metadata should yield credits");
            Assert.That(
                credits.licenseUrl,
                Does.Contain("creativecommons.org"),
                "the CC license URL should still travel"
            );
            Assert.That(
                credits.licenseRightsStatement,
                Is.EqualTo("Not to be used in advertising."),
                "the license notes must travel alongside the URL rather than being dropped"
            );
        }

        [Test]
        public void CreditsRoundTrip_CcLicenseWithNotes_KeepsTheNotesOnTheNewFile()
        {
            // The whole BL-16603 journey for a CC-licensed illustration that has license notes:
            // out to the AI image editor on launch, back verbatim on commit (the AI image editor
            // carries the credits object opaquely), and embedded into the generated result. The
            // notes have to survive all of it, not just the first hop.
            var sourceName = MakePngWithCredits(
                "source.png",
                "Ada Lovelace",
                "Copyright 1843 Ada",
                "Not to be used in advertising."
            );
            var newName = MakePlainPng("ai-image1.png");

            // Sanity: the generated result starts with no license of its own, so anything we
            // find on it afterwards came from the round trip.
            var before = Metadata.FromFile(Path.Combine(_bookFolder.Path, newName));
            Assert.That(
                before.License?.RightsStatement,
                Is.Null.Or.Empty,
                "setup: the generated file should start with no rights statement"
            );

            var credits = AiImageEditorApi.GetCreditsForImageFile(_bookFolder.Path, sourceName);
            AiImageEditorApi.EmbedCreditsInNewImageFile(_bookFolder.Path, newName, credits);

            var after = Metadata.FromFile(Path.Combine(_bookFolder.Path, newName));
            Assert.That(after.Creator, Is.EqualTo("Ada Lovelace"));
            Assert.That(
                after.License,
                Is.InstanceOf<CreativeCommonsLicense>(),
                "the CC license itself should survive the round trip"
            );
            Assert.That(after.License.Url, Does.Contain("creativecommons.org"));
            Assert.That(
                after.License.RightsStatement,
                Is.EqualTo("Not to be used in advertising."),
                "and so should its license notes (BL-16603)"
            );
        }

        [Test]
        public void GetCreditsForImageFile_PercentEncodedName_StillFindsTheFile()
        {
            // A book's image src can arrive percent-encoded (BL-3901). If we don't decode it,
            // the file lookup misses and we tell the AI image editor the image has no credits —
            // so a result derived from it legitimately gets none, losing the very credits this
            // code exists to carry. The name here has a space, which encodes to %20.
            var name = MakePngWithCredits("my pic.png", "Ada Lovelace", "Copyright 1843 Ada");

            // Sanity: the undecoded name really does not name a file, so a non-null result
            // below can only come from the decoding.
            Assert.That(
                RobustFile.Exists(Path.Combine(_bookFolder.Path, "my%20pic.png")),
                Is.False,
                "setup: the encoded form should not exist on disk"
            );
            Assert.That(
                RobustFile.Exists(Path.Combine(_bookFolder.Path, name)),
                Is.True,
                "setup: the decoded form should exist on disk"
            );

            var credits = AiImageEditorApi.GetCreditsForImageFile(_bookFolder.Path, "my%20pic.png");

            Assert.That(
                credits,
                Is.Not.Null,
                "an encoded src should still resolve to its file and yield its credits"
            );
            Assert.That(credits.creator, Is.EqualTo("Ada Lovelace"));
            Assert.That(credits.copyrightNotice, Is.EqualTo("Copyright 1843 Ada"));
        }

        [Test]
        public void GetCreditsForImageFile_NoMetadata_ReturnsNull()
        {
            var name = MakePlainPng("plain.png");

            Assert.That(
                AiImageEditorApi.GetCreditsForImageFile(_bookFolder.Path, name),
                Is.Null,
                "an image with no embedded metadata should yield no credits object"
            );
        }

        // ------------------------------------------------------------------
        // ReadCreditAttributes: what a current-page replacement sends the front-end to put in
        // data-copyright/data-creator/data-license. The front-end used to copy those attributes
        // off the element being replaced, so a credit-less new image inherited the old image's
        // credits in the DOM: the edit tab showed no "missing information" indicator even though
        // the file (and the credits dialog) had none (BL-16603).
        // ------------------------------------------------------------------

        [Test]
        public void ReadCreditAttributes_ReadsThemFromTheFilesEmbeddedMetadata()
        {
            var name = MakePngWithCredits("pic.png", "Ada Lovelace", "Copyright 1843 Ada");

            var attributes = AiImageEditorApi.ReadCreditAttributes(_bookFolder.Path, name);

            Assert.That(attributes.creator, Is.EqualTo("Ada Lovelace"));
            Assert.That(attributes.copyright, Is.EqualTo("Copyright 1843 Ada"));
            Assert.That(
                attributes.license,
                Is.EqualTo("cc-by"),
                "data-license holds the short ClearShare token, not the license URL"
            );
        }

        [Test]
        public void ReadCreditAttributes_NoMetadata_ReturnsEmptyStrings()
        {
            // The case that made the bug visible: an AI-generated result with no credits. Empty
            // strings (not nulls) are what the attributes must end up holding, because an empty
            // data-copyright is what makes the edit tab show "missing information".
            var name = MakePlainPng("ai-image1.png");

            var attributes = AiImageEditorApi.ReadCreditAttributes(_bookFolder.Path, name);

            Assert.That(attributes.copyright, Is.Empty);
            Assert.That(attributes.creator, Is.Empty);
            Assert.That(attributes.license, Is.Empty);
        }

        [Test]
        public void ReadCreditAttributes_MissingFile_ReturnsEmptyStrings()
        {
            var attributes = AiImageEditorApi.ReadCreditAttributes(
                _bookFolder.Path,
                "no-such-file.png"
            );

            Assert.That(attributes.copyright, Is.Empty);
            Assert.That(attributes.creator, Is.Empty);
            Assert.That(attributes.license, Is.Empty);
        }

        [Test]
        public void ReadCreditAttributes_MatchesWhatBloomsOwnUpdaterWouldWrite()
        {
            // The point of this method is that a current-page element (updated by the
            // front-end from these values) says the same thing as an off-page element
            // (updated by ImageUpdater) and as the next book-up-to-date pass. Pin that
            // agreement down rather than trusting the two to stay in step by inspection.
            var name = MakePngWithCredits("pic.png", "Ada Lovelace", "Copyright 1843 Ada");
            var img = MakeImgWithClass(null); // its src is "pic.png", the file we just made

            ImageUpdater.UpdateImgMetadataAttributesToMatchImage(
                _bookFolder.Path,
                img,
                new NullProgress()
            );

            // Sanity: the updater really did put something there, so an agreeing pair below
            // isn't just two empty results.
            Assert.That(img.GetAttribute("data-creator"), Is.EqualTo("Ada Lovelace"), "setup");

            var attributes = AiImageEditorApi.ReadCreditAttributes(_bookFolder.Path, name);
            Assert.That(attributes.copyright, Is.EqualTo(img.GetAttribute("data-copyright")));
            Assert.That(attributes.creator, Is.EqualTo(img.GetAttribute("data-creator")));
            Assert.That(attributes.license, Is.EqualTo(img.GetAttribute("data-license")));
        }
    }

    /// <summary>
    /// Tests for <see cref="AiImageEditorApi.GetLinkedEditorUrlOverride"/>, which honors the
    /// obsolete BLOOM_AI_EDITOR_URL name so a developer who still has it set keeps getting
    /// their linked dev server instead of silently falling back to the staged build.
    /// </summary>
    [TestFixture]
    public class AiImageEditorLinkedUrlOverrideTests
    {
        private string _originalCurrent;
        private string _originalObsolete;

        [SetUp]
        public void Setup()
        {
            _originalCurrent = Get(AiImageEditorApi.kLinkedEditorUrlEnvironmentVariable);
            _originalObsolete = Get(AiImageEditorApi.kLinkedEditorUrlObsoleteEnvironmentVariable);
            Set(AiImageEditorApi.kLinkedEditorUrlEnvironmentVariable, null);
            Set(AiImageEditorApi.kLinkedEditorUrlObsoleteEnvironmentVariable, null);
            // Sanity: with neither name set we must start from "no override", or a test below
            // could pass on a value left over from the developer's own environment.
            Assert.That(
                AiImageEditorApi.GetLinkedEditorUrlOverride(),
                Is.Null,
                "setup: neither variable should be in play at the start of a test"
            );
        }

        [TearDown]
        public void TearDown()
        {
            Set(AiImageEditorApi.kLinkedEditorUrlEnvironmentVariable, _originalCurrent);
            Set(AiImageEditorApi.kLinkedEditorUrlObsoleteEnvironmentVariable, _originalObsolete);
        }

        private static string Get(string name) => Environment.GetEnvironmentVariable(name);

        private static void Set(string name, string value) =>
            Environment.SetEnvironmentVariable(name, value);

        [Test]
        public void CurrentNameSet_IsUsed()
        {
            Set(AiImageEditorApi.kLinkedEditorUrlEnvironmentVariable, "http://localhost:3000/");
            Assert.That(
                AiImageEditorApi.GetLinkedEditorUrlOverride(),
                Is.EqualTo("http://localhost:3000/")
            );
        }

        [Test]
        public void OnlyObsoleteNameSet_IsStillHonored()
        {
            Set(
                AiImageEditorApi.kLinkedEditorUrlObsoleteEnvironmentVariable,
                "http://localhost:4000/"
            );
            Assert.That(
                AiImageEditorApi.GetLinkedEditorUrlOverride(),
                Is.EqualTo("http://localhost:4000/"),
                "the obsolete name must keep working during the transition"
            );
        }

        [Test]
        public void BothNamesSet_CurrentWins()
        {
            Set(AiImageEditorApi.kLinkedEditorUrlEnvironmentVariable, "http://current/");
            Set(AiImageEditorApi.kLinkedEditorUrlObsoleteEnvironmentVariable, "http://obsolete/");
            Assert.That(
                AiImageEditorApi.GetLinkedEditorUrlOverride(),
                Is.EqualTo("http://current/")
            );
        }

        [TestCase("")]
        [TestCase("   ")]
        public void CurrentNameBlank_FallsBackToObsolete(string blank)
        {
            Set(AiImageEditorApi.kLinkedEditorUrlEnvironmentVariable, blank);
            Set(AiImageEditorApi.kLinkedEditorUrlObsoleteEnvironmentVariable, "http://obsolete/");
            Assert.That(
                AiImageEditorApi.GetLinkedEditorUrlOverride(),
                Is.EqualTo("http://obsolete/"),
                "a blank current name should not mask a usable obsolete one"
            );
        }

        [TestCase("")]
        [TestCase("   ")]
        public void BothBlankOrUnset_ReturnsNull(string blank)
        {
            Set(AiImageEditorApi.kLinkedEditorUrlEnvironmentVariable, blank);
            Set(AiImageEditorApi.kLinkedEditorUrlObsoleteEnvironmentVariable, blank);
            Assert.That(AiImageEditorApi.GetLinkedEditorUrlOverride(), Is.Null);
        }
    }

    /// <summary>
    /// Tests for <see cref="AiImageEditorApi.ShouldShowDeveloperTools"/>, the opt-in that
    /// lets a tester on a channel like beta see the AI image editor's tester tools
    /// (currently the "Local Dummy (No AI)" model). See BL-16770.
    /// </summary>
    [TestFixture]
    public class AiImageEditorDeveloperToolsOptInTests
    {
        private string _originalValue;

        [SetUp]
        public void Setup()
        {
            _originalValue = Environment.GetEnvironmentVariable(
                AiImageEditorApi.kShowTesterToolsEnvironmentVariable
            );
            // Sanity: unit tests run on a channel that is neither developer nor alpha, so
            // every "on" result below really comes from the environment variable and not
            // from the channel check short-circuiting the method.
            Assert.That(
                ApplicationUpdateSupport.IsDevOrAlpha,
                Is.False,
                "setup: unit tests should not look like a dev/alpha channel"
            );
        }

        [TearDown]
        public void TearDown()
        {
            SetVariable(_originalValue);
        }

        private static void SetVariable(string value)
        {
            Environment.SetEnvironmentVariable(
                AiImageEditorApi.kShowTesterToolsEnvironmentVariable,
                value
            );
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("0")]
        [TestCase("f")]
        [TestCase("n")]
        [TestCase("no")]
        [TestCase("false")]
        [TestCase("please")]
        [TestCase("truely")]
        public void ShouldShowDeveloperTools_NotOptedIn_False(string value)
        {
            SetVariable(value);
            Assert.That(AiImageEditorApi.ShouldShowDeveloperTools(), Is.False);
        }

        [TestCase("true")]
        [TestCase("t")]
        [TestCase("y")]
        [TestCase("yes")]
        [TestCase("1")]
        // The same values as above, spelled the way a tester might actually type them.
        [TestCase("TRUE")]
        [TestCase("True")]
        [TestCase("T")]
        [TestCase("Y")]
        [TestCase("Yes")]
        [TestCase("YES")]
        [TestCase(" 1 ")]
        public void ShouldShowDeveloperTools_OptedIn_True(string value)
        {
            SetVariable(value);
            Assert.That(AiImageEditorApi.ShouldShowDeveloperTools(), Is.True);
        }

        /// <summary>
        /// Guards the list itself: every documented "on" value must actually turn the tools
        /// on, so adding a spelling to kTesterToolsOnValues without a matching TestCase above
        /// still can't ship broken.
        /// </summary>
        [Test]
        public void ShouldShowDeveloperTools_EveryDocumentedOnValue_True()
        {
            Assert.That(
                AiImageEditorApi.kTesterToolsOnValues,
                Is.Not.Empty,
                "setup: there should be some accepted values"
            );
            foreach (var value in AiImageEditorApi.kTesterToolsOnValues)
            {
                SetVariable(value);
                Assert.That(
                    AiImageEditorApi.ShouldShowDeveloperTools(),
                    Is.True,
                    $"'{value}' is documented as an accepted value"
                );
            }
        }
    }
}
