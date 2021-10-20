using System.Collections.Generic;
using System.Drawing;
using System.Linq;

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
		public static Color AlternatingRowsColor1 = Color.FromArgb(215, 239, 242); // lighter version of Color.PowderBlue
		public static Color AlternatingRowsColor2 = Color.FromArgb(237, 249, 250); // even lighter version of Color.PowderBlue
		public static Color HiddenColor = Color.FromArgb(210, 210, 210); // light gray

		public const string MetadataKeyColumnLabel = "[metadata key]";
		public const string PageNumberColumnLabel = "[page]";
		public const string ImageThumbnailColumnLabel = "[image thumbnail]";
		public const string ImageSourceColumnLabel = "[image source]";

		public const string ImageRowLabel = "[image]";
		public const string TextGroupRowLabel = "[textgroup]";
		public const string BookTitleRowLabel = "[bookTitle]";

		private List<SpreadsheetRow> _rows = new List<SpreadsheetRow>();
		private HeaderRow _header;

		public SpreadsheetExportParams Params = new SpreadsheetExportParams();

		public string[] StandardLeadingColumns = new[]
		{
			MetadataKeyColumnLabel, // what kind of data is in the row; might be book-data key or [textgroup] or [image]
			PageNumberColumnLabel, // value from data-page-number of bloom-page
			// Todo: [page layout], // something that indicates the template for the page
			ImageSourceColumnLabel, // the full path of where the image comes from
			ImageThumbnailColumnLabel, // a small version of the image embedded and displayed in the excel sheet
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
			_header = new HeaderRow(this);
			if (populateHeader)
			{
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

		public void AddRow(SpreadsheetRow row)
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
				if (_header.GetCell(i).Content.Equals(columnLabel))
					return i;
			}

			_header.AddCell(columnLabel);
			return _header.Count - 1;
		}

		public int ColumnForPageNumber => ColumnForTag(PageNumberColumnLabel); // or just answer 2? or cache?

		public List<int> HiddenColumns => new List<int>{ColumnForTag(PageNumberColumnLabel), ColumnForTag(ImageSourceColumnLabel)};

		public void SortHiddenRowsToTheBottom()
		{
			// Needs to be a stable sort, so can't use .Sort().
			_rows = _rows.OrderBy(r => r.Hidden).ToList();
		}

		public void WriteToFile(string path)
		{
			SpreadsheetIO.WriteSpreadsheet(this, path, Params.RetainMarkup);
		}

		public static InternalSpreadsheet ReadFromFile(string path)
		{
			var result = new InternalSpreadsheet(false);
			SpreadsheetIO.ReadSpreadsheet(result, path);
			return result;
		}
	}
}
