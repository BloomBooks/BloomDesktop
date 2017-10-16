using System.Collections.Generic;
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
	<div class=""bloom-page bloom-frontMatter"" data-page-number="""">
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
		<div class=""marginBox"">
			<div class=""bloom-translationGroup"" id=""titlePageTitleBlock"">
				<div class=""bloom-editable"" lang=""fr""><p>french title</p></div>
				<div class=""bloom-editable bloom-content1"" lang=""en""><p>Test leveled reader</p></div>
			</div>
			<div class=""bloom-translationGroup"" id=""originalContributions"">
				<div class=""bloom-editable bloom-contentNational1"" lang=""en""><p>names</p></div>
			</div>
			<div class=""bloom-translationGroup"" id=""funding"">
				<div class=""bloom-editable bloom-contentNational1"" lang=""en""/>
			</div>
			<div>
				<img class=""branding"" src=""title-page.svg?optional=true"" type=""image/svg""/>
			</div>
		</div>
	</div>
	<div class=""bloom-page numberedPage"" lang="""" data-page-number=""2"">
		<div class=""marginBox"">
<!-- extra 'split-pane' div layers removed here and elsewhere -->
			<div title=""The Moon and The Cap_Page 041.jpg 105.32 KB 1500 x 1236 357 DPI (should be 300-600) Bit Depth: 24"" class=""bloom-imageContainer"">
				<img data-license=""cc-by"" data-creator=""Angie and Upesh"" data-copyright=""Copyright © 2017, Pratham Books"" src=""The%20Moon%20and%20The%20Cap_Page%20041.jpg""/>
			</div>
			<div class=""bloom-translationGroup"">
				<div class=""bloom-editable bloom-content1"" lang=""en""><p /></div>
				<div class=""bloom-editable"" lang=""fr""><p /></div>
			</div>
			<div class=""bloom-translationGroup"">
				<div class=""bloom-editable bloom-content1"" lang=""en""><p>text here</p></div>
				<div class=""bloom-editable bloom-contentNational1"" lang=""en"" />
				<div class=""bloom-editable"" lang=""fr""><p /></div>
			</div>
			<div class=""bloom-translationGroup"">
				<div class=""bloom-editable bloom-content1"" lang=""en""><p /></div>
				<div class=""bloom-editable "" lang=""fr""><p /></div>
			</div>
			<div class=""bloom-imageContainer"">
				<img src=""placeHolder.png""/>
			</div>
			<div class=""bloom-translationGroup"">
				<div class=""bloom-editable bloom-content1"" lang=""en""><p>text here</p></div>
				<div class=""bloom-editable"" lang=""fr""><p /></div>
			</div>
		</div>
	</div>
	<div class=""bloom-page numberedPage"" lang="""" data-page-number=""3"">
		<div class=""marginBox"">
			<div title=""The Moon and The Cap_Page 041.jpg 105.32 KB 1500 x 1236 357 DPI (should be 300-600) Bit Depth: 24"" class=""bloom-imageContainer"">
				<img data-license=""cc-by"" data-creator=""Angie and Upesh"" data-copyright=""Copyright © 2017, Pratham Books"" src=""The%20Moon%20and%20The%20Cap_Page%20041.jpg""/>
			</div>
			<div class=""bloom-translationGroup"">
				<div class=""bloom-editable bloom-contentNational1"" lang=""en"">
					<p>On the way home, a very strong wind came.
					<br />It blew my cap away.</p>
				</div>
				<div class=""bloom-editable"" lang=""fr"">
					<p>En revenant à la maison, un très gros coup de vent a soufﬂé et ma nouvelle casquette s’est envolée.</p>
				</div>
			</div>
		</div>
	</div>
	<div class=""bloom-page numberedPage"" lang="""" data-page-number=""4"">
		<div class=""marginBox"">
			<div class=""bloom-translationGroup"">
				<div class=""bloom-editable bloom-content1"" lang=""en""><p /></div>
				<div class=""bloom-editable"" lang=""fr""><p /></div>
			</div>
			<div title=""AOR_EAG00864.png 18.55 KB 564 x 457 273 DPI (should be 300-600) Bit Depth: 32"" class=""bloom-imageContainer"">
				<img data-license=""cc-by-nd"" data-creator=""Roel Ottow"" data-copyright=""Copyright, SIL International 2009."" src=""AOR_EAG00864.png""/>
			</div>
			<div title=""AOR_abbb007.png 84.21 KB 1136 x 1500 543 DPI (should be 300-600) Bit Depth: 32"" class=""bloom-imageContainer"">
				<img data-license=""cc-by-nd"" data-creator="""" data-copyright=""Copyright, SIL International 2009."" src=""AOR_abbb007.png""/>
			</div>
			<div class=""bloom-translationGroup"">
				<div class=""bloom-editable bloom-content1"" lang=""en""><p /></div>
				<div class=""bloom-editable"" lang=""fr""><p /></div>
			</div>
			<div title=""AOR_EAG00864.png 18.55 KB 564 x 457 273 DPI (should be 300-600) Bit Depth: 32"" class=""bloom-imageContainer"">
				<img data-license=""cc-by-nd"" data-creator=""Roel Ottow"" data-copyright=""Copyright, SIL International 2009."" src=""AOR_EAG00864.png""/>
			</div>
			<div class=""bloom-translationGroup"">
				<div class=""bloom-editable bloom-content1"" lang=""en""><p>Some text</p></div>
				<div class=""bloom-editable"" lang=""fr""><p /></div>
			</div>
		</div>
	</div>
	<div class=""bloom-page numberedPage"" lang="""" data-page-number=""5"">
		<div class=""marginBox"">
			<div class=""bloom-translationGroup"">
				<div class=""bloom-editable bloom-content1"" lang=""en""><p>There was a box.</p></div>
				<div class=""bloom-editable"" lang=""fr""><p/></div>
			</div>
			<div class=""bloom-translationGroup"">
				<div class=""bloom-editable bloom-content1"" lang=""en""><p /></div>
				<div class=""bloom-editable"" lang=""fr""><p /></div>
			</div>
			<div title=""AOR_EAG00864.png 6.58 KB 341 x 335 209 DPI (should be 300-600) Bit Depth: 32"" class=""bloom-imageContainer"">
				<img data-license=""cc-by-nd"" data-creator=""Cathy Marlett"" data-copyright=""Copyright, SIL International 2009."" src=""AOR_EAG00864.png""/>
			</div>
			<div title=""AOR_ACC029M.png 83.35 KB 1500 x 806 382 DPI (should be 300-600) Bit Depth: 32"" class=""bloom-imageContainer"">
				<img data-license=""cc-by-nd"" data-creator=""Cathy Marlett"" data-copyright=""Copyright, SIL International 2009."" src=""AOR_ACC029M.png""/>
			</div>
			<div class=""bloom-translationGroup"">
				<div class=""bloom-editable bloom-content1"" lang=""en""><p>The cat liked the box.</p></div>
				<div class=""bloom-editable"" lang=""fr""><p /></div>
			</div>
		</div>
	</div>
<!-- Is this what the page numbering system does with backMatter? No change in pagenum from here on out. -->
	<div class=""bloom-page bloom-backMatter"" data-page-number=""5"">
		<div class=""marginBox"">
			<div class=""bloom-translationGroup"">
				<div class=""bloom-editable bloom-content1"" lang=""en""></div>
			</div>
		</div>
	</div>
	<div class=""bloom-page bloom-backMatter"" data-page-number=""5"">
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
			Assert.IsTrue(imageNameToPages.ContainsKey("title-page.svg"), "Branding images get filtered later");
			Assert.IsTrue(imageNameToPages.ContainsKey("back-cover-outside.svg"), "Branding images get filtered later");
			Assert.IsTrue(imageNameToPages.ContainsKey("placeHolder.png"), "Placeholder images get filtered later");
			Assert.IsTrue(!imageNameToPages.ContainsKey("AOR_aa013m.png"), "Wrongly includes background images from frontmatter");
			Assert.IsTrue(imageNameToPages.ContainsKey("The Moon and The Cap_Page 041.jpg"), "Missing normal Moon and Cap image");
			var moonAndCap = imageNameToPages["The Moon and The Cap_Page 041.jpg"];
			Assert.AreEqual(2, moonAndCap.Count, "Wrong number of Moon and Cap images");
			Assert.AreEqual(new List<int> { 2, 3 }, moonAndCap);
			var aorEag = imageNameToPages["AOR_EAG00864.png"];
			Assert.AreEqual(2, aorEag.Count);
			Assert.AreEqual(new List<int> { 4, 5 }, aorEag, "Should only count once per page (no 4, 4, 5)");
		}
	}
}
