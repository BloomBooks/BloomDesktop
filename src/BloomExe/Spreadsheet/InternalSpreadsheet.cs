using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Spreadsheet
{
	/// <summary>
	/// This class is an internal representation of a simple spreadsheet that has rows of cells
	/// each containing text. It understands that the first row and column are labels,
	/// and that a row corresponds to a bloom-translationGroup or bloom-imageContainer or
	/// data-book div if its label has a certain form, otherwise it is just commentary.
	/// It also understands that certain columns correspond to bloom-editables in a certain language,
	/// and one column may represent pictures.
	/// </summary>
	public class InternalSpreadsheet
	{
		public const string ImageIndexOnPageLabel = "(image index on page)";
		public const string ImageThumbnailLabel = "[image thumbnail]";
		public const string ImageSourceLabel = "[image source]";
		public const string ImageKeyLabel = "[image]";
		public const string TextIndexOnPageLabel = "(text index on page)";
		public const string MetadataKeyLabel = "[metadata key]";
		public const string PageNumberLabel = "[page]";
		public const string TextGroupLabel = "[textgroup]";
		private List<SpreadsheetRow> _rows = new List<SpreadsheetRow>();
		private HeaderRow _header = new HeaderRow();

		public string[] StandardLeadingColumns = new[]
		{
			MetadataKeyLabel, // what kind of data is in the row; might be book-data key or [textgroup] or [image]
			PageNumberLabel, // value from data-page-number of bloom-page
			// Todo: [page layout], // something that indicates the template for the page
			ImageIndexOnPageLabel, // for images, its index in document order
			ImageSourceLabel, // the full path of where the image comes from
			ImageThumbnailLabel, // a small version of the image embedded and displayed in the excel sheet
			TextIndexOnPageLabel, // for textgroups, its index in reading order
			// Todo: (lang slot) // L1, L2, L3, auto etc...which languages should be visible here?
		};

		public int LangCount => Languages.Count;

		public List<string> Languages
		{
			get
			{
				var result = new List<string>();
				for (int i = StandardLeadingColumns.Length; i < _header.Count; i++)
				{
					var content = _header.GetCell(i).Content.Trim();
					if (!content.StartsWith("["))
						continue;
					if (!content.EndsWith("]"))
						continue;
					// Todo: may need to skip some [] columns, if we use only that convention
					// to indicate meaningful columns.
					result.Add(content.Substring(1, content.Length - 2));
				}

				return result;
			}
		}

		public InternalSpreadsheet() : this(true) {}

		private InternalSpreadsheet(bool populateHeader)
		{
			if (populateHeader)
			{
				_rows.Add(_header);

				while (_header.Count < StandardLeadingColumns.Length)
				{
					var tag = StandardLeadingColumns[_header.Count];
					_header.AddCell(tag);
				}
			}
		}

		public IEnumerable<ContentRow> ContentRows
		{
			get
			{
				foreach (var row in _rows)
				{
					if (row is ContentRow cr)
						yield return cr;
				}
			}
		}

		public int ColumnCount => _header.Count;

		public HeaderRow Header => _header;

		public IEnumerable<SpreadsheetRow> AllRows()
		{
			return _rows;
		}

		public void AddRow(ContentRow row)
		{
			_rows.Add(row);
			row.Spreadsheet = this;
		}

		public int ColumnForLang(string lang)
		{
			string columnLabel = "[" + lang + "]";
			return ColumnForTag(columnLabel);
		}

		public int ColumnForTag(string columnLabel) {
			for (var i = 0; i < _header.Count; i++)
			{
				if (_header.GetCell(i).Content == columnLabel)
					return i;
			}

			_header.AddCell(columnLabel);
			return _header.Count - 1;
		}

		public int ColumnForPageNumber => ColumnForTag(PageNumberLabel); // or just answer 2? or cache?

		public void WriteToFile(string path)
		{
			SpreadsheetIO.WriteSpreadsheet(this, path);
		}

		public static InternalSpreadsheet ReadFromFile(string path)
		{
			var result = new InternalSpreadsheet(false);
			SpreadsheetIO.ReadSpreadsheet(result, path);
			return result;
		}
	}
}
