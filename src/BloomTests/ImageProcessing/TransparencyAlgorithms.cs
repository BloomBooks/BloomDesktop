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
    /// The algorithms compared by the transparency report (BL-16778): the production one, earlier
    /// versions copied from git history, and ideas proposed on the card. Each per-pixel rule is a
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
                Id = "original-6.5",
                Released = true,
                Date = "2026-06-04",
                Where = "Bloom 6.5 (commit ac1d454619, BL-16336)",
                Description =
                    "What Bloom 6.5 does before BL-16778 (since June 2026): pixels lighter than a set brightness become fully see-through, darker pixels stay solid, and pixels in between become partly see-through but keep their own colour.",
                Run = img =>
                    LikeProduction(img, source => BrightnessRamp(source, 220.0, 180.0, null)),
            },
            new TransparencyAlgorithm
            {
                Id = "proposed-6.5",
                Date = "2026-11-06",
                Where = "This branch, for 6.5 (BL-16778)",
                Description =
                    "Bloom's own code, as this branch changes it for 6.5 (BL-16778): each pixel is treated as ink over white paper. The darker its darkest colour channel, the more solid it is, and its colour becomes the ink that would give that pixel when laid over white. This is what Bloom did on May 19 2026 (commit 625c7891ff), brought back for BL-16778.",
                Run = RuntimeImageProcessor.MakePngBackgroundTransparent,
            },
            new TransparencyAlgorithm
            {
                Id = "colorkey-253",
                Released = true,
                Date = "2017-11-27",
                Where = "Bloom 6.4 and earlier (in the code by 2017, commit b396ab1e1c)",
                Description =
                    "What Bloom 6.4 and earlier do: only pixels whose red, green and blue are all 253 or more become see-through, and every other pixel stays solid.",
                Run = ColorKey253,
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
            new TransparencyAlgorithm
            {
                Id = "ramp-force-grey",
                Date = "2026-05-22",
                Where = "BL-16336 work in May (commit 7e8bdde10c), on a branch never merged",
                Description =
                    "The original-6.5 rule, except that partly see-through pixels that are nearly grey are made exactly grey.",
                Run = img => LikeProduction(img, RampForceGrey),
            },
            new TransparencyAlgorithm
            {
                Id = "narrow-ramp",
                Where = "Proposed on BL-16778",
                Description =
                    "The original-6.5 rule, but only very light pixels become see-through, and pixels a little darker stay solid.",
                Run = img => LikeProduction(img, NarrowRamp),
            },
            new TransparencyAlgorithm
            {
                Id = "edge-fill",
                Where = "Proposed on BL-16778",
                Description =
                    "Uses the original-6.5 rule only on the light area that connects to the edge of the picture (plus two pixels around it), so light areas enclosed by lines stay solid.",
                Run = img => LikeProduction(img, EdgeFill),
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

        /// <summary>
        /// RemoveWhiteBackground from commit 7e8bdde10c (John Thomson, May 22 2026, on a branch
        /// that was not merged): a brightness ramp (transparent at 220 or lighter, opaque at 180
        /// or darker) that keeps RGB, except that partly transparent pixels whose channels differ
        /// by 6 or less are set to exactly grey. The Debug.WriteLine timing lines are left out.
        /// </summary>
        public static Image RampForceGrey(Bitmap source)
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

            // 2. Build brightness thresholds for a transparency ramp.
            // t0: pixels at/above this brightness become fully transparent.
            // t1: pixels at/below this brightness stay fully opaque.
            // Values between t1..t0 get linearly ramped alpha.
            const double kTransparentBrightnessThreshold = 220.0;
            const double kOpaqueBrightnessThreshold = 180.0;
            var detectedBackgroundBrightness = 0.299 * maxR + 0.587 * maxG + 0.114 * maxB;
            var transparentBrightnessThreshold = Math.Min(
                kTransparentBrightnessThreshold,
                detectedBackgroundBrightness
            );
            var opaqueBrightnessThreshold = Math.Min(
                kOpaqueBrightnessThreshold,
                transparentBrightnessThreshold - 1.0
            );

            // 3. For each pixel, set alpha from perceptual brightness using t0/t1 ramp.
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
                    var brightness = 0.299 * r + 0.587 * g + 0.114 * b;
                    byte newAlpha;
                    if (brightness >= transparentBrightnessThreshold)
                    {
                        newAlpha = 0;
                    }
                    else if (brightness <= opaqueBrightnessThreshold)
                    {
                        newAlpha = 255;
                    }
                    else
                    {
                        newAlpha = (byte)(
                            255.0
                            * (transparentBrightnessThreshold - brightness)
                            / (transparentBrightnessThreshold - opaqueBrightnessThreshold)
                        );
                    }
                    pixels[idx + 3] = newAlpha;
                    if (newAlpha > 0 && newAlpha < 255)
                    {
                        var maxChannel = Math.Max(r, Math.Max(g, b));
                        var minChannel = Math.Min(r, Math.Min(g, b));
                        if (maxChannel - minChannel <= 6)
                        {
                            var luma = (byte)Math.Round(brightness);
                            pixels[idx] = luma;
                            pixels[idx + 1] = luma;
                            pixels[idx + 2] = luma;
                        }
                    }
                }
            }
            Marshal.Copy(pixels, 0, dstData.Scan0, pixels.Length);
            result.UnlockBits(dstData);
            return result;
        }

        /// <summary>
        /// Proposed on BL-16778: the original-6.5 rule with the ramp moved up, so pixels at
        /// brightness 245 or more are transparent and pixels at 225 or less are opaque.
        /// </summary>
        public static Image NarrowRamp(Bitmap source)
        {
            return BrightnessRamp(source, 245.0, 225.0, null);
        }

        /// <summary>
        /// Proposed on BL-16778: flood-fill (4-connected) from every border pixel through pixels
        /// with brightness 230 or more, grow the filled area by 2 pixels in every direction, and
        /// apply the 220 to 180 brightness ramp only inside it. Pixels outside it stay opaque.
        /// </summary>
        public static Image EdgeFill(Bitmap source)
        {
            const double kFillBrightness = 230.0;
            const int kGrowBy = 2;
            var width = source.Width;
            var height = source.Height;
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

            var light = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * stride + x * 4;
                    var brightness =
                        0.299 * pixels[idx + 2] + 0.587 * pixels[idx + 1] + 0.114 * pixels[idx];
                    light[y * width + x] = brightness >= kFillBrightness;
                }
            }

            var filled = new bool[width * height];
            var queue = new Queue<int>();
            void Seed(int x, int y)
            {
                var i = y * width + x;
                if (light[i] && !filled[i])
                {
                    filled[i] = true;
                    queue.Enqueue(i);
                }
            }
            for (int x = 0; x < width; x++)
            {
                Seed(x, 0);
                Seed(x, height - 1);
            }
            for (int y = 0; y < height; y++)
            {
                Seed(0, y);
                Seed(width - 1, y);
            }
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                int x = i % width;
                int y = i / width;
                if (x > 0)
                    Seed(x - 1, y);
                if (x < width - 1)
                    Seed(x + 1, y);
                if (y > 0)
                    Seed(x, y - 1);
                if (y < height - 1)
                    Seed(x, y + 1);
            }

            // Grow the filled area: every pixel within kGrowBy pixels (horizontally and
            // vertically, so a square neighbourhood) of a filled pixel is included.
            var grown = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!filled[y * width + x])
                        continue;
                    for (int dy = -kGrowBy; dy <= kGrowBy; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= height)
                            continue;
                        for (int dx = -kGrowBy; dx <= kGrowBy; dx++)
                        {
                            int nx = x + dx;
                            if (nx >= 0 && nx < width)
                                grown[ny * width + nx] = true;
                        }
                    }
                }
            }
            return BrightnessRamp(source, 220.0, 180.0, grown);
        }

        /// <summary>
        /// The brightness ramp that RuntimeImageProcessor.RemoveWhiteBackground used from June 2026 (commits ac1d454619 and 2c7ec06e6c) until BL-16778, with its
        /// two thresholds as parameters. When <paramref name="onlyHere"/> is given, pixels where it
        /// is false are left fully opaque. The ramp is never narrower than the distance between
        /// the two thresholds given (that version hard-coded that distance as 40).
        /// </summary>
        private static Image BrightnessRamp(
            Bitmap source,
            double kTransparentBrightnessThreshold,
            double kOpaqueBrightnessThreshold,
            bool[] onlyHere
        )
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

            double kMinRampWidth = kTransparentBrightnessThreshold - kOpaqueBrightnessThreshold;
            const double kMinOpaqueBrightnessThreshold = 60.0;
            var detectedBackgroundBrightness = 0.299 * maxR + 0.587 * maxG + 0.114 * maxB;
            var transparentBrightnessThreshold = Math.Min(
                kTransparentBrightnessThreshold,
                detectedBackgroundBrightness
            );
            var opaqueBrightnessThreshold = Math.Max(
                kMinOpaqueBrightnessThreshold,
                Math.Min(kOpaqueBrightnessThreshold, transparentBrightnessThreshold - kMinRampWidth)
            );

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * stride + x * 4;
                    if (onlyHere != null && !onlyHere[y * width + x])
                    {
                        pixels[idx + 3] = 255;
                        continue;
                    }
                    int b = pixels[idx];
                    int g = pixels[idx + 1];
                    int r = pixels[idx + 2];
                    var brightness = 0.299 * r + 0.587 * g + 0.114 * b;
                    byte newAlpha;
                    if (brightness >= transparentBrightnessThreshold)
                    {
                        newAlpha = 0;
                    }
                    else if (brightness <= opaqueBrightnessThreshold)
                    {
                        newAlpha = 255;
                    }
                    else
                    {
                        newAlpha = (byte)(
                            255.0
                            * (transparentBrightnessThreshold - brightness)
                            / (transparentBrightnessThreshold - opaqueBrightnessThreshold)
                        );
                    }
                    pixels[idx + 3] = newAlpha;
                }
            }
            Marshal.Copy(pixels, 0, dstData.Scan0, pixels.Length);
            result.UnlockBits(dstData);
            return result;
        }
    }
}
