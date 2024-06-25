using System.Linq;
using System.Xml;
using Bloom.Book;
using Bloom.SafeXml;
using NUnit.Framework;

namespace BloomTests.Book
{
    [TestFixture]
    public class SizeAndOrientationTests
    {
        [Test]
        public void GetConfigurationsFromConfigurationOptionsString_Simple()
        {
            var x = Layout.GetConfigurationsFromConfigurationOptionsString(
                "{'layouts': ['A4Landscape']}"
            );
            Assert.AreEqual(1, x.Count());
            Assert.AreEqual("A4", x.First().SizeAndOrientation.PageSizeName);
            Assert.IsTrue(x.First().SizeAndOrientation.IsLandScape);
        }

        [Test]
        public void GetConfigurationsFromConfigurationOptionsString_Complex()
        {
            string json =
                @"{'layouts': [
		'A5Portrait',
		{'A4Landscape' : { 'Styles': ['Default', 'Foobar']}}
	]}";
            var x = Layout.GetConfigurationsFromConfigurationOptionsString(json);
            Assert.AreEqual(3, x.Count());
            Assert.AreEqual("A5", x.First().SizeAndOrientation.PageSizeName);
            Assert.IsFalse(x.First().SizeAndOrientation.IsLandScape);
            Assert.That(x.First().Style, Is.Null.Or.Empty);

            Layout a4landscapeDefault = x.ToArray()[1];
            Assert.AreEqual("A4", a4landscapeDefault.SizeAndOrientation.PageSizeName);
            Assert.IsTrue(a4landscapeDefault.SizeAndOrientation.IsLandScape);
            Assert.AreEqual("Default", a4landscapeDefault.Style);

            Layout a4landscapeFoobar = x.ToArray()[2];
            Assert.AreEqual("A4", a4landscapeFoobar.SizeAndOrientation.PageSizeName);
            Assert.IsTrue(a4landscapeFoobar.SizeAndOrientation.IsLandScape);
            Assert.AreEqual("Foobar", a4landscapeFoobar.Style);
        }

        [Test]
        public void PageSizeName_USLetter()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                @"<html ><body><div id='foo'></div><div class='blah bloom-page LetterPortrait'></div></body></html>"
            );
            Assert.AreEqual(
                "Letter",
                SizeAndOrientation.GetSizeAndOrientation(dom, "A5Portrait").PageSizeName
            );
        }

        [Test]
        public void PageSizeName_A5LANDSCAPE()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                @"<html ><body><div id='foo'></div><div class='blah bloom-page A5Landscape'></div></body></html>"
            );
            Assert.AreEqual(
                "A5",
                SizeAndOrientation.GetSizeAndOrientation(dom, "A5Portrait").PageSizeName
            );
        }

        [Test]
        public void IsLandscape_portraitCSS_false()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                @"<html ><body><div id='foo'></div><div class='blah bloom-page a5Portrait'></div></body></html>"
            );
            Assert.IsFalse(SizeAndOrientation.GetSizeAndOrientation(dom, "A5Portrait").IsLandScape);
        }

        [Test]
        public void IsLandscape_landscapeCSS_true()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                @"<html ><body><div id='foo'></div><div class='blah bloom-page A5Landscape'></div></body></html>"
            );
            Assert.IsTrue(SizeAndOrientation.GetSizeAndOrientation(dom, "A5Portrait").IsLandScape);
        }
    }
}
