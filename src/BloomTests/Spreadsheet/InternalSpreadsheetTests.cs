using System;
using System.Collections.Generic;
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
	}
}
