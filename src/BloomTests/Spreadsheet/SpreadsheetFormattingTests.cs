using System;
using Bloom.Spreadsheet;
using NUnit.Framework;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Text.RegularExpressions;
using System.Drawing;
using Bloom.SafeXml;

namespace BloomTests.Spreadsheet
{
    /// <summary>
    /// This class tests parsing an Xml string into runs marked with their formatting
    /// </summary>
    public class SpreadsheetFormattingTests
    {
        [TestCase(
            "<p><strong>Bob</strong> and<em>Izzy</em> went <u>underneath</u> the <sup>supper</sup> table <sup><u><em><strong>everyday.</strong></em></u></sup></p>"
        )]
        [TestCase(
            "<p><b>Bob</b> and<i>Izzy</i> went <u>underneath</u> the <sup>supper</sup> table <sup><u><em><b>everyday.</b></em></u></sup></p>"
        )]
        public void ParsesFormatttedXmlSimple(string input)
        {
            MarkedUpText parsed1 = MarkedUpText.ParseXml(input);
            Assert.That(parsed1.Count, Is.EqualTo(9));
            AssertHasFormatting(
                parsed1.GetRun(0),
                "Bob",
                bolded: true,
                italicized: false,
                superscripted: false,
                underlined: false
            );
            AssertHasFormatting(
                parsed1.GetRun(1),
                " and",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false
            );
            AssertHasFormatting(
                parsed1.GetRun(2),
                "Izzy",
                bolded: false,
                italicized: true,
                superscripted: false,
                underlined: false
            );
            AssertHasFormatting(
                parsed1.GetRun(4),
                "underneath",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: true
            );
            AssertHasFormatting(
                parsed1.GetRun(6),
                "supper",
                bolded: false,
                italicized: false,
                superscripted: true,
                underlined: false
            );
            AssertHasFormatting(
                parsed1.GetRun(8),
                "everyday.",
                bolded: true,
                italicized: true,
                superscripted: true,
                underlined: true
            );
        }

        [Test]
        public void ParsesFormattedXmlNested()
        {
            MarkedUpText parsed = MarkedUpText.ParseXml(
                "<p><strong>Bertha and <sup>Bessy</sup></strong><sup><u>sunk</u></sup><u>under</u> the water.</p>"
            );
            Assert.That(parsed.Count, Is.EqualTo(5));
            AssertHasFormatting(
                parsed.GetRun(0),
                "Bertha and ",
                bolded: true,
                italicized: false,
                superscripted: false,
                underlined: false
            );
            AssertHasFormatting(
                parsed.GetRun(1),
                "Bessy",
                bolded: true,
                italicized: false,
                superscripted: true,
                underlined: false
            );
            AssertHasFormatting(
                parsed.GetRun(2),
                "sunk",
                bolded: false,
                italicized: false,
                superscripted: true,
                underlined: true
            );
            AssertHasFormatting(
                parsed.GetRun(3),
                "under",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: true
            );
            AssertHasFormatting(
                parsed.GetRun(4),
                " the water.",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false
            );
        }

        [Test]
        public void ParsesFormattedXmlVeryNested()
        {
            MarkedUpText parsed = MarkedUpText.ParseXml(
                "<p>  <u><strong><sup>b</sup></strong><em>c</em><strong>d</strong></u>e<u>f</u></p>"
            );
            Assert.That(parsed.Count, Is.EqualTo(6));
            AssertHasFormatting(
                parsed.GetRun(0),
                "  ",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false
            );
            AssertHasFormatting(
                parsed.GetRun(1),
                "b",
                bolded: true,
                italicized: false,
                superscripted: true,
                underlined: true
            );
            AssertHasFormatting(
                parsed.GetRun(2),
                "c",
                bolded: false,
                italicized: true,
                superscripted: false,
                underlined: true
            );
            AssertHasFormatting(
                parsed.GetRun(3),
                "d",
                bolded: true,
                italicized: false,
                superscripted: false,
                underlined: true
            );
            AssertHasFormatting(
                parsed.GetRun(4),
                "e",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false
            );
            AssertHasFormatting(
                parsed.GetRun(5),
                "f",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: true
            );
        }

        [Test]
        public void ParsesColorBasic()
        {
            MarkedUpText parsed = MarkedUpText.ParseXml(
                "<p>a<span style=\"color:#123def;\">b</span>c</p>"
            );
            Assert.That(parsed.Count, Is.EqualTo(3));
            AssertHasFormatting(
                parsed.GetRun(0),
                "a",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false,
                color: Color.Empty
            );
            AssertHasFormatting(
                parsed.GetRun(1),
                "b",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false,
                color: ColorTranslator.FromHtml("#123def")
            );
            AssertHasFormatting(
                parsed.GetRun(2),
                "c",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false,
                color: Color.Empty
            );
        }

        [Test]
        public void ParsesColorWithWhitespaceAndNoSemicolon()
        {
            MarkedUpText parsed = MarkedUpText.ParseXml(
                "<p>a<span style=\"color : #123def \">b</span>c</p>"
            );
            Assert.That(parsed.Count, Is.EqualTo(3));
            AssertHasFormatting(
                parsed.GetRun(0),
                "a",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false,
                color: Color.Empty
            );
            AssertHasFormatting(
                parsed.GetRun(1),
                "b",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false,
                color: ColorTranslator.FromHtml("#123def")
            );
            AssertHasFormatting(
                parsed.GetRun(2),
                "c",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false,
                color: Color.Empty
            );
        }

        [Test]
        public void ParsesColorNested()
        {
            MarkedUpText parsed = MarkedUpText.ParseXml(
                "<p>a<strong>b<span style=\"color:#123def;\">c<em>d</em></span>e</strong></p>"
            );
            Assert.That(parsed.Count, Is.EqualTo(5));
            AssertHasFormatting(
                parsed.GetRun(0),
                "a",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false,
                color: Color.Empty
            );
            AssertHasFormatting(
                parsed.GetRun(1),
                "b",
                bolded: true,
                italicized: false,
                superscripted: false,
                underlined: false,
                color: Color.Empty
            );
            AssertHasFormatting(
                parsed.GetRun(2),
                "c",
                bolded: true,
                italicized: false,
                superscripted: false,
                underlined: false,
                color: ColorTranslator.FromHtml("#123def")
            );
            AssertHasFormatting(
                parsed.GetRun(3),
                "d",
                bolded: true,
                italicized: true,
                superscripted: false,
                underlined: false,
                color: ColorTranslator.FromHtml("#123def")
            );
            AssertHasFormatting(
                parsed.GetRun(4),
                "e",
                bolded: true,
                italicized: false,
                superscripted: false,
                underlined: false,
                color: Color.Empty
            );
        }

        [Test]
        public void ParsesFormattedXml_nonXml_returnsOriginalString()
        {
            MarkedUpText parsed = MarkedUpText.ParseXml("An elephant walked into a bar.");
            Assert.That(parsed.Count, Is.EqualTo(1));
            AssertHasFormatting(
                parsed.GetRun(0),
                "An elephant walked into a bar.",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false
            );
        }

        [Test]
        public void MarkedUpText_ToString_ReturnsDecentXmlString()
        {
            MarkedUpText parsed = MarkedUpText.ParseXml(
                "<p>This <strong>papaya</strong> </p><p>tastes perfect</p><p></p>"
            );
            Assert.That(
                parsed.ToString(),
                Is.EqualTo(
                    $"<p>This <strong>papaya</strong> </p>{Environment.NewLine}<p>tastes perfect</p>{Environment.NewLine}<p></p>"
                )
            );
        }

        private void AssertHasFormatting(
            MarkedUpTextRun textRun,
            string text,
            bool bolded,
            bool italicized,
            bool superscripted,
            bool underlined,
            Color? color = null
        )
        {
            Assert.That(textRun.Text, Is.EqualTo(text));
            Assert.That(textRun.Bold, Is.EqualTo(bolded));
            Assert.That(textRun.Italic, Is.EqualTo(italicized));
            Assert.That(textRun.Superscript, Is.EqualTo(superscripted));
            Assert.That(textRun.Underlined, Is.EqualTo(underlined));
            if (color != null)
            {
                Assert.That(textRun.Color, Is.EqualTo(color));
            }
        }

        [TestCase("", "")]
        [TestCase("<p>1Some text</p>", "1Some text")]
        [TestCase("\r\n\t\t<p>2Some text</p>\r\n", "2Some text")]
        [TestCase("\r\n\t\t<p>3Some text.</p><p>Some more.</p>\r\n", "3Some text.\nSome more.")]
        [TestCase("\r\n\t\t<p>4Some text.</p><p>Some more.</p>\r\n", "4Some text.\nSome more.")]
        [TestCase(
            "\r\n\t\t<p>4Some text.</p>\r\n\t\t<p>Some more.</p>\r\n",
            "4Some text.\nSome more."
        )]
        [TestCase("\r\n\t\t<p></p><p></p><p>5Some text</p>\r\n", "\n\n5Some text")]
        [TestCase("\r\n\t\t<p>6Some text</p><p></p>", "6Some text\n")]
        //handles other types of line breaks:
        [TestCase(@"<p>7Some text.<span class=""bloom-linebreak""></span></p>", "7Some text.\n")]
        [TestCase(
            @"<p>8Some text.<span class=""bloom-linebreak""></span>Some more.</p>",
            "8Some text.\nSome more."
        )]
        [TestCase("<p><br></br>9Some text.</p>", "\n9Some text.")]
        [TestCase(
            @"<p>Some text.<span class=""bloom-linebreak""></span>Some more.</p>",
            "Some text.\nSome more."
        )]
        [TestCase(
            @"<p>Some text.<span class=""bloom-linebreak""></span>Some more.</p>",
            "Some text.\nSome more."
        )]
        //Keeps text outside of <p> tags:
        [TestCase(
            "\r\nfront text\t<p>Some text.</p>middletext<p>Some more.</p>\t end text",
            "\nfront text\tSome text.\nmiddletextSome more.\n\t end text"
        )]
        [TestCase(
            "<div>front div text</div><p>Some paragraph text.</p><div>middle div text</div><p>More paragraph text.</p><div>End div text</div>",
            "front div textSome paragraph text.\nmiddle div textMore paragraph text.\nEnd div text"
        )]
        //Keeps all whitespace if there are no <p> tags:
        [TestCase(
            "\r\n\t\tText without p tags.\r\n\tSome more.\r\n",
            "\n\t\tText without p tags.\n\tSome more.\n"
        )]
        public void ParsesFormattedXmlHandlesWhitespace(string input, string expected)
        {
            var xmlresult = MarkedUpText.ParseXml(input);
            var result = xmlresult.PlainText();
            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase("<p>Some <span>text</span></p>", "Some text")] // span without class caused problems at one point
        public void ParsesFormattedXmlSpecialCases(string input, string expected)
        {
            var xmlresult = MarkedUpText.ParseXml(input);
            var result = xmlresult.PlainText();
            Assert.That(result, Is.EqualTo(expected));
        }
    }

    /// <summary>
    /// This class tests parsing an Excel spreadsheet cell and creating an xml string with the content and formatting
    /// </summary>
    public class ParseExcelCellFormattingTests
    {
        static ParseExcelCellFormattingTests()
        {
            // The package requires us to do this as a way of acknowledging that we
            // accept the terms of the NonCommercial license.
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        private ExcelWorksheet _worksheet;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var package = new ExcelPackage();
            _worksheet = package.Workbook.Worksheets.Add("MarkdownTest");
            ExcelRange firstCell = _worksheet.Cells[1, 1];
            firstCell.IsRichText = true;
            AddRunToCell(
                firstCell,
                "nnn",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false
            );
            AddRunToCell(
                firstCell,
                "uuu",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: true
            );
            AddRunToCell(
                firstCell,
                "nnn",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false
            );
            AddRunToCell(
                firstCell,
                "sss",
                bolded: false,
                italicized: false,
                superscripted: true,
                underlined: false
            );
            AddRunToCell(
                firstCell,
                "iii",
                bolded: false,
                italicized: true,
                superscripted: false,
                underlined: false
            );
            AddRunToCell(
                firstCell,
                "bbb",
                bolded: true,
                italicized: false,
                superscripted: false,
                underlined: false
            );

            ExcelRange secondCell = _worksheet.Cells[2, 2];
            secondCell.IsRichText = true;
            AddRunToCell(
                secondCell,
                "We wish ",
                bolded: true,
                italicized: false,
                superscripted: false,
                underlined: false
            );
            AddRunToCell(
                secondCell,
                "you",
                bolded: true,
                italicized: true,
                superscripted: true,
                underlined: false
            );
            AddRunToCell(
                secondCell,
                " a merry ",
                bolded: true,
                italicized: true,
                superscripted: true,
                underlined: true
            );
            AddRunToCell(
                secondCell,
                "Chri",
                bolded: true,
                italicized: true,
                superscripted: true,
                underlined: false
            );
            AddRunToCell(
                secondCell,
                "stmas",
                bolded: true,
                italicized: true,
                superscripted: false,
                underlined: false
            );

            ExcelRange thirdCell = _worksheet.Cells[3, 3];
            thirdCell.IsRichText = true;
            AddRunToCell(
                thirdCell,
                "\r\nApples\r\nBananas\r\n",
                bolded: true,
                italicized: false,
                superscripted: false,
                underlined: false
            );
            AddRunToCell(
                thirdCell,
                "Cherries\r\n\r\nDurian",
                bolded: false,
                italicized: true,
                superscripted: false,
                underlined: false
            );

            ExcelRange fourthCell = _worksheet.Cells[4, 4];
            thirdCell.IsRichText = true;
            AddRunToCell(
                fourthCell,
                "Some text.\r\n",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false
            );

            ExcelRange fifthCell = _worksheet.Cells[5, 5];
            fifthCell.IsRichText = true;
            AddRunToCell(
                fifthCell,
                "Head",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false
            );
            AddRunToCell(
                fifthCell,
                " shoulders",
                bolded: false,
                italicized: false,
                superscripted: true,
                underlined: false,
                colorString: "#def123"
            );
            AddRunToCell(
                fifthCell,
                " knees",
                bolded: false,
                italicized: false,
                superscripted: true,
                underlined: false
            );
            AddRunToCell(
                fifthCell,
                " and toes",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false,
                colorString: "#abcabc"
            );

            ExcelRange sixthCell = _worksheet.Cells[6, 6];
            thirdCell.IsRichText = true;
            AddRunToCell(
                sixthCell,
                "One.\r\nTwo.\r\n\xfeffThree.\r\nFour.",
                bolded: false,
                italicized: false,
                superscripted: false,
                underlined: false
            );
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() { }

        private void AddRunToCell(
            ExcelRange cell,
            string text,
            bool bolded,
            bool italicized,
            bool superscripted,
            bool underlined,
            string colorString = null
        )
        {
            ExcelRichText rt = cell.RichText.Add(text);
            rt.Bold = bolded;
            rt.Italic = italicized;
            rt.UnderLine = underlined;
            if (superscripted)
            {
                rt.VerticalAlign = ExcelVerticalAlignmentFont.Superscript;
            }
            if (colorString != null)
            {
                rt.Color = ColorTranslator.FromHtml(colorString);
            }
            else
            {
                rt.Color = Color.FromArgb(255, 0, 0, 0);
            }
        }

        [Test]
        public void BuildsXmlSimple()
        {
            ExcelRange firstCell = _worksheet.Cells[1, 1];
            string xmlString = SpreadsheetIO.BuildXmlString(firstCell);
            Assert.That(
                xmlString,
                Is.EqualTo("<p>nnn<u>uuu</u>nnn<sup>sss</sup><em>iii</em><strong>bbb</strong></p>")
            );
        }

        [Test]
        public void BuildsXmlParagraphs()
        {
            ExcelRange thirdCell = _worksheet.Cells[3, 3];
            string xmlString = SpreadsheetIO.BuildXmlString(thirdCell);
            Assert.That(
                xmlString,
                Is.EqualTo(
                    "<p></p><p><strong>Apples</strong></p><p><strong>Bananas</strong></p><p><em>Cherries</em></p><p></p><p><em>Durian</em></p>"
                )
            );
        }

        [Test]
        public void BuildsXmlTrailingNewline()
        {
            ExcelRange fourthCell = _worksheet.Cells[4, 4];
            string xmlString = SpreadsheetIO.BuildXmlString(fourthCell);
            Assert.That(xmlString, Is.EqualTo("<p>Some text.</p><p></p>"));
        }

        [Test]
        public void BuildsXmlWithColors()
        {
            string possibleExpected =
                "<p>Head<sup><span style=\"color:#DEF123;\"> shoulders</span></sup><sup> knees</sup><span style=\"color:#ABCABC;\"> and toes</span></p>";
            string supRegex =
                "<p>Head.*<sup>.* shoulders.*</sup>.*<sup> knees</sup><span style=\"color:#ABCABC;\"> and toes</span></p>";
            string colorRegex =
                "<p>Head.*<span  style=\"color:#DEF123;\">.* shoulders.*</span>.*<sup> knees</sup><span style=\"color:#ABCABC;\"> and toes</span></p>";
            ExcelRange fifthCell = _worksheet.Cells[5, 5];
            string xmlString = SpreadsheetIO.BuildXmlString(fifthCell);
            Assert.That(xmlString.Length, Is.EqualTo(possibleExpected.Length));
            Assert.That(Regex.IsMatch(xmlString, supRegex));
        }

        [Test]
        public void BuildsXmlBloomLinebreak()
        {
            ExcelRange sixthcell = _worksheet.Cells[6, 6];
            string xmlString = SpreadsheetIO.BuildXmlString(sixthcell);
            Assert.That(
                xmlString,
                Is.EqualTo(
                    "<p>One.</p><p>Two.<span class=\"bloom-linebreak\"></span>\xfeffThree.</p><p>Four.</p>"
                )
            );
        }

        [Test]
        public void BuildsXmlNested()
        {
            //Expect this, but the tag nesting orders can be switched
            string possibleExpected =
                "<p><strong>We wish </strong><strong><em><sup>you</sup></em></strong><strong><em><sup><u> a merry </u></sup></em></strong><strong><em><sup>Chri</sup></em></strong><strong><em>stmas</em></strong></p>";

            string boldRegex =
                "<p><strong>We wish <\\/strong>.*<strong>.*you.*<\\/strong>.*<strong>.* a merry .*<\\/strong>.*<strong>.*Chri.*<\\/strong>.*<strong>.*stmas.*<\\/strong>.*</p>";
            string italicRegex =
                "<p>.*We wish .*<em>.*you.*<\\/em>.*<em>.* a merry .*<\\/em>.*<em>.*Chri.*<\\/em>.*<em>.*stmas.*<\\/em>.*</p>";
            string underlineRegex = "<p>.*We wish .*you.*<u>.* a merry .*<\\/u>.*Chri.*stmas.*</p>";
            string superscriptRegex =
                "<p>.*We wish .*<sup>.*you.*<\\/sup>.*<sup>.* a merry .*<\\/sup>.*<sup>.*Chri.*<\\/sup>.*stmas.*</p>";

            ExcelRange secondCell = _worksheet.Cells[2, 2];
            string xmlString = SpreadsheetIO.BuildXmlString(secondCell);
            Assert.That(xmlString.Length, Is.EqualTo(possibleExpected.Length));
            Assert.That(Regex.IsMatch(xmlString, boldRegex));
            Assert.That(Regex.IsMatch(xmlString, italicRegex));
            Assert.That(Regex.IsMatch(xmlString, underlineRegex));
            Assert.That(Regex.IsMatch(xmlString, superscriptRegex));
            Assert.That(IsValidXml("wrapper" + xmlString + "wrapper"));
        }

        private bool IsValidXml(string s)
        {
            var doc = SafeXmlDocument.Create();
            var wrappedXmlString = "<wrapper>" + s + "</wrapper>";
            try
            {
                doc.LoadXml(wrappedXmlString);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
