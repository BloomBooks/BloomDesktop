using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Bloom.ImageProcessing;
using SIL.Windows.Forms.ImageToolbox;

namespace BloomTests.ImageProcessing
{
    /// <summary>
    /// One way of making the white background of a line-art image transparent, as compared by
    /// <see cref="TransparencyReportTests"/>.
    /// </summary>
    public class TransparencyAlgorithm
    {
        /// <summary>Short name used for output folders and in the report.</summary>
        public string Id;

        /// <summary>One plain sentence, shown at the top of the report.</summary>
        public string Description;

        /// <summary>True for the algorithm the report should point out as the new candidate.</summary>
        public bool IsNewCandidate;

        /// <summary>
        /// When this rule was written or shipped, as yyyy-MM-dd, for the report's timeline; null for
        /// an idea that was never committed.
        /// </summary>
        public string Date;

        /// <summary>Which Bloom version, or which piece of work, the rule belongs to, for the timeline.</summary>
        public string Where;

        /// <summary>True when a released version of Bloom used this rule.</summary>
        public bool Released;

        /// <summary>Takes the original image and returns what Bloom would display.</summary>
        public Func<PalasoImage, Image> Run;
    }

    /// <summary>
    /// The algorithms compared by the transparency report (BL-16778): the production one and
    /// earlier versions copied from git history. Each per-pixel rule is a
    /// function from a 32bpp ARGB bitmap to a new 32bpp ARGB bitmap, and all but the oldest go
    /// through <see cref="LikeProduction"/>, so the results differ only in that rule.
    ///
    /// To add an algorithm, write one more rule function and add one line to <see cref="All"/>.
    /// </summary>
    public static class TransparencyAlgorithms
    {
        /// <summary>
        /// Every algorithm in the report, in the order its rows appear.
        /// </summary>
        public static readonly List<TransparencyAlgorithm> All = new List<TransparencyAlgorithm>
        {
            new TransparencyAlgorithm
            {
                Id = "Before 6.5",
                Released = true,
                Date = "2017-11-27",
                Where = "Bloom 6.4 and earlier (in the code by 2017, commit b396ab1e1c)",
                Description =
                    "What Bloom 6.4 and earlier do: only pixels whose red, green and blue are all 253 or more become see-through, and every other pixel stays solid.",
                Run = ColorKey253,
            },
            new TransparencyAlgorithm
            {
                Id = "Bloom 6.5",
                Date = "2026-11-06",
                Where = "Bloom 6.5 (BL-16778)",
                Description =
                    "Bloom's own code: each pixel is treated as ink over the picture's paper. The paper is the most common value of the darkest colour channel among pixels from 215 up; when it is below 250 (a scan of gray paper), the picture is first brightened, the same factor for all three channels, so that anything within 10 of the paper becomes white. Then the darker a pixel's darkest colour channel, the more solid it is, and its colour becomes the ink that would give that pixel when laid over white. On white paper this is what Bloom did on May 19 2026 (commit 625c7891ff).",
                Run = RuntimeImageProcessor.MakePngBackgroundTransparent,
            },
            new TransparencyAlgorithm
            {
                Id = "ink-over-white",
                Date = "2026-05-19",
                Where =
                    "BL-16336 work in May (commit 625c7891ff), merged into 6.4 and reverted on June 8",
                Description =
                    "Each pixel is treated as ink over white paper, with no search for the paper: Bloom 6.5 without its brightening of gray scanned paper.",
                Run = img => LikeProduction(img, source => PaperLevel(source, 256)),
            },
            new TransparencyAlgorithm
            {
                Id = "deficit-keep-rgb",
                Date = "2026-05-21",
                Where =
                    "BL-16336 work in May (commit 2a7377b9fe), merged into 6.4 and reverted on June 8",
                Description =
                    "A pixel is more solid the further its weakest colour channel falls below the lightest colour in the image, and it keeps its own colour.",
                Run = img => LikeProduction(img, DeficitKeepRgb),
            },
        };

        /// <summary>
        /// A copy of the production <c>RuntimeImageProcessor.MakePngBackgroundTransparent</c>
        /// that takes the per-pixel rule as a parameter: skip images that already have
        /// transparency, make the palette's near-white entries transparent for an indexed
        /// image, otherwise apply the rule at full size and then scale to at most 1000 wide.
        /// </summary>
        public static Image LikeProduction(PalasoImage originalImage, Func<Bitmap, Image> rule)
        {
            if (ImageUtils.HasTransparency(originalImage.Image))
                return originalImage.Image.Clone() as Image;

            if (
                SIL.PlatformUtilities.Platform.IsWindows
                && ImageUtils.IsIndexedAndOpaque(originalImage.Image)
            )
            {
                var revisedBitmap = originalImage.Image.Clone() as Image;
                revisedBitmap.Palette = GivePaletteTransparentBackground(revisedBitmap);
                return revisedBitmap;
            }
            var destinationWidth = originalImage.Image.Width;
            var destinationHeight = originalImage.Image.Height;
            using (
                var scaledSource = new Bitmap(
                    destinationWidth,
                    destinationHeight,
                    PixelFormat.Format32bppArgb
                )
            )
            using (var g = Graphics.FromImage(scaledSource))
            {
                g.DrawImage(originalImage.Image, 0, 0, destinationWidth, destinationHeight);
                var transparentImage = rule(scaledSource);

                var scaledWidth = Math.Min(1000, transparentImage.Width);
                if (scaledWidth >= transparentImage.Width)
                    return transparentImage;

                var scaledHeight = (int)(
                    (float)transparentImage.Height
                    * ((float)scaledWidth / (float)transparentImage.Width)
                );
                var scaledTransparentImage = new Bitmap(
                    scaledWidth,
                    scaledHeight,
                    PixelFormat.Format32bppArgb
                );
                using (var gScaled = Graphics.FromImage(scaledTransparentImage))
                {
                    gScaled.DrawImage(transparentImage, 0, 0, scaledWidth, scaledHeight);
                }
                transparentImage.Dispose();
                return scaledTransparentImage;
            }
        }

        /// <summary>
        /// Copy of the private production <c>RuntimeImageProcessor.GivePaletteTransparentBackground</c>,
        /// which has not changed across the versions compared here.
        /// </summary>
        private static ColorPalette GivePaletteTransparentBackground(Image bitmap)
        {
            Debug.Assert((bitmap.PixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed);
            var palette = bitmap.Palette;
            for (var i = 0; i < palette.Entries.Length; i++)
            {
                var color = palette.Entries[i];
                if (ImageUtils.IsNearWhite(color))
                {
                    palette.Entries[i] = Color.FromArgb(0, color.R, color.G, color.B);
                }
            }
            return palette;
        }

        private static ImageAttributes _convertWhiteToTransparent;

        /// <summary>
        /// The colour key that the version before commit 625c7891ff used: every pixel from
        /// (253,253,253) to white becomes fully transparent.
        /// </summary>
        private static ImageAttributes ConvertWhiteToTransparent
        {
            get
            {
                if (_convertWhiteToTransparent == null)
                {
                    _convertWhiteToTransparent = new ImageAttributes();
                    _convertWhiteToTransparent.SetColorKey(
                        Color.FromArgb(253, 253, 253),
                        Color.White
                    );
                }
                return _convertWhiteToTransparent;
            }
        }

        /// <summary>
        /// MakePngBackgroundTransparent as it was before commit 625c7891ff (John Hatton, May 19 2026):
        /// shrink to at most 1000 pixels wide, then draw through a GDI+ colour key that makes
        /// pixels from (253,253,253) to white fully transparent and leaves every other pixel opaque.
        /// This one keeps its own wrapper because it scaled before removing the background.
        /// </summary>
        public static Image ColorKey253(PalasoImage originalImage)
        {
            if (ImageUtils.HasTransparency(originalImage.Image))
                return originalImage.Image.Clone() as Image;

            if (
                SIL.PlatformUtilities.Platform.IsWindows
                && ImageUtils.IsIndexedAndOpaque(originalImage.Image)
            )
            {
                var revisedBitmap = originalImage.Image.Clone() as Image;
                revisedBitmap.Palette = GivePaletteTransparentBackground(revisedBitmap);
                return revisedBitmap;
            }
            //impose a maximum size because in BL-2871 "Opposites" had about 6k x 6k and we got an ArgumentException
            //from the new BitMap()
            var destinationWidth = Math.Min(1000, originalImage.Image.Width);
            var destinationHeight = (int)(
                (float)originalImage.Image.Height
                * ((float)destinationWidth / (float)originalImage.Image.Width)
            );
            var processedBitmap = new Bitmap(destinationWidth, destinationHeight);
            using (var g = Graphics.FromImage(processedBitmap))
            {
                var destRect = new Rectangle(0, 0, destinationWidth, destinationHeight);
                lock (ConvertWhiteToTransparent)
                {
                    g.DrawImage(
                        originalImage.Image,
                        destRect,
                        0,
                        0,
                        originalImage.Image.Width,
                        originalImage.Image.Height,
                        GraphicsUnit.Pixel,
                        ConvertWhiteToTransparent
                    );
                }
            }
            return processedBitmap;
        }

        /// <summary>
        /// Ink over white, as in the production RemoveWhiteBackground, but against the picture's
        /// own paper. The paper is the most common darkest-channel value from
        /// <paramref name="darkestPossiblePaper"/> up; when it is below 250, every channel is
        /// multiplied by 255 / (paper - 10), so the paper and its scanner grain become white
        /// before the ink is worked out. A <paramref name="darkestPossiblePaper"/> of 256 never
        /// brightens, which gives plain ink over white.
        /// </summary>
        public static Image PaperLevel(Bitmap source, int darkestPossiblePaper)
        {
            var width = source.Width;
            var height = source.Height;
            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var srcData = source.LockBits(
                rect,
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb
            );
            var stride = Math.Abs(srcData.Stride);
            var pixels = new byte[stride * height];
            Marshal.Copy(srcData.Scan0, pixels, 0, pixels.Length);
            source.UnlockBits(srcData);

            var histogram = new int[256];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int idx = y * stride + x * 4;
                histogram[Math.Min(pixels[idx], Math.Min(pixels[idx + 1], pixels[idx + 2]))]++;
            }
            int paper = 255;
            for (int v = darkestPossiblePaper; v < 256; v++)
                if (histogram[v] > histogram[paper])
                    paper = v;
            double scale = paper < 250 ? 255.0 / (paper - 10) : 1.0;

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int idx = y * stride + x * 4;
                int b = (int)Math.Min(255, Math.Round(pixels[idx] * scale));
                int g = (int)Math.Min(255, Math.Round(pixels[idx + 1] * scale));
                int r = (int)Math.Min(255, Math.Round(pixels[idx + 2] * scale));
                int minChannel = Math.Min(r, Math.Min(g, b));
                int alpha = 255 - minChannel;
                pixels[idx + 3] = (byte)alpha;
                if (alpha > 0)
                {
                    pixels[idx] = (byte)Math.Round((b - minChannel) * 255.0 / alpha);
                    pixels[idx + 1] = (byte)Math.Round((g - minChannel) * 255.0 / alpha);
                    pixels[idx + 2] = (byte)Math.Round((r - minChannel) * 255.0 / alpha);
                }
                else
                {
                    pixels[idx] = 0;
                    pixels[idx + 1] = 0;
                    pixels[idx + 2] = 0;
                }
            }

            var dstData = result.LockBits(
                rect,
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb
            );
            Marshal.Copy(pixels, 0, dstData.Scan0, pixels.Length);
            result.UnlockBits(dstData);
            return result;
        }

        /// <summary>
        /// RemoveWhiteBackground from commit 2a7377b9fe (John Thomson, May 21 2026): alpha comes
        /// from the largest amount by which any one channel falls below the lightest colour in the
        /// image, scaled so the largest such amount is opaque; RGB is left unchanged.
        /// </summary>
        public static Image DeficitKeepRgb(Bitmap source)
        {
            var width = source.Width;
            var height = source.Height;
            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var srcData = source.LockBits(
                rect,
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb
            );
            var dstData = result.LockBits(
                rect,
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb
            );
            var stride = Math.Abs(srcData.Stride);
            var pixels = new byte[stride * height];
            Marshal.Copy(srcData.Scan0, pixels, 0, pixels.Length);
            source.UnlockBits(srcData);

            // 1. Find the lightest color in the image (max R+G+B sum)
            int maxR = 0,
                maxG = 0,
                maxB = 0;
            int maxSum = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * stride + x * 4;
                    int b = pixels[idx];
                    int g = pixels[idx + 1];
                    int r = pixels[idx + 2];
                    int sum = r + g + b;
                    if (sum > maxSum)
                    {
                        maxSum = sum;
                        maxR = r;
                        maxG = g;
                        maxB = b;
                    }
                }
            }

            // 2. Compute the largest channel deficit from the lightest color (for normalization)
            // This maps the palest color to alpha=0 while keeping strong saturated colors opaque.
            int maxDeficit = 1; // avoid divide by zero
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * stride + x * 4;
                    int b = pixels[idx];
                    int g = pixels[idx + 1];
                    int r = pixels[idx + 2];
                    int deficit = Math.Max(maxR - r, Math.Max(maxG - g, maxB - b));
                    if (deficit > maxDeficit)
                        maxDeficit = deficit;
                }
            }

            // 3. For each pixel, set alpha based on distance from the lightest color.
            // Keep this pass after the two read-only analysis passes above: we mutate pixels in place.
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * stride + x * 4;
                    int b = pixels[idx];
                    int g = pixels[idx + 1];
                    int r = pixels[idx + 2];
                    // pixels[idx + 3] is source alpha, always 255 for opaque input.
                    // Use the strongest per-channel deficit from the background candidate.
                    int deficit = Math.Max(maxR - r, Math.Max(maxG - g, maxB - b));
                    // alpha = 0 for lightest color, 255 for largest deficit from it
                    byte newAlpha = (byte)(255.0 * deficit / maxDeficit);
                    pixels[idx + 3] = newAlpha;
                    // Keep RGB as-is to preserve color intensity (e.g. pure red stays red).
                }
            }
            Marshal.Copy(pixels, 0, dstData.Scan0, pixels.Length);
            result.UnlockBits(dstData);
            return result;
        }
    }
}
