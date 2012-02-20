using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using Bloom.Book;
using NUnit.Framework;

namespace BloomTests.Book
{
	[TestFixture]
	public class SizeAndOrientationTests
	{

		[Test]
		public void PageSizeName_USLetter()
		{
			var dom = new XmlDocument();
			dom.LoadXml(@"<html ><body><div id='foo'></div><div class='blah bloom-page LetterPortrait'></div></body></html>");
			Assert.AreEqual("Letter", SizeAndOrientation.GetSizeAndOrientation(dom, "A5Portrait").PageSizeName);
		}


		[Test]
		public void PageSizeName_A5LANDSCAPE()
		{
			var dom = new XmlDocument();
			dom.LoadXml(@"<html ><body><div id='foo'></div><div class='blah bloom-page A5Landscape'></div></body></html>");
			Assert.AreEqual("A5", SizeAndOrientation.GetSizeAndOrientation(dom, "A5Portrait").PageSizeName);
		}

		[Test]
		public void IsLandscape_portraitCSS_false()
		{
			var dom = new XmlDocument();
			dom.LoadXml(@"<html ><body><div id='foo'></div><div class='blah bloom-page a5Portrait'></div></body></html>");
			Assert.IsFalse(SizeAndOrientation.GetSizeAndOrientation(dom, "A5Portrait").IsLandScape);
		}
		[Test]
		public void IsLandscape_landscapeCSS_true()
		{
			var dom = new XmlDocument();
			dom.LoadXml(@"<html ><body><div id='foo'></div><div class='blah bloom-page A5Landscape'></div></body></html>");
			Assert.IsTrue(SizeAndOrientation.GetSizeAndOrientation(dom, "A5Portrait").IsLandScape);
		}
	}
}
