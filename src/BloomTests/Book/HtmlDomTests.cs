using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using NUnit.Framework;
using Bloom.Book;
using Palaso.TestUtilities;

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
		public void SetBaseForRelativePaths_NoHead_Throw()
		{
			var dom = new HtmlDom(
						  @"<html></html>");
			Assert.Throws<ArgumentNullException>(() => dom.SetBaseForRelativePaths("theBase"));
		}
		[Test]
		public void SetBaseForRelativePaths_NoExistingBase_Adds()
		{
			var dom = new HtmlDom(
						  @"<html><head/></html>");
			dom.SetBaseForRelativePaths("theBase");
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("html/head/base[@href='theBase']", 1);
		}
		[Test]
		public void SetBaseForRelativePaths_HasExistingBase_Replaces()
		{
			var dom = new HtmlDom(
						  @"<html><head><base href='original'/></head></html>");
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("html/head/base[@href='original']", 1);
			dom.SetBaseForRelativePaths("new");
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("html/head/base[@href='original']", 0);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("html/head/base[@href='new']", 1);
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
	}
}
