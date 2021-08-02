using Bloom.Spreadsheet;
using NUnit.Framework;

namespace BloomTests.Spreadsheet
{
	/// <summary>
	/// This class tests parsing an Xml string into runs marked with their formatting
	/// </summary>
	public class SpreadsheetFormattingTests
	{
		[TestCase("<p><strong>Bob</strong> and<em>Izzy</em> went <u>underneath</u> the <sup>supper</sup> table <sup><u><em><strong>everyday.</strong></em></u></sup></p>")]
		[TestCase("<p><b>Bob</b> and<i>Izzy</i> went <u>underneath</u> the <sup>supper</sup> table <sup><u><em><b>everyday.</b></em></u></sup></p>")]
		public void ParsesFormatttedXmlSimple(string input)
		{
			MarkedUpText parsed1 = MarkedUpText.ParseXml(input);
			Assert.That(parsed1.Count, Is.EqualTo(9));
			assertHasFormatting(parsed1.GetRun(0), "Bob", bolded: true, italicized: false, superscripted: false, underlined: false);
			assertHasFormatting(parsed1.GetRun(1), " and", bolded: false, italicized: false, superscripted: false, underlined: false);
			assertHasFormatting(parsed1.GetRun(2), "Izzy", bolded: false, italicized: true, superscripted: false, underlined: false);
			assertHasFormatting(parsed1.GetRun(4), "underneath", bolded: false, italicized: false, superscripted: false, underlined: true);
			assertHasFormatting(parsed1.GetRun(6), "supper", bolded: false, italicized: false, superscripted: true, underlined: false);
			assertHasFormatting(parsed1.GetRun(8), "everyday.", bolded: true, italicized: true, superscripted: true, underlined: true);
		}

		[Test]
		public void ParsesFormattedXmlNested()
		{
			MarkedUpText parsed = MarkedUpText.ParseXml("<p><strong>Bertha and <sup>Bessy</sup></strong><sup><u>sunk</u></sup><u>under</u> the water.</p>");
			Assert.That(parsed.Count, Is.EqualTo(5));
			assertHasFormatting(parsed.GetRun(0), "Bertha and ", bolded: true, italicized: false, superscripted: false, underlined: false);
			assertHasFormatting(parsed.GetRun(1), "Bessy", bolded: true, italicized: false, superscripted: true, underlined: false);
			assertHasFormatting(parsed.GetRun(2), "sunk", bolded: false, italicized: false, superscripted: true, underlined: true);
			assertHasFormatting(parsed.GetRun(3), "under", bolded: false, italicized: false, superscripted: false, underlined: true);
			assertHasFormatting(parsed.GetRun(4), " the water.", bolded: false, italicized: false, superscripted: false, underlined: false);
		}

		[Test]
		public void ParsesFormattedXmlVeryNested()
		{
			MarkedUpText parsed = MarkedUpText.ParseXml("<p>  <u><strong><sup>b</sup></strong><em>c</em><strong>d</strong></u>e<u>f</u></p>");
			Assert.That(parsed.Count, Is.EqualTo(6));
			assertHasFormatting(parsed.GetRun(0), "  ", bolded: false, italicized: false, superscripted: false, underlined: false);
			assertHasFormatting(parsed.GetRun(1), "b", bolded: true, italicized: false, superscripted: true, underlined: true);
			assertHasFormatting(parsed.GetRun(2), "c", bolded: false, italicized: true, superscripted: false, underlined: true);
			assertHasFormatting(parsed.GetRun(3), "d", bolded: true, italicized: false, superscripted: false, underlined: true);
			assertHasFormatting(parsed.GetRun(4), "e", bolded: false, italicized: false, superscripted: false, underlined: false);
			assertHasFormatting(parsed.GetRun(5), "f", bolded: false, italicized: false, superscripted: false, underlined: true);
		}

		public void ParsesFormattedXml_nonXml_returnsOriginalString()
		{
			MarkedUpText parsed = MarkedUpText.ParseXml("An elephant walked into a bar.");
			Assert.That(parsed.Count, Is.EqualTo(1));
			assertHasFormatting(parsed.GetRun(0), "An elephant walked into a bar.", bolded: false, italicized: false, superscripted: false, underlined: false);

			MarkedUpText parsed2 = MarkedUpText.ParseXml("<p>a<u><strong><sup>b</sup>");
			assertHasFormatting(parsed2.GetRun(0), "<p>a<u><strong><sup>b</sup>", bolded: false, italicized: false, superscripted: false, underlined: false);
		}

		private void assertHasFormatting(MarkedUpTextRun textRun, string text, bool bolded, bool italicized, bool superscripted, bool underlined)
		{
			Assert.That(textRun.Text, Is.EqualTo(text));
			Assert.That(textRun.Bold, Is.EqualTo(bolded));
			Assert.That(textRun.Italic, Is.EqualTo(italicized));
			Assert.That(textRun.Superscript, Is.EqualTo(superscripted));
			Assert.That(textRun.Underlined, Is.EqualTo(underlined));
		}

		//[TestCase("", "")]
		//[TestCase("<div><p>1Some text</p></div>", "1Some text")]
		//[TestCase("<div>\r\n\t\t<p>2Some text</p>\r\n</div>", "2Some text")]
		//[TestCase("<div>\r\n\t\t<p>3Some text.</p><p>Some more.</p>\r\n</div>", "3Some text.\r\nSome more.")]
		//[TestCase("<div>\r\n\t\t<p>4Some text.</p><p>Some more.</p>\r\n</div>", "4Some text.\r\nSome more.")]
		//[TestCase("<div>\r\n\t\t<p>4Some text.</p>\r\n\t\t<p>Some more.</p>\r\n</div>", "4Some text.\r\nSome more.")]
		//[TestCase("<div>\r\n\t\t<p></p><p></p><p>5Some text</p>\r\n</div>", "\r\n\r\n5Some text")]
		//[TestCase("<div>\r\n\t\t<p>6Some text</p><p></p></div>", "6Some text\r\n")]
		//[TestCase(@"<div><p>7Some text.<span class=""bloom-linebreak""></span></p></div>", "7Some text.\r\n")]
		//[TestCase(@"<div><p>8Some text.<span class=""bloom-linebreak""></span>Some more.</p></div>", "8Some text.\r\nSome more.")]
		//[TestCase("<div><p>9<br></br>Some text.</p></div>", "\r\n9Some text.")]
		//[TestCase(@"<div><p>Some text.<span class=""bloom-linebreak""></span>Some more.</p></div>", "8Some text.\r\nSome more.")]
		[TestCase("", "")]
		[TestCase("<p>1Some text</p>", "1Some text")]
		[TestCase("\r\n\t\t<p>2Some text</p>\r\n", "2Some text")]
		[TestCase("\r\n\t\t<p>3Some text.</p><p>Some more.</p>\r\n", "3Some text.\r\nSome more.")]
		[TestCase("\r\n\t\t<p>4Some text.</p><p>Some more.</p>\r\n", "4Some text.\r\nSome more.")]
		[TestCase("\r\n\t\t<p>4Some text.</p>\r\n\t\t<p>Some more.</p>\r\n", "4Some text.\r\nSome more.")]
		[TestCase("\r\n\t\t<p></p><p></p><p>5Some text</p>\r\n", "\r\n\r\n5Some text")]
		[TestCase("\r\n\t\t<p>6Some text</p><p></p>", "6Some text\r\n")]
		[TestCase(@"<p>7Some text.<span class=""bloom-linebreak""></span></p>", "7Some text.\r\n")]
		[TestCase(@"<p>8Some text.<span class=""bloom-linebreak""></span>Some more.</p>", "8Some text.\r\nSome more.")]
		[TestCase("<p><br></br>9Some text.</p>", "\r\n9Some text.")]
		[TestCase(@"<p>Some text.<span class=""bloom-linebreak""></span>Some more.</p>", "Some text.\r\nSome more.")]
		public void ParsesFormattedXmlHandlesWhitespace(string input, string expected)
		{
			var xmlresult = MarkedUpText.ParseXml(input);
			var result = xmlresult.PlainText();
			Assert.That(result, Is.EqualTo(expected));
		}
	}
}
