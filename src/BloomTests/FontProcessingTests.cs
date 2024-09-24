using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.FontProcessing;
using Newtonsoft.Json;
using NUnit.Framework;
using SIL.PlatformUtilities;

namespace BloomTests
{
    [TestFixture]
    public class FontProcessingTests
    {
        IDictionary<string, FontMetadata> _fontMetadata;
        int _fontCount;

        [OneTimeSetUp]
        public void FontProcessingSetup()
        {
            var fontMetadata = FontsApi.GetAllFontMetadata(); // loads everything in before returning.
            _fontCount = fontMetadata.Count();
            _fontMetadata = FontsApi.AvailableFontMetadataDictionary;
            //var json = JsonConvert.SerializeObject(fontMetadata);
            //Console.WriteLine("DEBUG font metadata json = {0}", json);
        }

        [Test]
        public void BasicFontMetadataCheck()
        {
            Assert.That(_fontMetadata.Count, Is.GreaterThan(0));
            Assert.That(_fontMetadata.Count, Is.EqualTo(_fontCount));
            if (Platform.IsWindows)
            {
                Assert.That(_fontMetadata.Keys, Does.Contain("Arial"));
                var arialMeta = _fontMetadata["Arial"];
                Assert.That(arialMeta.manufacturer, Does.Contain("Monotype"));
                Assert.That(arialMeta.license, Does.Contain("Microsoft supplied font"));
                Assert.That(arialMeta.copyright, Does.Contain("Monotype"));
                Assert.That(arialMeta.fsType, Is.EqualTo("Editable"));
                Assert.That(arialMeta.determinedSuitability, Is.EqualTo("unsuitable"));
                Assert.That(arialMeta.determinedSuitabilityNotes, Is.EqualTo("Microsoft font"));
                Assert.That(
                    arialMeta.licenseURL,
                    Is.EqualTo("https://learn.microsoft.com/en-us/typography/fonts/font-faq")
                );
            }
            else
            {
                Assert.That(_fontMetadata.Keys, Does.Contain("DejaVu Sans"));
                var dejavuMeta = _fontMetadata["DejaVu Sans"];
                Assert.That(dejavuMeta.manufacturer, Is.EqualTo("DejaVu fonts team"));
                Assert.That(dejavuMeta.license, Does.Contain("public domain"));
                Assert.That(
                    dejavuMeta.licenseURL,
                    Is.EqualTo("http://dejavu.sourceforge.net/wiki/index.php/License")
                );
                Assert.That(dejavuMeta.copyright, Does.Contain("Bitstream"));
                Assert.That(dejavuMeta.fsType, Is.EqualTo("Installable"));
                Assert.That(dejavuMeta.determinedSuitability, Is.EqualTo("ok"));
                Assert.That(
                    dejavuMeta.determinedSuitabilityNotes,
                    Is.EqualTo("Bitstream free license")
                );
            }
        }
    }
}
