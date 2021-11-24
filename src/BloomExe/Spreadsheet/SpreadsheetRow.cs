using System.Collections.Generic;
using System.Drawing;

namespace Bloom.Spreadsheet
{
	/// <summary>
	/// One row of an InternalSpreadsheet.
	/// </summary>
	public class SpreadsheetRow
	{
		private List<string> _cells = new List<string>();
		public InternalSpreadsheet Spreadsheet;
		public Color BackgroundColor;
		public bool Hidden;

		public virtual bool IsHeader => false;
		

		public SpreadsheetRow(InternalSpreadsheet spreadsheet)
		{
			spreadsheet.AddRow(this);
		}

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

		public void SetCell(string columnName, string content)
		{
			int index = Spreadsheet.GetColumnForTag(columnName);
			SetCell(index, content);
		}

		public string PageNumber
		{
			get
			{
				var label = _cells[Spreadsheet.ColumnForPageNumber];
				return label.Trim();
			}
		}

		public string MetadataKey
		{
			get
			{
				return _cells[Spreadsheet.GetColumnForTag(InternalSpreadsheet.MetadataKeyColumnLabel)];
			}
		}

		public SpreadsheetCell GetCell(int index)
		{
			if (index >= _cells.Count)
				return new SpreadsheetCell() {Content = ""};
			return new SpreadsheetCell() {Content = _cells[index]};
		}

		public SpreadsheetCell GetCell(string columnName)
		{
			return GetCell(Spreadsheet.GetColumnForTag(columnName));
		}

		public int Count => _cells.Count;
	}	
}
