using Bloom.SafeXml;
using Bloom.web;
using NUnit.Framework;

namespace BloomTests.web
{
    [TestFixture]
    public class PageListApiTests
    {
        /// <summary>
        /// Builds a one-page DOM containing a single img with the given (already URL-encoded)
        /// src, runs MarkImageNodesForThumbnail over it, and returns the resulting src.
        /// </summary>
        private static string GetThumbnailSrc(string originalSrc)
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                $"<div class='bloom-page'><div class='marginBox'><img src='{originalSrc}'/></div></div>"
            );
            var pageElement = (SafeXmlElement)dom.FirstChild;
            // Sanity check: we really did get the src we meant to start from.
            var img = (SafeXmlElement)pageElement.SafeSelectNodes(".//img")[0];
            Assert.AreEqual(
                originalSrc,
                img.GetAttribute("src"),
                "Setup failed: the img did not start out with the src we specified."
            );

            PageListApi.MarkImageNodesForThumbnail(pageElement);

            return ((SafeXmlElement)pageElement.SafeSelectNodes(".//img")[0]).GetAttribute("src");
        }

        [Test]
        public void MarkImageNodesForThumbnail_SimpleName_AddsThumbnailQuery()
        {
            Assert.AreEqual("picture.jpg?thumbnail=1", GetThumbnailSrc("picture.jpg"));
        }

        // BL-16658: the src of a thumbnail image must stay URL-encoded. If the encoding is lost,
        // characters like # and ? in the file name make the browser request the wrong thing
        // (or nothing at all), and the page list shows a broken image.
        [Test]
        public void MarkImageNodesForThumbnail_NameWithSpecialCharacters_KeepsPathEncoded()
        {
            // This is the encoding of the real file name: This Image !@#$%^&()2.jpg
            const string encodedName = "This%20Image%20!%40%23%24%25%5e%26()2.jpg";

            var result = GetThumbnailSrc(encodedName);

            Assert.AreEqual(
                encodedName + "?thumbnail=1",
                result,
                "The path must remain URL-encoded and the query must remain literal."
            );
        }

        [Test]
        public void MarkImageNodesForThumbnail_ExistingQuery_AppendsThumbnailAndKeepsPathEncoded()
        {
            const string encodedName = "This%20Image%20!%40%23%24%25%5e%26()2.jpg";

            var result = GetThumbnailSrc(encodedName + "?optional=true");

            Assert.AreEqual(encodedName + "?optional=true&thumbnail=1", result);
        }

        [Test]
        public void MarkImageNodesForThumbnail_ApiUrl_LeftAlone()
        {
            const string brandingUrl = "/bloom/api/branding/image?id=cover-lower-left.png";

            Assert.AreEqual(brandingUrl, GetThumbnailSrc(brandingUrl));
        }

        [Test]
        public void MarkImageNodesForThumbnail_BackgroundImage_KeepsPathEncoded()
        {
            const string encodedName = "This%20Image%20!%40%23%24%25%5e%26()2.jpg";
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                $"<div class='bloom-page'><div class='marginBox' style=\"background-image:url('{encodedName}')\"/></div>"
            );
            var pageElement = (SafeXmlElement)dom.FirstChild;

            PageListApi.MarkImageNodesForThumbnail(pageElement);

            var marginBox = (SafeXmlElement)
                pageElement.SafeSelectNodes(".//*[contains(@style,'background-image')]")[0];
            Assert.AreEqual(
                $"background-image:url('{encodedName}?thumbnail=1')",
                marginBox.GetAttribute("style")
            );
        }
    }
}
