using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.SafeXml;
using Bloom.web.controllers;
using BloomTemp;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using SIL.IO;

namespace Bloom.Publish.PDF
{
    /// <summary>
    /// A video that should be embedded in the PDF where its bloom-videoContainer was printed.
    /// </summary>
    public class PdfVideo
    {
        public string FilePath;

        /// <summary>
        /// The part of the video the book plays, as the "start,end" of the #t= fragment of the video's
        /// url; empty if the book plays all of it.
        /// </summary>
        public string RawTimings;
    }

    /// <summary>
    /// Embeds the book's videos in the PDF as playable Screen annotations (ISO 32000 12.5.6.18)
    /// with Rendition actions (12.6.4.13). Acrobat Reader and Foxit play these; browser PDF viewers
    /// show only the printed frame.
    ///
    /// The browser that prints the PDF is the only thing that knows where each video lands on the page,
    /// so before printing we wrap each video in a link to a marker URL. Chromium turns that link into
    /// a Link annotation with the video's rectangle, and Ghostscript keeps it. After Ghostscript,
    /// we replace each marker link with a Screen annotation that embeds the video file.
    ///
    /// The page shows a picture of the video wherever it is not played (on paper, in browser PDF
    /// viewers, and in Acrobat until it is clicked). We print a frame from just after the start of the
    /// part the book plays, where a video that fades in is past its dark opening.
    /// </summary>
    public static class PdfVideoEmbedder
    {
        private const string kMarkerUrlPrefix = "https://bloom-pdf-video.invalid/";

        // How far after the start of the part the book plays the printed frame is.
        private const decimal kPrintedFrameOffsetSeconds = 0.5m;

        /// <summary>
        /// Wrap each video in the dom in a link to a marker URL whose last segment is the video's index
        /// in the returned list. The link fills the video container, so it gets the container's rectangle.
        /// Where we can, replace the video with a picture of the frame to print.
        /// </summary>
        public static List<PdfVideo> MarkVideosForPdf(HtmlDom dom, string bookFolderPath)
        {
            var videos = new List<PdfVideo>();
            foreach (
                var container in HtmlDom
                    .SelectChildVideoElements(dom.RawDom.DocumentElement)
                    .Cast<SafeXmlElement>()
            )
            {
                var video = container.GetChildWithName("video");
                var source = video?.GetChildWithName("source");
                var src = source?.GetAttribute("src");
                if (string.IsNullOrEmpty(src))
                    continue; // an empty video placeholder; nothing to embed
                var relativePath = SignLanguageApi.StripTimingFromVideoUrl(src, out var rawTimings);
                var path = Path.GetFullPath(Path.Combine(bookFolderPath, relativePath));
                if (!RobustFile.Exists(path))
                    continue;

                var link = dom.RawDom.CreateElement("a");
                link.SetAttribute(
                    "href",
                    kMarkerUrlPrefix + videos.Count.ToString(CultureInfo.InvariantCulture)
                );
                link.SetAttribute("style", "display:block;width:100%;height:100%");
                container.InsertBefore(link, video);
                var frame = GetFrameToPrint(path, rawTimings);
                if (frame == null)
                {
                    // The browser prints whatever frame it has loaded.
                    link.AppendChild(video);
                }
                else
                {
                    // Sized and placed as basePage.less places the video: shrunk to fit, centered.
                    var img = dom.RawDom.CreateElement("img");
                    img.SetAttribute(
                        "src",
                        "data:image/png;base64," + Convert.ToBase64String(frame)
                    );
                    img.SetAttribute(
                        "style",
                        "display:block;width:100%;height:100%;object-fit:contain"
                    );
                    link.AppendChild(img);
                    container.RemoveChild(video);
                }

                videos.Add(new PdfVideo { FilePath = path, RawTimings = rawTimings });
            }
            return videos;
        }

        /// <summary>
        /// A PNG of the frame to print for the video, or null if we cannot get one.
        /// </summary>
        private static byte[] GetFrameToPrint(string videoPath, string rawTimings)
        {
            var timings = new[] { 0.0m, 0.0m };
            SignLanguageApi.ConvertRawTimingsToDecimalArray(rawTimings, timings);
            var start = timings[0];
            var end = timings[1];
            var offset = kPrintedFrameOffsetSeconds;
            if (end > start && end - start < 2 * offset)
                offset = (end - start) / 2; // a very short clip: its middle
            // A video shorter than the offset has no frame there, so fall back to its start.
            return SignLanguageApi.GetVideoFrameAsPng(videoPath, start + offset)
                ?? SignLanguageApi.GetVideoFrameAsPng(videoPath, start);
        }

        /// <summary>
        /// Replace each marker link that MarkVideosForPdf caused with a Screen annotation that plays the
        /// corresponding video. If embed is false, just remove the marker links.
        /// </summary>
        public static void ReplaceMarkerLinks(PdfDocument pdfDoc, List<PdfVideo> videos, bool embed)
        {
            // Keyed by the video's path and timings.
            var embeddedFiles = new Dictionary<string, PdfDictionary>();
            using (var trimFolder = new TemporaryFolder("PdfVideos-" + System.Guid.NewGuid()))
            {
                ReplaceMarkerLinks(pdfDoc, videos, embed, embeddedFiles, trimFolder);
            }
        }

        private static void ReplaceMarkerLinks(
            PdfDocument pdfDoc,
            List<PdfVideo> videos,
            bool embed,
            Dictionary<string, PdfDictionary> embeddedFiles,
            TemporaryFolder trimFolder
        )
        {
            foreach (var page in pdfDoc.Pages)
            {
                var annots = page.Elements.GetArray("/Annots");
                if (annots == null)
                    continue;
                for (var i = annots.Elements.Count - 1; i >= 0; i--)
                {
                    var annot = Resolve(annots.Elements[i]) as PdfDictionary;
                    var index = GetMarkerIndex(annot);
                    if (index < 0)
                        continue;
                    if (!embed)
                    {
                        annots.Elements.RemoveAt(i);
                        continue;
                    }
                    var screen = MakeScreenAnnotation(
                        pdfDoc,
                        page,
                        annot.Elements.GetArray("/Rect"),
                        videos[index],
                        embeddedFiles,
                        trimFolder.FolderPath
                    );
                    annots.Elements[i] = screen.Reference;
                }
            }
        }

        private static PdfItem Resolve(PdfItem item)
        {
            return item is PdfReference reference ? reference.Value : item;
        }

        /// <summary>
        /// The index in the video list that this annotation's marker URL names, or -1 if it is not a marker link.
        /// </summary>
        private static int GetMarkerIndex(PdfDictionary annot)
        {
            if (annot?.Elements.GetName("/Subtype") != "/Link")
                return -1;
            var action = Resolve(annot.Elements["/A"]) as PdfDictionary;
            var uri = action?.Elements.GetString("/URI");
            if (uri == null || !uri.StartsWith(kMarkerUrlPrefix))
                return -1;
            return int.Parse(uri.Substring(kMarkerUrlPrefix.Length), CultureInfo.InvariantCulture);
        }

        private static PdfDictionary MakeScreenAnnotation(
            PdfDocument pdfDoc,
            PdfPage page,
            PdfArray rect,
            PdfVideo video,
            Dictionary<string, PdfDictionary> embeddedFiles,
            string trimFolder
        )
        {
            var fileName = Path.GetFileName(video.FilePath);
            var contentType = fileName.EndsWith(".webm") ? "video/webm" : "video/mp4";

            // A video used on several pages is stored once.
            var key = video.FilePath + "#" + video.RawTimings;
            if (!embeddedFiles.TryGetValue(key, out var embeddedFile))
            {
                var bytes = ReadPlayedPart(video, trimFolder);
                embeddedFile = new PdfDictionary(pdfDoc);
                embeddedFile.Elements.SetName("/Type", "/EmbeddedFile");
                embeddedFile.Elements.SetName("/Subtype", "/" + contentType.Replace("/", "#2F"));
                var parameters = new PdfDictionary(pdfDoc);
                parameters.Elements.SetInteger("/Size", bytes.Length);
                embeddedFile.Elements["/Params"] = parameters;
                embeddedFile.CreateStream(bytes);
                pdfDoc.Internals.AddObject(embeddedFile);
                embeddedFiles[key] = embeddedFile;
            }

            var fileSpec = new PdfDictionary(pdfDoc);
            fileSpec.Elements.SetName("/Type", "/Filespec");
            fileSpec.Elements.SetString("/F", fileName);
            fileSpec.Elements.SetString("/UF", fileName);
            var ef = new PdfDictionary(pdfDoc);
            ef.Elements.SetReference("/F", embeddedFile);
            fileSpec.Elements["/EF"] = ef;

            // TEMPACCESS lets the viewer write the video to a temp file that its player can read.
            var permissions = new PdfDictionary(pdfDoc);
            permissions.Elements.SetString("/TF", "TEMPACCESS");

            var clipData = new PdfDictionary(pdfDoc);
            clipData.Elements.SetName("/Type", "/MediaClip");
            clipData.Elements.SetName("/S", "/MCD");
            clipData.Elements.SetString("/N", fileName);
            clipData.Elements.SetString("/CT", contentType);
            clipData.Elements["/D"] = fileSpec;
            clipData.Elements["/P"] = permissions;

            // Show the player's controls. /F 0 is "meet": scale to fit the rectangle, keeping the aspect ratio.
            var mustHonor = new PdfDictionary(pdfDoc);
            mustHonor.Elements.SetBoolean("/C", true);
            mustHonor.Elements.SetInteger("/F", 0);
            var playParams = new PdfDictionary(pdfDoc);
            playParams.Elements.SetName("/Type", "/MediaPlayParams");
            playParams.Elements["/BE"] = mustHonor;

            var rendition = new PdfDictionary(pdfDoc);
            rendition.Elements.SetName("/Type", "/Rendition");
            rendition.Elements.SetName("/S", "/MR");
            rendition.Elements.SetString("/N", fileName);
            rendition.Elements["/C"] = clipData;
            rendition.Elements["/P"] = playParams;

            var screen = new PdfDictionary(pdfDoc);
            pdfDoc.Internals.AddObject(screen);
            screen.Elements.SetName("/Type", "/Annot");
            screen.Elements.SetName("/Subtype", "/Screen");
            screen.Elements["/Rect"] = rect.Clone();
            screen.Elements.SetReference("/P", page);
            screen.Elements.SetInteger("/F", 4); // Print
            screen.Elements.SetString("/T", fileName);

            // Play the rendition in this annotation when it is clicked (OP 0 also stops any other video).
            var action = new PdfDictionary(pdfDoc);
            action.Elements.SetName("/Type", "/Action");
            action.Elements.SetName("/S", "/Rendition");
            action.Elements.SetInteger("/OP", 0);
            action.Elements.SetReference("/AN", screen);
            action.Elements["/R"] = rendition;
            screen.Elements["/A"] = action;
            return screen;
        }

        /// <summary>
        /// The bytes of the part of the video that the book plays, trimmed as every form of publishing
        /// trims it (SignLanguageApi.TrimVideoForPublishing). The PDF is made from the book's own folder,
        /// so the trimmed video goes in trimFolder, which the caller deletes.
        /// </summary>
        private static byte[] ReadPlayedPart(PdfVideo video, string trimFolder)
        {
            return RobustFile.ReadAllBytes(
                SignLanguageApi.TrimVideoForPublishing(video.FilePath, video.RawTimings, trimFolder)
            );
        }
    }
}
