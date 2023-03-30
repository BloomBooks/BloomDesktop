using System.Collections.Generic;
using System.Drawing;
using System.Linq;

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
				var columnForTag = Spreadsheet.GetColumnForTag(InternalSpreadsheet.RowTypeColumnLabel);
				// We give up import quite early if we can't identify this column, but this gets called even
				// earlier, during creation of the Spreadsheeet from the file, so we'll just consider the row
				// not to have a tag if we can't even figure out what column it should be in.
				if (columnForTag < 0)
					return "";
				return _cells[columnForTag].Content;
			}
		}

		public SpreadsheetCell GetCell(int index)
		{
			if (index >= _cells.Count || index < 0)
				return new SpreadsheetCell() {Content = ""};
			return _cells[index];
		}

		public SpreadsheetCell GetCell(string columnName)
		{
			return GetCell(Spreadsheet.GetColumnForTag(columnName));
		}

		// currently for testing
		internal IEnumerable<SpreadsheetCell> Cells => _cells;

		// for testing
		internal IEnumerable<string> CellContents => _cells.Select(c => c.Content);

		public int Count => _cells.Count;

		/// <summary>
		/// If there is a cell at index, move it to the next column.
		/// If there is already a cell in that column, move it back;
		/// otherwise, insert an empty one.
		/// </summary>
		/// <param name="index"></param>
		public void SwapNext(int index)
		{
			if (_cells.Count <= index)
				return;
			if (_cells.Count == index + 1)
			{
				var cell = new SpreadsheetCell() { Content = "" };
				_cells.Insert(index, cell);
			}
			else
			{
				var cell = _cells[index + 1];
				_cells.RemoveAt(index + 1);
				_cells.Insert(index, cell);
			}
		}
	}	
}
