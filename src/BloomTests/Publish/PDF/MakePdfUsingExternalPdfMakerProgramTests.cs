using Bloom.Publish.PDF;
using NUnit.Framework;

namespace BloomTests.Publish.PDF
{
    [TestFixture]
    public class MakePdfUsingExternalPdfMakerProgramTests
    {
        [TestCase("A0", false, 1195, 847)]
        [TestCase("A6", false, 154, 111)]
        [TestCase("B5", false, 256, 182)]
        [TestCase("Letter", false, 285.4, 221.9)]
        [TestCase("Legal", false, 361.6, 221.9)]
        [TestCase("HalfLetter", false, 221.9, 145.7)]
        [TestCase("QuarterLetter", false, 145.7, 113.95)]
        [TestCase("USComic", false, 266.35, 174.275)]
        [TestCase("Size6x9", false, 234.6, 158.4)]
        [TestCase("Cm13", false, 136, 136)]
        [TestCase("In8", false, 209, 209)]
        public void GetFullBleedPageSize_SupportedPaperSize_ReturnsBleedDimensions(
            string paperSize,
            bool landscape,
            double expectedHeightMm,
            double expectedWidthMm
        )
        {
            var dimensions = MakePdfUsingExternalPdfMakerProgram.GetFullBleedPageSize(
                paperSize,
                landscape
            );

            Assert.That(
                dimensions.HasValue,
                Is.True,
                $"Expected {paperSize} to support full bleed"
            );
            Assert.That(dimensions.Value.height, Is.EqualTo(expectedHeightMm).Within(0.2));
            Assert.That(dimensions.Value.width, Is.EqualTo(expectedWidthMm).Within(0.2));
        }

        [Test]
        public void GetFullBleedPageSize_Landscape_SwapsDimensions()
        {
            var portrait = MakePdfUsingExternalPdfMakerProgram.GetFullBleedPageSize("A4", false);
            var landscape = MakePdfUsingExternalPdfMakerProgram.GetFullBleedPageSize("A4", true);

            Assert.That(portrait.HasValue, Is.True);
            Assert.That(landscape.HasValue, Is.True);
            Assert.That(landscape.Value.height, Is.EqualTo(portrait.Value.width).Within(0.01));
            Assert.That(landscape.Value.width, Is.EqualTo(portrait.Value.height).Within(0.01));
        }

        [TestCase("Device16x9")]
        [TestCase("BogusSize")]
        [TestCase("")]
        [TestCase(null)]
        public void GetFullBleedPageSize_NonPaperLayout_ReturnsNull(string paperSize)
        {
            var dimensions = MakePdfUsingExternalPdfMakerProgram.GetFullBleedPageSize(
                paperSize,
                false
            );

            Assert.That(dimensions.HasValue, Is.False);
        }
    }
}
