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

        [TestCase("Ebook2x3Portrait", "Ebook2x3", false)]
        [TestCase("Ebook7x5Landscape", "Ebook7x5", true)]
        public void FromString_EbookLayouts_RoundTrip(
            string layout,
            string expectedPageSizeName,
            bool expectedLandscape
        )
        {
            var sizeAndOrientation = SizeAndOrientation.FromString(layout);

            Assert.That(sizeAndOrientation.PageSizeName, Is.EqualTo(expectedPageSizeName));
            Assert.That(sizeAndOrientation.IsLandScape, Is.EqualTo(expectedLandscape));
            // ToString() is PageSizeName + OrientationName; this is the exact class used in the
            // markup and the pageSizes.json / CSS keys, so it must match case-for-case.
            Assert.That(sizeAndOrientation.ToString(), Is.EqualTo(layout));
        }

        [TestCase("Ebook9x16Portrait", "Device16x9Portrait", false)]
        [TestCase("EBook16x9Landscape", "Device16x9Landscape", true)]
        public void FromString_Ebook16x9Aliases_NormalizeToDevice16x9(
            string layout,
            string expectedCanonicalLayout,
            bool expectedLandscape
        )
        {
            var sizeAndOrientation = SizeAndOrientation.FromString(layout);

            Assert.That(sizeAndOrientation.PageSizeName, Is.EqualTo("Device16x9"));
            Assert.That(sizeAndOrientation.IsLandScape, Is.EqualTo(expectedLandscape));
            Assert.That(sizeAndOrientation.ToString(), Is.EqualTo(expectedCanonicalLayout));
        }

        // Ebook2x3/Ebook7x5 are screen/ebook sizes, so they should be treated like the Device family.
        [TestCase("Ebook2x3Portrait")]
        [TestCase("Ebook7x5Landscape")]
        [TestCase("Device16x9Portrait")]
        public void IsDeviceLayout_TrueForEbookAndDeviceSizes(string layout)
        {
            var sizeAndOrientation = SizeAndOrientation.FromString(layout);
            var bloomLayout = new Layout { SizeAndOrientation = sizeAndOrientation };
            Assert.That(bloomLayout.IsDeviceLayout, Is.True);
        }

        [TestCase("A5Portrait")]
        [TestCase("LetterLandscape")]
        public void IsDeviceLayout_FalseForPaperSizes(string layout)
        {
            var sizeAndOrientation = SizeAndOrientation.FromString(layout);
            var bloomLayout = new Layout { SizeAndOrientation = sizeAndOrientation };
            Assert.That(bloomLayout.IsDeviceLayout, Is.False);
        }
    }
}
