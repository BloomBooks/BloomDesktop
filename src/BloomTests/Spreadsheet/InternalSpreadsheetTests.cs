using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Spreadsheet;
using NUnit.Framework;

namespace BloomTests.Spreadsheet
{
    /// <summary>
    /// Tests various specific functions of the InternalSpreadsheet class
    /// </summary>
    public class InternalSpreadsheetTests
    {
        [Test]
        public void AddColumn_KeepsAsteriskLast()
        {
            var ss = new InternalSpreadsheet();
            var indexEn = ss.AddColumnForTag("[en]", "English", "EnglishComment");
            var indexFr = ss.AddColumnForTag("[fr]", "French", "FrenchComment");
            var indexAsterisk = ss.AddColumnForTag("[*]", "unknown", "UnknownComment");
            var row1 = new ContentRow(ss);
            var row2 = new ContentRow(ss);
            var row3 = new ContentRow(ss);
            row1.SetCell(indexEn, "English1");
            row1.SetCell(indexFr, "French1");
            row1.SetCell(indexAsterisk, "asterisk1");
            row2.SetCell(indexEn, "English2");
            row2.SetCell(indexFr, "French2");
            var row2Count = row2.Count;
            var row3Count = row3.Count;
            var indexDe = ss.AddColumnForTag("[de]", "German", "GermanComment");

            Assert.That(indexDe, Is.EqualTo(indexAsterisk));
            indexAsterisk++;
            Assert.That(ss.GetColumnForTag("[*]"), Is.EqualTo(indexAsterisk));
            Assert.That(row1.GetCell(indexEn).Content, Is.EqualTo("English1"));
            Assert.That(row1.GetCell(indexFr).Content, Is.EqualTo("French1"));
            Assert.That(row1.GetCell(indexDe).Content, Is.EqualTo(""));
            Assert.That(row1.GetCell(indexAsterisk).Content, Is.EqualTo("asterisk1"));

            Assert.That(row2.Count, Is.EqualTo(row2Count));
            Assert.That(row2.GetCell(indexEn).Content, Is.EqualTo("English2"));
            Assert.That(row2.GetCell(indexFr).Content, Is.EqualTo("French2"));

            Assert.That(row3.Count, Is.EqualTo(row3Count));
            Assert.That(row3.GetCell(indexEn).Content, Is.EqualTo(""));
        }

        [TestCase("copyright", "[copyright]")]
        [TestCase("bookTitle", "[book title]")]
        [TestCase("language1", "[language 1]")]
        [TestCase("contentLanguage2", "[content language 2]")]
        [TestCase("ISBN", "[ISBN]")]
        [TestCase("contentLanguage1Rtl", "[content language 1 rtl]")]
        public void MapDataBookLabelToRowLabel_Works(string dataBookLabel, string expectedRowLabel)
        {
            var rowLabel = InternalSpreadsheet.MapDataBookLabelToRowLabel(dataBookLabel);
            Assert.That(rowLabel, Is.EqualTo(expectedRowLabel));
        }

        [TestCase("[copyright]", "copyright")]
        [TestCase("[book title]", "bookTitle")]
        [TestCase("[language 1]", "language1")]
        [TestCase("[content language 2]", "contentLanguage2")]
        [TestCase("[ISBN]", "ISBN")]
        [TestCase("[content language 1 rtl]", "contentLanguage1Rtl")]
        public void MapRowLabelToDataBookLabel_Works(string rowLabel, string expectedDataBookLabel)
        {
            var dataBookLabel = InternalSpreadsheet.MapRowLabelToDataBookLabel(rowLabel);
            Assert.That(dataBookLabel, Is.EqualTo(expectedDataBookLabel));
        }

        /// <summary>
        /// The two mappings must not depend on the user's culture. Turkish is the case that breaks
        /// naive casing: it maps 'i' to the DOTTED capital 'İ' (U+0130) and 'I' to the DOTLESS 'ı'
        /// (U+0131). Before BL-16754 these used ToUpper()/ToLower(), so a Turkish user turned
        /// "[cover image]" into "coverİmage", which matches no data-book label — the cover image was
        /// silently dropped from the spreadsheet, with no error.
        /// </summary>
        /// <remarks>
        /// [SetCulture] rather than the BLOOM_TEST_CULTURE sweep (see src/BloomTests/TestCulture.cs)
        /// so this specific regression is caught on every PR build rather than once a week.
        /// </remarks>
        [TestCase("coverImage", "[cover image]")]
        [TestCase("bookTitle", "[book title]")]
        [TestCase("insideFontCover", "[inside front cover]")]
        [SetCulture("tr-TR")]
        public void MapDataBookLabelToRowLabel_InTurkish_IsUnaffectedByCulture(
            string dataBookLabel,
            string expectedRowLabel
        )
        {
            AssertTurkishCasingReallyIsInEffect();
            Assert.That(
                InternalSpreadsheet.MapDataBookLabelToRowLabel(dataBookLabel),
                Is.EqualTo(expectedRowLabel)
            );
        }

        [TestCase("[cover image]", "coverImage")]
        [TestCase("[book title]", "bookTitle")]
        [TestCase("[ISBN]", "ISBN")]
        [SetCulture("tr-TR")]
        public void MapRowLabelToDataBookLabel_InTurkish_IsUnaffectedByCulture(
            string rowLabel,
            string expectedDataBookLabel
        )
        {
            AssertTurkishCasingReallyIsInEffect();
            Assert.That(
                InternalSpreadsheet.MapRowLabelToDataBookLabel(rowLabel),
                Is.EqualTo(expectedDataBookLabel)
            );
        }

        /// <summary>
        /// Guards against these tests passing for the wrong reason. If [SetCulture] ever stops
        /// taking effect, the assertions above would pass trivially in English and we would think
        /// the Turkish case was covered when it was not.
        /// </summary>
        private static void AssertTurkishCasingReallyIsInEffect()
        {
            if (CultureInfo.CurrentCulture.Name != "tr-TR")
                Assert.Fail(
                    $"This test must run in tr-TR, but the culture is '{CultureInfo.CurrentCulture.Name}'; "
                        + "[SetCulture] is not doing anything, so the Turkish casing hazard is not being tested."
                );
            Assert.That(
                "i".ToUpper(),
                Is.Not.EqualTo("I"),
                "In tr-TR, 'i' should uppercase to the dotted 'İ'. It did not, so this test would pass "
                    + "even with the culture-sensitive ToUpper() that BL-16754 removed."
            );
        }
    }
}
