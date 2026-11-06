using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bloom.ImageProcessing;
using NUnit.Framework;
using SIL.Windows.Forms.ImageToolbox;

namespace BloomTests.ImageProcessing
{
    /// <summary>
    /// Produces the images and numbers for the transparency comparison report (BL-16778).
    /// It is not a test of anything: it runs every algorithm in
    /// <see cref="TransparencyAlgorithms.All"/> on every image listed in
    /// output/transparency-report/images.json and writes the results under
    /// output/transparency-report. docs/transparency-report/README.md says how to run it and
    /// how to turn the results into the report.
    /// </summary>
    [TestFixture]
    [Explicit]
    [Category("TransparencyReport")]
    public class TransparencyReportTests
    {
        private const int kPreviewWidth = 480;

        /// <summary>
        /// Runs every algorithm on every image and writes the full results, the previews and
        /// results.json.
        /// </summary>
        [Test]
        public void MakeTransparencyReportImages()
        {
            var reportFolder = Path.Combine(FindRepoRoot(), "output", "transparency-report");
            var imagesJsonPath = Path.Combine(reportFolder, "images.json");
            Assert.That(
                File.Exists(imagesJsonPath),
                $"Expected the list of images at {imagesJsonPath}; see docs/transparency-report/README.md"
            );
            var images = JsonNode.Parse(File.ReadAllText(imagesJsonPath)).AsArray();
            Assert.That(images.Count, Is.GreaterThan(0), "images.json lists no images");

            var outFolder = Path.Combine(reportFolder, "out");
            if (Directory.Exists(outFolder))
                Directory.Delete(outFolder, true);
            Directory.CreateDirectory(Path.Combine(outFolder, "original"));
            foreach (var algorithm in TransparencyAlgorithms.All)
                Directory.CreateDirectory(Path.Combine(outFolder, algorithm.Id));

            WarmUp();
            var errors = new List<string>();
            var imageResults = new JsonArray();
            foreach (var imageNode in images)
            {
                var id = (string)imageNode["id"];
                var file = (string)imageNode["file"];
                var imagePath = Path.Combine(reportFolder, "images", file);
                Console.Error.WriteLine($"[transparency-report] {id}");
                var imageResult = new JsonObject { ["id"] = id };
                imageResults.Add(imageResult);
                if (!File.Exists(imagePath))
                {
                    errors.Add($"{id}: missing file {imagePath}");
                    imageResult["error"] = $"missing file {file}";
                    continue;
                }

                using (var original = PalasoImage.FromFileRobustly(imagePath))
                {
                    imageResult["shouldMakeTransparent"] =
                        ImageUtils.ShouldMakeBackgroundTransparent(original);
                    imageResult["width"] = original.Image.Width;
                    imageResult["height"] = original.Image.Height;
                    imageResult["pixelFormat"] = original.Image.PixelFormat.ToString();
                    // These two decide whether the per-pixel rules run at all.
                    imageResult["alreadyHasTransparency"] = ImageUtils.HasTransparency(
                        original.Image
                    );
                    imageResult["indexedAndOpaque"] = ImageUtils.IsIndexedAndOpaque(original.Image);
                    using (var opaqueCopy = new Bitmap(original.Image))
                        SavePreview(
                            opaqueCopy,
                            Path.Combine(outFolder, "original", id + ".preview.png")
                        );

                    var algorithmResults = new JsonObject();
                    imageResult["algorithms"] = algorithmResults;
                    foreach (var algorithm in TransparencyAlgorithms.All)
                    {
                        try
                        {
                            // Each algorithm gets its own fresh copy, because production code
                            // may change the image it is given (e.g. the palette).
                            using (var copy = PalasoImage.FromFileRobustly(imagePath))
                            {
                                var stopwatch = Stopwatch.StartNew();
                                using (var result = algorithm.Run(copy))
                                {
                                    stopwatch.Stop();
                                    var folder = Path.Combine(outFolder, algorithm.Id);
                                    result.Save(Path.Combine(folder, id + ".png"), ImageFormat.Png);
                                    using (var argb = new Bitmap(result))
                                    {
                                        SavePreview(
                                            argb,
                                            Path.Combine(folder, id + ".preview.png")
                                        );
                                        var shares = CountAlpha(argb);
                                        algorithmResults[algorithm.Id] = new JsonObject
                                        {
                                            ["ms"] = Math.Round(
                                                stopwatch.Elapsed.TotalMilliseconds,
                                                1
                                            ),
                                            ["width"] = result.Width,
                                            ["height"] = result.Height,
                                            ["transparent"] = shares.transparent,
                                            ["partial"] = shares.partial,
                                            ["opaque"] = shares.opaque,
                                        };
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            errors.Add($"{id} / {algorithm.Id}: {e}");
                            algorithmResults[algorithm.Id] = new JsonObject
                            {
                                ["error"] = e.Message,
                            };
                        }
                    }
                }
            }

            var algorithmsJson = new JsonArray();
            foreach (var algorithm in TransparencyAlgorithms.All)
            {
                algorithmsJson.Add(
                    new JsonObject
                    {
                        ["id"] = algorithm.Id,
                        ["description"] = algorithm.Description,
                        ["isNewCandidate"] = algorithm.IsNewCandidate,
                    }
                );
            }
            var results = new JsonObject
            {
                ["generated"] = DateTime.UtcNow.ToString("o"),
                ["algorithms"] = algorithmsJson,
                ["images"] = imageResults,
            };
            File.WriteAllText(
                Path.Combine(reportFolder, "results.json"),
                results.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            );
            Console.Error.WriteLine(
                $"[transparency-report] wrote {Path.Combine(reportFolder, "results.json")}"
            );
            if (errors.Count > 0)
                Assert.Fail(
                    "Some images or algorithms failed (their results.json entries say so):\n"
                        + string.Join("\n", errors)
                );
        }

        /// <summary>
        /// Run every algorithm once on a small image, so that the time recorded for the first
        /// real image does not include compiling the code.
        /// </summary>
        private static void WarmUp()
        {
            var path = Path.Combine(Path.GetTempPath(), "transparency-report-warm-up.png");
            using (var bitmap = new Bitmap(16, 16, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.White);
                    g.DrawLine(Pens.Black, 0, 0, 15, 15);
                }
                bitmap.Save(path, ImageFormat.Png);
            }
            foreach (var algorithm in TransparencyAlgorithms.All)
            {
                using (var image = PalasoImage.FromFileRobustly(path))
                using (algorithm.Run(image)) { }
            }
            File.Delete(path);
        }

        /// <summary>
        /// Walk up from the test assembly until we reach the folder that holds src/BloomExe.
        /// </summary>
        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "BloomExe", "BloomExe.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not find the repository root above "
                    + TestContext.CurrentContext.TestDirectory
            );
        }

        /// <summary>
        /// Save a copy at most kPreviewWidth wide, keeping alpha, using high-quality interpolation.
        /// </summary>
        private static void SavePreview(Bitmap source, string path)
        {
            var width = Math.Min(kPreviewWidth, source.Width);
            var height = Math.Max(1, (int)Math.Round((double)source.Height * width / source.Width));
            using (var preview = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(preview))
            using (var attributes = new ImageAttributes())
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                // Without this, the bicubic filter blends the outermost pixels with transparency.
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(
                    source,
                    new Rectangle(0, 0, width, height),
                    0,
                    0,
                    source.Width,
                    source.Height,
                    GraphicsUnit.Pixel,
                    attributes
                );
                preview.Save(path, ImageFormat.Png);
            }
        }

        /// <summary>
        /// The share (0 to 1) of pixels that are fully transparent, partly transparent and opaque.
        /// </summary>
        private static (double transparent, double partial, double opaque) CountAlpha(Bitmap argb)
        {
            var rect = new Rectangle(0, 0, argb.Width, argb.Height);
            var data = argb.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * argb.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            argb.UnlockBits(data);
            long transparent = 0,
                opaque = 0;
            for (int y = 0; y < argb.Height; y++)
            {
                for (int x = 0; x < argb.Width; x++)
                {
                    var alpha = pixels[y * stride + x * 4 + 3];
                    if (alpha == 0)
                        transparent++;
                    else if (alpha == 255)
                        opaque++;
                }
            }
            double total = (double)argb.Width * argb.Height;
            return (
                Math.Round(transparent / total, 4),
                Math.Round((total - transparent - opaque) / total, 4),
                Math.Round(opaque / total, 4)
            );
        }
    }
}
