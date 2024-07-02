using System.Globalization;
using Bloom.Book;
using Bloom.Publish.Epub;
using Bloom.SafeXml;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

namespace BloomTests.Publish
{
    [TestFixture]
    public class EPubMakerTests
    {
        [Test]
        public void HandleImageDescriptions_LicenseAlt_SetToDefault()
        {
            string inputImageHtml =
                "<img class=\"licenseImage\" src=\"a.png\" alt=\"This picture, a.png, is missing or was loading too slowly.\" />";
            var htmlDom = new HtmlDom(
                $"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{inputImageHtml}</div></body></html>"
            );

            HandleImageDescriptions(htmlDom);

            string expectedImageHtml =
                "<img class=\"licenseImage\" src=\"a.png\" alt=\"Image representing the license of this book\" role=\"presentation\" />";
            Assert.That(
                htmlDom.InnerXml,
                Is.EqualTo(
                    $"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{expectedImageHtml}</div></body></html>"
                )
            );
        }

        [Test]
        public void HandleImageDescriptions_BrandingAlt_Custom_Unchanged()
        {
            string inputImageHtml = "<img src=\"a.png\" alt=\"Logo of CLB\" />";
            var htmlDom = new HtmlDom(
                $"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{inputImageHtml}</div></body></html>"
            );

            HandleImageDescriptions(htmlDom);

            string expectedImageHtml =
                "<img src=\"a.png\" alt=\"Logo of CLB\" role=\"presentation\" />";
            Assert.That(
                htmlDom.InnerXml,
                Is.EqualTo(
                    $"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{expectedImageHtml}</div></body></html>"
                )
            );
        }

        [Test]
        public void HandleImageDescriptions_BrandingAlt_Placeholder_SetToDefault()
        {
            string inputImageHtml =
                "<img src=\"a.png\" alt=\"This picture, a.png, is missing or was loading too slowly.\" />";
            var htmlDom = new HtmlDom(
                $"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{inputImageHtml}</div></body></html>"
            );

            HandleImageDescriptions(htmlDom);

            string expectedImageHtml =
                "<img src=\"a.png\" alt=\"Logo of the book sponsors\" role=\"presentation\" />";
            Assert.That(
                htmlDom.InnerXml,
                Is.EqualTo(
                    $"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{expectedImageHtml}</div></body></html>"
                )
            );
        }

        [Test]
        public void HandleImageDescriptions_BrandingAlt_NotSet_SetToDefault()
        {
            string inputImageHtml = "<img src=\"a.png\" />";
            var htmlDom = new HtmlDom(
                $"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{inputImageHtml}</div></body></html>"
            );

            HandleImageDescriptions(htmlDom);

            string expectedImageHtml =
                "<img src=\"a.png\" alt=\"Logo of the book sponsors\" role=\"presentation\" />";
            Assert.That(
                htmlDom.InnerXml,
                Is.EqualTo(
                    $"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{expectedImageHtml}</div></body></html>"
                )
            );
        }

        [Test]
        public void HandleImageDescriptions_AriaHidden()
        {
            string inputImageHtml = "<img src='a.png'/>";
            var htmlDom = new HtmlDom(
                $@"<html><body>
	<div class='bloom-page' data-page-number='5'>
		<div class='marginBox'>
			<div class='bloom-imageContainer' aria-hidden='true'>
				{inputImageHtml}
				<div class='bloom-translationGroup bloom-imageDescription bloom-content1'>
					<div class='bloom-editable bloom-visibility-code-on' lang='en'></div>
				</div>
			</div>
		</div>
	</div>
</body></html>"
            );

            HandleImageDescriptions(htmlDom);

            var imgNodes = htmlDom.SafeSelectNodes("//img[@src='a.png']");
            Assert.That(imgNodes, Is.Not.Null);
            Assert.That(imgNodes.Length, Is.EqualTo(1));
            var img = imgNodes[0] as SafeXmlElement;
            Assert.That(img, Is.Not.Null);
            Assert.That(img.AttributeNames.Length, Is.EqualTo(2));
            Assert.That(img.GetAttribute("role"), Is.EqualTo("presentation"));
        }

        [Test]
        public void HandleImageDescriptions_AriaNotHidden()
        {
            string inputImageHtml = "<img src='a.png'/>";
            var htmlDom = new HtmlDom(
                $@"<html><body>
	<div class='bloom-page' data-page-number='5'>
		<div class='marginBox'>
			<div class='bloom-imageContainer'>
				{inputImageHtml}
				<div class='bloom-translationGroup bloom-imageDescription'>
					<div class='bloom-editable bloom-visibility-code-on bloom-content1' lang='en'>
						<p>This is a test.</p>
					</div>
				</div>
			</div>
		</div>
	</div>
</body></html>"
            );

            HandleImageDescriptions(htmlDom);

            var imgNodes = htmlDom.SafeSelectNodes("//img[@src='a.png']");
            Assert.That(imgNodes, Is.Not.Null);
            Assert.That(imgNodes.Length, Is.EqualTo(1));
            var img = imgNodes[0] as SafeXmlElement;
            Assert.That(img, Is.Not.Null);
            Assert.That(img.AttributeNames.Length, Is.EqualTo(2));
            Assert.That(img.GetAttribute("role"), Is.EqualTo(""));
            Assert.That(img.GetAttribute("alt"), Is.EqualTo("This is a test."));
        }

        [Test]
        public void HandleImageDescriptions_HowTo_OnPage_MovesDescriptionToAside()
        {
            string inputImageHtml = "<img src='a.png'/>";
            var htmlDom = new HtmlDom(
                $@"<html><body>
	<div class='bloom-page' data-page-number='5'>
		<div class='marginBox'>
			<div class='bloom-imageContainer'>
				{inputImageHtml}
				<div class='bloom-translationGroup bloom-imageDescription'>
					<div class='bloom-editable bloom-visibility-code-on bloom-content1' lang='en'>
						<p>This is a test.</p>
					</div>
				</div>
			</div>
		</div>
	</div>
</body></html>"
            );

            HandleImageDescriptions(htmlDom, BookInfo.HowToPublishImageDescriptions.OnPage);

            var divList = htmlDom.SafeSelectNodes("//div[@class='marginBox']/div");
            Assert.That(divList, Is.Not.Null);
            Assert.That(divList.Length, Is.EqualTo(2));

            var divImage = divList[0] as SafeXmlElement;
            Assert.That(divImage.GetAttribute("class"), Is.EqualTo("bloom-imageContainer"));
            var imgNodes = divImage.SafeSelectNodes("./*");
            Assert.That(imgNodes, Is.Not.Null);
            Assert.That(imgNodes.Length, Is.EqualTo(1));
            var img = imgNodes[0] as SafeXmlElement;
            Assert.That(img.LocalName, Is.EqualTo("img"));
            Assert.That(img.GetAttribute("alt"), Is.EqualTo("This is a test."));

            var divAside = divList[1] as SafeXmlElement;
            Assert.That(divAside.GetAttribute("class"), Is.EqualTo("asideContainer"));
            var asideNodes = divAside.SafeSelectNodes("./*");
            Assert.That(asideNodes, Is.Not.Null);
            Assert.That(asideNodes.Length, Is.EqualTo(1));
            var aside = asideNodes[0] as SafeXmlElement;
            Assert.That(aside.LocalName, Is.EqualTo("aside"));
            var paraNodes = aside.SafeSelectNodes("./*");
            Assert.That(paraNodes, Is.Not.Null);
            Assert.That(paraNodes.Length, Is.EqualTo(1));
            var para = paraNodes[0] as SafeXmlElement;
            Assert.That(para.LocalName, Is.EqualTo("p"));
            Assert.That(para.InnerText, Is.EqualTo("This is a test."));
            Assert.That(para.InnerXml, Is.EqualTo("This is a test."));
        }

        [Test]
        public void HandleImageDescriptions_HowTo_None_LeavesDescriptionAlone()
        {
            string inputImageHtml = "<img src='a.png'/>";
            var htmlDom = new HtmlDom(
                $@"<html><body>
	<div class='bloom-page' data-page-number='5'>
		<div class='marginBox'>
			<div class='bloom-imageContainer'>
				{inputImageHtml}
				<div class='bloom-translationGroup bloom-imageDescription'>
					<div class='bloom-editable bloom-visibility-code-on bloom-content1' lang='en'>
						<p>This is a test.</p>
					</div>
				</div>
			</div>
		</div>
	</div>
</body></html>"
            );

            HandleImageDescriptions(htmlDom);

            var divList = htmlDom.SafeSelectNodes("//div[@class='marginBox']/div");
            Assert.That(divList, Is.Not.Null);
            Assert.That(divList.Length, Is.EqualTo(1));
            var divImage = divList[0] as SafeXmlElement;
            Assert.That(divImage.GetAttribute("class"), Is.EqualTo("bloom-imageContainer"));
            var imgNodes = divImage.SafeSelectNodes("./*");
            Assert.That(imgNodes, Is.Not.Null);
            Assert.That(imgNodes.Length, Is.EqualTo(2));

            var img = imgNodes[0] as SafeXmlElement;
            Assert.That(img.LocalName, Is.EqualTo("img"));
            Assert.That(img.GetAttribute("alt"), Is.EqualTo("This is a test."));

            var div = imgNodes[1] as SafeXmlElement;
            Assert.That(div.LocalName, Is.EqualTo("div"));
            Assert.That(
                div.GetAttribute("class"),
                Is.EqualTo("bloom-translationGroup bloom-imageDescription")
            );
            var divDescList = div.SafeSelectNodes("./*");
            Assert.That(divDescList, Is.Not.Null);
            Assert.That(divDescList.Length, Is.EqualTo(1));
            var divDesc = divDescList[0] as SafeXmlElement;
            Assert.That(divDesc.LocalName, Is.EqualTo("div"));
            Assert.That(divDesc.GetAttribute("lang"), Is.EqualTo("en"));
            var paraNodes = divDesc.SafeSelectNodes("./*");
            Assert.That(paraNodes, Is.Not.Null);
            Assert.That(paraNodes.Length, Is.EqualTo(1));
            var para = paraNodes[0] as SafeXmlElement;
            Assert.That(para.LocalName, Is.EqualTo("p"));
            Assert.That(para.InnerText, Is.EqualTo("This is a test."));
            Assert.That(para.InnerXml, Is.EqualTo("This is a test."));
        }

        private void HandleImageDescriptions(
            HtmlDom htmlDom,
            BookInfo.HowToPublishImageDescriptions howTo =
                BookInfo.HowToPublishImageDescriptions.None
        )
        {
            var obj = new Bloom.Publish.Epub.EpubMaker(null, null);
            SIL.Reflection.ReflectionHelper.SetProperty(obj, "PublishImageDescriptions", howTo);
            SIL.Reflection.ReflectionHelper.CallMethod(obj, "HandleImageDescriptions", htmlDom);
        }

        [TestCase("A5Portrait", 559.37, 793.7)] // gets most common case right
        [TestCase("A5Landscape", 793.7, 559.37)] // make sure orientation matters
        [TestCase("LetterPortrait", 816, 1056)] // one where dimensions are inches
        [TestCase("Garbage", 559.37, 793.7)] // default to A5Portrait
        public void GetPageDimensions(string name, double expectedWidth, double expectedHeight)
        {
            EpubMaker.GetPageDimensions(name, out double width, out double height);
            // Use culture invariant formatted strings to compare for testing, so the tests will work
            // in all culture environments.
            Assert.That(
                expectedWidth.ToString(CultureInfo.InvariantCulture),
                Is.EqualTo(width.ToString("####.##", CultureInfo.InvariantCulture))
            );
            Assert.That(
                expectedHeight.ToString(CultureInfo.InvariantCulture),
                Is.EqualTo(height.ToString("####.##", CultureInfo.InvariantCulture))
            );
        }
    }
}
