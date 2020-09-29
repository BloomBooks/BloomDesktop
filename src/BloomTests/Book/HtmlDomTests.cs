using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Bloom;
using Bloom.Book;
using NUnit.Framework;
using SIL.Xml;

namespace BloomTests.Book
{
	[TestFixture]
	public sealed class HtmlDomTests
	{
		[Test]
		public void Title_EmptyDom_RoundTrips()
		{
			var dom = new HtmlDom();
			dom.Title = "foo";
			Assert.AreEqual("foo",dom.Title);
		}
		[Test]
		public void Title_CanChange()
		{
			var dom = new HtmlDom();
			dom.Title = "one";
			dom.Title = "two";
			Assert.AreEqual("two", dom.Title);
		}
		[Test]
		public void Title_HasHtml_Stripped()
		{
			var dom = new HtmlDom();
			dom.Title = "<b>one</b>1";
			Assert.AreEqual("one1", dom.Title);
		}

		[Test]
		public void RemoveCkEditorMarkup_RemovesCke_editable()
		{
			var dom = new HtmlDom(
				@"<html><body><div>
					<div id='middle' class='bloom-content1 cke_editable cke_focus cke_content_ltr another-style'></div>
					<div id='end' class='bloom-content1 cke_editable cke_focus cke_content_ltr'></div>
					<div id='start' class='cke_editable cke_focus cke_content_ltr bloom-content1'></div>
					<div id='whole' class='cke_editable cke_focus cke_content_ltr'></div>
					<div id='none' class='bloom-content1'></div>
				</div></body></html>");
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//*[contains(@class, 'cke_')]", 4); // test this xpath :-)
			HtmlDom.RemoveCkEditorMarkup(dom.RawDom.DocumentElement);
			var assertThatResult = AssertThatXmlIn.Dom(dom.RawDom);
			assertThatResult.HasSpecifiedNumberOfMatchesForXpath("//div[@id='middle' and @class='bloom-content1 another-style']",1);
			assertThatResult.HasSpecifiedNumberOfMatchesForXpath("//div[@id='end' and @class='bloom-content1']", 1);
			assertThatResult.HasSpecifiedNumberOfMatchesForXpath("//div[@id='start' and @class='bloom-content1']", 1);
			assertThatResult.HasSpecifiedNumberOfMatchesForXpath("//div[@id='whole' and @class='']", 1);
			assertThatResult.HasSpecifiedNumberOfMatchesForXpath("//div[@id='none' and @class='bloom-content1']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//*[contains(@class, 'cke_')]"); // further verify result
		}

		[Test]
		public void BaseForRelativePaths_NoHead_NoLongerThrows()
		{
			var dom = new HtmlDom(
						  @"<html></html>");
			dom.BaseForRelativePaths = "theBase";
			Assert.AreEqual("theBase", dom.BaseForRelativePaths);
		}

		[TestCase("", "", "")]
		[TestCase("a", "a", "")] // no other class present
		[TestCase("a", "b", "a")] // target class not found
		[TestCase("abc def ghk", "abc", "def ghk")] // target class at start
		[TestCase("def ghk abc", "abc", "def ghk")] // target class at end
		[TestCase("def ghk abc", "ghk", "def abc")] // target class in middle
		[TestCase("def", "de", "def")] // don't remove prefix at start
		[TestCase("def ghk", "de", "def ghk")] // don't remove prefix at start with following space
		[TestCase("def ghk abc", "gh", "def ghk abc")] // don't remove prefix in middle
		[TestCase("def ghk", "gh", "def ghk")] // don't remove prefix from last class
		[TestCase("def", "ef", "def")] // don't remove suffix at end
		[TestCase("def ghk", "hk", "def ghk")] // don't remove suffix at end
		[TestCase("def ghk abc", "hk", "def ghk abc")] // don't remove sufffix in middle
		[TestCase("def ghkj abc", "hk", "def ghkj abc")] // don't remove central part
		public void RemoveClass_RemovesCorrectly(string input, string className, string output)
		{
			Assert.That(HtmlDom.RemoveClass(className, input), Is.EqualTo(output));
		}

		[Test]
		public void BaseForRelativePaths_NullPath_SetsToEmpty()
		{
			var dom = new HtmlDom(
						  @"<html><head><base href='original'/></head></html>");
			dom.BaseForRelativePaths = null;
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("html/head/base", 0);
			Assert.AreEqual(string.Empty, dom.BaseForRelativePaths);
		}
		[Test]
		public void BaseForRelativePaths_HasExistingBase_Removes()
		{
			var dom = new HtmlDom(
						  @"<html><head><base href='original'/></head></html>");
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("html/head/base[@href='original']", 1);
			dom.BaseForRelativePaths = "new";
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("html/head/base", 0);
			Assert.AreEqual("new", dom.BaseForRelativePaths);
		}


		[Test]
		public void RemoveMetaValue_IsThere_RemovesIt()
		{
			var dom = new HtmlDom(
						  @"<html><head>
				<meta name='one' content='1'/>
				</head></html>");
			dom.RemoveMetaElement("one");
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//meta[@name='one']", 0);
		}
		[Test]
		public void RemoveMetaValue_NotThere_OK()
		{
			var dom = new HtmlDom();
			dom.RemoveMetaElement("notthere");
		}

		[Test]
		public void AddClassIfMissing_AlreadyThere_LeavesAlone()
		{
			var dom = new XmlDocument();
			dom.LoadXml(@"<div class='one two three'/>");
			HtmlDom.AddClassIfMissing((XmlElement) dom.FirstChild, "two");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("div[@class='one two three']",1);
		}
		[Test]
		public void AddClassIfMissing_Missing_Adds()
		{
			var dom = new XmlDocument();
			dom.LoadXml(@"<div class='one three'/>");
			HtmlDom.AddClassIfMissing((XmlElement)dom.FirstChild, "two");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("div[@class='one three two']", 1);
		}
		[Test]
		public void AddClassIfMissing_NoClasses_Adds()
		{
			var dom = new XmlDocument();
			dom.LoadXml(@"<div class=''/>");
			HtmlDom.AddClassIfMissing((XmlElement)dom.FirstChild, "two");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("div[@class='two']", 1);
		}

		[Test]
		[TestCase(null, "0.0")]
		[TestCase("Foobar 6 !", "0.0")]
		[TestCase("Bloom Version 3.8 (apparent build date: 28-Mar-2017)", "3.8")]
		[TestCase("Bloom Version 3.8.0 (apparent build date: 28-Mar-2017)", "3.8.0")]
		public void GetGeneratorVersion_Works(string value, string expected)
		{
			var dom = new HtmlDom($@"<html><head><meta name='Generator' content='{value}'></meta></head></html>");
			Assert.AreEqual(new System.Version(expected), dom.GetGeneratorVersion());
		}

		[Test]
		public void SortStyleSheetLinks_LeavesBasePageBeforePreviewMode()
		{
			var dom = new HtmlDom(
			   @"<html><head>
				<link rel='stylesheet' href='../../previewMode.css' type='text/css' />
				<link rel='stylesheet' href='basePage.css' type='text/css' />
				</head></html>");

			dom.SortStyleSheetLinks();

			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//head/link[1][@href='basePage.css']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//head/link[2][@href='../../previewMode.css']", 1);
		}

		[Test]
		public void SortStyleSheetLinks_LeavesOverridesAtEndAndSpecialFilesInMiddle()
		{
			var content =
			   @"<html><head>
				<link rel='stylesheet' href='my special b.css' type='text/css' />
				<link rel='stylesheet' href='Factory-Xmatter.css' type='text/css' />
				<link rel='stylesheet' href='my special a.css' type='text/css' />
				<link rel='stylesheet' href='defaultLangStyles.css' type='text/css' />
				<link rel='stylesheet' href='my special c.css' type='text/css' />
				<link rel='stylesheet' href='Basic book.css' type='text/css' />
				<link rel='stylesheet' href='customCollectionStyles.css' type='text/css' />
				<link rel='stylesheet' href='customBookStyles.css' type='text/css' />
				<link rel='stylesheet' href='basePage.css' type='text/css' />
				<link rel='stylesheet' href='langVisibility.css' type='text/css' />
				<link rel='stylesheet' href='../../editMode.css' type='text/css' />

				</head></html>";

			var bookdom = new HtmlDom(content);

			bookdom.SortStyleSheetLinks();
			var dom = bookdom.RawDom;

			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[1][@href='basePage.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[2][@href='../../editMode.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[3][@href='Basic book.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[4][@href='Factory-Xmatter.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[5][@href='my special a.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[6][@href='my special b.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[7][@href='my special c.css']", 1);

			//NB: I (JH) don't for sure know yet what the order of this should be. I think it should be last-ish.
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[8][@href='langVisibility.css']", 1);

			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[9][@href='defaultLangStyles.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[10][@href='customCollectionStyles.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[11][@href='customBookStyles.css']", 1);

		}


		[Test]
		public void MergeClassesIntoNewPage_BothEmtpy()
		{
			AssertHasClasses("", MergeClasses("","", new[] { "dropMe" }));
		}
		[Test]
		public void MergeClassesIntoNewPage_TargetEmpty()
		{
			AssertHasClasses("one two", MergeClasses("one two", "", new[] {"dropMe"}));
		}
		[Test]
		public void MergeClassesIntoNewPage_SourceEmpty()
		{
			AssertHasClasses("one two", MergeClasses("", "one two", new[] { "dropMe" }));
		}
		[Test]
		public void MergeClassesIntoNewPage_SourceAllDroppable()
		{
			AssertHasClasses("one two", MergeClasses("dropMe dropMe dropMe ", "one two", new[] { "dropMe" }));
		}
		[Test]
		public void MergeClassesIntoNewPage_MergesAndDropsItemsInDropList()
		{
			AssertHasClasses("one two three", MergeClasses("one drop two delete", "three", new[] { "drop","delete" }));
		}
		[Test]
		public void GetImageElementUrl_ElementIsImg_ReturnsSrc()
		{
			var element = MakeElement("<img src='test%20me'/>");
			Assert.AreEqual("test%20me", HtmlDom.GetImageElementUrl(element).UrlEncoded);
		}
		[Test]
		[Ignore("Does not currently work...not sure how to make it right.")]
		public void GetImageElementUrl_ElementIsImgWithPercent2B_ReturnsSrc()
		{
			var element = MakeElement("<img src='test%2bme'/>");
			Assert.AreEqual("test%2bme", HtmlDom.GetImageElementUrl(element).UrlEncoded);
		}
		[Test]
		[Ignore("Does not currently work...not sure how to make it right.")]
		public void GetImageElementUrl_ElementIsImgWithPlus_ReturnsSrc()
		{
			var element = MakeElement("<img src='test+me'/>");
			Assert.AreEqual("test+me", HtmlDom.GetImageElementUrl(element).UrlEncoded);
		}
		[Test]
		public void GetImageElementUrl_ElementIsDivWithBackgroundImage_ReturnsUrl()
		{
			var element = MakeElement("<div style='font-face:url(\"somefont\"); background-image:url(\"test%20me\")'/>");
            Assert.AreEqual("test%20me", HtmlDom.GetImageElementUrl(element).UrlEncoded);

			//stress the regex a bit
			element = MakeElement("<div style=\" background-image : URL(\'test%20me\')\"/>");
			Assert.AreEqual("test%20me", HtmlDom.GetImageElementUrl(element).UrlEncoded, "Query was too restrictive somehow");
		}

		[Test]
		public void GetImageElementUrl_ElementHasUrlOnFont_ReturnsEmpty()
		{
			var element = MakeElement("<div style='font-face:url(\"somefont\")'/>");
			Assert.AreEqual("", HtmlDom.GetImageElementUrl(element).UrlEncoded);
		}

		[Test]
		public void GetImageElementUrl_ElementHasNoImage_ReturnsEmpty()
		{
			var element = MakeElement("<div style='width:50px'/>");
			Assert.AreEqual("", HtmlDom.GetImageElementUrl(element).UrlEncoded);
		}

		private ElementProxy MakeElement(string xml)
		{
			var dom = new XmlDocument();
			dom.LoadXml(xml);
			return new ElementProxy(dom.DocumentElement);
		}

		private void AssertHasClasses(string expectedString, string actualString)
		{
			var expected = expectedString.Split(new[] { ' ' });
			var actual = actualString.Split(new[] {' '});
			Assert.AreEqual(expected.Length, actual.Length);
			foreach (var e in expected)
			{
				Assert.IsTrue(actual.Contains(e));
			}
		}

		private string MergeClasses(string sourceClasses, string targetClasses, string[] classesToDrop)
		{
			var sourceDom = new XmlDocument();
			sourceDom.LoadXml(string.Format("<div class='{0}'/>", sourceClasses));
			var targetDom = new XmlDocument();
			targetDom.LoadXml(string.Format("<div class='{0}'/>", targetClasses));
			var targetNode = (XmlElement)targetDom.SelectSingleNode("div");
			HtmlDom.MergeClassesIntoNewPage((XmlElement)sourceDom.SelectSingleNode("div"), targetNode, classesToDrop);
			return targetNode.GetStringAttribute("class");
		}

		[Test]
		public void RemoveExtraBookTitles_BookTitlesThatAreJustGeneric_Removed()
		{
			var bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='en'>something unique</div>
						<div data-book='bookTitle' lang='id'>Buku Dasar</div>
						<div data-book='bookTitle' lang='tpi'>Nupela Buk</div>
				</div>
				<div id='somePage'>
					<div class='bloom-translationGroup bookTitle'>
						<div class='bloom-editable' data-book='bookTitle' lang='tpi'>
							<p>Nupela Buk<br/></p>
						</div>
						<div class='bloom-editable' data-book='bookTitle' lang='id'>
							<p>Buku Dasar</p>
						</div>
						<div class='bloom-editable' data-book='bookTitle'>
							<p>something unique<br/></p>
						</div>
					</div>
				</div>
			 </body></html>");
			bookDom.RemoveExtraBookTitles();
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='bookTitle' and @lang='en' and text()='something unique']", 1);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='bookTitle' and @lang='id']", 0);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='bookTitle' and @lang='tpi']", 0);
		}

		[Test]
		public void SetImageElementUrl_GivenImg_SetsSrc()
		{
			var img = MakeElement("<img src='old.png'/>");
			HtmlDom.SetImageElementUrl(img, UrlPathString.CreateFromUrlEncodedString("test%20me"));
			Assert.AreEqual("test%20me",img.GetAttribute("src"));
			Assert.AreEqual("", img.GetAttribute("style"));
		}
		[Test]
		public void SetImageElementUrl_GivenDiv_SetsStyle()
		{
			var div = MakeElement("<div style=\" background-image : URL(\'old.png\')\"/>");
			HtmlDom.SetImageElementUrl(div, UrlPathString.CreateFromUrlEncodedString("test%20me"));
			Assert.AreEqual("background-image:url(\'test%20me\')", div.GetAttribute("style"));
			Assert.AreEqual("", div.GetAttribute("src"));
		}

		[Test]
		public void SelectChildImgAndBackgroundImageElements()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<div>" +
							"<div id='thisShouldBeIgnored'>" +
								"<img/>" +
								"<div style=\"background-image : URL(\'old.png\')\"/>" +
								"<img/>" +
								"<div style=\"background-image : URL(\'old.png\')\"/>" +
							"</div>" +
							"<div id='thisOne'>" +
								"<foo/>" +
								"<img/>" +
								"<div style=\"color:red;\">hello</div><div style=\" background-image : URL(\'old.png\')\"/>" +
							"</div>" +
						"</div>");
			var elements = HtmlDom.SelectChildImgAndBackgroundImageElements(dom.SelectSingleNode("//*[@id='thisOne']") as XmlElement);
			Assert.AreEqual(2,elements.Count);
		}

		[Test]
		public void GetIsImgOrSomethingWithBackgroundImage_NoBackgroundImageProperty_False()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<div style=\"color:red;\"></div>");
			Assert.IsFalse(HtmlDom.IsImgOrSomethingWithBackgroundImage(dom.DocumentElement));
		}
		[Test]
		public void GetIsImgOrSomethingWithBackgroundImage_HasBackgroundImageProperty_True()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<div style=\" background-image : URL(\'old.png\')\"></div>");
			Assert.IsTrue(HtmlDom.IsImgOrSomethingWithBackgroundImage(dom.DocumentElement));

			dom.LoadXml("<div style=\'background-image:url( \"old.png\" )\'></div>");
			Assert.IsTrue(HtmlDom.IsImgOrSomethingWithBackgroundImage(dom.DocumentElement), "Regex needs work?");
		}

		[Test]
		public void GetIsImgOrSomethingWithBackgroundImage_Img_True()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><img/></body></html>");
			Assert.IsTrue(HtmlDom.IsImgOrSomethingWithBackgroundImage(dom.SelectNodes("//img")[0] as XmlElement));
		}
		[Test]
		public void GetIsImgOrSomethingWithBackgroundImage_ImgIsSybling_False()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><img/><foo/></body></html>");
			Assert.IsFalse(HtmlDom.IsImgOrSomethingWithBackgroundImage(dom.SelectNodes("//foo")[0] as XmlElement));
		}
		[Test]
		public void GetIsImgOrSomethingWithBackgroundImage_ImgIsChild_False()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div><img/></div></body></html>");
			Assert.IsFalse(HtmlDom.IsImgOrSomethingWithBackgroundImage(dom.SelectNodes("//div")[0] as XmlElement));
		}
		[Test]
		public void RemoveBookSetting_TwoVariationsWereThere_BothRemoved()
		{
			var bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='leaveMe' lang='en'>something unique</div>
						<div data-book='removeMe' lang='id'>Buku Dasar</div>
						<div data-book='removeMe' lang='tpi'>Nupela Buk</div>
				</div>
			 </body></html>");
			bookDom.RemoveBookSetting("removeMe");
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='removeMe']", 0);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='leaveMe']", 1);
		}
		[Test]
		public void RemoveBookSetting_NoneThere_NothingHappens()
		{
			var bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='leaveMe' lang='en'>something unique</div>
				</div>
			 </body></html>");
			bookDom.RemoveBookSetting("foobar");
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='leaveMe']", 1);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book]", 1);
		}
		[Test]
		public void GetBookSetting_TwoVariationsWereThere_ReturnsBoth()
		{
			var bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='leaveMe' lang='en'>something unique</div>
						<div data-book='getMe' lang='id'>Buku</div>
						<div data-book='getMe' lang='tpi'>Buk</div>
				</div>
			 </body></html>");
			var result = bookDom.GetBookSetting("getMe");
			Assert.AreEqual(2,result.Count);
			Assert.AreEqual("Buk", result["tpi"]);
			Assert.AreEqual("Buku", result["id"]);
		}
		[Test]
		public void GetBookSetting_NotThere_ReturnsEmptyMultistring()
		{
			var bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
				</div>
			 </body></html>");
			var result = bookDom.GetBookSetting("getMe");
			Assert.AreEqual(0, result.Count);
		}

		[Test]
		public void SetBookSetting_WasMissingCompletely_DataDivHasNewString()
		{
			var bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
				</div>
			 </body></html>");
			bookDom.SetBookSetting("foo","xyz","hello");
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='foo' and @lang='xyz' and text()='hello']", 1);
		}
		[Test]
		public void SetBookSetting_NoDataDivYet_SettingAdded()
		{
			var bookDom = new HtmlDom(@"<html ><head></head><body>
			 </body></html>");
			bookDom.SetBookSetting("foo", "xyz", "hello");
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//body/div[@id='bloomDataDiv']/div[@data-book='foo' and @lang='xyz' and text()='hello']", 1);
		}
		[Test]
		public void SetBookSetting_HadADifferentValueCompletely_NewValue()
		{
			var bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					<div data-book='foo' lang='en'>blah</div>
					<div data-book='foo' lang='xyz'>boo</div>
				</div>
			 </body></html>");
			bookDom.SetBookSetting("foo", "xyz", "hello");
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='foo' and @lang='xyz' and text()='hello']", 1);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='foo' and @lang='en' and text()='blah']", 1);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='foo']", 2);
		}

		[Test]
		public void SetBookSetting_HadValueWithNoLanguage_RemovesItAndSavesNewValue()
		{
			var bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					<div data-book='foo'>blah</div>
					<div data-book='foo' lang='en'>blah</div>
				</div>
			 </body></html>");
			bookDom.SetBookSetting("foo", "xyz", "hello");
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='foo' and @lang='xyz' and text()='hello']", 1);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='foo' and @lang='en' and text()='blah']", 1);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasNoMatchForXpath("//div[@data-book='foo' and not(@lang)]");
		}

		[Test]
		public void SetBookSetting_AddANewVariationToAnExistingKey_Added()
		{
			var bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					<div data-book='foo' lang='en'>English</div>
					<div data-book='foo' lang='id'>Indonesian</div>
				</div>
			 </body></html>");
			bookDom.SetBookSetting("foo", "fr", "French");
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='foo' and @lang='id' and text()='Indonesian']", 1);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='foo' and @lang='en' and text()='English']", 1);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='foo' and @lang='fr' and text()='French']", 1);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='foo']", 3);
		}

		[Test]
		public void SetElementFromUserStringSafely_Various()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<div></div>");
			var target = dom.FirstChild as XmlElement;
			HtmlDom.SetElementFromUserStringSafely(target, "1<br />2");
			Assert.AreEqual("<div>1<br />2</div>", dom.InnerXml);
			HtmlDom.SetElementFromUserStringSafely(target, "1<br/>2");
			Assert.AreEqual("<div>1<br />2</div>",dom.InnerXml);

			HtmlDom.SetElementFromUserStringSafely(target, "1<br/>2<br />3");
			Assert.AreEqual("<div>1<br />2<br />3</div>", dom.InnerXml);

			HtmlDom.SetElementFromUserStringSafely(target, "1 2 3");
			Assert.AreEqual("<div>1 2 3</div>", dom.InnerXml);

			HtmlDom.SetElementFromUserStringSafely(target, "1 < 3 > 0");
			Assert.AreEqual("<div>1 &lt; 3 &gt; 0</div>", dom.InnerXml);

			// The sort of thing I think we're really trying to prevent.
			HtmlDom.SetElementFromUserStringSafely(target, "1 <input type=\"text\" id=\"lname\" name=\"lname\"> 0");
			Assert.AreEqual("<div>1 &lt;input type=\"text\" id=\"lname\" name=\"lname\"&gt; 0</div>", dom.InnerXml);

			// cite is another exception, for the sake of titles in originalCopyrightAndLicense
			HtmlDom.SetElementFromUserStringSafely(target, "Hello <cite>world</cite> That was a citation");
			Assert.AreEqual("<div>Hello <cite>world</cite> That was a citation</div>", dom.InnerXml);

			HtmlDom.SetElementFromUserStringSafely(target, "Hello <cite class=\"myClass\">world</cite> That was a citation");
			Assert.AreEqual("<div>Hello <cite class=\"myClass\">world</cite> That was a citation</div>", dom.InnerXml);
		}

		[Test]
		public void ConvertHtmlBreaksToNewLines_Works()
		{
			var NL = System.Environment.NewLine;
			Assert.AreEqual("1"+NL+"2",HtmlDom.ConvertHtmlBreaksToNewLines("1<br />2"));
			Assert.AreEqual("1" + NL + "2", HtmlDom.ConvertHtmlBreaksToNewLines("1<br>2"));
			Assert.AreEqual("1" + NL + "2", HtmlDom.ConvertHtmlBreaksToNewLines("1<br/>2"));
		}

		private const string StylesContainsXpath = "//style[@title=\"userModifiedStyles\" and contains(text(),'{0}')]";

		private void VerifyUserStylesCdataWrapping(XmlNode dom)
		{
			var stylesNodes = dom.SelectNodes("/html/head/style[@title=\"userModifiedStyles\"]");
			Assert.AreEqual(1, stylesNodes.Count, "Should only be one userModifiedStyles element.");
			var contents = stylesNodes[0].InnerText.Trim();
			Assert.That(contents.LastIndexOf(Browser.CdataPrefix).Equals(0),
				"userModifiedStyles begins with a unique copy of the CDATA prefix.");
			Assert.That(contents.IndexOf(Browser.CdataSuffix).Equals(contents.Length - Browser.CdataSuffix.Length),
				"userModifiedStyles ends with a unique copy of the CDATA suffix");
		}

		[Test]
		public void MergeUserModifiedStyles_EmptyExisting_Works()
		{
			var bookDom = new HtmlDom(
				@"<html>
					<head></head>
					<body></body>
				</html>");
			var pageDom = new XmlDocument();
			pageDom.LoadXml(
				@"<html>
					<head>
						<style type='text/css' title='userModifiedStyles'>
							.MyTest-style { font-size: ginormous; }
						</style>
					</head>
					<body></body>
				</html>");

			var pageStyleNode = pageDom.SelectSingleNode("//style");
			var bookStyleNode = HtmlDom.AddEmptyUserModifiedStylesNode(bookDom.Head);
			// SUT
			bookStyleNode.InnerText = HtmlDom.MergeUserStylesOnInsertion(bookStyleNode, pageStyleNode);

			var xpath = string.Format(StylesContainsXpath, ".MyTest-style { font-size: ginormous; }");
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(xpath, 1);
			VerifyUserStylesCdataWrapping(bookDom.RawDom);
		}

		[Test]
		public void MergeUserModifiedStyles_EmptyMergingPage_Works()
		{
			var bookDom = new HtmlDom(
				@"<html>
					<head>
						<style type='text/css' title='userModifiedStyles'>
							.MyTest-style { font-size: ginormous; }
						</style>
					</head>
					<body></body>
				</html>");
			var pageDom = new XmlDocument();
			pageDom.LoadXml(
				@"<html>
					<head></head>
					<body></body>
				</html>");

			var pageStyleNode = pageDom.SelectSingleNode("//style");
			var bookStyleNode = HtmlDom.GetUserModifiedStyleElement(bookDom.Head);
			// SUT
			bookStyleNode.InnerText = HtmlDom.MergeUserStylesOnInsertion(bookStyleNode, pageStyleNode);

			var xpath = string.Format(StylesContainsXpath, ".MyTest-style { font-size: ginormous; }");
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(xpath, 1);
			VerifyUserStylesCdataWrapping(bookDom.RawDom);
		}

		[Test]
		public void MergeUserModifiedStyles_ExistingStyle_NotOverwritten()
		{
			var bookDom = new HtmlDom(
				@"<html>
					<head>
						<style type='text/css' title='userModifiedStyles'>
							.MyTest-style { font-size: ginormous; }</style>
					</head>
					<body>
						<div class='MyTest-style bogus'></div>
					</body>
				</html>");
			var pageDom = new XmlDocument();
			pageDom.LoadXml(@"<html>
				<head>
					<style type='text/css' title='userModifiedStyles'>
						.MyTest-style { font-size: smaller; }</style>
				</head>
				<body>
					<div class='MyTest-style bogus'></div>
				</body>
			</html>");

			var pageStyleNode = pageDom.SelectSingleNode("//style");
			var bookStyleNode = HtmlDom.GetUserModifiedStyleElement(bookDom.Head);
			// SUT
			bookStyleNode.InnerText = HtmlDom.MergeUserStylesOnInsertion(bookStyleNode, pageStyleNode);

			var xpath = string.Format(StylesContainsXpath, "font-size: ginormous;");
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(xpath, 1);
			var xpath2 = "//style[@title=\"userModifiedStyles\" and contains(text(),'font-size: smaller;')]";
			AssertThatXmlIn.Dom(bookDom.RawDom).HasNoMatchForXpath(xpath2);
			VerifyUserStylesCdataWrapping(bookDom.RawDom);
		}

		[Test]
		public void MergeUserModifiedStyles_NewStyleHasLangAttr_Ignored()
		{
			var bookDom = new HtmlDom(
				@"<html>
					<head>
						<style type='text/css' title='userModifiedStyles'>
							.MyTest-style { font-size: ginormous; }</style>
					</head>
					<body>
						<div class='MyTest-style bogus'></div>
					</body>
				</html>");
			var pageDom = new XmlDocument();
			pageDom.LoadXml(@"<html>
				<head>
					<style type='text/css' title='userModifiedStyles'>
						.MyTest-style[lang='en'] { font-size: smaller; }
					</style>
				</head>
				<body>
					<div class='MyTest-style bogus'></div>
				</body>
			</html>");

			var pageStyleNode = pageDom.SelectSingleNode("//style");
			var bookStyleNode = HtmlDom.GetUserModifiedStyleElement(bookDom.Head);
			// SUT
			bookStyleNode.InnerText = HtmlDom.MergeUserStylesOnInsertion(bookStyleNode, pageStyleNode);

			var xpath = string.Format(StylesContainsXpath, "font-size: ginormous;");
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(xpath, 1);
			var xpath2 = "//style[@title=\"userModifiedStyles\" and contains(text(),\".MyTest-style[lang='en'] { font-size: smaller;\")]";
			AssertThatXmlIn.Dom(bookDom.RawDom).HasNoMatchForXpath(xpath2);
			VerifyUserStylesCdataWrapping(bookDom.RawDom);
		}

		[Test]
		public void MergeUserModifiedStyles_NewStyleHasMultipleLines()
		{
			var bookDom = new HtmlDom(
				@"<html>
					<head>
						<style type='text/css' title='userModifiedStyles'>
							.SomeOther-style { font-size: ginormous; }</style>
					</head>
					<body>
						<div class='MyTest-style bogus'></div>
					</body>
				</html>");
			var pageDom = new XmlDocument();
			pageDom.LoadXml(@"<html>
				<head>
					<style type='text/css' title='userModifiedStyles'>
.MyTest-style[lang='en']
{
	font-size: smaller;
}
					</style>
				</head>
				<body>
					<div class='MyTest-style bogus'></div>
				</body>
			</html>");

			var pageStyleNode = pageDom.SelectSingleNode("//style");
			var bookStyleNode = HtmlDom.GetUserModifiedStyleElement(bookDom.Head);
			// SUT
			bookStyleNode.InnerText = HtmlDom.MergeUserStylesOnInsertion(bookStyleNode, pageStyleNode);

			var commonXpathPart = "//style[@title=\"userModifiedStyles\" and contains(text()";
			var xpath = commonXpathPart + ",'font-size: ginormous;')]";
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(xpath, 1);
			var xpath2 = commonXpathPart + ",\".MyTest-style[lang='en']" + Environment.NewLine +
				"{" + Environment.NewLine + "font-size: smaller;" + Environment.NewLine + "}" + Environment.NewLine + "\")]";
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(xpath2, 1);
			VerifyUserStylesCdataWrapping(bookDom.RawDom);
		}

		[Test]
		public void FixAnyAddedCustomPages_Works()
		{
			var content =
				@"<html>
					<head></head>
					<body>
						<div id='bloomDataDiv'>
							<div data-book='styleNumberSequence' lang='*'>0</div>
						</div>
						<div class='bloom-page numberedPage customPage A5Portrait bloom-monolingual' data-page='' id='2141ae70-d84f-4b40-bb30-18c20941e84e' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398386' lang=''>
							<div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Custom' lang='en'>Custom</div>
							<div class='pageDescription' lang='en'>A blank page that allows you to add items.</div>
							<div class='marginBox'></div>
						</div>
						<div class='bloom-page numberedPage customPage A5Portrait bloom-monolingual' data-page='extra' id='ff5f83b6-d26e-439f-8df6-cf982a521de4' data-pagelineage='5dcd48df-e9ab-4a07-afd4-6a24d0398386' lang=''>
							<div class='pageLabel' lang='en'>Dual Picture Before Text</div>
							<div class='pageDescription' lang='en'></div>
							<div class='marginBox'></div>
						</div>
					</body>
				</html>";

			var bookDom = new HtmlDom(content);

			var allTopLevelDivs = "/html/body/div";
			var pageDivs = "/html/body/div[contains(@class,'bloom-page') and contains(@class,'customPage')]";
			var badPageDivs = "/html/body/div[contains(@class,'bloom-page') and contains(@class,'customPage') and @data-page='']";
			var goodPageDivs = "/html/body/div[contains(@class,'bloom-page') and contains(@class,'customPage') and @data-page='extra']";
			var badPageLabels = "/html/body/div[contains(@class,'bloom-page') and contains(@class,'customPage')]/div[@class='pageLabel' and @data-i18n]";
			var goodPageLabels = "/html/body/div[contains(@class,'bloom-page') and contains(@class,'customPage')]/div[@class='pageLabel' and not(@data-i18n)]";
			var pageDescription = "/html/body/div[contains(@class,'bloom-page') and contains(@class,'customPage')]/div[@class='pageDescription']";

			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(allTopLevelDivs, 3);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(pageDivs, 2);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(badPageDivs, 1);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(goodPageDivs, 1);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(badPageLabels, 1);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(goodPageLabels, 1);
			var countEmpty = 0;
			foreach (XmlElement node in bookDom.RawDom.SafeSelectNodes(pageDescription))
			{
				if (String.IsNullOrWhiteSpace(node.InnerXml))
					++countEmpty;
			}
			Assert.That(countEmpty, Is.EqualTo(1));

			bookDom.FixAnyAddedCustomPages();

			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(allTopLevelDivs, 3);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(pageDivs, 2);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasNoMatchForXpath(badPageDivs);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(goodPageDivs, 2);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasNoMatchForXpath(badPageLabels);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(goodPageLabels, 2);
			countEmpty = 0;
			foreach (XmlElement node in bookDom.RawDom.SafeSelectNodes(pageDescription))
			{
				if (String.IsNullOrWhiteSpace(node.InnerXml))
					++countEmpty;
			}
			Assert.That(countEmpty, Is.EqualTo(2));
		}

		[TestCase("first page", 1,
			"<div id='ego' class='bloom-page numberedPage'/>" +
			"<div class='bloom-page numberedPage'/>" +
			"<div class='bloom-page numberedPage'/>")]
		// REVIEW: should we be returning string so that we can say "cover", for example?
		[TestCase("on a page that itself is not numbered", 0,
			"<div id='ego' class='bloom-page'/>")]
		[TestCase("first page is numbered, this is second numbered page", 2,
			"<div class='bloom-page numberedPage'></div><div id='ego' class='bloom-page numberedPage'/>")]
		[TestCase("first page is not numbered",
			1, " <div class='bloom-page'/><div id='ego' class='bloom-page numberedPage'/>")]
		[TestCase("previous page is countPageButDoNotShowNumber",
			2, " <div class='bloom-page countPageButDoNotShowNumber'/><div id='ego' class='bloom-page numberedPage'/>")]
		[TestCase("bloom-startPageNumbering restarts numbering", 1,
			"<div class='bloom-page numberedPage'></div><div id='ego' class='bloom-page bloom-startPageNumbering'/>")]
		[TestCase("page not found for this item", -1,
			"<div id='ego'/>")]
		[TestCase("the works", 2, "<div class='bloom-page'/>" +
								"<div class='bloom-page numberedPage'/>" +
								"<div class='bloom-page bloom-startPageNumbering'/>" +
								"<div id='ego' class='bloom-page numberedPage'/>" +
								"<div class='bloom-page numberedPage'/>" +
								"<div class='bloom-page numberedPage'/>")]
		public void GetPageNumberOfPage_ReturnsExpectedNumber(string description, int expected, string contents)
		{
			var dom = new HtmlDom(@"<html ><head></head><body><div id='bloomDataDiv'></div>" + contents + "</body></html>");
			var ego = dom.RawDom.SelectSingleNode("//div[@id='ego']") as XmlElement;
			Assert.AreEqual(expected, dom.GetPageNumberOfPage(ego), "Failed " + description);
		}

		[Test]
		public void UpdatePageNumberAndSideClassOfPages_TestSideClasses()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
					<div id='cover' class='bloom-page side-foo'/>
					<div id='insideFrontCover' class='bloom-page side-foo'/>
					<div id='firstWhitePage' class='bloom-page side-foo'/>
				</body></html>");

			dom.UpdatePageNumberAndSideClassOfPages("abcdefghij", false /* not rtl */);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='cover' and contains(@class,'side-right')]", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='insideFrontCover' and contains(@class,'side-left')]", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='firstWhitePage' and contains(@class,'side-right')]", 1);
		}


		[Test]
		public void UpdatePageNumberAndSideClassOfPages_RightToLeft_TestSideClasses()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
					<div id='cover' class='bloom-page side-foo'/>
					<div id='insideFrontCover' class='bloom-page side-foo'/>
					<div id='firstWhitePage' class='bloom-page side-foo'/>
				</body></html>");

			dom.UpdatePageNumberAndSideClassOfPages("abcdefghij", true /* rtl */);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='cover' and contains(@class,'side-left')]", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='insideFrontCover' and contains(@class,'side-right')]", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='firstWhitePage' and contains(@class,'side-left')]", 1);
		}

		[TestCase("12", "")] // just use default (0..9)
		[TestCase("bc", "abcdefghij")] // provide explicit digits
		public void UpdatePageNumberAndSideClassOfPages_TestPageNumbers(string page12Number, string numberStyleOrDigits)
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
					<div id='frontmatter' class='bloom-page' data-page-number='99'/>
					<div  class='bloom-page numberedPage'/>
					<div  class='bloom-page numberedPage'/>
					<div  class='bloom-page numberedPage'/>
					<div  class='bloom-page numberedPage'/>
					<div  class='bloom-page numberedPage'/>
					<div  class='bloom-page numberedPage'/>
					<div  class='bloom-page numberedPage'/>
					<div  class='bloom-page numberedPage'/>
					<div  class='bloom-page numberedPage'/>
					<div  class='bloom-page numberedPage'/>
					<div  class='bloom-page numberedPage'/>
					<div id='12' class='bloom-page numberedPage'/>
				</body></html>");

			dom.UpdatePageNumberAndSideClassOfPages(numberStyleOrDigits, false /* not rtl */);
			// we don't have to test anything except that a number does get added or updated,
			// because the testing of the number logic is done on the GetPageNumberOfPage() method

			//this first one wasn't marked for getting a page number
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='frontmatter' and @data-page-number='']", 1);
			//should be page 2 ('c' in our pretend script)
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath($"//div[@id='12' and @data-page-number='{page12Number}']", 1);

		}

		[Test]
		public void UpdatePageNumberAndSideClassOfPages_EmptyXmatterPageNumbers()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
					<div id='frontCover' class='bloom-page frontCover'/>
					<div id='insideFrontCover' class='bloom-page insideFrontCover'/>
					<div id='page-1' class='bloom-page numberedPage'/>
					<div id='page-2' class='bloom-page numberedPage'/>
					<div id='page-3' class='bloom-page numberedPage'/>
					<div id='insideBackCover' class='bloom-page insideBackCover'/>
					<div id='backCover' class='bloom-page backCover'/>
				</body></html>");
			dom.UpdatePageNumberAndSideClassOfPages("", false /* not rtl */);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='frontCover' and @data-page-number='']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='insideFrontCover' and @data-page-number='']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='page-1' and @data-page-number='1']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='page-2' and @data-page-number='2']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='page-3' and @data-page-number='3']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='insideBackCover' and @data-page-number='']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='backCover' and @data-page-number='']", 1);
		}

		[Test]
		public void StripUnwantedTagsPreservingText_StripsEmbeddedSpan()
		{
			var tagsToPreserve = new[] { "div", "p", "br" };
			var dom = new HtmlDom(@"<html><head></head><body>
						<div id='testthiselement'>
							<div class='bloom-editable bloom-content1 bloom-contentNational1' contenteditable='true' lang='en'>
								<h1>My test question.</h1> <br/>
								<p>‌Answer 1 </p>
								<p>‌*Ans<span class='audio-sentence'>wer 2</span></p>
								<p>‌</p>
								<p>‌Second test question <em>weird stuff!</em></p>
								<p>‌*Some right answer </p>
								<p><span data-duration='1.600227' id='i125f143d-7c30-44c1-8d23-0e000f484e08' class='audio-sentence' recordingmd5='undefined'>My test text.</span></p>
							</div>
						</div>
				</body></html>");
			var testableElement = dom.SelectSingleNode("//div[@id='testthiselement']");

			// SUT
			HtmlDom.StripUnwantedTagsPreservingText(dom.RawDom, testableElement, tagsToPreserve);

			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//span");
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//h1");
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//em");
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//p[.='‌*Some right answer ']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//p[.='‌*Answer 2']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//p[.='My test text.']", 1);
		}

		[Test]
		public void MigrateChildren_MigratesVideo()
		{
			var pageDom = new HtmlDom(@"<html><head></head><body>
					<div class='bloom-page' id='pageGuid'>
						<div class='split-pane-component-inner'>
							<div class='bloom-translationGroup'>
								<div class='bloom-editable ' contenteditable='true' lang='en'>Contents</div>
							</div>
							<div class='bloom-videoContainer'>
								<video>
									<source src='video/videoGuid.mp4' type='video/mp4'></source>
								</video>
							</div>
						</div>
					</div>
				</body></html>");
			var templateDom = new HtmlDom(@"<html><head></head><body>
					<div class='bloom-page' id='templateGuid'>
						<div class='split-pane-component-inner'>
							<div class='bloom-videoContainer bloom-noVideoSelected' />
							<div class='bloom-translationGroup'>
								<div class='bloom-editable ' contenteditable='true' lang='en'></div>
							</div>
						</div>
					</div>
				</body></html>");
			bool didChange;
			var lineage = "someGuid";
			var pageElement = pageDom.SelectSingleNode("//div[@class='bloom-page']");
			var templateElement = templateDom.SelectSingleNode("//div[@class='bloom-page']");

			// SUT
			pageDom.MigrateEditableData(pageElement, templateElement, lineage, false, out didChange);

			// Verification
			Assert.That(didChange, Is.True);
			AssertThatXmlIn.Dom(pageDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-pagelineage='someGuid']", 1);
			var textContentsXpath = "//div[contains(@class,'bloom-editable') and text()='Contents']";
			var videoXpath = "//video/source[@src='video/videoGuid.mp4' and @type='video/mp4']";
			var noVideoXpath = "//div[contains(@class,'bloom-noVideoSelected')]";
			AssertThatXmlIn.Dom(pageDom.RawDom).HasNoMatchForXpath("//div[@id='templateGuid']");
			AssertThatXmlIn.Dom(pageDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='pageGuid']", 1);
			AssertThatXmlIn.Dom(pageDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(textContentsXpath, 1);
			AssertThatXmlIn.Dom(pageDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(videoXpath, 1);
			AssertThatXmlIn.Dom(pageDom.RawDom).HasNoMatchForXpath(noVideoXpath);
		}

		[Test]
		public void RemoveComments_WorksForCssData()
		{
			const string cssContent = @"
// This is a test of font-family: inside of comments;
body {
    font-family: arial;
}
//h1 {
//    font-family: times;
//}
/*h2 {
    font-family: serif;
}*/
p {
    text-before: "" /* "";
    font-family: sans;
    text-after: "" */ "";
}
";
			var noComments = HtmlDom.RemoveCommentsFromCss(cssContent);
			Assert.Less(noComments.Length, cssContent.Length, "Comments should be removed from the original CSS");
			var idxFontFamily = noComments.IndexOf("font-family:");
			Assert.Greater(idxFontFamily, 0, "The first font-family should be found.");
			var length = noComments.IndexOf(";", idxFontFamily) + 1 - idxFontFamily;
			Assert.Greater(length, 0, "The first font-family should be terminated properly.");

			idxFontFamily = noComments.IndexOf("font-family:", idxFontFamily + length);
			Assert.Greater(idxFontFamily, 0, "The second font-family should be found.");
			length = noComments.IndexOf(";", idxFontFamily) + 1 - idxFontFamily;
			Assert.Greater(length, 0, "The second font-family should be terminated properly.");

			idxFontFamily = noComments.IndexOf("font-family:", idxFontFamily + length);
			Assert.Less(idxFontFamily, 0, "A third font-family should not be found!");

			var fonts = new HashSet<string>();
			HtmlDom.FindFontsUsedInCss(cssContent, fonts, true);
			Assert.AreEqual(2, fonts.Count, "Two fonts are used in the test css data");
			Assert.IsTrue(fonts.Contains("arial"), "The css data refers to arial for one font.");
			Assert.IsTrue(fonts.Contains("sans"), "The css data refers to sans for the other font.");
		}

		[Test]
		public void FindFontsUsedInCss_WorksWithHtml()
		{
			const string htmlContent = @"<html>
<head>
<style type=""text/css"">
    /*<![CDATA[*/
    .Title-On-Cover-style[lang=""en""] { font-family: NikoshBAN ! important; }
    .small-style[lang=""en""] { font-family: Andika New Basic ! important; font-size: 9pt ! important; font-weight: normal ! important; font-style: normal ! important; }
    .Inside-Back-Cover-style[lang=""jmx""] { font-size: 7pt ! important; /*font-family: Times New Roman ! important;*/ }
    .big-style[lang=""jmx""] {
        font-size: 18pt ! important;
        //font-family: Saysettha OT ! important;
     }
    /*]]>*/
</style>
<!--
<style type=""text/css"">
    .bloom-editable.lesonnumber-style[lang=""tpi""] { font-family: Andika ! important; font-size: 16pt ! important; }
    .normal-style[lang=""en""] { font-family: Scheherazade ! important; }
</style>
-->
</head>
<body>
<div style=""font-size:26.0pt; font-family:&quot;Charis SIL First&quot;;"">Test 1</div>
<p style=""font-size:26.0pt; font-family:'Charis SIL Second';"">Test 2</p>
<div style=""font-size:26.0pt; /*font-family:&quot;Charis SIL Third&quot;;*/"">Test 3</div>
<!--div style=""font-size:26.0pt; font - family:&quot;Charis SIL Fourth&quot;;"">Test 4</div-->
</body>
</html>";

			var fonts = new HashSet<string>();
			HtmlDom.FindFontsUsedInCss(htmlContent, fonts, true);
			Assert.AreEqual(4, fonts.Count, "Four fonts are used in the test html/css data");
			Assert.IsTrue(fonts.Contains("NikoshBAN"), "The html/css data refers to NikoshBAN as a font.");
			Assert.IsTrue(fonts.Contains("Andika New Basic"), "The css data refers to Andika New Basic as a font.");
			Assert.IsTrue(fonts.Contains("Charis SIL First"), "The css data refers to Charis SIL First as a font.");
			Assert.IsTrue(fonts.Contains("Charis SIL Second"), "The css data refers to Charis SIL Second as a font.");
		}

		[Test]
		public void MigrateChildren_MigratesTextOverPicture_DoesNotLoseText()
		{
			var pageDom = new HtmlDom(@"<html><head></head><body>
					<div class='bloom-page' id='pageGuid'>
						<div class='split-pane-component-inner'>
							<div class='bloom-translationGroup'>
								<div class='bloom-editable ' contenteditable='true' lang='en'>First text contents</div>
							</div>
							<div class='bloom-imageContainer'>
								<div class='bloom-textOverPicture'>
									<div class='bloom-translationGroup'>
										<div class='bloom-editable'>
											<p>Text over picture text</p>
										</div>
									</div>
								</div>
								<img src='myImageFile.png'></img>
								<div class='bloom-translationGroup bloom-imageDescription'>
									<div class='bloom-editable'>
										<p>Image description text</p>
									</div>
								</div>
							</div>
						</div>
						<div class='split-pane-divider vertical-divider'></div>
						<div class='split-pane-component-inner'>
							<div class='bloom-translationGroup'>
								<div class='bloom-editable ' contenteditable='true' lang='en'>Second text contents</div>
							</div>
						</div>
					</div>
				</body></html>");
			var templateDom = new HtmlDom(@"<html><head></head><body>
					<div class='bloom-page' id='templateGuid'>
						<div class='split-pane-component-inner'>
							<div class='bloom-videoContainer bloom-noVideoSelected' />
							<div class='bloom-translationGroup'>
								<div class='bloom-editable ' contenteditable='true' lang='en'></div>
							</div>
						</div>
						<div class='split-pane-component-inner'>
							<div title='placeHolder.png' class='bloom-imageContainer'>
								<img src='placeHolder.png' alt=''></img>
							</div>
						</div>
						<div class='split-pane-component-inner'>
							<div class='bloom-translationGroup'>
								<div class='bloom-editable ' contenteditable='true' lang='en'></div>
							</div>
						</div>
					</div>
				</body></html>");
			bool didChange;
			var lineage = "someGuid";
			var pageElement = pageDom.SelectSingleNode("//div[@class='bloom-page']");
			var templateElement = templateDom.SelectSingleNode("//div[@class='bloom-page']");

			// SUT
			pageDom.MigrateEditableData(pageElement, templateElement, lineage, false, out didChange);

			// Verification
			Assert.That(didChange, Is.True);
			AssertThatXmlIn.Dom(pageDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-pagelineage='someGuid']", 1);
			var firstTextXpath = "//div[contains(@class,'bloom-editable') and text()='First text contents']";
			var secondTextXpath = "//div[contains(@class,'bloom-editable') and text()='Second text contents']";
			var topTextXpath = "//div[contains(@class,'bloom-imageContainer')]//div[contains(@class,'bloom-editable')]/p[text()='Text over picture text']";
			var imageDescXpath = "//div[contains(@class,'bloom-imageContainer')]/div[contains(@class,'bloom-imageDescription')]//p[text()='Image description text']";
			AssertThatXmlIn.Dom(pageDom.RawDom).HasNoMatchForXpath("//div[@id='templateGuid']");
			AssertThatXmlIn.Dom(pageDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='pageGuid']", 1);
			AssertThatXmlIn.Dom(pageDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(firstTextXpath, 1);
			AssertThatXmlIn.Dom(pageDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(secondTextXpath, 1);
			AssertThatXmlIn.Dom(pageDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(topTextXpath, 1);
			AssertThatXmlIn.Dom(pageDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(imageDescXpath, 1);
		}

		[Test]
		public void MigrateChildren_MigratesStylesCorrectly()
		{
			var pageDom = new HtmlDom(@"<html><head></head><body>
					<div class='bloom-page' id='pageGuid'>
						<div class='split-pane-component-inner'>
							<div class='bloom-translationGroup'>
								<div class='bloom-editable normal-style' contenteditable='true' lang='en'>First text contents</div>
							</div>
							<div class='bloom-imageContainer'>
								<img src='myImageFile.png'></img>
							</div>
						</div>
						<div class='split-pane-divider vertical-divider'></div>
						<div class='split-pane-component-inner'>
							<div class='bloom-translationGroup'>
								<div class='bloom-editable' contenteditable='true' lang='en'>Second english contents</div>
								<div class='bloom-editable' contenteditable='true' lang='fr'>Second french contents</div>
							</div>
						</div>
						<div class='split-pane-component-inner'>
							<div class='bloom-translationGroup'>
								<div class='bloom-editable' contenteditable='true' lang='en'>Third text contents</div>
							</div>
						</div>
					</div>
				</body></html>");
			var templateDom = new HtmlDom(@"<html><head></head><body>
					<div class='bloom-page' id='templateGuid'>
						<div class='split-pane-component-inner'>
							<div class='bloom-videoContainer bloom-noVideoSelected' />
							<div class='bloom-translationGroup'>
								<div class='bloom-editable bigger-style' contenteditable='true' lang='z'></div>
							</div>
						</div>
						<div class='split-pane-component-inner'>
							<div title='placeHolder.png' class='bloom-imageContainer'>
								<img src='placeHolder.png' alt=''></img>
							</div>
						</div>
						<div class='split-pane-component-inner'>
							<div class='bloom-translationGroup'>
								<div class='bloom-editable strangeNew-style' contenteditable='true' lang='z'></div>
								<div class='bloom-editable strangeSecondary-style' contenteditable='true' lang='en'></div>
							</div>
						</div>
						<div class='split-pane-component-inner'>
							<div class='bloom-translationGroup groupLevel-style'>
								<div class='bloom-editable' contenteditable='true' lang='z'></div>
							</div>
						</div>
					</div>
				</body></html>");
			bool didChange;
			var lineage = "someGuid";
			var pageElement = pageDom.SelectSingleNode("//div[@class='bloom-page']");
			var templateElement = templateDom.SelectSingleNode("//div[@class='bloom-page']");

			// SUT
			pageDom.MigrateEditableData(pageElement, templateElement, lineage, false, out didChange);

			// Verification
			Assert.That(didChange, Is.True);
			var firstTextXpath = "//div[contains(@class,'bloom-editable') and contains(@class, 'bigger-style')]";
			var secondTextXpath = "//div[contains(@class,'bloom-editable') and contains(@class, 'strangeNew-style') and @lang='fr']";
			var thirdTextXpath = "//div[contains(@class,'bloom-editable') and contains(@class, 'strangeSecondary-style') and @lang='en']";
			var fourthTextXpath = "//div[contains(@class,'bloom-translationGroup') and contains(@class, 'groupLevel-style')]";
			AssertThatXmlIn.Dom(pageDom.RawDom).HasNoMatchForXpath("//div[@id='templateGuid']");
			AssertThatXmlIn.Dom(pageDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='pageGuid']", 1);
			AssertThatXmlIn.Dom(pageDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(firstTextXpath, 1);
			AssertThatXmlIn.Dom(pageDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(secondTextXpath, 1);
			AssertThatXmlIn.Dom(pageDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(thirdTextXpath, 1);
			AssertThatXmlIn.Dom(pageDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(fourthTextXpath, 1);
		}

		// Tests that "", "*", and "z" are properly filtered.
		[Test]
		public void GatherDataBookLanguages_ContainsLangsWildcardAndZ_RemovesUnnecessaryLangs()
		{
			var dom = new HtmlDom(
				@"<html><body><div>
					<div id='bloomDataDiv'>
						<div data-book='styleNumberSequence' lang=''>0</div>
						<div data-book='styleNumberSequence' lang='*'>0</div>
						<div data-book='styleNumberSequence' lang='z'>0</div>
						<div data-book='styleNumberSequence' lang='en'>0</div>
						<div data-book='styleNumberSequence' lang='es'>0</div>
						<div data-book='styleNumberSequence' lang='zh-CN'>0</div>
						<div data-book='styleNumberSequence' lang='zh-TW'>0</div>
					</div>
				</div></body></html>");

			var result = dom.GatherDataBookLanguages();

			Assert.AreEqual(4, result.Count, "Count");
			Assert.IsFalse(result.Contains(""), "Unexpected item \"\" found");
			Assert.IsFalse(result.Contains("*"), "Unexpected item \"*\" found");
			Assert.IsFalse(result.Contains("z"), "Unexpected item \"z\" found");
		}

		[TestCase(true)]
		[TestCase(false)]
		public void SelectChildNarrationAudioElements_IncludeSplitTextBoxAudio_FindsTextBoxIfRequested(bool includeSplitTextBoxAudio)
		{
			XmlDocument doc = new XmlDocument();
			doc.LoadXml(
@"<html>
	<div id='textbox1' class='bloom-editable' data-audiorecordingmode='Sentence'>
		<p>
			<span class='audio-sentence'>Sentence 1.</span>
		</p>
	</div>
	<div  id='textbox2' class='bloom-editable' data-audiorecordingmode='TextBox'>
		<p>
			<span class='audio-sentence'>Sentence 2.</span>
		</p>
	</div>
	<div class='bloom-editable' id='textbox3'>
		<p>
			<span>Sentence 3.</span>
		</p>
	</div>
</html>");

			XmlElement htmlElement = (XmlElement)doc.FirstChild;

			// System under test
			var result = HtmlDom.SelectChildNarrationAudioElements(htmlElement, includeSplitTextBoxAudio);

			Assert.IsTrue(result.Count > 0, "Count should not be 0.");

			var resultEnumerable = result.Cast<XmlNode>();
			Assert.AreEqual(2, resultEnumerable.Where(node => node.Name == "span").Count(), "Matching span count");

			if (includeSplitTextBoxAudio)
			{
				Assert.AreEqual(1, resultEnumerable.Where(node => node.Name == "div").Count(), "Matching div count");
				Assert.AreEqual("textbox2", resultEnumerable.Where(node => node.Name == "div").First().GetStringAttribute("id"));
				Assert.AreEqual(3, result.Count, "The result had too many elements.");
			}
			else
			{
				Assert.AreEqual(0, resultEnumerable.Where(node => node.Name == "div").Count(), "Matching div count");
				Assert.AreEqual(2, result.Count, "The result had too many elements.");
			}
		}

		[TestCase(true)]
		[TestCase(false)]
		// Tests to make sure that the find works not only on all descendants, but on the element itself.
		public void SelectChildNarrationAudioElements_RootIsTextBox_Matched(bool includeSplitTextBoxAudio)
		{
			XmlDocument doc = new XmlDocument();
			doc.LoadXml(
@"<html>
	<div id='textbox1' class='bloom-editable audio-sentence'>
		<p>
			<span>Sentence 1.</span>
		</p>
	</div>
	<div  id='textbox2' class='bloom-editable' data-audiorecordingmode='TextBox'>
		<p>
			<span class='audio-sentence'>Sentence 2.</span>
		</p>
	</div>
</html>");

			var nodeList = doc.GetElementsByTagName("div");

			foreach (var node in nodeList)
			{
				var element = (XmlElement)node;
				var result = HtmlDom.SelectChildNarrationAudioElements(element, includeSplitTextBoxAudio);

				if (element.GetStringAttribute("id") == "textbox2")
				{
					if (includeSplitTextBoxAudio)
					{
						Assert.AreEqual(2, result.Count, "Count does not match. Both the text box and the span should match in this case.");
					}
					else
					{
						Assert.AreEqual(1, result.Count, "Count does not match. Only the span should match in this case.");
					}
				}
				else
				{
					Assert.AreEqual(1, result.Count, "Count does not match. Only the text box should match in this case.");
				}
			}
		}

		[TestCase(true, 3)]
		[TestCase(false, 1)]
		public void GetLanguageDivs_CheckXmatter_ReturnsCorrectNumberOfDivs(bool includeXMatter, int expectedCount)
		{
			var htmlDom = new HtmlDom(
@"<html>
	<body>
		<div class='bloom-page bloom-frontMatter'>
			<div class='marginBox'>
				<div class='bloom-translationGroup'>
					<div id='page1box1' class='bloom-editable' lang='en'>
						<p><span>Page 1</span></p>
					</div>
				</div>
			</div>
		</div>
		<div class='bloom-page'>
			<div class='marginBox'>
				<div class='bloom-translationGroup'>
					<div id='page2box1' class='bloom-editable' lang='es'>
						<p><span>Page 2</span></p>
					</div>
				</div>
			</div>
		</div>
		<div class='bloom-page bloom-backMatter'>
			<div class='marginBox'>
				<div class='bloom-translationGroup'>
					<div id='page3box1' class='bloom-editable' lang='fr'>
						<p><span>Page 3</span></p>
					</div>
				</div>
			</div>
		</div>
	</body>
</html>");

			List<XmlElement> result = htmlDom.GetLanguageDivs(includeXMatter).ToList();

			Assert.AreEqual(expectedCount, result.Count);
		}

		[TestCase("en")]
		[TestCase("zzz")]
		public void IsLanguageValid_ValidLanguages_ReturnsTrue(string lang)
		{
			bool result = HtmlDom.IsLanguageValid(lang);
			Assert.IsTrue(result);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("*")]
		[TestCase("z")]
		public void IsLanguageValid_InvalidLanguages_ReturnsFalse(string lang)
		{
			bool result = HtmlDom.IsLanguageValid(lang);
			Assert.IsFalse(result);
		}

		[Test]
		public void SelectVideoElements()
		{
			var htmlDom = new HtmlDom(
@"<html>
	<div class='bloom-page bloom-frontMatter'>
		<div class='bloom-translationGroup'>
			<div id='page1box1' class='bloom-editable' lang='en'>
				<p><span>Page 1</span></p>
			</div>
		</div>
		<div class='bloom-videoContainer'>
			<video><source id='vidSource'></source></video>
		</div>
	</div>
</html>");

			XmlNodeList result = htmlDom.SelectVideoElements();

			Assert.AreEqual(1, result.Count, "Count does not match");
			Assert.AreEqual("vidSource", ((XmlElement)result[0]).GetAttribute("id"), "ID does not match.");
		}

		[Test]
		public void GetLangCodesWithImageDescription()
		{
			var htmlDom = new HtmlDom(
@"<html>
	<body>
		<div class='bloom-page bloom-frontMatter'>
			<div class='bloom-imageContainer'>
				<img src='coverImage.jpg'></img>
				<div class='bloom-translationGroup bloom-imageDescription'>
					<div id='badFrontXmatter' class='bloom-editable' lang='en'>
						<p>Image Description, Cover</p>
					</div>
				</div>
			</div>
		</div>
		<div class='bloom-page'>
			<div class='bloom-imageContainer'>
				<img src='page1Image.jpg'></img>
				<div class='bloom-translationGroup bloom-imageDescription'>
					<div id='good1' class='bloom-editable' lang='es'>
						<p>Image Description, Cover</p>
					</div>
				</div>
				<div class='bloom-translationGroup bloom-imageDescription'>
					<div id='badInvalidLangStar' class='bloom-editable' lang='*'>
						<p>Image Description, Cover</p>
					</div>
				</div>
				<div class='bloom-translationGroup bloom-imageDescription'>
					<div id='badInvalidLangZ' class='bloom-editable' lang='z'>
						<p>Image Description, Cover</p>
					</div>
				</div>
				<div class='bloom-translationGroup bloom-imageDescription'>
					<div id='badAllWhitespace' class='bloom-editable' lang='fr'>					
					</div>
				</div>
			</div>
			<div class='bloom-translationGroup'>
				<div id='badNotAnImageDescription' lang='de'>
					Hallo Welt
				</div>
			</div>
		</div>
		<div class='bloom-page bloom-backMatter'>
			<div class='bloom-imageContainer'>
				<img src='backCoverImage.jpg'></img>
				<div class='bloom-translationGroup bloom-imageDescription'>
					<div id='badBackXmatter' class='bloom-editable' lang='fr'>
						<p>Image Description, Back Cover</p>
					</div>
				</div>
			</div>
		</div>
	</body>
</html>");

			List<string> result = htmlDom.GetLangCodesWithImageDescription().ToList();

			var expectedLangCodes = new string[] { "es" };
			CollectionAssert.AreEqual(expectedLangCodes, result);
		}

		[TestCase("audioSpanId", "es")]	// Tests the "child" case, where we need to search up the tree to find the lang
		[TestCase("editableId", "es")]	// Tests the "self" case, where the element itself contains the lang
		[TestCase("pageId", "")]		// Tests the case where lang cannot be found
		public void GetClosestLangCode_GivenAnElement_FindsCorrectLangCode(string id, string expectedLangCode)
		{
			var htmlDom = new HtmlDom(
@"<html>
	<body>
		<div id='pageId' class='bloom-page'>
			<div class='marginBox'>
				<div id='wrongMatch' class='someMisguidedDivThatAccidentallyHasLang' lang='de'>
					<div class='bloom-translationGroup bloom-imageDescription'>
						<div id='editableId' class='bloom-editable' lang='es'>
							<p><span id='audioSpanId'>Sentence 1.</span></p>
						</div>
					</div>
				</div>
			</div>
		</div>
	</body>
</html>");
			// Continue setting up test: Go from an ID to the corresonding XmlElement instance for that ID.
			var element = htmlDom.SafeSelectNodes($"//*[@id='{id}']").Cast<XmlElement>().FirstOrDefault();
			Assert.IsNotNull(element, "Test setup failure: element was not found");

			// System under test
			var langCode = HtmlDom.GetClosestLangCode(element);

			// Verification
			Assert.AreEqual(expectedLangCode, langCode);
		}

		[Test]
		[TestCase(" color : rgb(98, 19, 45);", ",`backgroundColors`: [`white`,`#7b8eb8`] ", "",
				"", "",
				"", "", 0)]
		[TestCase("", "", "",
			"color: #000000;", ",`backgroundColors`:[`oldLace`]",
			"", "", 1)]
		[TestCase("", ",`backgroundColors`:[`gray`]", ", `opacity` : `0.66`",
			"", "",
			"color: #000000;", ",`backgroundColors`:[`purple`]", 2)]
		public void GetColorsUsedInBook_works(
			string textColorLoc1, string backColorLoc1, string opacityLoc1,
			string textColorLoc2, string backColorLoc2,
			string textColorLoc3, string backColorLoc3, int responseIndex)
		{
			var jsonResponses = new[] {
				"[{\"colors\":[\"rgb(98, 19, 45)\"]},{\"colors\":[\"white\",\"#7b8eb8\"],\"opacity\":1}]",
				"[{\"colors\":[\"#000000\"]},{\"colors\":[\"oldLace\"],\"opacity\":1}]",
				"[{\"colors\":[\"gray\"],\"opacity\":0.66},{\"colors\":[\"#000000\"]},{\"colors\":[\"purple\"],\"opacity\":1}]"
			};
			var bookDom = new HtmlDom(@"<html><head></head><body>
				<div class='bloom-page' id='pageGuid'>
					<div class='split-pane-component-inner'>
						<div class='bloom-translationGroup'>
							<div class='bloom-editable'>First text contents</div>
						</div>
						<div class='bloom-imageContainer'>
							<div class='bloom-textOverPicture' style='left: 8.50603%; " + textColorLoc1 + @"'
								data-bubble='{`version`:`1.0`" + backColorLoc1 + opacityLoc1 + @"}'>
								<div class='bloom-translationGroup'>
									<div class='bloom-editable'>
										<p>Text over picture text</p>
									</div>
								</div>
							</div>
						</div>
						<div class='bloom-imageContainer'>
							<div class='bloom-textOverPicture' style='left: 8.50603%; " + textColorLoc2 + @"'
								data-bubble='{`version`:`1.0`" + backColorLoc2 + @"}'>
								<div class='bloom-translationGroup'>
									<div class='bloom-editable'>
										<p>Text over picture text</p>
									</div>
								</div>
							</div>
						</div>
					</div>
				</div>
				<div class='bloom-page' id='pageGuid2'>
					<div class='split-pane-component-inner'>
						<div class='bloom-imageContainer'>
							<div class='bloom-textOverPicture' style='left: 8.50603%; " + textColorLoc3 + @"'
								data-bubble='{`version`:`1.0`" + backColorLoc3 + @"}'>
								<div class='bloom-translationGroup'>
									<div class='bloom-editable'>
										<p>Text over picture text</p>
									</div>
								</div>
							</div>
						</div>
					</div>
				</div>
				</body></html>");

			// SUT
			var jsonString = bookDom.GetColorsUsedInBookBubbleElements();

			// Verification
			Assert.That(jsonString, Is.EqualTo(jsonResponses[responseIndex]));
		}

		[Test]
		[TestCase("{`version`:`1.0`,`backgroundColors`:[`white`,`#7b8eb8`],`style`:`none`}", "none")]
		[TestCase("{`style`:`caption`,`version`:`1.0`,`backgroundColors`:[`white`,`#7b8eb8`],`opacity`:`0.66`}", "caption")]
		[TestCase("{`version`:`1.0`,`backgroundColors`:[`white`,`#7b8eb8`]}", "none")]
		[TestCase("{`version`:`1.0`,`backgroundColors`:[`white`,`#7b8eb8`],`style`:`exclamation`}", "exclamation")]
		public void GetStyleFromDataBubble_Works(string dataBubbleAttrVal, string result)
		{
			// SUT
			var obj = HtmlDom.GetJsonObjectFromDataBubble(dataBubbleAttrVal);
			var jsonString = HtmlDom.GetStyleFromDataBubbleJsonObj(obj);

			Assert.That(jsonString, Is.EqualTo(result));
		}

		[Test]
		public void GetUserModifiableStylesUsedOnPage_works()
		{
			var domForTestingInsertedPage = new HtmlDom(
@"<html>
	<head>
		<meta charset='UTF-8' />
		<style type='text/css' title='userModifiedStyles'>
.BigWords-style { font-size: 45pt !important; text-align: center !important; }
.QuizAnswer-style { font-size: 12pt !important;}
.QuizQuestion-style {font-size: 12pt !important; font-weight: bold !important;}
		</style>
	</head>
	<body>
		<div class='A5Portrait bloom-page simple-comprehension-quiz bloom-interactive-page enterprise-only numberedPage bloom-monolingual' id='F125A8B6-EA15-4FB7-9F8D-271D7B3C8D4D' data-page='extra' data-analyticscategories='comprehension' data-reader-version='2'>
			<div class='marginBox'>
				<div class='quiz'>
					<div class='bloom-translationGroup QuizHeader-style ' data-default-languages='auto'>
						<div class='bloom-editable bloom-contentNational1' lang='en'>Check Your Understanding</div>
					</div>
					<div class='bloom-translationGroup QuizQuestion-style' data-default-languages='auto'>
						<div class='bloom-editable' lang='z' contenteditable='true' />
					</div>
					<div class='checkbox-and-textbox-choice'>
						<input class='styled-check-box' type='checkbox' name='Correct' />
						<div class='bloom-translationGroup QuizAnswer-style' data-default-languages='auto'>
							<div class='bloom-editable' lang='z' />
						</div>
						<div class='placeToPutVariableCircle' />
					</div>
				</div>
			</div>
		</div>
	</body>
</html>");

			// SUT
			var resultNode = HtmlDom.GetUserModifiableStylesUsedOnPage(domForTestingInsertedPage);

			// Verification
			const string expectedStyle1 = ".QuizAnswer-style";
			const string expectedStyle2 = ".QuizQuestion-style";
			const string unexpectedStyle1 = ".BigWords-style";
			const string unexpectedStyle2 = ".QuizHeader-style";
			Assert.That(resultNode.InnerXml.Contains(expectedStyle1), Is.True);
			Assert.That(resultNode.InnerXml.Contains(expectedStyle2), Is.True);
			Assert.That(resultNode.InnerXml.Contains(unexpectedStyle1), Is.False);
			Assert.That(resultNode.InnerXml.Contains(unexpectedStyle2), Is.False);
		}

		[TestCase("A5Portrait")]
		[TestCase("A5Landscape")]
		public void InsertFullBleedMarkup_InsertsExpectedMarkup(string pageSizeClass)
		{
			var doc = new XmlDocument();
			var input = @"
<html>
	<body>
		<div class='bloom-page " + pageSizeClass + @" bloom-frontMatter'>this is a page</div>
		<div class='bloom-page cover " + pageSizeClass + @" bloom-backMatter'>this is another page</div>
	</body>
</html>";

			doc.LoadXml(input);
			HtmlDom.InsertFullBleedMarkup(doc.DocumentElement.GetElementsByTagName("body").Cast<XmlElement>().First());

			var assertThatDoc = AssertThatXmlIn.Dom(doc);
			assertThatDoc.HasSpecifiedNumberOfMatchesForXpath("//div[@class='bloom-mediaBox " + pageSizeClass + "']/div[contains(@class, 'bloom-page')]", 2);
			assertThatDoc.HasSpecifiedNumberOfMatchesForXpath("//body[@class='bloom-fullBleed']",1);
		}
	}
}
