using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Bloom.web.controllers;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
	[TestFixture]
	internal class ImageApiTests
	{
		[Test]
		public void GetWhichImagesAreUsedOnWhichPages_AccumulatesCorrectly()
		{
			const string xhtml = @"
<body>
	<div class=""bloom-page bloom-frontMatter cover"" data-page-number="""">
		<div data-after-content="""" class=""pageLabel"" lang=""en"">
			Front Cover
		</div >
		<div class=""marginBox"">
			<div class=""bloom-translationGroup bookTitle"">
				<div class=""bloom-editable"" lang=""fr""><p>french title</p></div>
				<div class=""bloom-editable"" lang=""en""><p>Test leveled reader</p></div>
			</div>
			<div class=""bloom-imageContainer bloom-backgroundImage"" style=""background-image:url('AOR_aa013m.png')""/>
			<div class=""bottomBlock"">
				<img class=""branding"" src=""cover-bottom-left.svg?optional=true"" type=""image/svg""/> 
			</div>
		</div>
	</div>
	<div class=""bloom-page bloom-frontMatter"" data-page-number="""">
		<div data-after-content="""" class=""pageLabel"" lang=""en"">
			Credits Page
		</div >
		<div class=""marginBox"">
			<div class=""bloom-metaData licenseAndCopyrightBlock"" lang=""en"">
				<div class=""copyright"">Some copyright</div>
				<div class=""licenseBlock"">
					<img class=""licenseImage"" src=""license.png""/>
				</div>
			</div>
			<img class=""branding"" src=""credits-page.svg?optional=true"" type=""image/svg""/>
		</div>
	</div>
	<div class=""bloom-page bloom-frontMatter countPageButDoNotShowNumber"" data-page-number=""1"">
		<div data-after-content="""" class=""pageLabel"" lang=""en"">
			Title Page
		</div >
		<div class=""marginBox"">
			<div class=""bloom-translationGroup"" id=""titlePageTitleBlock""></div>
			<div class=""bloom-translationGroup"" id=""originalContributions""></div>
			<div class=""bloom-translationGroup"" id=""funding""></div>
			<div>
				<img class=""branding"" src=""title-page.svg?optional=true"" type=""image/svg""/>
			</div>
		</div>
	</div>
	<div class=""bloom-page numberedPage"" lang="""" data-page-number=""2"">
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
	<div class=""bloom-page numberedPage"" lang="""" data-page-number=""3"">
		<div class=""marginBox"">
			<div title=""The Moon and The Cap_Page 041.jpg 105.32 KB 1500 x 1236 357 DPI (should be 300-600) Bit Depth: 24"" class=""bloom-imageContainer"">
				<img data-license=""cc-by"" data-creator=""Angie and Upesh"" data-copyright=""Copyright © 2017, Pratham Books"" src=""The%20Moon%20and%20The%20Cap_Page%20041.jpg""/>
			</div>
			<div class=""bloom-translationGroup""></div>
		</div>
	</div>
	<div class=""bloom-page numberedPage"" lang="""" data-page-number=""4"">
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
	<div class=""bloom-page numberedPage"" lang="""" data-page-number=""5"">
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
<!-- We skip a bunch of pages here in order to test what happens when we jump past single digits,
	 but we aren't actually testing the code that creates the numbers here, so we should be fine. -->
	<div class=""bloom-page numberedPage"" lang="""" data-page-number=""10"">
		<div data-after-content="""" class=""pageLabel"" lang=""en"">
			Basic Text &amp; Picture
		</div >
		<div class=""marginBox"">
			<div class=""bloom-translationGroup""></div>
			<div title=""AOR_EAG00864.png 6.58 KB 341 x 335 209 DPI (should be 300-600) Bit Depth: 32"" class=""bloom-imageContainer"">
				<img data-license=""cc-by-nd"" data-creator=""Cathy Marlett"" data-copyright=""Copyright, SIL International 2009."" src=""AOR_EAG00864.png""/>
			</div>
			<div class=""bloom-translationGroup""></div>
		</div>
	</div>
<!-- Is this what the page numbering system does with backMatter? No change in pagenum from here on out. -->
	<div class=""bloom-page bloom-backMatter cover"" data-page-number=""10"">
		<div data-after-content="""" class=""pageLabel"" lang=""en"">
			Inside Back Cover
		</div >
		<div class=""marginBox"">
			<div class=""bloom-translationGroup""></div>
		</div>
	</div>
	<div class=""bloom-page bloom-backMatter"" data-page-number=""10"">
		<div data-after-content="""" class=""pageLabel"" lang=""en"">
			Outside Back Cover
		</div >
		<div class=""marginBox"">
			<div class=""bloom-translationGroup""></div>
			<img class=""branding branding-wide"" src=""back-cover-outside-wide.svg?optional=true"" type=""image/svg""/>
			<img class=""branding"" src=""back-cover-outside.svg?optional=true"" type=""image/svg""/>
		</div>
	</div>
</body>";

			var dom = new XmlDocument();
			dom.LoadXml(xhtml);
			var imageNameToPages = ImageApi.GetWhichImagesAreUsedOnWhichPages(dom.SelectSingleNode("//body"));
			Assert.AreEqual(13, imageNameToPages.Keys.Count, "Should be a total of 13 unique images");
			Assert.AreEqual("Front Cover", imageNameToPages["AOR_aa013m.png"].First(), "Should include xmatter pics");
			Assert.IsTrue(imageNameToPages.ContainsKey("title-page.svg"), "Branding images get filtered later");
			Assert.IsTrue(imageNameToPages.ContainsKey("back-cover-outside.svg"), "Branding images get filtered later");
			Assert.IsTrue(imageNameToPages.ContainsKey("placeHolder.png"), "Placeholder images get filtered later");
			Assert.IsTrue(imageNameToPages.ContainsKey("AOR_aa017m.png"), "Should include background images from numbered pages");
			Assert.IsTrue(imageNameToPages.ContainsKey("The Moon and The Cap_Page 041.jpg"), "Missing normal Moon and Cap image");
			var moonAndCap = imageNameToPages["The Moon and The Cap_Page 041.jpg"];
			Assert.AreEqual(2, moonAndCap.Count, "Wrong number of Moon and Cap images");
			Assert.AreEqual("2", moonAndCap.First());
			Assert.AreEqual("3", moonAndCap.Last());
			var aorEag = imageNameToPages["AOR_EAG00864.png"];
			Assert.AreEqual(3, aorEag.Count);
			Assert.AreEqual("4", aorEag.First(), "Should sort '4' before '5' and '10'");
			Assert.AreEqual("10", aorEag.Last(), "Should sort '10' after '5'");
			Assert.IsTrue(aorEag.Contains("5"), "'5' should be there in the middle");
			var backCvr = imageNameToPages["back-cover-outside.svg"];
			Assert.AreEqual("Outside Back Cover", backCvr.First(), "Back cover image should report Outside Back Cover");
		}

		[Test]
		public void GetWhichImagesAreUsedOnWhichPages_WorksWithBengaliNumerals()
		{
			const string xhtml = @"
<body>
	<div class=""bloom-page bloom-frontMatter"" data-page-number="""">
		<div data-after-content="""" class=""pageLabel"" lang=""en"">
			Front Cover
		</div >
		<div class=""marginBox"">
			<div class=""bloom-translationGroup bookTitle""></div>
			<div class=""bloom-imageContainer bloom-backgroundImage"" style=""background-image:url('AOR_aa013m.png')""/>
			<div class=""bottomBlock"">
				<img class=""branding"" src=""cover-bottom-left.svg?optional=true"" type=""image/svg""/> 
			</div>
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
			<img class=""branding branding-wide"" src=""back-cover-outside-wide.svg?optional=true"" type=""image/svg""/>
			<img class=""branding"" src=""back-cover-outside.svg?optional=true"" type=""image/svg""/>
		</div>
	</div>
</body>";

			var dom = new XmlDocument();
			dom.LoadXml(xhtml);
			var imageNameToPages = ImageApi.GetWhichImagesAreUsedOnWhichPages(dom.SelectSingleNode("//body"));
			Assert.AreEqual(10, imageNameToPages.Keys.Count, "Should be a total of 10 unique images");
			Assert.IsTrue(imageNameToPages.ContainsKey("The Moon and The Cap_Page 041.jpg"), "Missing normal Moon and Cap image");
			var moonAndCap = imageNameToPages["The Moon and The Cap_Page 041.jpg"];
			Assert.AreEqual(2, moonAndCap.Count, "Wrong number of Moon and Cap images");
			Assert.AreEqual(new SortedSet<string> { "১", "২" }, moonAndCap, "Should return Bengali page numbers");
			Assert.AreEqual("বাইরের পিছনে কভার", imageNameToPages["back-cover-outside-wide.svg"].First(),
				"If pageLabel is translated, we should get the translated value.");
		}
	}
}
