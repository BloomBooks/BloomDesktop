using System.ComponentModel;
using System.IO;
using Bloom.Publish;
using Bloom.Publish.PDF;
using DotImpose.LayoutMethods;
using NUnit.Framework;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SIL.IO;

namespace BloomTests.Publish.PDF
{
    [TestFixture]
#if __MonoCS__
    [Apartment(System.Threading.ApartmentState.STA)]
#endif
    [NUnit.Framework.Category("RequiresUI")]
    public class PdfMakerTests
    {
        [Test]
        public void NullLayoutMethod_FullBleedWithCropMarks_KeepsTrimAtFinalPageSize()
        {
            using (var input = TempFile.WithExtension("pdf"))
            using (var output = TempFile.WithExtension("pdf"))
            {
                // Full-bleed source pages carry explicit trim/bleed boxes, exactly as
                // PdfMaker sets them before the dotImpose handoff.
                CreateSinglePagePdf(input.Path, 216, 303, fullBleedBoxes: true);
                var method = new NullLayoutMethod();

                method.Layout(
                    XPdfForm.FromFile(input.Path),
                    input.Path,
                    output.Path,
                    new PaperTarget("A4", PageSize.A4),
                    false,
                    true
                );

                using (var outputDoc = PdfReader.Open(output.Path, PdfDocumentOpenMode.Import))
                {
                    var trimBox = outputDoc.Pages[0].TrimBox.ToXRect();
                    Assert.AreEqual(
                        210,
                        XUnit.FromPoint(trimBox.Width).Millimeter,
                        0.2,
                        "Trim width should stay at final A4 width"
                    );
                    Assert.AreEqual(
                        297,
                        XUnit.FromPoint(trimBox.Height).Millimeter,
                        0.2,
                        "Trim height should stay at final A4 height"
                    );
                }
            }
        }

        [Test]
        public void MakePdf_BookStyleIsNone_OutputsPdf()
        {
            var maker = new PdfMaker();
            using (var input = TempFile.WithExtension("html"))
            using (var output = new TempFile())
            {
                File.WriteAllText(input.Path, "<html><body>Hello</body></html>");
                File.Delete(output.Path);
                RunMakePdf(
                    maker,
                    input.Path,
                    output.Path,
                    "A5",
                    false,
                    false,
                    PublishModel.BookletLayoutMethod.SideFold,
                    PublishModel.BookletPortions.AllPagesNoBooklet
                );
                //we don't actually have a way of knowing it did a booklet
                Assert.IsTrue(
                    File.Exists(output.Path),
                    "Failed to convert trivial HTML file to PDF (AllPagesNoBooklet)"
                );
                var bytes = File.ReadAllBytes(output.Path);
                Assert.Less(
                    1000,
                    bytes.Length,
                    "Generated PDF file is way too small! (AllPagesNoBooklet)"
                );
                Assert.IsTrue(
                    bytes[0] == (byte)'%'
                        && bytes[1] == (byte)'P'
                        && bytes[2] == (byte)'D'
                        && bytes[3] == (byte)'F',
                    "Generated PDF file started with the wrong 4-byte signature (AllPagesNoBooklet)"
                );
            }
        }

        [Test]
        public void MakePdf_BookStyleIsBooklet_OutputsPdf()
        {
            var maker = new PdfMaker();
            using (var input = TempFile.WithExtension("html"))
            using (var output = new TempFile())
            {
                File.WriteAllText(input.Path, "<html><body>Hello</body></html>");
                File.Delete(output.Path);
                RunMakePdf(
                    maker,
                    input.Path,
                    output.Path,
                    "A5",
                    false,
                    false,
                    PublishModel.BookletLayoutMethod.SideFold,
                    PublishModel.BookletPortions.BookletPages
                );
                //we don't actually have a way of knowing it did a booklet
                Assert.IsTrue(
                    File.Exists(output.Path),
                    "Failed to convert trivial HTML file to PDF (BookletPages)"
                );
                var bytes = File.ReadAllBytes(output.Path);
                Assert.Less(
                    1000,
                    bytes.Length,
                    "Generated PDF file is way too small! (BookletPages)"
                );
                Assert.IsTrue(
                    bytes[0] == (byte)'%'
                        && bytes[1] == (byte)'P'
                        && bytes[2] == (byte)'D'
                        && bytes[3] == (byte)'F',
                    "Generated PDF file started with the wrong 4-byte signature (BookletPages)"
                );
            }
        }

        /// <summary>
        /// This tests for a regretion on BL-81, BL-96, BL-76; wkhtmltopdf itself couldn't handle file names anything up out of ascii-land
        /// </summary>
        [Test]
        public void MakePdf_BookNameIsChinese_OutputsPdf()
        {
            var maker = new PdfMaker();
            using (var input = TempFile.WithFilename("北京.html"))
            using (var output = TempFile.WithFilename("北京.pdf"))
            {
                RobustFile.WriteAllText(input.Path, "<html><body>北京</body></html>");
                RobustFile.Delete(output.Path);
                RunMakePdf(
                    maker,
                    input.Path,
                    output.Path,
                    "A5",
                    false,
                    false,
                    PublishModel.BookletLayoutMethod.SideFold,
                    PublishModel.BookletPortions.BookletPages
                );
                //we don't actually have a way of knowing it did a booklet
                Assert.IsTrue(
                    File.Exists(output.Path),
                    "Failed to convert trivial HTML file to PDF (Chinese filenames and content)"
                );
                var bytes = File.ReadAllBytes(output.Path);
                Assert.Less(
                    1000,
                    bytes.Length,
                    "Generated PDF file is way too small! (Chinese filenames and content)"
                );
                Assert.IsTrue(
                    bytes[0] == (byte)'%'
                        && bytes[1] == (byte)'P'
                        && bytes[2] == (byte)'D'
                        && bytes[3] == (byte)'F',
                    "Generated PDF file started with the wrong 4-byte signature (Chinese filenames and content)"
                );
            }
        }

        [Test]
        public void MakePdf_BookNameIsNonAscii_OutputsPdf()
        {
            var maker = new PdfMaker();
            using (var input = TempFile.WithFilename("എന്റെ ബുക്ക്.html"))
            using (var output = TempFile.WithFilename("എന്റെ ബുക്ക്.pdf"))
            {
                File.WriteAllText(
                    input.Path,
                    "<META HTTP-EQUIV=\"content-type\" CONTENT=\"text/html; charset=utf-8\"><html><body>എന്റെ ബുക്ക്</body></html>"
                );
                File.Delete(output.Path);
                RunMakePdf(
                    maker,
                    input.Path,
                    output.Path,
                    "A5",
                    false,
                    false,
                    PublishModel.BookletLayoutMethod.SideFold,
                    PublishModel.BookletPortions.BookletPages
                );
                //we don't actually have a way of knowing it did a booklet
                Assert.IsTrue(
                    File.Exists(output.Path),
                    "Failed to convert trivial HTML file to PDF (Indic script filenames and content)"
                );
                var bytes = File.ReadAllBytes(output.Path);
                Assert.Less(
                    1000,
                    bytes.Length,
                    "Generated PDF file is way too small! (Indic script filenames and content)"
                );
                Assert.IsTrue(
                    bytes[0] == (byte)'%'
                        && bytes[1] == (byte)'P'
                        && bytes[2] == (byte)'D'
                        && bytes[3] == (byte)'F',
                    "Generated PDF file started with the wrong 4-byte signature (Indic script filenames and content)"
                );
            }
        }

        /// <summary>
        /// Every page size Bloom offers has to be one BloomPdfMaker also knows about, or the book
        /// can't be turned into a PDF at all -- which blocks uploading too, since that makes a PDF
        /// preview.  The StoryWeaver ebook sizes were added to Bloom's list of page sizes without
        /// being added to BloomPdfMaker's, so they failed outright until BL-16684.  Device16x9 is
        /// here as a control: it is the screen size that always worked.
        /// </summary>
        [TestCase("Ebook2x3", false)]
        [TestCase("Ebook7x5", true)]
        [TestCase("Device16x9", true)]
        public void MakePdf_ScreenPageSize_OutputsPdf(string paperSize, bool landscape)
        {
            var maker = new PdfMaker();
            using (var input = TempFile.WithExtension("html"))
            using (var output = new TempFile())
            {
                File.WriteAllText(input.Path, "<html><body>Hello</body></html>");
                File.Delete(output.Path);
                Assert.IsFalse(
                    File.Exists(output.Path),
                    $"Setup failure: the output file should not exist before we make the PDF ({paperSize})"
                );

                var error = RunMakePdf(
                    maker,
                    input.Path,
                    output.Path,
                    paperSize,
                    landscape,
                    false,
                    PublishModel.BookletLayoutMethod.SideFold,
                    PublishModel.BookletPortions.AllPagesNoBooklet
                );

                Assert.IsNull(
                    error,
                    $"Making a PDF at page size {paperSize} reported an error: {error?.Message}"
                );
                Assert.IsTrue(
                    File.Exists(output.Path),
                    $"Failed to convert trivial HTML file to PDF at page size {paperSize}"
                );
                var bytes = File.ReadAllBytes(output.Path);
                Assert.Less(
                    1000,
                    bytes.Length,
                    $"Generated PDF file is way too small! ({paperSize})"
                );
                Assert.IsTrue(
                    bytes[0] == (byte)'%'
                        && bytes[1] == (byte)'P'
                        && bytes[2] == (byte)'D'
                        && bytes[3] == (byte)'F',
                    $"Generated PDF file started with the wrong 4-byte signature ({paperSize})"
                );
            }
        }

        /// <summary>
        /// If we ever again offer a page size BloomPdfMaker can't render (which is what BL-16684
        /// was), the user should be told that in so many words, naming the size, rather than being
        /// shown the generic failure plus the child process's developer output.  "NoSuchSize" is a
        /// stand-in for the next page size someone adds to Bloom and forgets to add to
        /// BloomPdfMaker.
        /// </summary>
        [Test]
        public void MakePdf_PageSizeBloomPdfMakerDoesNotKnow_SaysSoAndNamesTheSize()
        {
            var maker = new PdfMaker();
            using (var input = TempFile.WithExtension("html"))
            using (var output = new TempFile())
            {
                File.WriteAllText(input.Path, "<html><body>Hello</body></html>");
                File.Delete(output.Path);

                var error = RunMakePdf(
                    maker,
                    input.Path,
                    output.Path,
                    "NoSuchSize",
                    false,
                    false,
                    PublishModel.BookletLayoutMethod.SideFold,
                    PublishModel.BookletPortions.AllPagesNoBooklet
                );

                Assert.IsNotNull(
                    error,
                    "Setup failure: making a PDF at an unknown page size should have failed"
                );
                Assert.That(
                    error.Message,
                    Does.Contain("NoSuchSize"),
                    "The message should name the offending page size so the user (and we) know which one it is"
                );
                Assert.That(
                    error.Message,
                    Does.Not.Contain("did not produce the expected document"),
                    "This should be the specific page-size message, not the generic PDF failure"
                );
                Assert.That(
                    error.Message,
                    Does.Not.Contain("UNSUPPORTED-PAGE-SIZE"),
                    "The marker we grep for is internal and should not reach the user"
                );
            }
        }

        /// <summary>
        /// Runs PdfMaker.MakePdf() with the desired arguments.  Note that the implementation (as of March 2015)
        /// uses an external program to generate the PDF from the HTML file, so it doesn't need to be run on
        /// a background thread.  The process includes a (possibly overgenerous) timeout, so we don't try to
        /// impose one here.
        /// </summary>
        /// <remarks>
        /// Running this on a background thread would be okay, except that on Linux, the interaction between
        /// Mono and NUnit and the Bloom method result in the BackgroundWorker.RunWorkerCompleted event
        /// never being fired if tests other than those in this file are run along with these tests.  This is
        /// almost certainly an obscure bug in Mono.  Running the method directly as we do here sidesteps that
        /// problem.  (See https://jira.sil.org/browse/BL-831.)
        /// </remarks>
        /// <returns>
        /// The exception MakePdf passed back through the DoWorkEventArgs, or null if it succeeded.
        /// A test that only checks for the output file reports "no PDF" when what the caller really
        /// wants to know is why, and the exception carries BloomPdfMaker's own explanation.
        /// </returns>
        System.Exception RunMakePdf(
            PdfMaker maker,
            string input,
            string output,
            string paperSize,
            bool landscape,
            bool rightToLeft,
            PublishModel.BookletLayoutMethod layout,
            PublishModel.BookletPortions portion
        )
        {
            // Passing in a DoWorkEventArgs object prevents a possible exception being thrown.  Which may not
            // really matter much in the test situation since NUnit would catch the exception.  But I'd rather
            // have a nice test failure message than an unexpected exception caught message.
            var eventArgs = new DoWorkEventArgs(null);
            maker.MakePdf(
                new PdfMakingSpecs()
                {
                    InputHtmlPath = input,
                    OutputPdfPath = output,
                    PaperSizeName = paperSize,
                    Landscape = landscape,
                    LayoutPagesForRightToLeft = rightToLeft,
                    BooketLayoutMethod = layout,
                    BookletPortion = portion,
                },
                null,
                eventArgs,
                null
            );
            return eventArgs.Result as System.Exception;
        }

        private static void CreateSinglePagePdf(
            string path,
            double widthMm,
            double heightMm,
            bool fullBleedBoxes = false
        )
        {
            using (var doc = new PdfDocument())
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromMillimeter(widthMm);
                page.Height = XUnit.FromMillimeter(heightMm);
                if (fullBleedBoxes)
                    PdfMaker.SetFullBleedPageBoxes(page);
                doc.Save(path);
            }
        }
    }
}
