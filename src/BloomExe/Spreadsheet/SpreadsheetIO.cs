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

using Bloom.ImageProcessing;
using L10NSharp;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using SIL.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Bloom.web;

namespace Bloom.Spreadsheet
{
	/// <summary>
	/// Class that manages writing and reading Excel files using EPPlus library.
	/// </summary>
	public class SpreadsheetIO
	{
		private const int standardLeadingColumnWidth = 15;
		private const int languageColumnWidth = 30;
		private const int defaultImageWidth = 150; //width of images in pixels.

		public static HashSet<string> WysiwygFormattedRowKeys = new HashSet<string>()
			{ InternalSpreadsheet.PageContentRowLabel, InternalSpreadsheet.BookTitleRowLabel,
				"[licenseDescription]", "[originalContributions]", "[versionAcknowledgments]", "[originalAcknowledgments]"};

		static SpreadsheetIO()
		{
			// The package requires us to do this as a way of acknowledging that we
			// accept the terms of the NonCommercial license.
			ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
		}

		public static void WriteSpreadsheet(InternalSpreadsheet spreadsheet, string outputPath, bool retainMarkup, IWebSocketProgress progress = null)
		{
			using (var package = new ExcelPackage())
			{
				var worksheet = package.Workbook.Worksheets.Add("BloomBook");

				worksheet.DefaultColWidth = languageColumnWidth;
				for (int i = 1; i <= spreadsheet.StandardLeadingColumns.Length; i++)
				{
					worksheet.Column(i).Width = standardLeadingColumnWidth;
				}

				var imageSourceColumn = spreadsheet.GetColumnForTag(InternalSpreadsheet.ImageSourceColumnLabel);
				var imageThumbnailColumn = spreadsheet.GetColumnForTag(InternalSpreadsheet.ImageThumbnailColumnLabel);
				// Apparently the width is in some approximation of 'characters'. This empirically determined
				// conversion factor seems to do a pretty good job.
				worksheet.Column(imageThumbnailColumn + 1).Width = defaultImageWidth /6.88;

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

						var sourceCell = row.GetCell(c);
						var content = sourceCell.Content;
						// Parse xml for markdown formatting on language columns,
						// Display formatting in excel spreadsheet
						ExcelRange currentCell = worksheet.Cells[r, c + 1];
						if (!string.IsNullOrEmpty(sourceCell.Comment))
						{
							// Second arg is supposed to be the author.
							currentCell.AddComment(sourceCell.Comment, "Bloom");
						}

						if (!retainMarkup
						    && c >= spreadsheet.StandardLeadingColumns.Length
						    && IsWysiwygFormattedRow(row))
                        {
							MarkedUpText markedUpText = MarkedUpText.ParseXml(content);
							if (markedUpText.HasFormatting)
							{
								currentCell.IsRichText = true;
								foreach (MarkedUpTextRun run in markedUpText.Runs)
								{
									if (!run.Text.Equals(""))
									{
										ExcelRichText text = currentCell.RichText.Add(run.Text);
										text.Bold = run.Bold;
										text.Italic = run.Italic;
										text.UnderLine = run.Underlined;
										if (run.Superscript)
										{
											text.VerticalAlign = ExcelVerticalAlignmentFont.Superscript;
										}
									}
								}
							}
							else
							{
								currentCell.Value = markedUpText.PlainText();
							}
						}
						else
						{
							// Either the retainMarkup flag is set, or this is not book text. It could be header or leading column.
							// Generally, we just want to blast our cell content into the spreadsheet cell.
							// However, there are cases where we put an error message in an image thumbnail cell when processing the image path.
							// We don't want to overwrite these. An easy way to prevent it is to not overwrite any cell that already has content.
							// Since export is creating a new spreadsheet, cells we want to write will always be empty initiall.
							if (currentCell.Value == null)
							{
								currentCell.Value = content;
							}
						}


						//Embed any images in the excel file
						if (c == imageSourceColumn)
						{
							var imageSrc = sourceCell.Content;

							// if this row has an image source value that is not a header 
							if (imageSrc != "" && !row.IsHeader)
							{
								var sheetFolder = Path.GetDirectoryName(outputPath);
								var imagePath = Path.Combine(sheetFolder, imageSrc);
								//Images show up in the cell 1 row greater and 1 column greater than assigned
								//So this will put them in row r, column imageThumbnailColumn+1 like we want
								var rowHeight = embedImage(imagePath, r - 1, imageThumbnailColumn);
								worksheet.Row(r).Height = rowHeight * 72/96 + 3; //so the image is visible; height seems to be points						
							}
						}
					}

					if (row is HeaderRow)
					{
						using (ExcelRange rng = GetRangeForRow(worksheet, r))
							rng.Style.Font.Bold = true;
					}

					if (row.Hidden)
					{
						worksheet.Row(r).Hidden = true;
						SetBackgroundColorOfRow(worksheet, r, InternalSpreadsheet.HiddenColor);
					}
					else if (row.BackgroundColor != default(Color))
					{
						SetBackgroundColorOfRow(worksheet, r, row.BackgroundColor);
					}
				}
				worksheet.Cells[1, 1, r, spreadsheet.ColumnCount].Style.WrapText = true;


				int embedImage(string imageSrcPath, int rowNum, int colNum)
				{
					int finalHeight = 30; //  a reasonable default if we don't manage to embed an image.
					try
					{
						using (Image image = Image.FromFile(imageSrcPath))
						{
							string imageName = Path.GetFileNameWithoutExtension(imageSrcPath);
							var origImageHeight = image.Size.Height;
							var origImageWidth = image.Size.Width;
							int finalWidth = defaultImageWidth;
							finalHeight = (int)(finalWidth * origImageHeight / origImageWidth);
							var size = new Size(finalWidth, finalHeight);
							using (Image thumbnail = ImageUtils.ResizeImageIfNecessary(size, image, false))
							{
								var excelImage = worksheet.Drawings.AddPicture(imageName, thumbnail);
								excelImage.SetPosition(rowNum, 2, colNum, 2);
							}
						}
					}
					catch (Exception)
					{
						string errorText;
						if (!RobustFile.Exists(imageSrcPath))
						{
							errorText = "Missing";
						}
						else if (Path.GetExtension(imageSrcPath).ToLowerInvariant().Equals(".svg"))
						{
							errorText = "Can't display SVG";
						}
						else
						{
							errorText = "Bad image file";
						}
						progress?.MessageWithoutLocalizing(errorText + ": " + imageSrcPath);
						worksheet.Cells[r, imageThumbnailColumn + 1].Value = errorText;
					}
					return Math.Max(finalHeight, 30);
				}

				foreach (var iColumn in spreadsheet.HiddenColumns)
				{
					// This is pretty yucky... our internal spreadsheet is all 0-based, but the EPPlus library is all 1-based...
					var iColumn1Based = iColumn + 1;

					worksheet.Column(iColumn1Based).Hidden = true;
					SetBackgroundColorOfColumn(worksheet, iColumn1Based, InternalSpreadsheet.HiddenColor);
				}

				try
				{
					RobustFile.Delete(outputPath);
					var xlFile = new FileInfo(outputPath);
					package.SaveAs(xlFile);
				}
				catch (IOException ex) when ((ex.HResult & 0x0000FFFF) == 32)//ERROR_SHARING_VIOLATION
				{
					Console.WriteLine("Writing Spreadsheet failed. Do you have it open in another program?");
					Console.WriteLine(ex.Message);
					Console.WriteLine(ex.StackTrace);

					progress?.Message("Spreadsheet.SpreadsheetLocked", "",
						"Bloom could not write to the spreadsheet because another program has it locked. Do you have it open in another program?",
						ProgressKind.Error);
				}
				catch (Exception ex)
				{
					progress?.Message("Spreadsheet.ExportFailed", "",
						"Export failed: " + ex.Message,
						ProgressKind.Error);
				}
			}
		}

		private static bool IsWysiwygFormattedRow(SpreadsheetRow row)
		{
			return WysiwygFormattedRowKeys.Contains(row.MetadataKey);
		}

		private static bool IsWysiwygFormattedRow(ExcelWorksheet worksheet, int rowIndex, SpreadsheetRow row)
		{
			var metadataCol = row.Spreadsheet.GetColumnForTag(InternalSpreadsheet.RowTypeColumnLabel);
			string metadataKey = worksheet.Cells[rowIndex + 1, metadataCol + 1].Value.ToString();
			return WysiwygFormattedRowKeys.Contains(metadataKey);
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
				int numHeaderRows = spreadsheet.Header.RowCount;
				for (int i = 0; i < numHeaderRows; ++i)
				{
					ReadRow(worksheet, i, colCount, spreadsheet.Header.GetRow(i));
				}

				for (var r = numHeaderRows; r < rowCount; r++)
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
				ExcelRange currentCell = worksheet.Cells[rowIndex + 1, c + 1];
				if (c >= row.Spreadsheet.StandardLeadingColumns.Length
					&& IsWysiwygFormattedRow(worksheet, rowIndex, row))
				{
					row.AddCell(BuildXmlString(currentCell));
				}
				else
				{
					var cellContent = worksheet.Cells[rowIndex + 1, c + 1].Value ?? "";
					row.AddCell(cellContent.ToString());
				}
			}
		}

		public static string ReplaceExcelEscapedChars(string escapedString)
		{
			string plainString = escapedString;
			string pattern = "_x([0-9A-F]{4})_";
			Regex rgx = new Regex(pattern);
			Match match = rgx.Match(plainString);
			while (match.Success)
			{
				string x = match.Groups[1].Value;
				int value = Convert.ToInt32(x, 16);
				char charValue = (char)value;
				plainString = plainString.Replace(match.Value, charValue.ToString());

				match = rgx.Match(plainString);
			}
			return plainString;
		}

		public static string BuildXmlString(ExcelRange cell)
		{
			if (cell.Value == null)
			{
				return "";
			}

			string rawText = cell.Value.ToString();
			if (string.IsNullOrEmpty(rawText))
				return ""; // otherwise we'd wrap a paragraph around it, making it harder to detect empty cells.
			if (HasMarkup(rawText))
			{
				// spreadsheet was exported using the retainMarkup parameter
				return rawText;
			}

			StringBuilder markedupStringBuilder = new StringBuilder();
			var whitespaceSplitters = new string[] { "\n", "\r\n" };

			if (cell.IsRichText)
			{
				var content = cell.RichText;
				var cellLevelFormatting = cell.Style.Font;
				foreach (var run in content)
				{
					if (run.Text.Length > 0)
					{
						run.Text = ReplaceExcelEscapedChars(run.Text);
					}
					var splits = run.Text.Split(whitespaceSplitters, StringSplitOptions.None);
					string pending = "";
					foreach (var split in splits)
					{
						markedupStringBuilder.Append(pending);
						AddRunToXmlString(run, cellLevelFormatting, split, markedupStringBuilder);
						pending = "\r\n";
					}
				}
			}
			else
			{
				markedupStringBuilder.Append(ReplaceExcelEscapedChars(rawText));
			}

			StringBuilder paragraphedStringBuilder = new StringBuilder();
			var markedUpString = markedupStringBuilder.ToString();


			string[] paragraphs = markedUpString.Split(whitespaceSplitters, StringSplitOptions.None);
			if (paragraphs.Length >= 1)
			{
				paragraphedStringBuilder.Append("<p>");
				paragraphedStringBuilder.Append(paragraphs[0]);
			}
			for (int i=1; i<paragraphs.Length; i++)
			{
				// The \xfeff is not inserted by our export. Rather, it is inserted by the code in
				// BloomField.InsertLineBreak which handles shift-enter in Bloom. It is therefore
				// normal that a span with class bloom-linebreak will be exported with this invisible
				// (zero-width no-break space) character following it. We use that here to determine
				// that the Excel line-break should be imported as a bloom-linebreak rather than as
				// a transition between <p> elements.
				if (paragraphs[i].Length >= 1 && paragraphs[i][0] == '\xfeff')
				{
					paragraphedStringBuilder.Append(@"<span class=""bloom-linebreak""></span>");
				}
				else
				{
					paragraphedStringBuilder.Append("</p><p>");
				}
				paragraphedStringBuilder.Append(paragraphs[i]);
			}

			if (paragraphs.Length >= 1)
			{
				paragraphedStringBuilder.Append("</p>");
			}

			return paragraphedStringBuilder.ToString();
		}

		// Excel formatting can be at the entire cell level (e.g. the entire cell is marked italic)
		// or at the text level (e.g. some words in the cell are marked italic).
		// We detect and import both types, but if the user mixes levels for the same formatting type
		// e.g.selects the entire cell, bolds it, then selected some text within the cell and unbolds it,
		// we may get weird results, so we should tell users to use text-level formatting only
		/// <param name="formattingText">Has any text-level formatting we want this run to have. Text content does not matter.</param>
		/// <param name="cellFormatting">Has any cell-level formatting we want this run to have.</param>
		/// <param name="text">The text content of this run</param>
		/// <param name="stringBuilder">The string builder to which we are adding the xmlstring of this run
		private static void AddRunToXmlString(ExcelRichText formattingText, ExcelFont cellFormatting, string text, StringBuilder stringBuilder)
		{
			if (text.Length==0)
			{
				return;
			}

			List<string> endTags = new List<string>();
			if (formattingText.Bold || cellFormatting.Bold)
			{
				addTags("strong", endTags);
			}
			if (formattingText.Italic || cellFormatting.Italic)
			{
				addTags("em", endTags);
			}
			if (formattingText.UnderLine || cellFormatting.UnderLine)
			{
				addTags("u", endTags);
			}
			if (formattingText.VerticalAlign == ExcelVerticalAlignmentFont.Superscript
				|| cellFormatting.VerticalAlign == ExcelVerticalAlignmentFont.Superscript)
			{
				addTags("sup", endTags);
			}

			stringBuilder.Append(text);

			endTags.Reverse();
			foreach (var endTag in endTags)
			{
				stringBuilder.Append(endTag);
			}
		
			void addTags(string tagName, List<string> endTag)
			{
				stringBuilder.Append("<" + tagName + ">");
				endTag.Add("</" + tagName + ">");

			}
		}

		private static bool HasMarkup(string content)
		{
			// Anything that is bloom marked-up content is bound to have angle brackets.
			return content.IndexOf("<", StringComparison.InvariantCulture) >= 0;
		}

		private static void SetBackgroundColorOfRow(ExcelWorksheet worksheet, int iRow, Color color)
		{
			using (ExcelRange range = GetRangeForRow(worksheet, iRow))
				SetBackgroundColorOfRange(range, color);
		}

		private static void SetBackgroundColorOfColumn(ExcelWorksheet worksheet, int iColumn, Color color)
		{
			using (ExcelRange range = GetRangeForColumn(worksheet, iColumn))
				SetBackgroundColorOfRange(range, color);
		}

		private static void SetBackgroundColorOfRange(ExcelRange range, Color color)
		{ 
			range.Style.Fill.PatternType = ExcelFillStyle.Solid;
			range.Style.Fill.BackgroundColor.SetColor(color);
		}

		private static ExcelRange GetRangeForRow(ExcelWorksheet worksheet, int iRow)
		{
			return worksheet.Cells[iRow, 1, iRow, worksheet.Dimension.End.Column];
		}

		private static ExcelRange GetRangeForColumn(ExcelWorksheet worksheet, int iColumn)
		{
			return worksheet.Cells[1, iColumn, worksheet.Dimension.End.Row, iColumn];
		}
	}
}
