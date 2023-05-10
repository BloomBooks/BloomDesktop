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
using OfficeOpenXml;
using OfficeOpenXml.Style;
using SIL.IO;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Bloom.web;
using System.Linq;

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

		static SpreadsheetIO()
		{
			// The package requires us to do this as a way of acknowledging that we
			// accept the terms of the NonCommercial license.
			ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
		}

		public static void WriteSpreadsheet(InternalSpreadsheet spreadsheet, string outputPath, bool retainMarkup,
			IWebSocketProgress progress = null)
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
				worksheet.Column(imageThumbnailColumn + 1).Width = defaultImageWidth / 6.88;

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
							&& IsWysiwygFormattedPair(row, c))
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
							// Since export is creating a new spreadsheet, cells we want to write will always be empty initially.
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
								worksheet.Row(r).Height =
									rowHeight * 72 / 96 + 3; //so the image is visible; height seems to be points						
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
							// Allow for image reuse even though it shouldn't happen.
							if (worksheet.Drawings.Any(xx => xx.Name == imageName))
								imageName = $"{imageName}-{rowNum}";
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
					catch (Exception ex)
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

						progress?.MessageWithoutLocalizing(errorText + ": " + imageSrcPath + ": " + ex.Message, ProgressKind.Warning);
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
				catch (IOException ex) when ((ex.HResult & 0x0000FFFF) == 32) //ERROR_SHARING_VIOLATION
				{
					Console.WriteLine("Writing Spreadsheet failed. Do you have it open in another program?");
					Console.WriteLine(ex);

					progress?.Message("Spreadsheet.SpreadsheetLocked", "",
						"Bloom could not write to the spreadsheet because another program has it locked. Do you have it open in another program?",
						ProgressKind.Error);
				}
				catch (Exception ex)
				{
					progress?.MessageWithParams("Spreadsheet.ExportFailed", "{0} is a placeholder for the exception message",
						"Export failed: {0}", ProgressKind.Error, ex.Message);
				}
			}
		}

		private static bool IsWysiwygFormattedRow(SpreadsheetRow row)
		{
			var key = row.MetadataKey;
			// At this point we're treating all content rows except the headers (and some labels) as wysiwyg
			return key != InternalSpreadsheet.RowTypeColumnLabel && key.StartsWith("[") && key.EndsWith("]");
		}

		// A list of columns that, even if in Wysiwyg rows, should not receive Wysiwyg processing.
		// They are columns that do not contain markup when exported and should not receive paragraph
		// wrappers when imported. This is the data that supports the implementation of IsWysiwygFormattedColumn.
		// We use tags like this rather than comparing the column index to the count of leading columns
		// because, on import, some of the standard columns may be missing,
		// or there may be extra columns added as comments. The basic idea is that all the language
		// data columns should get the Wysiwyg processing, which prevents HTML markup appearing in
		// exports, and prevents unexpected HTML getting imported and processed.
		// The asterisk 'language' is special because it is used for data-div stuff like [styleNumberSequence]
		// for which we don't want import to wrap paragraph tags around the content.
		// Review: is it better to try to come up with an inventory of rows like [styleNumberSequence]
		// where the * column should not be wysiwyg processed? We had a hard time previously when attempting
		// to come up with a list of rows that SHOULD be.
		private static HashSet<string> nonWysiwygColumns = new HashSet<string>(new[]
		{
			"[*]", InternalSpreadsheet.RowTypeColumnLabel,
			InternalSpreadsheet.ImageSourceColumnLabel, InternalSpreadsheet.ImageThumbnailColumnLabel,
			InternalSpreadsheet.PageNumberColumnLabel
		});

		/// <summary>
		/// Combines the tests for IsWysiwygFormattedColumn and IsWysiwygFormattedRow
		/// with any special case combinations.
		/// </summary>
		private static bool IsWysiwygFormattedPair(SpreadsheetRow row, int index)
		{
			// need special case here because when index is 0 and we are creating the row,
			// its MetadataKey may not be defined. (The first column contains labels that are
			// never wysiwyg formatted.)
			if (index == 0)
				return false;
			var colKey = row.Spreadsheet.Header.GetRow(0).GetCell(index).Content;
			var rowKey = row.MetadataKey;
			// ISBNs are wysiwyg formatted even in the * column.
			// enhance: can make a dictionary if we get more exceptions.
			if (colKey == "[*]" && rowKey == "[ISBN]")
				return true;
			return IsWysiwygFormattedColumn(row, index) && IsWysiwygFormattedRow(row);
		}

		private static bool WantXmlEscaping(SpreadsheetRow row, int index)
		{
			if (row.Spreadsheet.AllRows().Count() <= 1)
			{
				// Arbitrary. Row zero should never have special characters.
				// But, we can't do the test below if row 0 has not yet been read!
				return false;
			}

			var key = row.Spreadsheet.Header.GetRow(0).GetCell(index).Content;
			// PageType column holds what is basically the InnerText of an element;
			// the InnerText property handles any XML escaping.
			return key != InternalSpreadsheet.PageTypeColumnLabel;
		}
	

	private static bool IsWysiwygFormattedColumn(SpreadsheetRow row, int index)
		{
			var key = row.Spreadsheet.Header.GetRow(0).GetCell(index).Content;
			if (key.StartsWith("[audio ")|| key == InternalSpreadsheet.VideoSourceColumnLabel
			                             || key == InternalSpreadsheet.WidgetSourceColumnLabel
			                             || key == InternalSpreadsheet.PageTypeColumnLabel
			                             || key == InternalSpreadsheet.AttributeColumnLabel)
				return false;
			return !nonWysiwygColumns.Contains(key);
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
				// The second row is special because it contains the headers needed by IsWysiwygFormattedPair
				if (rowIndex > 1 && IsWysiwygFormattedPair(row, c))
				{
					row.AddCell(BuildXmlString(currentCell));
				}
				else
				{
					var cellContent = worksheet.Cells[rowIndex + 1, c + 1].Value ?? "";
					row.AddCell(ReplaceExcelEscapedCharsAndEscapeXmlOnes(cellContent.ToString(), WantXmlEscaping(row, c)));
				}
			}
		}

		public static string ReplaceExcelEscapedCharsAndEscapeXmlOnes(string escapedString, bool wantXmlEscaping = true)
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
			if (wantXmlEscaping)
				// Note: ampersand must be handled first! Otherwise it will modify the output of the other replaces.
				return plainString.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
			else
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
						run.Text = ReplaceExcelEscapedCharsAndEscapeXmlOnes(run.Text);
					}
					var splits = run.Text.Split(whitespaceSplitters, StringSplitOptions.None);
					string pending = "";
					foreach (var split in splits)
					{
						markedupStringBuilder.Append(pending);
						AddRunToXmlString(run, cellLevelFormatting, split, markedupStringBuilder);
						// Not something that is or might be \r\n. See comment in MarkedUpText.ParseXml()
						pending = "\n";
					}
				}
			}
			else
			{
				markedupStringBuilder.Append(ReplaceExcelEscapedCharsAndEscapeXmlOnes(rawText));
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
