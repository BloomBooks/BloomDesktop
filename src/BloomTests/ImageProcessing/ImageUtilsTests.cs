using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using Bloom.Book;
using Bloom.ImageProcessing;
using Bloom.SafeXml;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;
using SIL.Windows.Forms.ImageToolbox;

namespace BloomTests.ImageProcessing
{
    [TestFixture]
    public class ImageUtilsTests
    {
        private const string _pathToTestImages = "src/BloomTests/ImageProcessing/images";

        [Test]
        public void ShouldChangeFormatToJpeg_Photo_True()
        {
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "man.png"
            );
            string jpegPath = Path.Combine(Path.GetTempPath(), Path.GetTempFileName() + ".jpg");
            try
            {
                Assert.IsTrue(
                    ImageUtils.TryChangeFormatToJpegIfHelpful(
                        PalasoImage.FromFileRobustly(path),
                        jpegPath
                    )
                );
                Assert.IsTrue(File.Exists(jpegPath));
            }
            finally
            {
                if (File.Exists(jpegPath))
                    File.Delete(jpegPath);
            }
        }

        [Test]
        public void ShouldChangeFormatToJpeg_OneColor_False()
        {
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "bird.png"
            );
            string jpegPath = Path.Combine(Path.GetTempPath(), Path.GetTempFileName() + ".jpg");
            Assert.IsFalse(
                ImageUtils.TryChangeFormatToJpegIfHelpful(
                    PalasoImage.FromFileRobustly(path),
                    jpegPath
                )
            );
            Assert.IsFalse(File.Exists(jpegPath));
        }

        [Test]
        public void ProcessAndSaveImageIntoFolder_PhotoButPNGFile_SavesAsJpeg()
        {
            ProcessAndSaveImageIntoFolder_AndTestResults("man.png", ImageFormat.Jpeg);
        }

        [Test]
        public void ProcessAndSaveImageIntoFolder_Photo_KeepsJpeg()
        {
            ProcessAndSaveImageIntoFolder_AndTestResults("man.jpg", ImageFormat.Jpeg);
        }

        [Test]
        public void ProcessAndSaveImageIntoFolder_OneColor_SavesAsPng()
        {
            ProcessAndSaveImageIntoFolder_AndTestResults("bird.png", ImageFormat.Png);
        }

        private static void ProcessAndSaveImageIntoFolder_AndTestResults(
            string testImageName,
            ImageFormat expectedOutputFormat
        )
        {
            var inputPath = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                testImageName
            );
            using (var image = PalasoImage.FromFileRobustly(inputPath))
            {
                using (
                    var folder = new TemporaryFolder(
                        "ImageUtilsTest_ProcessAndSaveImageIntoFolder_AndTestResults"
                    )
                )
                {
                    var fileName = ImageUtils.ProcessAndSaveImageIntoFolder(
                        image,
                        folder.Path,
                        false
                    );
                    Assert.AreEqual(
                        expectedOutputFormat == ImageFormat.Jpeg ? ".jpg" : ".png",
                        Path.GetExtension(fileName)
                    );
                    var outputPath = folder.Combine(fileName);
                    using (var img = Image.FromFile(outputPath))
                    {
                        Assert.AreEqual(expectedOutputFormat, img.RawFormat);
                    }

                    var alternativeThatShouldNotBeThere = Path.Combine(
                        Path.GetDirectoryName(outputPath),
                        Path.GetFileNameWithoutExtension(outputPath)
                            + (expectedOutputFormat.Equals(ImageFormat.Jpeg) ? ".png" : ".jpg")
                    );
                    Assert.IsFalse(
                        File.Exists(alternativeThatShouldNotBeThere),
                        "Did not expect to have the file " + alternativeThatShouldNotBeThere
                    );
                }
            }
        }

        // See BL-3646 which showed we were blacking out the image when converting from png to jpg
        [TestCase("shirt.png")]
        // I think shirt.png still has a transparent background after being fixed, but I'm not absolutely sure,
        // so I'm leaving both tests in place.
        [
            TestCase("shirtWithTransparentBg.png")
#if __MonoCS__
            ,
            Category("SkipOnTeamCity") // only for Linux: TeamCity creates an error from a low-level warning message.
#endif
        ]
        public static void ProcessAndSaveImageIntoFolder_SimpleImageHasTransparentBackground_ImageNotConvertedAndFileSizeNotIncreased(
            string sourceFileName
        )
        {
            var inputPath = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                sourceFileName
            );
            var originalFileSize = new FileInfo(inputPath).Length;
            using (var image = PalasoImage.FromFileRobustly(inputPath))
            {
                using (
                    var folder = new TemporaryFolder(
                        MethodBase.GetCurrentMethod().Name + sourceFileName
                    )
                )
                {
                    var fileName = ImageUtils.ProcessAndSaveImageIntoFolder(
                        image,
                        folder.Path,
                        false
                    );
                    Assert.AreEqual(".png", Path.GetExtension(fileName));
                    var outputPath = folder.Combine(fileName);
                    using (var result = Image.FromFile(outputPath))
                    {
                        Assert.AreEqual(ImageFormat.Png, result.RawFormat);
                        Assert.That(originalFileSize <= new FileInfo(outputPath).Length);
                    }
                }
            }
        }

        [Test]
        [TestCase("box", "box1")]
        [TestCase("box1", "box2")]
        [TestCase("12311", "12312")]
        [TestCase("12box", "12box1")]
        [TestCase("9", "10")]
        [TestCase("b", "b1")]
        [TestCase("box99", "box100")]
        [TestCase("IMG_20210825_141322", "IMG_20210825_141323")] // Several trailing digits already
        [TestCase("IMG_20210825141322", "IMG_20210825141322-1")] // Too many trailing digits
        [TestCase("IMG_20210825141322-1", "IMG_20210825141322-2")] // Digits all over the place...
        [TestCase("IMG_1629606288606", "IMG_1629606288606-1")] // Too many trailing digits
        public static void GetUnusedFilenameTests(string basename, string expectedResult)
        {
            const string extension = ".txt";
            using (var folder = new TemporaryFolder("UnusedFilenameTest"))
            {
                var basePath = Path.Combine(folder.Path, basename + extension);
                RobustFile.Delete(basePath); // just in case
                RobustFile.WriteAllText(basePath, "test contents");
                var filename = ImageUtils.GetUnusedFilename(
                    Path.GetDirectoryName(basePath),
                    basename,
                    extension
                );
                Assert.That(Path.GetFileNameWithoutExtension(filename), Is.EqualTo(expectedResult));
            }
        }

        [Test]
        [TestCase(3840, 2800, 3840, 2800)] // maximum size landscape
        [TestCase(2800, 3840, 2800, 3840)] // maximum size portrait
        [TestCase(3400, 2500, 3400, 2500)] // smaller than bounds landscape
        [TestCase(2500, 3400, 2500, 3400)] // smaller than bounds portrait
        [TestCase(3000, 3000, 2800, 2800)] // square too large
        [TestCase(4000, 3000, 3733, 2800)] // landscape, both too large squashed
        [TestCase(5376, 3920, 3840, 2800)] // landscape, both too large same aspect ratio
        [TestCase(5000, 3000, 3840, 2304)] // landscape, both too large elongated
        [TestCase(3000, 4000, 2800, 3733)] // portrait, both too large squashed
        [TestCase(3920, 5376, 2800, 3840)] // portrait, both too large same aspect ratio
        [TestCase(3000, 5000, 2304, 3840)] // portrait, both too large elongated
        [TestCase(2500, 5000, 1920, 3840)] // portrait, height too large
        [TestCase(5000, 2500, 3840, 1920)] // landscape, width too large
        [TestCase(3800, 3000, 3546, 2800)] // landscape, height too large
        [TestCase(3000, 3800, 2800, 3546)] // portrait, width too large
        public static void TestGetImageSizes(int width, int height, int newWidth, int newHeight)
        {
            var size = ImageUtils.GetDesiredImageSize(width, height);
            Assert.AreEqual(
                newWidth,
                size.Width,
                $"Computed width for {width},{height} is correct."
            );
            Assert.AreEqual(
                newHeight,
                size.Height,
                $"Computed height for {width},{height} is correct."
            );
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void DrawResizedImage_TestForDashedBorder_SmallSquareImage(bool addBorder)
        {
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "Othello 199.jpg"
            );
            var image = new Bitmap(path);
            var desiredThumbSize = new Size(200, 200);

            // SUT
            var result = ImageUtils.ResizeImageIfNecessary(desiredThumbSize, image, addBorder);

            // Testing
            TestImageResult(result, addBorder);
        }

        [Test]
        [TestCase(false, false)] // Resize, no border
        [TestCase(false, true)] // Resize, add dashed border
        [TestCase(true, false)] // Center image, no border
        [TestCase(true, true)] // Center image, add dashed border
        public void DrawResizedImage_TestForPresenceOfDashedBorder(bool centerImage, bool addBorder)
        {
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "bird.png"
            );
            var image = new Bitmap(path);
            var desiredThumbSize = new Size(70, 70);

            // SUT
            // I don't like conditionals in tests.
            // Unfortunately "DrawResizedImage()" is a private method that is very simply wrapped by two
            // public methods.
            var result = centerImage
                ? ImageUtils.CenterImageIfNecessary(desiredThumbSize, image, addBorder)
                : ImageUtils.ResizeImageIfNecessary(desiredThumbSize, image, addBorder);

            // Test image result
            TestImageResult(result, addBorder);
        }

        private static void TestImageResult(Image result, bool shouldHaveDashedBorder)
        {
            var bitmap = new Bitmap(result);

            var hitMax = false;
            var foundFirstDash = false;
            var foundFirstSpace = false;
            const int maxX = 20;
            for (var i = 0; i < maxX; i++)
            {
                var pixel = bitmap.GetPixel(i, 0);

                if (shouldHaveDashedBorder)
                {
                    if (IsColorOpaqueBlack(pixel))
                    {
                        if (foundFirstDash && foundFirstSpace)
                        {
                            break; // found a second dash
                        }

                        foundFirstDash = true;
                    }
                    else
                    {
                        foundFirstSpace = true;
                    }
                }
                else
                {
                    Assert.That(
                        IsColorOpaqueBlack(pixel),
                        Is.False,
                        $"Point ({i}, 0) should not be black."
                    );
                }

                if (i == maxX - 1)
                    hitMax = true;
            }
            Assert.That(
                shouldHaveDashedBorder,
                Is.Not.EqualTo(hitMax),
                "We should have a dashed border and we didn't find dashes, or we should not have a border and we didn't finish the loop."
            );
        }

        private static bool IsColorOpaqueBlack(Color color)
        {
            return (color.R | color.G | color.B) == 0 && color.A == 255;
        }

        // The pixel counts are for the randomization introduced by a seed of 271828182.  Not randomizing,
        // or using different seeds, changed the pixel counts as shown in parentheses.  For one image
        // (Retangles1.png), a seed of 123456 caused an incorrect return value (a false positive).
        // That particular image is really designed to make a partial check of pixels be problematic.
        [Test]
        [TestCase("aor_Nab037.png", true)] // indexed image with 2 colors, white found
        [TestCase("bluebird-indexed.png", false)] // indexed image with 2 colors, white not found
        [TestCase("MemoryReport-indexed.png", false)] // indexed image with 16 colors
        [TestCase("aor_oce003m.png", true)] // 100 pixels examined, 6 shades of gray/white/black found
        [TestCase("Boxes.png", true)] // 100 pixels examined, 2 colors found, white found
        [TestCase("bluebird.png", false)] // 77 pixels examined before 3 colors found (100,24,6,100,77)
        [TestCase("bird.png", false)] // 1 pixels examined before a transparent pixel found
        [TestCase("levels.png", false)] // 1 pixels examined before a transparent pixel found
        [TestCase("Mars 2.png", false)] // 1 pixels examined before a transparent pixel found
        [TestCase("Jesus Children.png", false)] // 3 pixels examined before 3 colors found (3,3,3,3,3)
        [TestCase("lady24b.png", false)] // 46 pixels examined before 3 colors found (46,46,26,46,46)
        // The rest of these are more like torture tests rather than realistic drawings likely to be used in Bloom.
        [TestCase("AceByDaisyError.png", false)] // 11 pixels examined before 3 colors found (100,12,6,6,11)
        [TestCase("LineDrawing-2017.png", false)] // 38 pixels examined before 3 colors found (34,34,34,34,38)
        [TestCase("Bloom-No-Microphone.png", false)] // 42 pixels examined before 3 colors found (42,12,12,13,42)
        [TestCase("UpdateNotice-2017.png", false)] // 22 pixels examined before 3 colors found (44,58,61,64,22)
        [TestCase("Rectangles1.png", false)] // 34 pixels examined before 3 colors found (45,100*,80,46,34)
        [TestCase("MemoryReport.png", false)] // 21 pixels examined before 3 colors found (52,45,13,17,21)
        [TestCase("CreateTC.png", false)] // 14 pixels examined before 3 colors found (97,24,14,14,14)
        public void TestForNeedingTransparentBackground(string filename, bool expectedResult)
        {
            var imagePath = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                filename
            );
            using (var image = PalasoImage.FromFileRobustly(imagePath))
            {
                var isBW = ImageUtils.ShouldMakeBackgroundTransparent(image);
                Assert.That(isBW, Is.EqualTo(expectedResult));
            }
        }

        [Test]
        public void StripMetadataFromImageFile()
        {
            var imagePath = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "ImageWithProblematicMetadata.jpg"
            );

            using (var image = PalasoImage.FromFileRobustly(imagePath))
            {
                using (var tempFile = TempFile.WithExtension(".jpg"))
                {
                    // Verify setup
                    Assert.Throws<System.InvalidOperationException>(() =>
                        ImageUtils.SaveImageMetadata(image, tempFile.Path)
                    );

                    // SUT
                    ImageUtils.StripMetadataFromImageFile(image);

                    // Verify
                    Assert.DoesNotThrow(() => ImageUtils.SaveImageMetadata(image, tempFile.Path));
                }
            }
        }

        [TestCase("myFolder", "myFile.png")]
        [TestCase("மரியாதை ராமன் கதைகள்", "テスト画像.png")]
        public void TryGetImageSize_NonAsciiFilePath_GetsSize(string folderName, string fileName)
        {
            var originalPath = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "bird.png"
            );

            string tempFolder = Path.Combine(Path.GetTempPath(), folderName);
            try
            {
                if (!Directory.Exists(tempFolder))
                    Directory.CreateDirectory(tempFolder);

                string newPath = Path.Combine(tempFolder, fileName);
                RobustFile.Copy(originalPath, newPath, true);

                bool result = ImageUtils.TryGetImageSize(newPath, out Size size);

                Assert.IsTrue(result, "TryGetImageSize should return true");

                // Compare with actual dimensions
                using (var img = Image.FromFile(originalPath))
                {
                    Assert.AreEqual(
                        img.Width,
                        size.Width,
                        "Width should match the actual image width"
                    );
                    Assert.AreEqual(
                        img.Height,
                        size.Height,
                        "Height should match the actual image height"
                    );
                }
            }
            finally
            {
                if (Directory.Exists(tempFolder))
                {
                    try
                    {
                        Directory.Delete(tempFolder, true);
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }
        }

        [TestCase("width", "width: 1433.16px; top: -146.703px; left: -706.162px;", 1433.16)]
        [TestCase("top", "width: 1433.16px; top: -146.703px; left: -706.162px;", -146.703)]
        [TestCase("left", "width: 1433.16px; top: -146.703px; left: -706.162px;", -706.162)]
        [TestCase("left", "some silly nonsence", 0)]
        [TestCase("left", "width: 20", 0)]
        public void GetNumberFromPx(string label, string input, double expected)
        {
            Assert.That(
                Math.Abs(expected - ImageUtils.GetNumberFromPx(label, input)),
                Is.LessThan(0.001)
            );
        }
    }

    [TestFixture]
    public class ReallyCropImagesTests
    {
        private HtmlDom _dom;
        private TemporaryFolder _folder;
        private SafeXmlElement _p1bgi1;
        private SafeXmlElement _p1i1c1;
        private SafeXmlElement _p2i1c1;
        private SafeXmlElement _p2i1c2;
        private SafeXmlElement _p2i1c3;
        private SafeXmlElement _p2i1a;
        private SafeXmlElement _p2i1b;
        private SafeXmlElement _p2i2a;
        private SafeXmlElement _p1i2c4;
        private SafeXmlElement _p1i3c5;
        private SafeXmlElement _p2i3c5;
        private byte[] _manPngBytes;
        private byte[] _manJpgBytes;
        private byte[] _lady24bPngBytes;
        private byte[] _p1i1c1Bytes;
        private byte[] _p2i1c2Bytes;
        private byte[] _p2i1c3Bytes;
        private byte[] _p1i2c4Bytes;
        private byte[] _p1i3c54Bytes;

        public static string MakeImageCanvasElement(
            string imgId,
            string src,
            string ceStyle,
            string imgStyle = null,
            bool background = false
        )
        {
            var backgroundString = background ? " bloom-backgroundImage" : "";
            var imgStyleString = imgStyle == null ? "" : $"style=\"{imgStyle}\"";
            return $"<div class=\"bloom-canvas-element{backgroundString}\" style=\"{ceStyle}\" data-bubble=\"{{`version`:`1.0`,`style`:`none`,`tails`:[],`level`:4,`backgroundColors`:[`transparent`],`shadowOffset`:0}}\">"
                + $"  <div tabindex=\"0\" class=\"bloom-imageContainer bloom-leadingElement\">"
                + $"      <img id=\"{imgId}\" src=\"{src}\" {imgStyleString} />"
                + $"  </div>"
                + $"</div>";
        }

        [OneTimeSetUp]
        public void Setup()
        {
            _folder = new TemporaryFolder("ReallyCropImagesTests");
            // much simplified dom, but enough for this.
            // We want: three uncropped images on two pages with the same name, all uncropped. Should remain the same names and file.
            // Two more images using the same filename, both cropped the same, on two pages, should get cropped once to a new name.
            // More versions of this image on two pages all cropped differently. Should get distinct names.
            // Two images using the same file name, where the first one is cropped, should result in the
            // uncropped one keeping the name, and the cropped one getting a new name.
            // Two images using another file name, all cropped the same, should keep name but change file content.
            _dom = new HtmlDom(
                @"
<html><head></head><body>
    <div class=""bloom-page"" id=""page1"">
        <div class=""marginBox"" id=""image1"">
            <div class=""bloom-canvas"">"
                    + MakeImageCanvasElement(
                        "p1bgi1",
                        "man.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        null,
                        true
                    )
                    + MakeImageCanvasElement(
                        "p1i1c1",
                        "man.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -80px; top: -20px"
                    )
                    + MakeImageCanvasElement(
                        "p1i2c4",
                        "lady24b.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -80px; top: -20px"
                    )
                    + MakeImageCanvasElement(
                        "p1i3c5",
                        "man%203.jpg",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -80px; top: -20px"
                    )
                    + @"
            </div>
        </div>
    </div>
    <div class=""bloom-page"" id=""page2"">
        <div class=""marginBox"" id=""image1"">
            <div class=""bloom-canvas"">"
                    + MakeImageCanvasElement(
                        "p2i1a",
                        "man.png",
                        "height: 350px; left: 300px; top: 6px; width: 140px;"
                    )
                    + MakeImageCanvasElement(
                        "p2i1b",
                        "man.png",
                        "height: 350px; left: 20px; top: 6px; width: 140px;"
                    )
                    // same image and crop as p1i1c1, should use same cropped file
                    + MakeImageCanvasElement(
                        "p2i1c1",
                        "man.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -80px; top: -20px",
                        true
                    )
                    // same image p1i1c1, but a different img style, so should get a different cropped file
                    + MakeImageCanvasElement(
                        "p2i1c2",
                        "man.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -70px; top: -20px",
                        true
                    )
                    // This one needs a different crop because the canvas element width is different
                    + MakeImageCanvasElement(
                        "p2i1c3",
                        "man.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 130px;",
                        "width: 280px; left: -70px; top: -20px",
                        true
                    )
                    + MakeImageCanvasElement(
                        "p2i2a",
                        "lady24b.png",
                        "height: 350px; left: 300px; top: 6px; width: 140px;"
                    )
                    + MakeImageCanvasElement(
                        "p2i3c5",
                        "man%203.jpg",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -80px; top: -20px"
                    )
                    + @"
            </div>
        </div>
    </div>
</body></html>"
            );
            var _pathToTestImages = "src\\BloomTests\\ImageProcessing\\images";
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "man.png"
            );
            RobustFile.Copy(path, Path.Combine(_folder.Path, "man.png"));
            _manPngBytes = RobustFile.ReadAllBytes(path);

            path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "man.jpg"
            );
            // We are deliberately changing the name slightly to test for a particular problem
            // that was occurring when cropping renamed the output to an 'original' name
            // containing a space. The space needs to be in the name of the image that does
            // NOT occur uncropped.
            RobustFile.Copy(path, Path.Combine(_folder.Path, "man 3.jpg"));
            _manJpgBytes = RobustFile.ReadAllBytes(path);

            path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "lady24b.png"
            );
            RobustFile.Copy(path, Path.Combine(_folder.Path, "lady24b.png"));
            _lady24bPngBytes = RobustFile.ReadAllBytes(path);

            // SUT
            ImageUtils.ReallyCropImages(_dom.RawDom, _folder.Path, _folder.Path);

            _p1bgi1 = _dom.SelectSingleNode("//img[@id='p1bgi1']");
            _p1i1c1 = _dom.SelectSingleNode("//img[@id='p1i1c1']");
            _p2i1c1 = _dom.SelectSingleNode("//img[@id='p2i1c1']");
            _p2i1c2 = _dom.SelectSingleNode("//img[@id='p2i1c2']");
            _p2i1c3 = _dom.SelectSingleNode("//img[@id='p2i1c3']");
            _p2i1a = _dom.SelectSingleNode("//img[@id='p2i1a']");
            _p2i1b = _dom.SelectSingleNode("//img[@id='p2i1b']");
            _p2i2a = _dom.SelectSingleNode("//img[@id='p2i2a']");
            _p1i2c4 = _dom.SelectSingleNode("//img[@id='p1i2c4']");
            _p1i3c5 = _dom.SelectSingleNode("//img[@id='p1i3c5']");
            _p2i3c5 = _dom.SelectSingleNode("//img[@id='p2i3c5']");

            _p1i1c1Bytes = RobustFile.ReadAllBytes(
                Path.Combine(_folder.Path, _p1i1c1.GetAttribute("src"))
            );
            _p2i1c2Bytes = RobustFile.ReadAllBytes(
                Path.Combine(_folder.Path, _p2i1c2.GetAttribute("src"))
            );
            _p2i1c3Bytes = RobustFile.ReadAllBytes(
                Path.Combine(_folder.Path, _p2i1c3.GetAttribute("src"))
            );
            _p1i2c4Bytes = RobustFile.ReadAllBytes(
                Path.Combine(_folder.Path, _p1i2c4.GetAttribute("src"))
            );
            _p1i3c54Bytes = RobustFile.ReadAllBytes(
                // Tempting to read from _p1i3c5.GetAttribute("src")).
                // But we're have assertions that the src is unchanged for this element.
                // What we want to ensure is that the original file was overwritten, rather
                // than a new one with the encoded name man%203.jpg being created.
                Path.Combine(_folder.Path, "man 3.jpg")
            );
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            _folder.Dispose();
        }

        [Test]
        public void UncroppedImages_SrcUnchanged()
        {
            Assert.That(_p1bgi1.GetAttribute("src"), Is.EqualTo("man.png"));
            Assert.That(_p2i1a.GetAttribute("src"), Is.EqualTo("man.png"));
            Assert.That(_p2i1b.GetAttribute("src"), Is.EqualTo("man.png"));
            Assert.That(_p2i2a.GetAttribute("src"), Is.EqualTo("lady24b.png"));
        }

        [Test]
        public void CroppedImage_HaveUncroppedWithSameFile_NewName()
        {
            var newSrc = _p1i1c1.GetAttribute("src");
            Assert.That(newSrc, Is.Not.EqualTo("man.png"));
            Assert.That(Path.GetExtension(newSrc), Is.EqualTo(".png"));
            newSrc = _p1i2c4.GetAttribute("src");
            Assert.That(newSrc, Is.Not.EqualTo("lady24b.png"));
            Assert.That(Path.GetExtension(newSrc), Is.EqualTo(".png"));
        }

        [Test]
        public void CroppedImage_HasDifferentContent()
        {
            Assert.That(
                _p1i1c1Bytes,
                Is.Not.EqualTo(_manPngBytes),
                "Cropped image should have different content from original"
            );
            Assert.That(
                _p2i1c2Bytes,
                Is.Not.EqualTo(_manPngBytes),
                "Cropped image should have different content from original"
            );
            Assert.That(
                _p2i1c3Bytes,
                Is.Not.EqualTo(_manPngBytes),
                "Cropped image should have different content from original"
            );
            Assert.That(
                _p1i2c4Bytes,
                Is.Not.EqualTo(_lady24bPngBytes),
                "Cropped image should have different content from original"
            );
        }

        [Test]
        public void CroppedImages_WithDifferentCrops_ProduceDifferentFiles()
        {
            Assert.That(
                _p1i1c1Bytes,
                Is.Not.EqualTo(_p2i1c2Bytes),
                "Cropped image file contents should be different"
            );
            Assert.That(
                _p1i1c1Bytes,
                Is.Not.EqualTo(_p2i1c3Bytes),
                "Cropped image file contents should be different"
            );
            Assert.That(
                _p2i1c3Bytes,
                Is.Not.EqualTo(_p2i1c2Bytes),
                "Cropped image file contents should be different"
            );
        }

        [Test]
        public void ImageWithSameSrcAndCrop_ShouldUseSameCroppedImgFile()
        {
            Assert.That(_p1i1c1.GetAttribute("src"), Is.EqualTo(_p2i1c1.GetAttribute("src")));
            Assert.That(_p2i3c5.GetAttribute("src"), Is.EqualTo(_p1i3c5.GetAttribute("src")));
        }

        [Test]
        public void ImageWithSameSrcButDifferentCrop_ShouldUseDifferentCroppedImgFile()
        {
            var i1c1Src = _p1i1c1.GetAttribute("src");
            var i2c2Src = _p2i1c2.GetAttribute("src");
            Assert.That(i1c1Src, Is.Not.EqualTo(i2c2Src));
            Assert.That(i2c2Src, Is.Not.EqualTo("man.png"));
        }

        [Test]
        public void CroppedImage_NoUncroppedWithSameName_UsesOriginalName()
        {
            Assert.That(_p1i3c5.GetAttribute("src"), Is.EqualTo("man%203.jpg"));
        }

        [Test]
        public void CroppedImage_NoUncroppedWithSameName_HasModifiedContent()
        {
            Assert.That(
                _p1i3c54Bytes,
                Is.Not.EqualTo(_manJpgBytes),
                "Cropped image should have different content from original"
            );
        }
    }
}
