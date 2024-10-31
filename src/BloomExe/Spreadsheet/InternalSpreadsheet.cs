using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom.Utils;
using Bloom.web;
using L10NSharp;

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

        // Column labels should not be possible to confuse with language tags.
        // See Languages property. Basically, have more than three letters and no hyphens.
        public const string RowTypeColumnLabel = "[row type]";
        public const string RowTypeColumnFriendlyName = "Row type";
        public const string PageNumberColumnLabel = "(exported page)";
        public const string PageNumberColumnFriendlyName = "Page Number";
        public const string ImageThumbnailColumnLabel = "[image thumbnail]";
        public const string ImageThumbnailColumnFriendlyName = "Image";
        public const string ImageSourceColumnLabel = "[image source]";
        public const string VideoSourceColumnLabel = "[video source]";
        public const string WidgetSourceColumnLabel = "[activities source]";
        public const string PageTypeColumnLabel = "[page type]";
        public const string AttributeColumnLabel = "[attribute]";
        public const string ImageSourceColumnFriendlyName = "Image File Path";

        public const string BlankContentIndicator = "[blank]";

        public const string BookTitleRowLabel = "[bookTitle]";
        public const string CoverImageRowLabel = "[coverImage]";
        public const string PageContentRowLabel = "[page content]";
        public const string ImageDescriptionRowLabel = "[image description]";

        private List<SpreadsheetRow> _rows = new List<SpreadsheetRow>();
        public SpreadsheetExportParams Params = new SpreadsheetExportParams();

        public KeyValuePair<string, string>[] StandardLeadingColumns = new KeyValuePair<
            string,
            string
        >[]
        {
            new KeyValuePair<string, string>(RowTypeColumnLabel, RowTypeColumnFriendlyName), // what kind of data is in the row; might be book-data key or [textgroup] or [image]
            new KeyValuePair<string, string>(PageNumberColumnLabel, PageNumberColumnFriendlyName), // value from data-page-number of bloom-page
            // Todo: [page layout], // something that indicates the template for the page
            new KeyValuePair<string, string>(ImageSourceColumnLabel, ImageSourceColumnFriendlyName), // the full path of where the image comes from
            new KeyValuePair<string, string>(
                ImageThumbnailColumnLabel,
                ImageThumbnailColumnFriendlyName
            ) // a small version of the image embedded and displayed in the excel sheet
            // Todo: (lang slot) // L1, L2, L3, auto etc...which languages should be visible here?
        };

        public int LangCount => Languages.Count;

        List<string> _languages;
        public List<string> Languages
        {
            get
            {
                if (_languages != null && _languages.Count > 0)
                    return _languages;
                var result = new List<string>();
                for (int i = StandardLeadingColumns.Length; i < Header.ColumnCount; i++)
                {
                    var content = Header.ColumnIdRow.GetCell(i).Content.Trim();
                    if (!content.StartsWith("["))
                        continue;
                    if (!content.EndsWith("]"))
                        continue;
                    // A valid language tag must be two or three letters (at least before the
                    // first hyphen). The main point of this, though, isn't to validate them,
                    // but to prevent optional special columns being treated as language codes.
                    // In particular the audio, video, and widget columns are not languages!
                    // We could test for that explicitly, but I wanted a test that (at least)
                    // would not fail if we add another special column and forget to fix this.
                    if (content.Length > 5)
                    {
                        var index = content.IndexOf('-');
                        if (index < 0 || index > 4)
                            continue;
                    }
                    // Or, we could be explicit:
                    //if (content.StartsWith("[audio"))
                    //	continue; // main audio column, audio alignment column
                    //if (content == VideoSourceColumnLabel || content == WidgetSourceColumnLabel |...)
                    //	continue;

                    var tag = content.Substring(1, content.Length - 2);
                    string langTag = MiscUtils.NormalizeLanguageTagCapitalization(tag);
                    if (langTag != tag)
                    {
                        var message =
                            $"Spreadsheet Import - Language tag {tag} was normalized to {langTag}";
                        Debug.WriteLine(message);
                        SIL.Reporting.Logger.WriteEvent(message);
                    }
                    result.Add(langTag);
                }
                _languages = result;
                return _languages;
            }
        }

        public InternalSpreadsheet()
            : this(true) { }

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
                    var addedCells = Header.AddColumn(tag, friendlyName);
                    if (tag == ImageSourceColumnLabel)
                    {
                        addedCells.nameCell.Comment = LocalizationManager.GetString(
                            "Spreadsheet.ImageSourceComment",
                            "A full path like (C:\\foo) or a path relative to the location of this spreadsheet like images\\foo.png"
                        );
                    }
                    else if (tag == ImageThumbnailColumnLabel)
                    {
                        addedCells.nameCell.Comment = LocalizationManager.GetString(
                            "Spreadsheet.ImageThumbnailComment",
                            "This is usually just so you can see what the picture is. Import is based on the path in a hidden column next to this one."
                        );
                    }
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
                throw new ArgumentException(
                    $"GetRequiredColumnForLang({langCode}): No column exists for language \"{langCode}\""
                );
            }
            return columnIndex;
        }

        public int GetOptionalColumnForLang(string langCode)
        {
            string columnLabel = "[" + langCode + "]";
            return GetColumnForTag(columnLabel);
        }

        private string AudioColumnName(string langCode)
        {
            return "[audio " + langCode + "]";
        }

        public int GetOptionalColumnForLangAudio(string langCode)
        {
            return GetColumnForTag(AudioColumnName(langCode));
        }

        private string AlignmentColumnName(string langCode)
        {
            return "[audio alignments " + langCode + "]";
        }

        public int GetOptionalColumnForAudioAlignment(string langCode)
        {
            return GetColumnForTag(AlignmentColumnName(langCode));
        }

        Regex _languageTagHeader = new Regex("^\\[[a-zA-Z0-9-]*\\]$", RegexOptions.Compiled);

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
                var content = Header.ColumnIdRow.GetCell(i).Content;
                if (content.Equals(columnLabel))
                    return i;
                if (content.Length >= 4 && _languageTagHeader.IsMatch(content)) // eg, "[fr]" or "[en-US]"
                {
                    var tag = content.Substring(1, content.Length - 2);
                    var langTag = MiscUtils.NormalizeLanguageTagCapitalization(tag);
                    if ($"[{langTag}]" == columnLabel)
                        return i;
                }
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
            string comment =
                langCode == "*"
                    ? "This column, which is for language \"*\", is used for book metadata that is the same regardless of what language is being used to read the book. This includes copyright, license URL, content language, languages of book, and original title."
                    : "Note, the real identification of this language is in a hidden row above this, e.g. [en]";
            return AddColumnForTag(columnLabel, langDisplayName, comment);
        }

        public int AddColumnForLangAudio(string langCode, string langDisplayName)
        {
            return AddColumnForTag(AudioColumnName(langCode), langDisplayName);
        }

        public int AddColumnForAudioAlignment(string langCode, string langDisplayName)
        {
            return AddColumnForTag(AlignmentColumnName(langCode), langDisplayName);
        }

        /// <summary>
        /// Adds a column. If the column label already exists, no changes will be made nor will a new column be added
        /// </summary>
        /// <param name="columnLabel">The label of the column</param>
        /// <param name="columnFriendlyName">The friendly name of the column</param>
        /// <param name="friendlyNameComment">Optional. If non-null and non-empty, sets a comment in the newly created cell for the friendly name of the column.</param>
        /// <returns>The index of the column (0-based)</returns>
        public int AddColumnForTag(
            string columnLabel,
            string columnFriendlyName,
            string friendlyNameComment = null
        )
        {
            for (var i = 0; i < Header.ColumnCount; i++)
            {
                if (Header.ColumnIdRow.GetCell(i).Content.Equals(columnLabel))
                {
                    return i;
                }
            }
            var addedCells = Header.AddColumn(columnLabel, columnFriendlyName);

            if (!string.IsNullOrEmpty(friendlyNameComment))
            {
                addedCells.nameCell.Comment = friendlyNameComment;
            }

            if (Header.ColumnIdRow.GetCell(Header.ColumnCount - 2).Content == "[*]")
            {
                foreach (var row in _rows)
                {
                    row.SwapNext(Header.ColumnCount - 2);
                }

                return Header.ColumnCount - 2;
            }

            return Header.ColumnCount - 1;
        }

        public int ColumnForPageNumber => GetColumnForTag(PageNumberColumnLabel); // or just answer 2? or cache?

        public List<int> HiddenColumns =>
            new List<int>
            {
                GetColumnForTag(PageNumberColumnLabel),
                GetColumnForTag(ImageSourceColumnLabel)
            };

        public void SortHiddenContentRowsToTheBottom()
        {
            // Needs to be a stable sort, so can't use .Sort().
            _rows = _rows.OrderBy(r => r.Hidden && !r.IsHeader).ToList();
        }

        public void WriteToFile(string path, IWebSocketProgress progress = null)
        {
            SpreadsheetIO.WriteSpreadsheet(this, path, Params.RetainMarkup, progress);
        }

        public static InternalSpreadsheet ReadFromFile(
            string path,
            IWebSocketProgress progress = null
        )
        {
            progress = progress ?? new NullWebSocketProgress();
            var result = new InternalSpreadsheet(false);
            try
            {
                SpreadsheetIO.ReadSpreadsheet(result, path);
            }
            catch (InvalidDataException e)
            {
                Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
                progress.MessageWithoutLocalizing(
                    "The input does not appear to be a valid Excel spreadsheet. Import failed.",
                    ProgressKind.Error
                );
                return null;
            }
            catch (SpreadsheetException se)
            {
                progress.MessageWithoutLocalizing(se.Message, ProgressKind.Error);
                return null;
            }
            catch (Exception e)
            {
                progress.MessageWithoutLocalizing(
                    "Something went wrong reading the input file. Import failed. " + e.Message,
                    ProgressKind.Error
                );
                return null;
            }

            return result;
        }

        public int GetIndexOfRow(SpreadsheetRow row)
        {
            return _rows.IndexOf(row);
        }
    }

    // Thrown when something goes wrong importing a spreadsheet file. The message
    // should be fully ready to show the user. We add this class so we can distinguish
    // these exceptions in catch clauses.
    class SpreadsheetException : ApplicationException
    {
        public SpreadsheetException(string message)
            : base(message) { }
    }
}
