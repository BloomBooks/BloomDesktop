using Bloom.Api;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    public class BrandingSettingsTests
    {
        [TestCase("Juarez-Guatemala", "", "")]
        [TestCase("Kyrgyzstan2020[English]", "English", "")]
        [TestCase("WorldVision(Zambia)", "", "Zambia")]
        public void SummaryHtmlGetsFlavorVariablesFilledIn(
            string fullEnterpriseCode,
            string expectedFlavor,
            string expectedSubUnitName
        )
        {
            var result = BrandingSettings.GetSummaryHtml(fullEnterpriseCode);
            Assert.That(result, Contains.Substring(expectedFlavor));
            Assert.That(result, Contains.Substring(expectedSubUnitName));
        }

        [TestCase("Juarez-Guatemala", "Juarez-Guatemala", "", "")]
        [TestCase("Kyrgyzstan2020[English]", "Kyrgyzstan2020", "English", "")]
        [TestCase("WorldVision(Zambia)", "WorldVision", "", "Zambia")]
        [TestCase("WorldVision(Zambia)[English]", "WorldVision", "English", "Zambia")]
        // This is not one we know, so we just don't modify it. Code elsewhere now handles missing branding.
        [TestCase("BloomProgram", "BloomProgram", "", "")]
        [TestCase("JaneDoe-Pro", "Default", "", "")]
        [TestCase("JaneDoe-Trainer", "Default", "", "")]
        [TestCase("OurGroup-LC", "Local-Community", "", "")]
        public void ParseSubscriptionDescriptor_ValidInput(
            string fullEnterpriseCode,
            string expectedBaseKey,
            string expectedFlavor,
            string expectedSubUnitName
        )
        {
            BrandingSettings.ParseSubscriptionDescriptor(
                fullEnterpriseCode,
                out var baseKey,
                out var flavor,
                out var subUnitName
            );
            Assert.That(baseKey, Is.EqualTo(expectedBaseKey));
            Assert.That(flavor, Is.EqualTo(expectedFlavor));
            Assert.That(subUnitName, Is.EqualTo(expectedSubUnitName));
        }
    }
}
