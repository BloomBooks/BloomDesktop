/*************************************************************************************************
  Required Notice: Copyright (C) EPPlus Software AB. 
  This software is licensed under PolyForm Noncommercial License 1.0.0 
  and may only be used for noncommercial purposes 
  https://polyformproject.org/licenses/noncommercial/1.0.0/
  A commercial license to use this software can be purchased at https://epplussoftware.com
 *************************************************************************************************/
// Actually THIS code in this file isn't under PolyForm Noncommercial License,
// it's under Bloom's usual one. But the notice above is apparently required
// to use this package. We qualify as non-commercial as a charitable organization
// (also as educational).

using System;
using System.IO;
using OfficeOpenXml;
using SIL.IO;

namespace Bloom.Spreadsheet
{
	/// <summary>
	/// Class that manages writing and reading Excel files using EPPlus library.
	/// </summary>
	public class SpreadsheetIO
	{
		static SpreadsheetIO()
		{
			// The package requires us to do this as a way of acknowledging that we
			// accept the terms of the NonCommercial license.
			ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
		}

		public static void WriteSpreadsheet(InternalSpreadsheet spreadsheet, string path)
		{
			using (var package = new ExcelPackage())
			{
				var worksheet = package.Workbook.Worksheets.Add("BloomBook");
				int r = 0;
				foreach (var row in spreadsheet.AllRows())
				{
					r++;
					for (var c = 0; c < row.Count; c++)
					{
						// Enhance: Excel complains about cells that contain pure numbers
						// but are created as strings. We could possibly tell it that cells
						// that contain simple numbers can be treated accordingly.
						// It might be helpful for some uses of the group-on-page-index
						// if Excel knew to treat them as numbers.
						worksheet.Cells[r, c+1].Value = row.GetCell(c).Content;
					}
				}
				// Review: is this helpful? Excel typically makes very small cells, so almost
				// nothing of a cell's content can be seen, and that only markup. But it also
				// starts out with very narrow cells, so WrapText makes them almost unmanageably tall.
				worksheet.Cells[1, 1, r, spreadsheet.ColumnCount].Style.WrapText = true;
				try
				{
					RobustFile.Delete(path);
					var xlFile = new FileInfo(path);
					package.SaveAs(xlFile);
				}
				catch (Exception ex)
				{
					Console.WriteLine("Writing Spreadsheet failed. Do you have it open in Excel?");
					Console.WriteLine(ex.Message);
					Console.WriteLine(ex.StackTrace);
				}
			}
		}

		public static void ReadSpreadsheet(InternalSpreadsheet spreadsheet, string path)
		{
			var info = new FileInfo(path);
			using (var package = new ExcelPackage(info))
			{
				var worksheet = package.Workbook.Worksheets[0];
				var rowCount = worksheet.Dimension.Rows;
				var colCount = worksheet.Dimension.Columns;
				// Enhance: eventually we should detect any rows that are not ContentRows,
				// and either drop them or make plain SpreadsheetRows.
				ReadRow(worksheet, 0, colCount, spreadsheet.Header);
				for (var r = 1; r < rowCount; r++)
				{
					var row = new ContentRow();
					ReadRow(worksheet, r, colCount, row);
					spreadsheet.AddRow(row);
				}
			}
		}

		private static void ReadRow(ExcelWorksheet worksheet, int rowIndex, int colCount, SpreadsheetRow row)
		{
			for (var c = 0; c < colCount; c++)
			{
				var cellContent = worksheet.Cells[rowIndex + 1, c + 1].Value ?? "";
				row.AddCell(cellContent.ToString());
			}
		}
	}
}
