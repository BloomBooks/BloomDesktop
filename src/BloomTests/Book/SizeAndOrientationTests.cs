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
		public void GetConfigurationsFromConfigurationOptionsString_Simple()
		{

			var x = SizeAndOrientation.GetConfigurationsFromConfigurationOptionsString("{'layouts': ['A4Landscape']}");
			Assert.AreEqual(1, x.Count());
			Assert.AreEqual("A4", x.First().SizeAndOrientation.PageSizeName);
			Assert.IsTrue(x.First().SizeAndOrientation.IsLandScape);
		}

		[Test]
		public void GetConfigurationsFromConfigurationOptionsString_Complex()
		{
			string json = @"{'layouts': [
		'A5Portrait',
		{'A4Landscape' : { 'Styles': ['Default', 'SideBySide']}}
	]}";
			var x = SizeAndOrientation.GetConfigurationsFromConfigurationOptionsString(json);
			Assert.AreEqual(3, x.Count());
			Assert.AreEqual("A5", x.First().SizeAndOrientation.PageSizeName);
			Assert.IsFalse(x.First().SizeAndOrientation.IsLandScape);
			Assert.IsNullOrEmpty(x.First().Style);

			Layout a4landscapeDefault = x.ToArray()[1];
			Assert.AreEqual("A4", a4landscapeDefault.SizeAndOrientation.PageSizeName);
			Assert.IsTrue(a4landscapeDefault.SizeAndOrientation.IsLandScape);
			Assert.AreEqual("Default", a4landscapeDefault.Style);

			Layout a4landscapeSideBySide = x.ToArray()[2];
			Assert.AreEqual("A4", a4landscapeSideBySide.SizeAndOrientation.PageSizeName);
			Assert.IsTrue(a4landscapeSideBySide.SizeAndOrientation.IsLandScape);
			Assert.AreEqual("SideBySide", a4landscapeSideBySide.Style);
		}

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
