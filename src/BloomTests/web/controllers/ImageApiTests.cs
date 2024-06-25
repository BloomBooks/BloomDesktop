using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Bloom.Book;
using Bloom.SafeXml;
using Bloom.web.controllers;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    [TestFixture]
    internal class ImageApiTests
    {
        private BookSelection _selection;
        private ImageApi _apiObject;
        private Dictionary<string, List<string>> _creditsToFormat;

        [SetUp]
        public void Setup()
        {
            _selection = new BookSelection();
            _apiObject = new ImageApi(_selection);
            _creditsToFormat = new Dictionary<string, List<string>>();
            // Ensure only English UI strings (the default value).
            L10NSharp.LocalizationManager.SetUILanguage("en", false);
        }

        [TearDown]
        public void TearDown()
        {
            _selection = null;
            _apiObject = null;
            _creditsToFormat = null;
        }

        [Test]
        public void GetFilteredImageNameToPagesDictionary_AccumulatesCorrectly()
        {
            const string xhtml =
                @"
<body>
	<div class='bloom-page bloom-frontMatter cover' data-page-number=''>
		<div data-after-content='' class='pageLabel' lang='en'>
			Front Cover
		</div >
		<div class='marginBox'>
			<div class='bloom-translationGroup bookTitle'>
				<div class='bloom-editable' lang='fr'><p>french title</p></div>
				<div class='bloom-editable' lang='en'><p>Test leveled reader</p></div>
			</div>
			<div class='bloom-imageContainer bloom-backgroundImage' style='background-image:url(""AOR_aa013m.png"")'/>
		</div>
	</div>
	<div class='bloom-page bloom-frontMatter' data-page-number=''>
		<div data-after-content='' class='pageLabel' lang='en'>
			Credits Page
		</div >
		<div class='marginBox'>
			<div class='bloom-metaData licenseAndCopyrightBlock' lang='en'>
				<div class='copyright'>Some copyright</div>
				<div class='licenseBlock'>
					<img class='licenseImage' src='license.png'/>
				</div>
			</div>
			<div data-book='credits-page-bottom-branding'>
				<img src='foo.png'/>
			</div>
		</div>
	</div>
	<div class='bloom-page bloom-frontMatter countPageButDoNotShowNumber' data-page-number='1'>
		<div data-after-content='' class='pageLabel' lang='en'>
			Title Page
		</div >
		<div class='marginBox'>
			<div class='bloom-translationGroup' id='titlePageTitleBlock'></div>
			<div class='bloom-translationGroup' id='originalContributions'></div>
			<div class='bloom-translationGroup' id='funding'></div>
			<div data-book='title-page-branding-bottom'>
				<img src='blah.svg'/>
			</div>
		</div>
	</div>
	<div class='bloom-page numberedPage' lang='' data-page-number='2'>
		<div data-after-content='' class='pageLabel' lang='en'>
			Basic Text &amp; Picture
		</div >
		<div class='marginBox'>
<!-- extra 'split-pane' div layers removed here and elsewhere -->
			<div title='The Moon and The Cap_Page 041.jpg 105.32 KB 1500 x 1236 357 DPI (should be 300-600) Bit Depth: 24' class='bloom-imageContainer'>
				<img data-license='cc-by' data-creator='Angie and Upesh' data-copyright='Copyright © 2017, Pratham Books' src='The%20Moon%20and%20The%20Cap_Page%20041.jpg'/>
			</div>
			<div class='bloom-translationGroup'></div>
			<div class='bloom-imageContainer'>
				<img src='placeHolder.png'/>
			</div>
			<div class='bloom-imageContainer bloom-backgroundImage' style='background-image:url(""AOR_aa017m.png"")'/>
			<div class='bloom-translationGroup'></div>
			<div class='bloom-imageContainer bloom-backgroundImage' style='background-image:url(""AOR_aa013m.png"")'/>
		</div>
	</div>
	<div class='bloom-page numberedPage' lang='' data-page-number='3'>
		<div class='marginBox'>
			<div title='The Moon and The Cap_Page 041.jpg 105.32 KB 1500 x 1236 357 DPI (should be 300-600) Bit Depth: 24' class='bloom-imageContainer'>
				<img data-license='cc-by' data-creator='Angie and Upesh' data-copyright='Copyright © 2017, Pratham Books' src='The%20Moon%20and%20The%20Cap_Page%20041.jpg'/>
			</div>
			<div class='bloom-translationGroup'></div>
		</div>
	</div>
	<div class='bloom-page numberedPage' lang='' data-page-number='4'>
		<div class='marginBox'>
			<div class='bloom-translationGroup'></div>
			<div title='AOR_EAG00864.png 18.55 KB 564 x 457 273 DPI (should be 300-600) Bit Depth: 32' class='bloom-imageContainer'>
				<img data-license='cc-by-nd' data-creator='Roel Ottow' data-copyright='Copyright, SIL International 2009.' src='AOR_EAG00864.png'/>
			</div>
			<div title='AOR_abbb007.png 84.21 KB 1136 x 1500 543 DPI (should be 300-600) Bit Depth: 32' class='bloom-imageContainer'>
				<img data-license='cc-by-nd' data-creator='' data-copyright='Copyright, SIL International 2009.' src='AOR_abbb007.png'/>
			</div>
			<div class='bloom-translationGroup'></div>
			<div title='AOR_EAG00864.png 18.55 KB 564 x 457 273 DPI (should be 300-600) Bit Depth: 32' class='bloom-imageContainer'>
				<img data-license='cc-by-nd' data-creator='Roel Ottow' data-copyright='Copyright, SIL International 2009.' src='AOR_EAG00864.png'/>
			</div>
			<div class='bloom-translationGroup'></div>
		</div>
	</div>
	<div class='bloom-page numberedPage' lang='' data-page-number='5'>
		<div class='marginBox'>
			<div class='bloom-translationGroup'></div>
			<div title='AOR_EAG00864.png 6.58 KB 341 x 335 209 DPI (should be 300-600) Bit Depth: 32' class='bloom-imageContainer'>
				<img data-license='cc-by-nd' data-creator='Cathy Marlett' data-copyright='Copyright, SIL International 2009.' src='AOR_EAG00864.png'/>
			</div>
			<div title='AOR_ACC029M.png 83.35 KB 1500 x 806 382 DPI (should be 300-600) Bit Depth: 32' class='bloom-imageContainer'>
				<img data-license='cc-by-nd' data-creator='Cathy Marlett' data-copyright='Copyright, SIL International 2009.' src='AOR_ACC029M.png'/>
			</div>
			<div class='bloom-translationGroup'></div>
		</div>
	</div>
<!-- We skip a bunch of pages here in order to test what happens when we jump past single digits,
	 but we aren't actually testing the code that creates the numbers here, so we should be fine. -->
	<div class='bloom-page numberedPage' lang='' data-page-number='10'>
		<div data-after-content='' class='pageLabel' lang='en'>
			Basic Text &amp; Picture
		</div >
		<div class='marginBox'>
			<div class='bloom-translationGroup'></div>
			<div title='AOR_EAG00864.png 6.58 KB 341 x 335 209 DPI (should be 300-600) Bit Depth: 32' class='bloom-imageContainer'>
				<img data-license='cc-by-nd' data-creator='Cathy Marlett' data-copyright='Copyright, SIL International 2009.' src='AOR_EAG00864.png'/>
			</div>
			<div class='bloom-translationGroup'></div>
			<div class='bloom-imageContainer bloom-backgroundImage' style='background-image:url(""AOR_aa018.png"")'/>
		</div>
	</div>
<!-- Is this what the page numbering system does with backMatter? No change in pagenum from here on out. -->
	<div class='bloom-page bloom-backMatter cover' data-page-number='10'>
		<div data-after-content='' class='pageLabel' lang='en'>
			Inside Back Cover
		</div >
		<div class='marginBox'>
			<div class='bloom-translationGroup'></div>
		</div>
	</div>
	<div class='bloom-page bloom-backMatter' data-page-number='10'>
		<div data-after-content='' class='pageLabel' lang='en'>
			Outside Back Cover
		</div >
		<div class='marginBox'>
			<div class='bloom-translationGroup'></div>
			<div class='bloom-imageContainer bloom-backgroundImage' style='background-image:url(""AOR_aa018.png"")'/>
			<div data-book='title-page-branding-bottom'>
				<img src='back-cover-outside-wide.png'/>
				<img src='back-cover-outside.svg' type='image/svg'/>
			</div>
		</div>
	</div>
</body>";

            var dom = SafeXmlDocument.Create();
            dom.LoadXml(xhtml);
            var imageNameToPages = _apiObject.GetFilteredImageNameToPagesDictionary(
                dom.SelectSingleNode("//body")
            );
            Assert.AreEqual(7, imageNameToPages.Keys.Count, "Should be a total of 7 unique images");
            Assert.AreEqual(
                "Front Cover",
                imageNameToPages["AOR_aa013m.png"].First(),
                "Should include xmatter pics"
            );
            Assert.AreEqual(
                "2",
                imageNameToPages["AOR_aa013m.png"].Last(),
                "Numbered pages should be after front matter"
            );
            Assert.IsFalse(
                imageNameToPages.ContainsKey("title-page.svg"),
                "Branding images get filtered out"
            );
            Assert.IsFalse(
                imageNameToPages.ContainsKey("credits-page-bottom.png"),
                "Branding images get filtered out"
            );
            Assert.IsFalse(
                imageNameToPages.ContainsKey("back-cover-outside.svg"),
                "Branding images get filtered out"
            );
            Assert.IsFalse(
                imageNameToPages.ContainsKey("back-cover-outside-wide.png"),
                "Branding images get filtered out"
            );
            Assert.IsFalse(
                imageNameToPages.ContainsKey("placeHolder.png"),
                "Placeholder images get filtered out"
            );
            Assert.IsTrue(
                imageNameToPages.ContainsKey("AOR_aa017m.png"),
                "Should include background images from numbered pages"
            );
            Assert.IsTrue(
                imageNameToPages.ContainsKey("The Moon and The Cap_Page 041.jpg"),
                "Missing normal Moon and Cap image"
            );
            var moonAndCap = imageNameToPages["The Moon and The Cap_Page 041.jpg"];
            Assert.AreEqual(2, moonAndCap.Count, "Wrong number of Moon and Cap images");
            Assert.AreEqual("2", moonAndCap.First());
            Assert.AreEqual("3", moonAndCap.Last());
            var aorEag = imageNameToPages["AOR_EAG00864.png"];
            Assert.AreEqual(3, aorEag.Count);
            Assert.AreEqual("4", aorEag.First(), "Should sort '4' before '5' and '10'");
            Assert.AreEqual("10", aorEag.Last(), "Should sort '10' after '5'");
            Assert.IsTrue(aorEag.Contains("5"), "'5' should be there in the middle");
            var backCvr = imageNameToPages["AOR_aa018.png"];
            Assert.AreEqual(
                "10",
                backCvr.First(),
                "Page 10 image should report before back matter"
            );
            Assert.AreEqual(
                "Outside Back Cover",
                backCvr.Last(),
                "Back cover non-branding image should report Outside Back Cover"
            );
        }

        [Test]
        public void GetFilteredImageNameToPagesDictionary_WorksWithBengaliNumerals()
        {
            const string xhtml =
                @"
<body>
	<div class=""bloom-page bloom-frontMatter"" data-page-number="""">
		<div data-after-content="""" class=""pageLabel"" lang=""en"">
			Front Cover
		</div >
		<div class=""marginBox"">
			<div class=""bloom-translationGroup bookTitle""></div>
			<div class=""bloom-imageContainer bloom-backgroundImage"" style=""background-image:url('AOR_aa013m.png')""/>
		</div>
	</div>
	<div class=""bloom-page numberedPage"" lang="""" data-page-number=""১"">
		<div data-after-content="""" class=""pageLabel"" lang=""en"">
			Basic Text &amp; Picture
		</div >
		<div class=""marginBox"">
<!-- extra 'split-pane' div layers removed here and elsewhere -->
			<div title=""The Moon and The Cap_Page 041.jpg 105.32 KB 1500 x 1236 357 DPI (should be 300-600) Bit Depth: 24"" class=""bloom-imageContainer"">
				<img data-license=""cc-by"" data-creator=""Angie and Upesh"" data-copyright=""Copyright © 2017, Pratham Books"" src=""The%20Moon%20and%20The%20Cap_Page%20041.jpg""/>
			</div>
			<div class=""bloom-translationGroup""></div>
			<div class=""bloom-imageContainer"">
				<img src=""placeHolder.png""/>
			</div>
			<div class=""bloom-imageContainer bloom-backgroundImage"" style=""background-image:url('AOR_aa017m.png')""/>
			<div class=""bloom-translationGroup""></div>
		</div>
	</div>
	<div class=""bloom-page numberedPage"" lang="""" data-page-number=""২"">
		<div data-after-content="""" class=""pageLabel"" lang=""en"">
			Basic Text &amp; Picture
		</div >
		<div class=""marginBox"">
			<div title=""The Moon and The Cap_Page 041.jpg 105.32 KB 1500 x 1236 357 DPI (should be 300-600) Bit Depth: 24"" class=""bloom-imageContainer"">
				<img data-license=""cc-by"" data-creator=""Angie and Upesh"" data-copyright=""Copyright © 2017, Pratham Books"" src=""The%20Moon%20and%20The%20Cap_Page%20041.jpg""/>
			</div>
			<div class=""bloom-translationGroup""></div>
		</div>
	</div>
	<div class=""bloom-page numberedPage"" lang="""" data-page-number=""৩"">
		<div data-after-content="""" class=""pageLabel"" lang=""en"">
			Basic Text &amp; Picture
		</div >
		<div class=""marginBox"">
			<div class=""bloom-translationGroup""></div>
			<div title=""AOR_EAG00864.png 18.55 KB 564 x 457 273 DPI (should be 300-600) Bit Depth: 32"" class=""bloom-imageContainer"">
				<img data-license=""cc-by-nd"" data-creator=""Roel Ottow"" data-copyright=""Copyright, SIL International 2009."" src=""AOR_EAG00864.png""/>
			</div>
			<div title=""AOR_abbb007.png 84.21 KB 1136 x 1500 543 DPI (should be 300-600) Bit Depth: 32"" class=""bloom-imageContainer"">
				<img data-license=""cc-by-nd"" data-creator="""" data-copyright=""Copyright, SIL International 2009."" src=""AOR_abbb007.png""/>
			</div>
			<div class=""bloom-translationGroup""></div>
			<div title=""AOR_EAG00864.png 18.55 KB 564 x 457 273 DPI (should be 300-600) Bit Depth: 32"" class=""bloom-imageContainer"">
				<img data-license=""cc-by-nd"" data-creator=""Roel Ottow"" data-copyright=""Copyright, SIL International 2009."" src=""AOR_EAG00864.png""/>
			</div>
			<div class=""bloom-translationGroup""></div>
		</div>
	</div>
	<div class=""bloom-page numberedPage"" lang="""" data-page-number=""৪"">
		<div data-after-content="""" class=""pageLabel"" lang=""en"">
			Basic Text &amp; Picture
		</div >
		<div class=""marginBox"">
			<div class=""bloom-translationGroup""></div>
			<div title=""AOR_EAG00864.png 6.58 KB 341 x 335 209 DPI (should be 300-600) Bit Depth: 32"" class=""bloom-imageContainer"">
				<img data-license=""cc-by-nd"" data-creator=""Cathy Marlett"" data-copyright=""Copyright, SIL International 2009."" src=""AOR_EAG00864.png""/>
			</div>
			<div title=""AOR_ACC029M.png 83.35 KB 1500 x 806 382 DPI (should be 300-600) Bit Depth: 32"" class=""bloom-imageContainer"">
				<img data-license=""cc-by-nd"" data-creator=""Cathy Marlett"" data-copyright=""Copyright, SIL International 2009."" src=""AOR_ACC029M.png""/>
			</div>
			<div class=""bloom-translationGroup""></div>
		</div>
	</div>
<!-- Is this what the page numbering system does with backMatter? No change in pagenum from here on out. -->
	<div class=""bloom-page bloom-backMatter"" data-page-number=""৪"">
		<div data-after-content="""" class=""pageLabel"" lang=""en"">
			Inside Back Cover
		</div >
		<div class=""marginBox"">
			<div class=""bloom-translationGroup"">
				<div class=""bloom-editable bloom-content1"" lang=""en""></div>
			</div>
		</div>
	</div>
	<div class=""bloom-page bloom-backMatter"" data-page-number=""৪"">
		<div data-after-content="""" class=""pageLabel"" lang=""bgl"">
			বাইরের পিছনে কভার
		</div >
		<div class=""marginBox"">
			<div class=""bloom-translationGroup"">
				<div class=""bloom-editable bloom-content1"" lang=""en""></div>
			</div>
			<div class=""bloom-imageContainer bloom-backgroundImage"" style=""background-image:url('AOR_aa010.png')""/>
			<div data-book='title-page-branding-bottom'>
				<img src=""back-cover-outside-wide.png?optional=true""/>
				<img src=""back-cover-outside.svg?optional=true"" type=""image/svg""/>
			</div>
		</div>
	</div>
</body>";

            var dom = SafeXmlDocument.Create();
            dom.LoadXml(xhtml);
            var imageNameToPages = _apiObject.GetFilteredImageNameToPagesDictionary(
                dom.SelectSingleNode("//body")
            );
            Assert.AreEqual(7, imageNameToPages.Keys.Count, "Should be a total of 7 unique images");
            Assert.IsTrue(
                imageNameToPages.ContainsKey("The Moon and The Cap_Page 041.jpg"),
                "Missing normal Moon and Cap image"
            );
            var moonAndCap = imageNameToPages["The Moon and The Cap_Page 041.jpg"];
            Assert.AreEqual(2, moonAndCap.Count, "Wrong number of Moon and Cap images");
            Assert.AreEqual(
                new SortedSet<string> { "১", "২" },
                moonAndCap,
                "Should return Bengali page numbers"
            );
            Assert.AreEqual(
                "বাইরের পিছনে কভার",
                imageNameToPages["AOR_aa010.png"].First(),
                "If pageLabel is translated, we should get the translated value."
            );
        }

        [Test]
        public void GetFilteredImageNameToPagesDictionary_WorksWithBishmallahImageInDataDiv()
        {
            const string xhtml =
                @"
<body>
	<div id=""bloomDataDiv"">
		<div lang=""*"" data-book=""bishmallah"">
			<img alt="""" src=""myBishmallahImage.png""/>
		</div>
	</div>
</body>";

            var dom = SafeXmlDocument.Create();
            dom.LoadXml(xhtml);
            var imageNameToPages = _apiObject.GetFilteredImageNameToPagesDictionary(
                dom.SelectSingleNode("//body")
            );
            Assert.AreEqual(0, imageNameToPages.Keys.Count, "Unique image is not on a bloom-page");
        }

        [Test]
        public void CollectFormattedCredits_SingleCredit_Works()
        {
            _creditsToFormat.Add("my credit", new List<string> { "Front Cover", "2" });
            var results = ImageApi.CollectFormattedCredits(_creditsToFormat);
            Assert.That(results.Count(), Is.EqualTo(1));
            Assert.That(
                results.First(),
                Is.EqualTo("Images by my credit."),
                "Single credit should have non-page format."
            );
        }

        [Test]
        public void CollectFormattedCredits_MultipleCredits_Works()
        {
            _creditsToFormat.Add("my credit", new List<string> { "Front Cover", "2" });
            _creditsToFormat.Add(
                "next credit",
                new List<string> { "2", "4", "Outside Back Cover" }
            );
            var results = ImageApi.CollectFormattedCredits(_creditsToFormat);
            Assert.That(results.Count(), Is.EqualTo(2));
            Assert.That(
                results.First(),
                Is.EqualTo("Images on pages Front Cover, 2 by my credit.")
            );
            Assert.That(
                results.Last(),
                Is.EqualTo("Images on pages 2, 4, Outside Back Cover by next credit.")
            );
        }

        [Test]
        public void CollectFormattedCredits_MultipleCredits_WorksWithEnDash()
        {
            _creditsToFormat.Add("my credit", new List<string> { "2" });
            _creditsToFormat.Add(
                "next credit",
                new List<string> { "2", "3", "4", "Outside Back Cover" }
            );
            var results = ImageApi.CollectFormattedCredits(_creditsToFormat);
            Assert.That(results.Count(), Is.EqualTo(2));
            Assert.That(results.First(), Is.EqualTo("Image on page 2 by my credit."));
            // This is an en-dash, not a hyphen!
            Assert.That(
                results.Last(),
                Is.EqualTo("Images on pages 2–4, Outside Back Cover by next credit.")
            );
        }

        [Test]
        public void CollectFormattedCredits_MultipleCredits_WorksWithEnDash2()
        {
            _creditsToFormat.Add("abc credit", new List<string> { "5", "7", "8", "9", "10", "12" });
            _creditsToFormat.Add("some credit", new List<string> { "6" });
            var results = ImageApi.CollectFormattedCredits(_creditsToFormat);
            Assert.That(results.Count(), Is.EqualTo(2));
            // This is an en-dash, not a hyphen!
            Assert.That(results.First(), Is.EqualTo("Images on pages 5, 7–10, 12 by abc credit."));
            Assert.That(results.Last(), Is.EqualTo("Image on page 6 by some credit."));
        }

        [Test]
        public void CollectFormattedCredits_MultipleCredits_WorksWithEnDash3()
        {
            _creditsToFormat.Add("some credit", new List<string> { "1" });
            _creditsToFormat.Add("abc credit", new List<string> { "5", "7", "8", "9", "10" });
            var results = ImageApi.CollectFormattedCredits(_creditsToFormat);
            Assert.That(results.Count(), Is.EqualTo(2));
            // This is an en-dash, not a hyphen!
            Assert.That(results.First(), Is.EqualTo("Image on page 1 by some credit."));
            Assert.That(results.Last(), Is.EqualTo("Images on pages 5, 7–10 by abc credit."));
        }
    }
}
