using System.IO;
using System.Linq;
using Bloom;
using Bloom.Book;
using Bloom.Publish.BloomPub;
using Bloom.SafeXml;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Publish.BloomPub
{
    /// <summary>
    /// Tests for BloomPubMaker.CompressImages, which shrinks (and sometimes re-encodes and
    /// makes transparent) each image in a book on its way into a BloomPub, and has to update
    /// the page's reference to the file when doing so renames it.
    /// </summary>
    [TestFixture]
    public class CompressImagesTests
    {
        private const string _pathToTestImages = "src/BloomTests/ImageProcessing/images";

        /// <summary>
        /// A file name that survives the resize rename unchanged is no test at all, so every
        /// case here uses a photographic PNG and asks for a size smaller than it, which forces
        /// both a resize and a jpg re-encoding, hence a new name.
        /// </summary>
        /// <remarks>
        /// The awkward names are the ones Steve McConnel reported against 6.5.1322 in BL-16669
        /// (a '%' or a '#' in the name), plus the neighbouring characters that the same
        /// mismatched encoder gets wrong.
        /// </remarks>
        [TestCase("plain.png", TestName = "CompressImages_PlainName_ReferenceFollowsRename")]
        [TestCase(
            "Vacation 2010 410.png",
            TestName = "CompressImages_Space_ReferenceFollowsRename"
        )]
        [TestCase(
            "Restroom%41%42.png",
            TestName = "CompressImages_PercentEscape_ReferenceFollowsRename"
        )]
        [TestCase(
            "photo%20restroom.png",
            TestName = "CompressImages_PercentTwenty_ReferenceFollowsRename"
        )]
        [TestCase("50% photo.png", TestName = "CompressImages_LonePercent_ReferenceFollowsRename")]
        [TestCase(
            "100%25 restroom.png",
            TestName = "CompressImages_PercentTwentyFive_ReferenceFollowsRename"
        )]
        [TestCase("100_1242#Copy.png", TestName = "CompressImages_Hash_ReferenceFollowsRename")]
        [TestCase("red & green.png", TestName = "CompressImages_Ampersand_ReferenceFollowsRename")]
        [TestCase("one+one.png", TestName = "CompressImages_Plus_ReferenceFollowsRename")]
        [TestCase("ብርሃን.png", TestName = "CompressImages_NonAscii_ReferenceFollowsRename")]
        public void CompressImages_RenamedFile_PageStillPointsAtIt(string imageFileName)
        {
            using (var bookFolder = new TemporaryFolder("CompressImagesTests"))
            {
                var sourceImage = FileLocationUtilities.GetFileDistributedWithApplication(
                    _pathToTestImages,
                    "man.png"
                );
                RobustFile.Copy(sourceImage, Path.Combine(bookFolder.Path, imageFileName));

                var dom = MakeDomWithBackgroundImage(imageFileName);
                var styleBefore = GetBackgroundImageDiv(dom).GetAttribute("style");
                // Sanity: the file we are about to publish really is on disk under the awkward
                // name, and the page really does refer to it, so a failure below is about the
                // rename and nothing else.
                Assert.That(
                    RobustFile.Exists(Path.Combine(bookFolder.Path, imageFileName)),
                    Is.True,
                    "setup: the image should be in the book folder under its original name"
                );
                Assert.That(
                    FilenameFromStyle(styleBefore),
                    Is.EqualTo(imageFileName),
                    "setup: the page should refer to the original file name"
                );

                // A photo bigger than this must be resized, which is what triggers the rename.
                BloomPubMaker.CompressImages(
                    bookFolder.Path,
                    new ImagePublishSettings { MaxWidth = 80, MaxHeight = 60 },
                    dom
                );

                var filesInFolder = Directory
                    .GetFiles(bookFolder.Path)
                    .Select(Path.GetFileName)
                    .ToList();
                // Sanity: this case really did go through the rename path on disk. If it didn't,
                // the assertion below would pass for the wrong reason.
                Assert.That(
                    filesInFolder,
                    Has.None.EqualTo(imageFileName),
                    "setup: the resize should have replaced the original file"
                );

                var referencedName = FilenameFromStyle(
                    GetBackgroundImageDiv(dom).GetAttribute("style")
                );
                Assert.That(
                    filesInFolder,
                    Has.Member(referencedName),
                    $"the page points at '{referencedName}', which is not in the book folder"
                );
            }
        }

        private static SafeXmlDocument MakeDomWithBackgroundImage(string imageFileName)
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                @"<html><body data-bffullscreenpicture=''>
                    <div class='bloom-page'>
                        <div class='bloom-canvas'>
                            <div class='bloom-canvas-element bloom-backgroundImage'>
                                <div class='bloom-imageContainer bloom-background-image-in-style-attr'></div>
                            </div>
                        </div>
                    </div>
                  </body></html>"
            );
            // Write the style the way production does, so the test is pinned to the real
            // encoding convention rather than to a guess about it.
            HtmlDom.SetImageElementUrl(
                GetBackgroundImageDiv(dom),
                UrlPathString.CreateFromUnencodedString(imageFileName)
            );
            return dom;
        }

        private static SafeXmlElement GetBackgroundImageDiv(SafeXmlDocument dom)
        {
            return dom.SafeSelectNodes(
                    "//div[contains(@class,'bloom-background-image-in-style-attr')]"
                )
                .Cast<SafeXmlElement>()
                .First();
        }

        /// <summary>
        /// Pull the file name back out of a background-image style, undoing the URL encoding
        /// that SetImageElementUrl applied.
        /// </summary>
        private static string FilenameFromStyle(string style)
        {
            const string prefix = "background-image:url('";
            var start = style.IndexOf(prefix) + prefix.Length;
            var encoded = style.Substring(start, style.IndexOf("'", start) - start);
            return UrlPathString.CreateFromUrlEncodedString(encoded).NotEncoded;
        }
    }
}
