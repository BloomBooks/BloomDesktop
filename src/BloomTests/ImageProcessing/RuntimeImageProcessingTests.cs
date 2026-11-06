using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Bloom;
using Bloom.ImageProcessing;
using NUnit.Framework;
using SIL.IO;
using SIL.Windows.Forms.ImageToolbox;

namespace BloomTests.RuntimeImageProcessing
{
    [TestFixture]
    public class RuntimeImageProcessingTests
    {
#if ShrinkLargeImages
        [Test]
        public void GetWideImage_ReturnsShrunkImageWithCorrectProportions()
        {
            using (
                var cache = new RuntimeImageProcessor(new BookRenamedEvent())
                {
                    TargetDimension = 100,
                }
            )
            using (var file = MakeTempPNGImage(200, 80))
            {
                using (
                    var img = ImageUtils.GetImageFromFile(cache.GetPathToAdjustedImage(file.Path))
                )
                {
                    Assert.AreEqual(100, img.Width);
                    Assert.AreEqual(40, img.Height);
                }
            }
        }

        [Test]
        public void GetJPG_ReturnsShrunkJPG()
        {
            using (
                var cache = new RuntimeImageProcessor(new BookRenamedEvent())
                {
                    TargetDimension = 100,
                }
            )
            using (var file = MakeTempJPGImage(200, 80))
            {
                var pathToResizedImage = cache.GetPathToAdjustedImage(file.Path);
                using (var img = ImageUtils.GetImageFromFile(pathToResizedImage))
                {
                    Assert.AreEqual(".jpg", Path.GetExtension(pathToResizedImage));

                    //TODO: why does this always report PNG format? Checks by hand of the file show it as jpg
                    //Assert.AreEqual(ImageFormat.Jpeg.Guid, img.RawFormat.Guid);

                    Assert.AreEqual(100, img.Width);
                    Assert.AreEqual(40, img.Height);
                }
            }
        }
#endif

        [Test]
        public void GetTinyImage_DoesNotChangeSize()
        {
            using (
                var cache = new RuntimeImageProcessor(new BookRenamedEvent())
                {
                    TargetDimension = 100,
                }
            )
            using (var file = MakeTempPNGImage(10, 10))
            {
                using (
                    var img = RobustImageIO.GetImageFromFile(
                        cache.GetPathToAdjustedImage(file.Path)
                    )
                )
                {
                    Assert.AreEqual(10, img.Width);
                }
            }
        }

        /// <summary>
        /// Making the white background of a line drawing transparent treats each pixel as ink over
        /// white paper (BL-16778): white becomes fully transparent, black stays opaque, a light gray
        /// becomes partly transparent black (so on a coloured page it shows as a shade of that
        /// colour, not as a gray tinted towards it), and every pixel drawn over white gives back
        /// the original colour.
        /// </summary>
        [Test]
        public void MakePngBackgroundTransparent_TreatsPixelsAsInkOverWhite()
        {
            var white = Color.FromArgb(255, 255, 255);
            var black = Color.FromArgb(0, 0, 0);
            var lightGray = Color.FromArgb(200, 200, 200);
            var paleRed = Color.FromArgb(255, 155, 155);
            var colours = new[] { white, black, lightGray, paleRed };
            using (var source = new Bitmap(colours.Length, 1, PixelFormat.Format24bppRgb))
            {
                for (var x = 0; x < colours.Length; x++)
                    source.SetPixel(x, 0, colours[x]);
                Assert.That(
                    source.GetPixel(2, 0).ToArgb(),
                    Is.EqualTo(lightGray.ToArgb()),
                    "setup: the light gray pixel should be in place before processing"
                );
                using (var palasoImage = PalasoImage.FromImage(source))
                using (
                    var result = (Bitmap)
                        RuntimeImageProcessor.MakePngBackgroundTransparent(palasoImage)
                )
                {
                    Assert.That(
                        result.GetPixel(0, 0).A,
                        Is.EqualTo(0),
                        "white should be fully transparent"
                    );
                    Assert.That(
                        result.GetPixel(1, 0).ToArgb(),
                        Is.EqualTo(Color.FromArgb(255, 0, 0, 0).ToArgb()),
                        "black should stay opaque black"
                    );

                    var gray = result.GetPixel(2, 0);
                    Assert.That(
                        gray.A,
                        Is.EqualTo(55),
                        "light gray 200 should become 55/255 opaque"
                    );
                    Assert.That(
                        new[] { gray.R, gray.G, gray.B },
                        Is.EqualTo(new byte[] { 0, 0, 0 }),
                        "light gray should become partly transparent black ink"
                    );

                    var red = result.GetPixel(3, 0);
                    Assert.That(
                        red.A,
                        Is.EqualTo(100),
                        "pale red's darkest channel (155) should set its opacity to 100/255"
                    );
                    Assert.That(
                        new[] { red.R, red.G, red.B },
                        Is.EqualTo(new byte[] { 255, 0, 0 }),
                        "pale red should become partly transparent pure red ink"
                    );

                    for (var x = 0; x < colours.Length; x++)
                    {
                        var p = result.GetPixel(x, 0);
                        var overWhite = new[] { p.R, p.G, p.B }
                            .Select(c =>
                                (int)System.Math.Round((c * p.A + 255 * (255 - p.A)) / 255.0)
                            )
                            .ToArray();
                        Assert.That(
                            overWhite,
                            Is.EqualTo(
                                new[] { colours[x].R, colours[x].G, colours[x].B }
                                    .Select(c => (int)c)
                                    .ToArray()
                            ),
                            "pixel " + x + " drawn over white should give back the original colour"
                        );
                    }
                }
            }
        }

        private TempFile MakeTempPNGImage(int width, int height)
        {
            var file = TempFile.WithExtension(".png");
            File.Delete(file.Path);
            using (var x = new Bitmap(width, height))
            {
                x.Save(file.Path, ImageFormat.Png);
            }
            return file;
        }

        private TempFile MakeTempJPGImage(int width, int height)
        {
            var file = TempFile.WithExtension(".jpg");
            File.Delete(file.Path);
            using (var x = new Bitmap(width, height))
            {
                x.Save(file.Path, ImageFormat.Jpeg);
            }
            return file;
        }
    }
}
