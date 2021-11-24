using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Spreadsheet
{
	/// <summary>
	/// This is a place to put any special behavior for the very first row of the spreadsheet.
	/// So far, all the special logic for the header row has felt more at home in the main
	/// InternalSpreadsheet class. But we have a place for it if needed.
	/// </summary>
	public class HeaderRow: SpreadsheetRow
	{
		public HeaderRow(InternalSpreadsheet sheet) : base(sheet)
		{
		}

		public override bool IsHeader => true;
	}
}
