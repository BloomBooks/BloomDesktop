using System.Collections.Generic;
using System.Drawing;

namespace Bloom.Spreadsheet
{
	/// <summary>
	/// One row of an InternalSpreadsheet.
	/// </summary>
	public class SpreadsheetRow
	{
		private List<SpreadsheetCell> _cells = new List<SpreadsheetCell>();
		public InternalSpreadsheet Spreadsheet;
		public Color BackgroundColor;
		public bool Hidden;

		public virtual bool IsHeader => false;
		

		public SpreadsheetRow(InternalSpreadsheet spreadsheet)
		{
			spreadsheet.AddRow(this);
		}

		public SpreadsheetCell AddCell(string content)
		{
			var cell = new SpreadsheetCell() { Content = content };
			_cells.Add(cell);
			return cell;
		}

		public void SetCell(int index, string content)
		{
			while (_cells.Count <= index)
				AddCell("");
			_cells[index].Content = content;
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
				return label.Content.Trim();
			}
		}

		public string MetadataKey
		{
			get
			{
				return _cells[Spreadsheet.GetColumnForTag(InternalSpreadsheet.RowTypeColumnLabel)].Content;
			}
		}

		public SpreadsheetCell GetCell(int index)
		{
			if (index >= _cells.Count)
				return new SpreadsheetCell() {Content = ""};
			return _cells[index];
		}

		public SpreadsheetCell GetCell(string columnName)
		{
			return GetCell(Spreadsheet.GetColumnForTag(columnName));
		}

		public int Count => _cells.Count;
	}	
}
