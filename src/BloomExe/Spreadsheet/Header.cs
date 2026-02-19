using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bloom.Spreadsheet
{
    /// <summary>
    /// Represents the spreadsheet's header rows in a structured way
    /// </summary>
    public class Header
    {
        public InternalSpreadsheet Spreadsheet { get; set; }

        // This is private to make modifying it through the class interface more convenient than modifying it directly
        // But we don't make that a requirement. And in fact, some places already do modify it indirectly.
        private List<HeaderRow> HeaderRows { get; set; }

        public Header(InternalSpreadsheet spreadsheet)
        {
            this.Spreadsheet = spreadsheet;
            HeaderRows = new List<HeaderRow>()
            {
                new HeaderRow(Spreadsheet),
                new HeaderRow(Spreadsheet),
            };
            ColumnIdRow.Hidden = true;
        }

        public int RowCount => HeaderRows.Count;
        public int ColumnCount => HeaderRows[0].Count;

        /// <summary>
        /// Returns the first row, which contains the column IDs.
        /// </summary>
        public HeaderRow ColumnIdRow => HeaderRows[0];

        /// <summary>
        /// Returns the second row, which contains the column names.
        /// </summary>
        public HeaderRow ColumnNameRow => HeaderRows[1];

        /// <summary>
        /// Adds a column, updating both its ColumnId and ColumnName at the same time
        /// </summary>
        /// <param name="identifier">The identifier of the column, e.g. "[en]"</param>
        /// <param name="friendlyName">The friendly name of the column. This should be a user-readable string</param>
        /// <returns>A tuple containing the two cells added. {idCell} is the cell added in the first row (the ColumnId row) and {nameCell} is the cell added in the 2nd row (the ColumnName row)</returns>
        public (SpreadsheetCell idCell, SpreadsheetCell nameCell) AddColumn(
            string identifier,
            string friendlyName
        )
        {
            var idCell = ColumnIdRow.AddCell(identifier);
            var nameCell = ColumnNameRow.AddCell(friendlyName);
            return (idCell, nameCell);
        }

        /// <summary>
        /// Sets a column to the specified ColumnId and ColumnName
        /// </summary>
        /// /// <param name="index">The 0-based index of which column number to update</param>
        /// <param name="identifier">The identifier of the column, e.g. "[en]"</param>
        /// <param name="friendlyName">The friendly name of the column. This should be a user-readable string</param>
        public void SetColumn(int index, string identifier, string friendlyName)
        {
            ColumnIdRow.SetCell(index, identifier);
            ColumnNameRow.SetCell(index, friendlyName);
        }

        public void AddAdditionalHeaderRow(HeaderRow extraHeaderRow)
        {
            HeaderRows.Add(extraHeaderRow);
        }

        /// <summary>
        /// Gets the row at the specified index
        /// </summary>
        /// <param name="index">The index of the row. The index is 0-based</param>
        internal HeaderRow GetRow(int index) => HeaderRows[index];
    }
}
