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
			dom.LoadXml(@"<html ><head>
									<link rel='stylesheet' href='LeTtErPortrait.css' type='text/css' />
									</head><body></body></html>");
			Assert.AreEqual("Letter", SizeAndOrientation.FromDom(dom).PageSizeName);
		}


		[Test]
		public void PageSizeName_A5LANDSCAPE()
		{
			var dom = new XmlDocument();
			dom.LoadXml(@"<html ><head>
									<link rel='stylesheet' href='a5LANDSCAPE.css' type='text/css' />
									</head><body></body></html>");

			Assert.AreEqual("A5", SizeAndOrientation.FromDom(dom).PageSizeName);
		}

		[Test]
		public void IsLandscape_portraitCSS_false()
		{
			var dom = new XmlDocument();
			dom.LoadXml(@"<html ><head>
									<link rel='stylesheet' href='a5Portrait.css' type='text/css' />
									</head><body></body></html>");

			Assert.IsFalse(SizeAndOrientation.FromDom(dom).IsLandScape);
		}
		[Test]
		public void IsLandscape_landscapeCSS_true()
		{
			var dom = new XmlDocument();
			dom.LoadXml(@"<html ><head>
									<link rel='stylesheet' href='a5LAndSCAPE.css' type='text/css' />
									</head><body></body></html>");

			Assert.IsTrue(SizeAndOrientation.FromDom(dom).IsLandScape);
		}
	}
}
