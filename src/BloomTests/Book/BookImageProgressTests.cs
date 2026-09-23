using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.SafeXml;
using BloomTests.ToPalaso;
using NUnit.Framework;
using SIL.Core.ClearShare;
using SIL.Windows.Forms.ClearShare;

namespace BloomTests.Book
{
    /// <summary>
    /// How the passes that work through a book's images report progress (BL-16893).
    /// Bringing a book up to date runs the React progress dialog, which appends every status
    /// line permanently, so that path must report per-image progress by percent only. The
    /// Copyright and License dialog's "add this to all images" runs a WinForms dialog with one
    /// overwriting status label, where a line per image is useful and must stay.
    /// </summary>
    public class BookImageProgressTests : BookTestsBase
    {
        private const string kPageWithOneImage =
            @"<div class='bloom-page numberedPage' id='guid1'>
                <div class='marginBox'>
                    <div class='bloom-canvas'>
                        <img src='sample.png' />
                    </div>
                </div>
            </div>";

        private Bloom.Book.Book MakeBookWithOneImage()
        {
            var book = CreateBookWithPhysicalFile(kPageWithOneImage);
            MakeSamplePngImageWithMetadata(Path.Combine(book.FolderPath, "sample.png"));
            return book;
        }

        private static SafeXmlElement GetSampleImg(Bloom.Book.Book book) =>
            book
                .OurHtmlDom.SafeSelectNodes("//img[@src='sample.png']")
                .Cast<SafeXmlElement>()
                .Single();

        private static readonly string[] kPerImageStatusPrefixes =
        {
            "Reading metadata from",
            "Writing metadata to HTML for",
            "Preparing image",
            "RemovingTransparency from image",
        };

        [Test]
        public void BringBookUpToDate_MirrorsImageMetadataWithoutAStatusLinePerImage()
        {
            var book = MakeBookWithOneImage();
            var progress = new RecordingProgress();

            book.BringBookUpToDate(progress);

            // Sanity: the per-image pass really ran and mirrored the file's metadata into the HTML.
            var img = GetSampleImg(book);
            Assert.That(img.GetAttribute("data-copyright"), Is.EqualTo("Copyright 1999 by me"));
            Assert.That(img.GetAttribute("data-creator"), Is.EqualTo("joe"));
            // It reported how far along it was...
            Assert.That(progress.Indicator.Percents, Is.Not.Empty);
            // ...and status lines in general still flow (the stage messages, e.g. "Updating pages...")...
            Assert.That(progress.Statuses, Is.Not.Empty);
            // ...but not one per image.
            var perImageLines = progress
                .Statuses.Where(s => kPerImageStatusPrefixes.Any(prefix => s.StartsWith(prefix)))
                .ToList();
            Assert.That(
                perImageLines,
                Is.Empty,
                "bringing a book up to date should not log a line per image"
            );
        }

        [Test]
        public void CopyImageMetadataToWholeBook_StillReportsEachImage()
        {
            var book = MakeBookWithOneImage();
            var metadata = new Metadata
            {
                CopyrightNotice = "Copyright 2026 Someone",
                Creator = "someone",
                License = new CreativeCommonsLicense(
                    true,
                    true,
                    CreativeCommonsLicense.DerivativeRules.Derivatives
                ),
            };
            var progress = new RecordingProgress();

            ImageUpdater.CopyImageMetadataToWholeBook(
                book.FolderPath,
                book.OurHtmlDom,
                metadata,
                progress
            );

            Assert.That(progress.Statuses, Does.Contain("Copying to sample.png"));
            Assert.That(progress.Statuses, Does.Contain("Writing metadata to HTML for sample.png"));
            // Sanity: the metadata really was applied.
            Assert.That(
                GetSampleImg(book).GetAttribute("data-copyright"),
                Is.EqualTo("Copyright 2026 Someone")
            );
        }
    }
}
