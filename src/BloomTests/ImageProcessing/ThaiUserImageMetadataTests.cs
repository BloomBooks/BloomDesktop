using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using Bloom;
using BloomTemp;
using NUnit.Framework;
using SIL.Code;
using SIL.IO;
using SIL.Windows.Forms.ImageToolbox;

namespace BloomTests.ImageProcessing
{
    /// <summary>
    /// BL-16926: TagLib# works out an image's file type from its name with a culture-sensitive
    /// LastIndexOf("."). Thai collation ignores the period, so when Bloom ran in the user's culture
    /// every read or write of image metadata on a Thai computer threw. Bloom now runs in the
    /// invariant culture (BL-16981), which fixes this for Bloom's own calls and libpalaso's alike.
    /// These tests run the way a Thai user's Bloom does: UserCulture is th-TH and the process
    /// culture is invariant.
    /// </summary>
    [TestFixture]
    public class ThaiUserImageMetadataTests
    {
        private TemporaryFolder _folder;
        private CultureInfo _originalUserCulture;

        [SetUp]
        public void Setup()
        {
            _originalUserCulture = UserCulture.Current;
            UserCulture.SetForTests(new CultureInfo("th-TH"));
            _folder = new TemporaryFolder("ThaiUserImageMetadataTests");
        }

        [TearDown]
        public void TearDown()
        {
            UserCulture.SetForTests(_originalUserCulture);
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
        /// Without this, the tests below would pass for nothing: they only show something if a
        /// Thai user's collation really does lose the period, and if the process really is
        /// invariant rather than Thai.
        /// </summary>
        private static void AssertThaiUserWouldHaveHitTheBug()
        {
            Assert.That(
                UserCulture.Current.CompareInfo.LastIndexOf("original.png", "."),
                Is.Not.EqualTo(8),
                "This runtime's th-TH collation finds the period, so it cannot reproduce BL-16926"
            );
            Assert.That(
                CultureInfo.CurrentCulture.Name,
                Is.EqualTo(""),
                "The process culture should be invariant, as it is in Bloom"
            );
        }

        [TestCase("png")]
        [TestCase("jpg")]
        public void ImageMetadata_ForAThaiUser_CanBeSavedAndReadBack(string extension)
        {
            AssertThaiUserWouldHaveHitTheBug();
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
        public void CreateTaglibFile_ForAThaiUser_RecognizesTheFileType(
            string extension,
            Type expectedType,
            string expectedMimeType
        )
        {
            AssertThaiUserWouldHaveHitTheBug();
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
