using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Spreadsheet
{
	/// <summary>
	/// One row of an InternalSpreadsheet.
	/// </summary>
	public class SpreadsheetRow
	{
		private List<string> _cells = new List<string>();
		public InternalSpreadsheet Spreadsheet;

		public void AddCell(string content)
		{
			_cells.Add(content);
		}

		public void SetCell(int index, string content)
		{
			while (_cells.Count <= index)
				AddCell("");
			_cells[index] = content;
		}

		public string PageNumber
		{
			get
			{
				var label = _cells[Spreadsheet.ColumnForPageNumber];
				return label.Trim();
			}
		}

		public SpreadsheetCell GetCell(int index)
		{
			if (index >= _cells.Count)
				return new SpreadsheetCell() {Content = ""};
			return new SpreadsheetCell() {Content = _cells[index]};
		}

		public int Count => _cells.Count;
	}
}
