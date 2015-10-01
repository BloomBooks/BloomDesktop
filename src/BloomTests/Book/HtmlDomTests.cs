using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using Bloom;
using NUnit.Framework;
using Bloom.Book;
using Palaso.TestUtilities;
using Palaso.Xml;

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
		public void BaseForRelativePaths_NoHead_NoLongerThrows()
		{
			var dom = new HtmlDom(
						  @"<html></html>");
			dom.BaseForRelativePaths = "theBase";
			Assert.AreEqual("theBase", dom.BaseForRelativePaths);
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
				<link rel='stylesheet' href='../settingsCollectionStyles.css' type='text/css' />
				<link rel='stylesheet' href='my special c.css' type='text/css' />
				<link rel='stylesheet' href='Basic book.css' type='text/css' />
				<link rel='stylesheet' href='../customCollectionStyles.css' type='text/css' />
				<link rel='stylesheet' href='customBookStyles.css' type='text/css' />
				<link rel='stylesheet' href='basePage.css' type='text/css' />
				<link rel='stylesheet' href='languageDisplay.css' type='text/css' />
				<link rel='stylesheet' href='../../editMode.css' type='text/css' />

				</head></html>";

			var bookdom = new HtmlDom(content);

			bookdom.SortStyleSheetLinks();
			var dom = bookdom.RawDom;

			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[1][@href='basePage.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[2][@href='languageDisplay.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[3][@href='../../editMode.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[4][@href='Basic book.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[5][@href='Factory-Xmatter.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[6][@href='my special a.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[7][@href='my special b.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[8][@href='my special c.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[9][@href='../settingsCollectionStyles.css']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//head/link[10][@href='../customCollectionStyles.css']", 1);
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
		public void GetImageElementUrl_ElementIsDivWithBackgroundImage_ReturnsUrl()
		{
			var element = MakeElement("<div style='background-image:url(\"test%20me\")'/>");
            Assert.AreEqual("test%20me", HtmlDom.GetImageElementUrl(element).UrlEncoded);

			//stress the regex a bit
			element = MakeElement("<div style=\" background-image : URL(\'test%20me\')\"/>");
			Assert.AreEqual("test%20me", HtmlDom.GetImageElementUrl(element).UrlEncoded, "Query was too restrictive somehow");
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
						<div data-book='bookTitle' lang='tpi'>Nupela Book</div>
				</div>
				<div id='somePage'>
					<div class='bloom-translationGroup bookTitle'>
						<div class='bloom-editable' data-book='bookTitle' lang='tpi'>
							<p>Nupela Book<br/></p>
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
			dom.LoadXml("<div><foo/><img/><div style=\"color:red;\">hello</div><div style=\" background-image : URL(\'old.png\')\"/></div>");
			var elements = HtmlDom.SelectChildImgAndBackgroundImageElements(dom.DocumentElement);
			Assert.AreEqual(2,elements.Count);
		}

		[Test]
		public void GetIsImgOrSomethingWithBackgroundImage_NoBackgroundImageProperty_False()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<div style=\"color:red;\"></div>");
			Assert.IsFalse(HtmlDom.GetIsImgOrSomethingWithBackgroundImage(dom.DocumentElement));
		}
		[Test]
		public void GetIsImgOrSomethingWithBackgroundImage_HasBackgroundImageProperty_True()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<div style=\" background-image : URL(\'old.png\')\"></div>");
			Assert.IsTrue(HtmlDom.GetIsImgOrSomethingWithBackgroundImage(dom.DocumentElement));

			dom.LoadXml("<div style=\'background-image:url( \"old.png\" )\'></div>");
			Assert.IsTrue(HtmlDom.GetIsImgOrSomethingWithBackgroundImage(dom.DocumentElement), "Regex needs work?");
		}

		[Test]
		public void GetIsImgOrSomethingWithBackgroundImage_Img_True()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><img/></body></html>");
			Assert.IsTrue(HtmlDom.GetIsImgOrSomethingWithBackgroundImage(dom.SelectNodes("//img")[0] as XmlElement));
		}
		[Test]
		public void GetIsImgOrSomethingWithBackgroundImage_ImgIsSybling_False()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><img/><foo/></body></html>");
			Assert.IsFalse(HtmlDom.GetIsImgOrSomethingWithBackgroundImage(dom.SelectNodes("//foo")[0] as XmlElement));
		}
		[Test]
		public void GetIsImgOrSomethingWithBackgroundImage_ImgIsChild_False()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div><img/></div></body></html>");
			Assert.IsFalse(HtmlDom.GetIsImgOrSomethingWithBackgroundImage(dom.SelectNodes("//div")[0] as XmlElement));
		}
	}
}
