using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Bloom.Book;
using Bloom.Spreadsheet;
using Gtk;
using NUnit.Framework;
using SIL.IO;

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
			MarkedUpText parsed1 = SpreadsheetIO.ParseXml(input);
			Assert.That(parsed1.Count, Is.EqualTo(9));
			assertHasFormatting(parsed1[0], "Bob", bolded: true, italicized: false, superscripted: false, underlined: false);
			assertHasFormatting(parsed1[1], " and", bolded: false, italicized: false, superscripted: false, underlined: false);
			assertHasFormatting(parsed1[2], "Izzy", bolded: false, italicized: true, superscripted: false, underlined: false);
			assertHasFormatting(parsed1[4], "underneath", bolded: false, italicized: false, superscripted: false, underlined: true);
			assertHasFormatting(parsed1[6], "supper", bolded: false, italicized: false, superscripted: true, underlined: false);
			assertHasFormatting(parsed1[8], "everyday.", bolded: true, italicized: true, superscripted: true, underlined: true);
		}

		[Test]
		public void ParsesFormattedXmlNested()
		{
			MarkedUpText parsed = SpreadsheetIO.ParseXml("<p><strong>Bertha and <sup>Bessy</sup></strong><sup><u>sunk</u></sup><u>under</u> the water.</p>");
			Assert.That(parsed.Count, Is.EqualTo(5));
			assertHasFormatting(parsed[0], "Bertha and ", bolded: true, italicized: false, superscripted: false, underlined: false);
			assertHasFormatting(parsed[1], "Bessy", bolded: true, italicized: false, superscripted: true, underlined: false);
			assertHasFormatting(parsed[2], "sunk", bolded: false, italicized: false, superscripted: true, underlined: true);
			assertHasFormatting(parsed[3], "under", bolded: false, italicized: false, superscripted: false, underlined: true);
			assertHasFormatting(parsed[4], " the water.", bolded: false, italicized: false, superscripted: false, underlined: false);
		}

		[Test]
		public void ParsesFormattedXmlVeryNested()
		{
			MarkedUpText parsed = SpreadsheetIO.ParseXml("<p>a<u><strong><sup>b</sup></strong><em>c</em><strong>d</strong></u>e<u>f</u></p>");
			Assert.That(parsed.Count, Is.EqualTo(6));
			assertHasFormatting(parsed[0], "a", bolded: false, italicized: false, superscripted: false, underlined: false);
			assertHasFormatting(parsed[1], "b", bolded: true, italicized: false, superscripted: true, underlined: true);
			assertHasFormatting(parsed[2], "c", bolded: false, italicized: true, superscripted: false, underlined: true);
			assertHasFormatting(parsed[3], "d", bolded: true, italicized: false, superscripted: false, underlined: true);
			assertHasFormatting(parsed[4], "e", bolded: false, italicized: false, superscripted: false, underlined: false);
			assertHasFormatting(parsed[5], "f", bolded: false, italicized: false, superscripted: false, underlined: true);
		}

		public void ParsesFormattedXml_nonXml_returnsOriginalString()
		{
			MarkedUpText parsed = SpreadsheetIO.ParseXml("An elephant walked into a bar.");
			Assert.That(parsed.Count, Is.EqualTo(1));
			assertHasFormatting(parsed[0], "An elephant walked into a bar.", bolded: false, italicized: false, superscripted: false, underlined: false);

			MarkedUpText parsed2 = SpreadsheetIO.ParseXml("<p>a<u><strong><sup>b</sup>");
			assertHasFormatting(parsed2[0], "<p>a<u><strong><sup>b</sup>", bolded: false, italicized: false, superscripted: false, underlined: false);
		}

		private void assertHasFormatting(MarkedUpTextRun textRun, string text, bool bolded, bool italicized, bool superscripted, bool underlined)
		{
			Assert.That(textRun.Text, Is.EqualTo(text));
			Assert.That(textRun.Bold, Is.EqualTo(bolded));
			Assert.That(textRun.Italic, Is.EqualTo(italicized));
			Assert.That(textRun.Superscript, Is.EqualTo(superscripted));
			Assert.That(textRun.Underlined, Is.EqualTo(underlined));
		}

		[TestCase("<div><p>1Some text</p></div>", "1Some text")]
		[TestCase("<div>\r\n\t\t<p>2Some text</p>\r\n</div>", "2Some text")]
		[TestCase("<div>\r\n\t\t<p>3Some text.</p><p>Some more.</p>\r\n</div>", "3Some text.\r\nSome more.")]
		[TestCase("<div>\r\n\t\t<p>4Some text.</p><p></p><p>Some more.</p>\r\n</div>", "4Some text.\r\n\r\nSome more.")]
		[TestCase("<div>\r\n5Some text</div>", "\r\n5Some text")]
		[TestCase("<div>\r\n<span>6Some text</span> <span>more text</span>\r\n</div>", "6Some text more text")]
		public void ParsesFormattedXmlHandlesWhitespace(string input, string expected)
		{
			var xmlresult = SpreadsheetIO.ParseXml(input);
			var result = xmlresult.ToString();
			Assert.That(result, Is.EqualTo(expected));
		}
	}
}
