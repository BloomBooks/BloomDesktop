using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using Bloom;
using Bloom.ImageProcessing;
using NUnit.Framework;
using SIL.IO;

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

        [Test]
        public void RemoveWhiteBackground_GraysLowChromaPartialAlphaPixels()
        {
            using (var source = new Bitmap(3, 1, PixelFormat.Format32bppArgb))
            {
                source.SetPixel(0, 0, Color.White);
                source.SetPixel(1, 0, Color.FromArgb(201, 200, 199));
                source.SetPixel(2, 0, Color.FromArgb(255, 170, 110));

                var method = typeof(RuntimeImageProcessor).GetMethod(
                    "RemoveWhiteBackground",
                    BindingFlags.NonPublic | BindingFlags.Static
                );
                Assert.IsNotNull(method);

                using (var result = (Bitmap)method.Invoke(null, new object[] { source }))
                {
                    var transparentPixel = result.GetPixel(0, 0);
                    Assert.AreEqual(0, transparentPixel.A);

                    var lowChromaPixel = result.GetPixel(1, 0);
                    Assert.That(lowChromaPixel.A, Is.GreaterThan(0).And.LessThan(255));
                    Assert.AreEqual(lowChromaPixel.R, lowChromaPixel.G);
                    Assert.AreEqual(lowChromaPixel.G, lowChromaPixel.B);
                    Assert.AreEqual(200, lowChromaPixel.R);

                    var coloredPixel = result.GetPixel(2, 0);
                    Assert.That(coloredPixel.A, Is.GreaterThan(0).And.LessThan(255));
                    Assert.AreNotEqual(coloredPixel.R, coloredPixel.G);
                    Assert.AreNotEqual(coloredPixel.G, coloredPixel.B);
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
