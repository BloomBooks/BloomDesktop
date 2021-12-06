using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Bloom.web;

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
		public const string MetadataKeyColumnFriendlyName = "Metadata Key";
		public const string PageNumberColumnLabel = "(exported page)";
		public const string PageNumberColumnFriendlyName = "Page Number";
		public const string ImageThumbnailColumnLabel = "[image thumbnail]";
		public const string ImageThumbnailColumnFriendlyName = "Image";
		public const string ImageSourceColumnLabel = "[image source]";
		public const string ImageSourceColumnFriendlyName = "Image File Path";

		public const string BlankContentIndicator = "[blank]";

		public const string BookTitleRowLabel = "[bookTitle]";
		public const string PageContentRowLabel = "[page content]";

		private List<SpreadsheetRow> _rows = new List<SpreadsheetRow>();
		public SpreadsheetExportParams Params = new SpreadsheetExportParams();

		public KeyValuePair<string, string>[] StandardLeadingColumns = new KeyValuePair<string, string>[]
		{
			new KeyValuePair<string, string>(MetadataKeyColumnLabel, MetadataKeyColumnFriendlyName), // what kind of data is in the row; might be book-data key or [textgroup] or [image]
			new KeyValuePair<string, string>(PageNumberColumnLabel, PageNumberColumnFriendlyName), // value from data-page-number of bloom-page
			// Todo: [page layout], // something that indicates the template for the page
			new KeyValuePair<string, string>(ImageSourceColumnLabel, ImageSourceColumnFriendlyName), // the full path of where the image comes from
			new KeyValuePair<string, string>(ImageThumbnailColumnLabel, ImageThumbnailColumnFriendlyName) // a small version of the image embedded and displayed in the excel sheet
			// Todo: (lang slot) // L1, L2, L3, auto etc...which languages should be visible here?
		};

		public int LangCount => Languages.Count;

		public List<string> Languages
		{
			get
			{
				var result = new List<string>();
				for (int i = StandardLeadingColumns.Length; i < Header.ColumnCount; i++)
				{
					var content = Header.ColumnIdRow.GetCell(i).Content.Trim();
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
			Header = new Header(this);
			if (populateHeader)
			{
				while (Header.ColumnCount < StandardLeadingColumns.Length)
				{
					var kvp = StandardLeadingColumns[Header.ColumnCount];
					var tag = kvp.Key;
					var friendlyName = kvp.Value;
					Header.AddColumn(tag, friendlyName);
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

		public int ColumnCount => Header.ColumnCount;

		public Header Header { get; }

		public IEnumerable<SpreadsheetRow> AllRows()
		{
			return _rows;
		}

		public void AddRow(SpreadsheetRow row)
		{
			_rows.Add(row);
			row.Spreadsheet = this;
		}

		public int GetRequiredColumnForLang(string langCode)
		{
			int columnIndex = GetOptionalColumnForLang(langCode);
			if (columnIndex < 0)
			{
				throw new ArgumentException($"GetRequiredColumnForLang({langCode}): No column exists for language \"{langCode}\"");
			}
			return columnIndex;
		}

		public int GetOptionalColumnForLang(string langCode)
		{
			string columnLabel = "[" + langCode + "]";
			return GetColumnForTag(columnLabel);
		}

		/// <summary>
		/// Gets the index for the specified column.
		/// </summary>
		/// <param name="columnLabel">The label (id) of the column to look up. (This is the value written in the first row's cell for that column</param>
		/// <remarks>This function has been changed so that if the column does not exist, it will NOT be added. That is, this is a pure, side-effect free getter.</remarks>
		/// <returns>The 0-based index of the column, or -1 if not found.</returns>
		public int GetColumnForTag(string columnLabel)
		{
			for (var i = 0; i < Header.ColumnCount; i++)
			{
				if (Header.ColumnIdRow.GetCell(i).Content.Equals(columnLabel))
					return i;
			}

			return -1;
		}

		/// <summary>
		/// Adds a column. If the column label already exists, no changes will be made nor will a new column be added
		/// </summary>
		/// <param name="langCode">The language code. It should not include surrounding brackets</param>
		/// <param name="langDisplayName">The display name of the language, which will be used as the column friendly name</param>
		/// <returns>The index of the column (0-based)</returns>

		public int AddColumnForLang(string langCode, string langDisplayName)
		{
			string columnLabel = "[" + langCode + "]";
			return AddColumnForTag(columnLabel, langDisplayName);
		}

		/// <summary>
		/// Adds a column. If the column label already exists, no changes will be made nor will a new column be added
		/// </summary>
		/// <param name="columnLabel">The label of the column</param>
		/// <param name="columnFriendlyName">The friendly name of the column</param>
		/// <returns>The index of the column (0-based)</returns>
		public int AddColumnForTag(string columnLabel, string columnFriendlyName)
		{
			for (var i = 0; i < Header.ColumnCount; i++)
			{
				if (Header.ColumnIdRow.GetCell(i).Content.Equals(columnLabel))
				{
					return i;
				}
			}
			Header.AddColumn(columnLabel, columnFriendlyName);
			return Header.ColumnCount - 1;
		}

		public int ColumnForPageNumber => GetColumnForTag(PageNumberColumnLabel); // or just answer 2? or cache?

		public List<int> HiddenColumns => new List<int>{GetColumnForTag(PageNumberColumnLabel), GetColumnForTag(ImageSourceColumnLabel)};

		public void SortHiddenContentRowsToTheBottom()
		{
			// Needs to be a stable sort, so can't use .Sort().
			_rows = _rows.OrderBy(r => r.Hidden && !r.IsHeader).ToList();
		}

		public void WriteToFile(string path, IWebSocketProgress progress = null)
		{
			SpreadsheetIO.WriteSpreadsheet(this, path, Params.RetainMarkup, progress);
		}

		public static InternalSpreadsheet ReadFromFile(string path)
		{
			var result = new InternalSpreadsheet(false);
			SpreadsheetIO.ReadSpreadsheet(result, path);
			return result;
		}
	}
}
