using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Bloom;
using Bloom.ImageProcessing;
using BloomTemp;
using NUnit.Framework;
using SIL.Code;
using SIL.IO;
using SIL.Windows.Forms.ImageToolbox;

namespace BloomTests.ImageProcessing
{
    /// <summary>
    /// BL-16926: on a Thai-locale computer TagLib# could not work out an image's file type from its
    /// name, so every read or write of image metadata threw. See TagLibCultureFix.
    /// </summary>
    [TestFixture]
    public class TagLibCultureFixTests
    {
        private TemporaryFolder _folder;

        [SetUp]
        public void Setup()
        {
            // The SetUpFixture registers this for the whole run; say so here too, so that these
            // tests do not depend on it.
            TagLibCultureFix.Register();
            _folder = new TemporaryFolder("TagLibCultureFixTests");
        }

        [TearDown]
        public void TearDown()
        {
            _folder.Dispose();
        }

        /// <summary>
        /// Makes a small image file of the given type ("png" or "jpg") and returns its path.
        /// </summary>
        private string MakeImage(string extension)
        {
            var path = Path.Combine(_folder.FolderPath, "original." + extension);
            var format = extension == "png" ? ImageFormat.Png : ImageFormat.Jpeg;
            using (var bitmap = new Bitmap(10, 10))
                RobustImageIO.SaveImage(bitmap, path, format);
            return path;
        }

        /// <summary>
        /// Without this, the tests below would pass for nothing on a runtime whose Thai collation
        /// does not ignore the period, since TagLib# would then find the extension unaided.
        /// </summary>
        private static void AssertThaiCollationIgnoresThePeriod()
        {
            Assert.That(
                "original.png".LastIndexOf("."),
                Is.Not.EqualTo(8),
                "This runtime's th-TH collation finds the period, so it cannot reproduce BL-16926"
            );
        }

        [TestCase("png")]
        [TestCase("jpg")]
        [SetCulture("th-TH")]
        public void ImageMetadata_UnderThaiCulture_CanBeSavedAndReadBack(string extension)
        {
            AssertThaiCollationIgnoresThePeriod();
            var imagePath = MakeImage(extension);
            Assert.That(RobustFileIO.MetadataFromFile(imagePath).Creator, Is.Null.Or.Empty);

            // This is libpalaso's route into TagLib#, as used whenever a picture's credits change.
            using (var image = PalasoImage.FromFileRobustly(imagePath))
            {
                image.Metadata.Creator = "joe";
                image.Metadata.CopyrightNotice = "Copyright 1999 by me";
                RetryUtility.Retry(() => image.SaveUpdatedMetadataIfItMakesSense());
            }

            var metadata = RobustFileIO.MetadataFromFile(imagePath);
            Assert.That(metadata.Creator, Is.EqualTo("joe"));
            Assert.That(metadata.CopyrightNotice, Is.EqualTo("Copyright 1999 by me"));
        }

        [TestCase("png", typeof(TagLib.Png.File), "taglib/png")]
        [TestCase("jpg", typeof(TagLib.Jpeg.File), "taglib/jpg")]
        [SetCulture("th-TH")]
        public void CreateTaglibFile_UnderThaiCulture_RecognizesTheFileType(
            string extension,
            Type expectedType,
            string expectedMimeType
        )
        {
            AssertThaiCollationIgnoresThePeriod();
            var imagePath = MakeImage(extension);

            // This is Bloom's own route into TagLib#.
            using (var file = RobustFileIO.CreateTaglibFile(imagePath))
            {
                Assert.That(file, Is.InstanceOf(expectedType));
                Assert.That(file.MimeType, Is.EqualTo(expectedMimeType));
            }
        }
    }
}
