using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Xml;
using Bloom;
using Bloom.Edit;
using NUnit.Framework;
using SIL.TestUtilities;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;

namespace BloomTests
{
    [TestFixture]
    public class PageEditingModelTests
    {
        private int kSampleImageDimension = 5;

        [Test]
        public void ChangePicture_PictureIsFromOutsideProject_PictureCopiedAndAttributeChanged_AndMetadataSaved()
        {
            using (var src = new TemporaryFolder("bloom pictures test source"))
            using (var dest = new TemporaryFolder("bloom picture tests dest"))
            {
                var newImagePath = src.Combine("new.png");
                using (var original = MakeSamplePngImage(newImagePath))
                {
                    original.Metadata.Creator = "Some nice user";
                    original.Metadata.HasChanges = true;
                    var result = PageEditingModel.ChangePicture(
                        dest.Path,
                        "pretendImageId",
                        UrlPathString.CreateFromUnencodedString("old.png"),
                        original
                    );
                    var pathToNewImage = dest.Combine("new.png");
                    Assert.IsTrue(File.Exists(pathToNewImage));
                    Assert.That(result.src, Is.EqualTo("new.png"));
                    var metadataFromImage = Metadata.FromFile(pathToNewImage);
                    Assert.That(metadataFromImage.Creator, Is.EqualTo(original.Metadata.Creator));
                }
            }
        }

        /*
            /// <summary>
            /// With this, we test the secenario where someone grabs, say "untitled.png", then does
            /// so again in a different place. At this time, we will just throw away the first one
            /// and use the new one, in both places in document. Alternatively, we could take the
            /// trouble to rename the second one to a safe name so that there are two files.
            /// </summary>
            [Test, Ignore("Test needs work")]
            public void ChangePicture_AlreadyHaveACopyInPublicationFolder_PictureUpdated()
            {
                var dom = new XmlDocument();
                dom.LoadXml(
                    "<html><body><div/><div><img id='one'/><img id='two' src='old.png'/></div></body></html>"
                );

                using (var src = new TemporaryFolder("bloom pictures test source"))
                using (var dest = new TemporaryFolder("bloom picture tests dest"))
                {
                    var dogImagePath = src.Combine("dog.png");
                    using (var original = MakeSamplePngImage(dogImagePath))
                    {
                        var destDogImagePath = dest.Combine("dog.png");
                        File.WriteAllText(destDogImagePath, "old dog");
                        ChangePicture(dest.Path, dom, "two", original);
                        Assert.IsTrue(
                            RobustImageIO.GetImageFromFile(destDogImagePath).Width
                                == kSampleImageDimension
                        );
                    }
                }
            }
    */
        private PalasoImage MakeSamplePngImage(string path)
        {
            var x = new Bitmap(kSampleImageDimension, kSampleImageDimension);
            x.Save(path, ImageFormat.Png);
            x.Dispose();
            return PalasoImage.FromFileRobustly(path);
        }

        private PalasoImage MakeSampleTifImage(string path)
        {
            var x = new Bitmap(kSampleImageDimension, kSampleImageDimension);
            x.Save(path, ImageFormat.Tiff);
            return PalasoImage.FromFileRobustly(path);
        }

        private PalasoImage MakeSampleJpegImage(string path)
        {
            var x = new Bitmap(kSampleImageDimension, kSampleImageDimension);
            x.Save(path, ImageFormat.Jpeg);
            //nb: even if we reload the image from the file, the rawformat will be memory bitmap, not jpg as we'd wish
            return PalasoImage.FromFileRobustly(path);
        }

        /// <summary>
        /// Some (or maybe all?) browsers can't show tiff, so we might as well convert it
        /// </summary>
        [Test]
        public void ChangePicture_PictureIsTiff_ConvertedToPng()
        {
            var dom = new XmlDocument();
            dom.LoadXml(
                "<html><body><div/><div><img id='one'/><img id='two' src='old.png'/></div></body></html>"
            );

            using (var src = new TemporaryFolder("bloom pictures test source"))
            using (var dest = new TemporaryFolder("bloom picture tests dest"))
            using (var original = MakeSampleTifImage(src.Combine("new.tif")))
            {
                var result = PageEditingModel.ChangePicture(
                    dest.Path,
                    "pretendImageId",
                    UrlPathString.CreateFromUnencodedString("old.png"),
                    original
                );
                Assert.IsTrue(File.Exists(dest.Combine("new.png")));
                Assert.That(result.src, Is.EqualTo("new.png"));
                using (var converted = Image.FromFile(dest.Combine("new.png")))
                {
                    Assert.AreEqual(ImageFormat.Png.Guid, converted.RawFormat.Guid);
                }
            }
        }

        [Test]
        public void ChangePicture_PictureIsJpg_StaysJpg()
        {
            using (var src = new TemporaryFolder("bloom pictures test source"))
            using (var dest = new TemporaryFolder("bloom picture tests dest"))
            using (var original = MakeSampleJpegImage(src.Combine("new.jpg")))
            {
                var result = PageEditingModel.ChangePicture(
                    dest.Path,
                    "pretendImageId",
                    UrlPathString.CreateFromUnencodedString("old.png"),
                    original
                );
                Assert.IsTrue(File.Exists(dest.Combine("new.jpg")));
                Assert.That(result.src, Is.EqualTo("new.jpg"));
                using (var converted = Image.FromFile(dest.Combine("new.jpg")))
                {
                    Assert.AreEqual(ImageFormat.Jpeg.Guid, converted.RawFormat.Guid);
                }
            }
        }
    }
}
