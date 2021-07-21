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
		private const int standardLeadingColumnWidth = 15;
		private const int languageColumnWidth = 30;
		private const int defaultImageWidth = 75; //width of images in pixels. Also used for default row height

		static SpreadsheetIO()
		{
			// The package requires us to do this as a way of acknowledging that we
			// accept the terms of the NonCommercial license.
			ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
		}

		public static void WriteSpreadsheet(InternalSpreadsheet spreadsheet, string outputPath)
		{
			using (var package = new ExcelPackage())
			{
				var worksheet = package.Workbook.Worksheets.Add("BloomBook");

				worksheet.DefaultColWidth = languageColumnWidth;
				for (int i = 1; i <= spreadsheet.StandardLeadingColumns.Length; i++)
				{
					worksheet.Column(i).Width = standardLeadingColumnWidth;
				}

				var imageSourceColumn = spreadsheet.ColumnForTag(InternalSpreadsheet.ImageSourceLabel);
				var imageThumbnailColumn = spreadsheet.ColumnForTag(InternalSpreadsheet.ImageThumbnailLabel);

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

						//Embed any images in the excel file
						if (c == imageSourceColumn)
						{
							var imageSrc = row.GetCell(c).Content;

							// if this row has an image source value that is not a header 
							if (imageSrc != "" && Array.IndexOf(spreadsheet.StandardLeadingColumns, imageSrc) == -1)
							{
								var imagePath = imageSrc;
								//Images show up in the cell 1 row greater and 1 column greater than assigned
								//So this will put them in row r, column imageThumbnailColumn+1 like we want
								embedImage(imagePath, r-1, imageThumbnailColumn);
								worksheet.Row(r).Height = defaultImageWidth; //so at least most of the image is visible								
							}
						}
					}
				}
				// Review: is this helpful? Excel typically makes very small cells, so almost
				// nothing of a cell's content can be seen, and that only markup. But it also
				// starts out with very narrow cells, so WrapText makes them almost unmanageably tall.
				worksheet.Cells[1, 1, r, spreadsheet.ColumnCount].Style.WrapText = true;


				void embedImage(string imageSrcPath, int rowNum, int colNum)
				{
					try
					{
						using (System.Drawing.Image image = System.Drawing.Image.FromFile(imageSrcPath))
						{
							string imageName = imageSrcPath;
							var excelImage = worksheet.Drawings.AddPicture(imageName, image);
							excelImage.SetPosition(rowNum, 1, colNum, 1);
							var origImageHeight = excelImage.Size.Height;
							var origImageWidth = excelImage.Size.Width;
							int finalWidth = defaultImageWidth;
							int finalHeight = (int)(finalWidth * origImageHeight / origImageWidth);
							excelImage.SetSize(finalWidth, finalHeight);
						}
					}
					catch (Exception)
					{
						if (!RobustFile.Exists(imageSrcPath))
						{
							worksheet.Cells[r, imageThumbnailColumn + 1].Value = "Missing";
						}
						else
						{
							worksheet.Cells[r, imageThumbnailColumn + 1].Value = "Bad image file";
						}
					}
				}

				try
				{
					RobustFile.Delete(outputPath);
					var xlFile = new FileInfo(outputPath);
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
					var row = new ContentRow(spreadsheet);
					ReadRow(worksheet, r, colCount, row);
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
