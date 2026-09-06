using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Bloom;
using Bloom.Edit;
using Bloom.SafeXml;
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

        /// <summary>
        /// BL-16669: importing a file whose name contains a '%' followed by two hex digits
        /// (e.g. "photo%41.jpg") used to hand the browser a src of "photoA.jpg", because the
        /// name was mistaken for URL-encoded text and decoded. The src must instead be the
        /// encoded form of the name we actually wrote into the book folder.
        /// </summary>
        [Test]
        public void ChangePicture_FileNameLooksUrlEncoded_SrcMatchesTheFileWeWrote()
        {
            using (var src = new TemporaryFolder("bloom pictures test source"))
            using (var dest = new TemporaryFolder("bloom picture tests dest"))
            using (var original = MakeSamplePngImage(src.Combine("photo%41.png")))
            {
                // Sanity check: the name really does reach ChangePicture with the '%' in it.
                Assert.That(original.FileName, Is.EqualTo("photo%41.png"));

                var result = PageEditingModel.ChangePicture(
                    dest.Path,
                    "pretendImageId",
                    UrlPathString.CreateFromUnencodedString("old.png"),
                    original
                );

                Assert.That(
                    File.Exists(dest.Combine("photo%41.png")),
                    "the file should have been copied in under its own name"
                );
                Assert.That(result.src, Is.EqualTo("photo%2541.png"));
                // ...which is what the browser will ask us for, and it resolves to the real file.
                Assert.That(
                    UrlPathString.CreateFromUrlEncodedString(result.src).NotEncoded,
                    Is.EqualTo("photo%41.png")
                );
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
                var dom = SafeXmlDocument.Create();
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
            var dom = SafeXmlDocument.Create();
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
