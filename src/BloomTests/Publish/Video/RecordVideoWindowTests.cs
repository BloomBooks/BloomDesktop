using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom.Publish.Video;
using Moq;
using NUnit.Framework;
using SIL.IO;
using Assert = NUnit.Framework.Assert;

namespace BloomTests.Publish.Video
{
    [TestFixture]
    internal class RecordVideoWindowTests
    {
        [TestCase(1920, 1080)]
        [TestCase(1280, 720)]
        [TestCase(854, 480)]
        public void RecordVideoWindowGetBestYouTubeSize_LandscapeBookOnExactStandardSizeScreen_ReturnsThatSize(
            int maxWidth,
            int maxHeight
        )
        {
            var result = RecordVideoWindow.GetBestYouTubeResolution(
                new Resolution(maxWidth, maxHeight),
                true
            );

            var expectedResult = new Resolution(maxWidth, maxHeight);
            Assert.AreEqual(expectedResult.ToString(), result.ToString());
        }

        [TestCase(2560, 1440)]
        public void RecordVideoWindowGetBestYouTubeSize_LandscapeBookOnBiggerThanFHDSizeScreen_ReturnsFHD(
            int maxWidth,
            int maxHeight
        )
        {
            var result = RecordVideoWindow.GetBestYouTubeResolution(
                new Resolution(maxWidth, maxHeight),
                true
            );

            var expectedResult = new Resolution(1920, 1080);
            Assert.AreEqual(expectedResult.ToString(), result.ToString());
        }

        [TestCase(2560 - 1, 1440, 1920, 1080)]
        [TestCase(1920, 1080 - 1, 1280, 720)]
        [TestCase(1280 - 1, 720, 854, 480)]
        [TestCase(854, 480 - 1, 640, 360)]
        public void RecordVideoWindowGetBestYouTubeSize_LandscapeBookOnSlightlySmallerThanStandardSizeScreen_ReturnsOneStandardSizeDown(
            int maxWidth,
            int maxHeight,
            int expectedWidth,
            int expectedHeight
        )
        {
            var result = RecordVideoWindow.GetBestYouTubeResolution(
                new Resolution(maxWidth, maxHeight),
                true
            );

            var expectedResult = new Resolution(expectedWidth, expectedHeight);
            Assert.AreEqual(expectedResult.ToString(), result.ToString());
        }

        [Test]
        public void RecordVideoWindowGetBestYouTubeSize_PortraitBookOnLandscapeScreen_ReturnsStandardHeightAndProportionalWidth()
        {
            var result = RecordVideoWindow.GetBestYouTubeResolution(
                new Resolution(1920, 1080),
                false
            );

            var expectedResult = new Resolution(480, 854);
            Assert.AreEqual(expectedResult.ToString(), result.ToString());
        }

        [TestCase(1080, 1920)]
        [TestCase(720, 1280)]
        [TestCase(480, 854)]
        [TestCase(360, 640)]
        [TestCase(240, 426)]
        [TestCase(144, 256)]
        public void RecordVideoWindowGetBestYouTubeSize_PortraitBookOnPortraitScreen_ReturnsStandardHeightAndProportionalWidth(
            int maxWidth,
            int expectedHeight
        )
        {
            int maxHeight = maxWidth * 2; // A 9x16 video will fit in this screen, with plenty of buffer
            var result = RecordVideoWindow.GetBestYouTubeResolution(
                new Resolution(maxWidth, maxHeight),
                false
            );

            int expectedWidth = maxWidth;
            var expectedResult = new Resolution(expectedWidth, expectedHeight);
            Assert.AreEqual(expectedResult.ToString(), result.ToString());
        }

        [TestCase(2160)]
        [TestCase(1440)]
        public void RecordVideoWindowGetBestYouTubeSize_PortraitBookOnBiggerThanFHDPortraitScreen_ReturnsFHDSize(
            int maxWidth
        )
        {
            int maxHeight = maxWidth * 2; // A 9x16 video will fit in this screen, with plenty of buffer
            var result = RecordVideoWindow.GetBestYouTubeResolution(
                new Resolution(maxWidth, maxHeight),
                false
            );

            var expectedResult = new Resolution(1080, 1920);
            Assert.AreEqual(expectedResult.ToString(), result.ToString());
        }

        private const string _pathToTestImages = "src/BloomTests/Publish/Video";

        [TestCase("testVideo1.mp4", true)]
        // It's important that this file name contains the string 'Audio',
        // to verify that finding it in the file name does not make the function
        // think it contains audio.
        [TestCase("videoWithoutAudio.mp4", false)]
        public void VideoHasAudio_correctResult(string fileName, bool result)
        {
            var rvw = new RecordVideoWindow(null);
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                fileName
            );
            Assert.That(rvw.VideoHasAudio(path), Is.EqualTo(result));
        }

        [Test]
        public void GetSuggestedSaveFileNameBase_Simple()
        {
            var obj = new RecordVideoWindow(null);
            SIL.Reflection.ReflectionHelper.SetField(
                obj,
                "_pathToRealBook",
                @"C:\MyCollection\bookTitleL1"
            );
            var result = obj.GetSuggestedSaveFileNameBase(out string langTag);

            Assert.AreEqual("bookTitleL1", result);
            Assert.IsNull(langTag);
        }

        [Test]
        public void GetSuggestedSaveFileNameBase_GivenVideoSettingsSpecifiesL2_SuggestedFileNameUsesL2()
        {
            ///////////
            // Setup //
            ///////////
            var obj = new RecordVideoWindow(null);

            // Setup videoSettings (the settings from the Bloom Player preview)
            obj.SetVideoSettingsFromPreview("{\"lang\":\"l2\"}");

            // Setup AllTitles
            var mockBook = new Mock<Bloom.Book.Book>();
            var mockBookInfo = new Mock<Bloom.Book.BookInfo>();
            mockBookInfo
                .SetupGet(x => x.AllTitles)
                .Returns("{ \"l1\": \"bookTitleL1\", \"l2\": \"bookTitleL2\"}");
            mockBook.Setup(book => book.BookInfo).Returns(mockBookInfo.Object);
            obj.SetBook(mockBook.Object);

            // Setup _pathToRealBook
            SIL.Reflection.ReflectionHelper.SetField(
                obj,
                "_pathToRealBook",
                @"C:\MyCollection\bookTitleL1"
            );

            ///////////////////////
            // System Under Test //
            ///////////////////////
            var result = obj.GetSuggestedSaveFileNameBase(out string langTag);

            //////////////////
            // Verification //
            //////////////////
            Assert.AreEqual("bookTitleL2", result);
            Assert.AreEqual("l2", langTag);
        }

        [Test]
        public void GetShortName_GivesValidResults()
        {
            var results = new HashSet<string>();
            // 37x37 gets us into three-letter names, since we are using 36 characters.
            for (var i = 0; i < 37 * 37; i++)
            {
                var result = RecordVideoWindow.GetShortName(i);
                Assert.That(result.Length < 4);
                Assert.That(results, Does.Not.Contain(result));
                results.Add(result);
            }
        }
    }
}
