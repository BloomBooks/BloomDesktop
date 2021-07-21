using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Spreadsheet
{
	/// <summary>
	/// A spreadsheet row that represents a bloom-translationGroup or bloom-imageContainer
	/// Todo: not yet clear whether we want subclasses for TG and IC
	/// </summary>
	public class ContentRow: SpreadsheetRow
	{
		public ContentRow()
		{
		}
		public ContentRow(InternalSpreadsheet sheet)
		{
			sheet.AddRow(this);
		}
	}
}
