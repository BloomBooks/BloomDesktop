using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom.Book;
using Bloom.Publish;
using Bloom.Publish.PDF;
using Bloom.SafeXml;
using Bloom.web.controllers;
using NUnit.Framework;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Publish.PDF
{
    [TestFixture]
    public class PdfVideoEmbedderTests
    {
        private const string kPageWithVideo =
            @"<html><head><style>
@page { size: 148mm 210mm; margin: 0 }
body { margin: 0 }
.bloom-page { width: 148mm; height: 210mm; position: relative; page-break-after: always; overflow: hidden }
.bloom-canvas-element { position: absolute; left: 20mm; top: 60mm; width: 108mm; height: 61mm }
.bloom-videoContainer { width: 100%; height: 100% }
</style></head><body>
<div class='bloom-page'><p>No video here</p></div>
<div class='bloom-page'><div class='bloom-canvas-element'>
  <div class='bloom-videoContainer'><video><source src='video/clip.mp4#t=0.5,2.5' type='video/mp4'></source></video></div>
</div></div>
</body></html>";

        /// <summary>
        /// Make a book folder containing a stand-in for the video file kPageWithVideo refers to.
        /// </summary>
        private static byte[] MakeVideoFile(TemporaryFolder bookFolder)
        {
            Directory.CreateDirectory(Path.Combine(bookFolder.Path, "video"));
            var bytes = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
            File.WriteAllBytes(Path.Combine(bookFolder.Path, "video", "clip.mp4"), bytes);
            return bytes;
        }

        /// <summary>
        /// Make a book folder containing a real 4-second H.264 video at the path kPageWithVideo
        /// refers to. Every frame is a key frame, so ffmpeg can cut it exactly where the book says.
        /// </summary>
        private static string MakeRealVideoFile(TemporaryFolder bookFolder)
        {
            Directory.CreateDirectory(Path.Combine(bookFolder.Path, "video"));
            const int width = 64,
                height = 48,
                framesPerSecond = 10,
                seconds = 4;
            var frames = Enumerable
                .Range(0, framesPerSecond * seconds)
                .SelectMany(frame => Enumerable.Repeat((byte)(frame * 6), width * height))
                .ToArray();
            var rawPath = Path.Combine(bookFolder.Path, "frames.gray");
            File.WriteAllBytes(rawPath, frames);
            var videoPath = Path.Combine(bookFolder.Path, "video", "clip.mp4");
            RunFfmpeg(
                $"-f rawvideo -pix_fmt gray -s {width}x{height} -r {framesPerSecond} -i \"{rawPath}\" -c:v libx264 -g 1 -pix_fmt yuv420p \"{videoPath}\""
            );
            Assert.That(
                GetDurationInSeconds(videoPath),
                Is.EqualTo(seconds).Within(0.15),
                "setup should have made a 4-second video"
            );
            return videoPath;
        }

        private static string RunFfmpeg(string arguments)
        {
            Assert.That(
                SignLanguageApi.FfmpegProgram,
                Is.Not.Empty,
                "these tests need Bloom's ffmpeg"
            );
            var startInfo = new ProcessStartInfo(
                SignLanguageApi.FfmpegProgram,
                "-hide_banner -y " + arguments
            )
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using (var process = Process.Start(startInfo))
            {
                var output = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return output;
            }
        }

        private static double GetDurationInSeconds(string videoPath)
        {
            var output = RunFfmpeg($"-i \"{videoPath}\"");
            var match = Regex.Match(output, @"Duration: (\d+:\d+:\d+\.\d+)");
            Assert.That(match.Success, Is.True, "ffmpeg did not report a duration: " + output);
            return TimeSpan.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture).TotalSeconds;
        }

        [Test]
        public void MarkVideosForPdf_WrapsVideoInMarkerLink_ReturnsFileAndTimings()
        {
            using (var bookFolder = new TemporaryFolder("MarkVideosForPdf"))
            {
                MakeVideoFile(bookFolder);
                var dom = new HtmlDom(kPageWithVideo);

                var videos = PdfVideoEmbedder.MarkVideosForPdf(dom, bookFolder.Path);

                Assert.That(videos.Count, Is.EqualTo(1));
                Assert.That(
                    videos[0].FilePath,
                    Is.EqualTo(Path.Combine(bookFolder.Path, "video", "clip.mp4"))
                );
                Assert.That(videos[0].RawTimings, Is.EqualTo("0.5,2.5"));
                AssertThatXmlIn
                    .Dom(dom.RawDom)
                    .HasSpecifiedNumberOfMatchesForXpath(
                        "//div[@class='bloom-videoContainer']/a[@href='https://bloom-pdf-video.invalid/0']/video/source",
                        1
                    );
            }
        }

        [Test]
        public void MarkVideosForPdf_RealVideo_ReplacesVideoWithFrameFromJustAfterTrimStart()
        {
            using (var bookFolder = new TemporaryFolder("MarkVideosForPdfFrame"))
            {
                MakeRealVideoFile(bookFolder);
                var dom = new HtmlDom(kPageWithVideo);

                var videos = PdfVideoEmbedder.MarkVideosForPdf(dom, bookFolder.Path);

                Assert.That(videos.Count, Is.EqualTo(1));
                AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//video");
                var img = (SafeXmlElement)
                    dom.RawDom.SelectSingleNode(
                        "//div[@class='bloom-videoContainer']/a[@href='https://bloom-pdf-video.invalid/0']/img"
                    );
                Assert.That(img, Is.Not.Null, "the video should have been replaced by its frame");
                const string prefix = "data:image/png;base64,";
                var src = img.GetAttribute("src");
                Assert.That(src, Does.StartWith(prefix));
                using (
                    var stream = new MemoryStream(
                        Convert.FromBase64String(src.Substring(prefix.Length))
                    )
                )
                using (var frame = new Bitmap(stream))
                {
                    Assert.That(frame.Width, Is.EqualTo(64));
                    Assert.That(frame.Height, Is.EqualTo(48));
                    // The book plays from 0.5 seconds, so the frame is from 1 second: frame 10, gray 60.
                    Assert.That(frame.GetPixel(32, 24).G, Is.EqualTo(60).Within(8));
                }
            }
        }

        [Test]
        public void MarkVideosForPdf_VideoFileMissing_LeavesVideoAlone()
        {
            using (var bookFolder = new TemporaryFolder("MarkVideosForPdfMissing"))
            {
                var dom = new HtmlDom(kPageWithVideo);

                var videos = PdfVideoEmbedder.MarkVideosForPdf(dom, bookFolder.Path);

                Assert.That(videos, Is.Empty);
                AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//a");
            }
        }

        [Test]
        public void ReplaceMarkerLinks_NotEmbedding_RemovesOnlyMarkerLinks()
        {
            using (var pdfFile = TempFile.WithExtension("pdf"))
            {
                using (var doc = new PdfDocument())
                {
                    var page = doc.AddPage();
                    var rect = new PdfRectangle(new XRect(10, 10, 100, 50));
                    page.AddWebLink(rect, "https://bloom-pdf-video.invalid/0");
                    page.AddWebLink(rect, "https://bloomlibrary.org");
                    doc.Save(pdfFile.Path);
                }
                using (var doc = PdfReader.Open(pdfFile.Path, PdfDocumentOpenMode.Modify))
                {
                    Assert.That(
                        doc.Pages[0].Elements.GetArray("/Annots").Elements.Count,
                        Is.EqualTo(2),
                        "setup should have made two links"
                    );

                    PdfVideoEmbedder.ReplaceMarkerLinks(doc, new List<PdfVideo>(), embed: false);

                    var annots = GetAnnotations(doc.Pages[0]);
                    Assert.That(annots.Count, Is.EqualTo(1));
                    Assert.That(
                        GetDict(annots[0], "/A").Elements.GetString("/URI"),
                        Is.EqualTo("https://bloomlibrary.org")
                    );
                }
            }
        }

        /// <summary>
        /// Runs the whole chain: the external PDF maker prints the marked html, Ghostscript processes it,
        /// and PdfMaker replaces the marker link with a Screen annotation that embeds the part of the
        /// video that the book plays.
        /// </summary>
        [Test]
        [NUnit.Framework.Category("RequiresUI")]
        public void MakePdf_PageWithTrimmedVideo_EmbedsTrimmedVideoWhereItWasPrinted()
        {
            using (var bookFolder = new TemporaryFolder("MakePdfWithVideo"))
            {
                var videoPath = MakeRealVideoFile(bookFolder);
                var dom = new HtmlDom(kPageWithVideo);
                var videos = PdfVideoEmbedder.MarkVideosForPdf(dom, bookFolder.Path);
                var htmlPath = Path.Combine(bookFolder.Path, "book.html");
                File.WriteAllText(htmlPath, dom.getHtmlStringDisplayOnly());
                var pdfPath = Path.Combine(bookFolder.Path, "book.pdf");

                var eventArgs = new DoWorkEventArgs(null);
                new PdfMaker().MakePdf(
                    new PdfMakingSpecs
                    {
                        InputHtmlPath = htmlPath,
                        OutputPdfPath = pdfPath,
                        PaperSizeName = "A5",
                        BooketLayoutMethod = PublishModel.BookletLayoutMethod.NoBooklet,
                        BookletPortion = PublishModel.BookletPortions.AllPagesNoBooklet,
                        HtmlPageCount = 2,
                        Videos = videos,
                    },
                    null,
                    eventArgs,
                    null
                );
                Assert.That(eventArgs.Result, Is.Null);

                using (var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import))
                {
                    Assert.That(doc.PageCount, Is.EqualTo(2));
                    Assert.That(GetAnnotations(doc.Pages[0]), Is.Empty);
                    var annots = GetAnnotations(doc.Pages[1]);
                    Assert.That(annots.Count, Is.EqualTo(1));
                    var screen = annots[0];
                    Assert.That(screen.Elements.GetName("/Subtype"), Is.EqualTo("/Screen"));

                    // The canvas element is 20mm from the left and 60mm from the top of an A5 page.
                    var rect = screen.Elements.GetRectangle("/Rect");
                    Assert.That(rect.X1, Is.EqualTo(20 * 72 / 25.4).Within(1));
                    Assert.That(rect.X2, Is.EqualTo(128 * 72 / 25.4).Within(1));
                    Assert.That(rect.Y2, Is.EqualTo((210 - 60) * 72 / 25.4).Within(1));
                    Assert.That(rect.Y1, Is.EqualTo((210 - 121) * 72 / 25.4).Within(1));

                    Assert.That(
                        HasImage(doc.Pages[1].Resources),
                        Is.True,
                        "the page should show the video's frame"
                    );

                    var action = GetDict(screen, "/A");
                    Assert.That(action.Elements.GetName("/S"), Is.EqualTo("/Rendition"));
                    var clipData = GetDict(GetDict(action, "/R"), "/C");
                    Assert.That(clipData.Elements.GetName("/S"), Is.EqualTo("/MCD"));
                    Assert.That(clipData.Elements.GetString("/CT"), Is.EqualTo("video/mp4"));
                    var embeddedFile = GetDict(GetDict(GetDict(clipData, "/D"), "/EF"), "/F");

                    // The book plays from 0.5 to 2.5 seconds, so the PDF holds just those 2 seconds.
                    var embeddedPath = Path.Combine(bookFolder.Path, "embedded.mp4");
                    File.WriteAllBytes(embeddedPath, embeddedFile.Stream.UnfilteredValue);
                    Assert.That(GetDurationInSeconds(embeddedPath), Is.EqualTo(2).Within(0.15));
                    Assert.That(
                        File.Exists(videoPath),
                        Is.True,
                        "the book's own video must be left alone"
                    );
                    Assert.That(GetDurationInSeconds(videoPath), Is.EqualTo(4).Within(0.15));
                }
            }
        }

        /// <summary>
        /// Whether the resources, or those of any form they use, include an image.
        /// </summary>
        private static bool HasImage(PdfDictionary resources)
        {
            var xObjects = resources == null ? null : GetDict(resources, "/XObject");
            if (xObjects == null)
                return false;
            foreach (var key in xObjects.Elements.Keys)
            {
                var xObject = GetDict(xObjects, key);
                if (xObject.Elements.GetName("/Subtype") == "/Image")
                    return true;
                if (HasImage(GetDict(xObject, "/Resources")))
                    return true;
            }
            return false;
        }

        private static List<PdfDictionary> GetAnnotations(PdfPage page)
        {
            var annots = page.Elements.GetArray("/Annots");
            if (annots == null)
                return new List<PdfDictionary>();
            return annots.Elements.Select(item => (PdfDictionary)Resolve(item)).ToList();
        }

        private static PdfDictionary GetDict(PdfDictionary dict, string key)
        {
            var item = dict.Elements[key];
            return item == null ? null : (PdfDictionary)Resolve(item);
        }

        private static PdfItem Resolve(PdfItem item)
        {
            return item is PdfReference reference ? reference.Value : item;
        }
    }
}
