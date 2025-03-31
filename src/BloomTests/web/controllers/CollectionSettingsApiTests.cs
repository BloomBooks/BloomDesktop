using System;
using Bloom.web.controllers;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    public class CollectionSettingsApiTests
    {
        [TestCase("Juarez-Guatemala", "Juarez-Guatemala", "")]
        [TestCase("Kyrgyzstan2020[English]", "Kyrgyzstan2020", "English")]
        public void SummaryHtmlGetsFlavorVariablesFilledIn(
            string fullEnterpriseCode,
            string expectedFolderName,
            string expectedFlavor
        )
        {
            var result = CollectionSettingsApi.GetSummaryHtml(fullEnterpriseCode);
            Assert.That(result, Contains.Substring(expectedFlavor));
        }
    }
}
