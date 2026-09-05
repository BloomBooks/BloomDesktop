using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bloom;
using Bloom.Book;
using Bloom.SafeXml;
using Bloom.Spreadsheet;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OfficeOpenXml;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Spreadsheet
{
    /// <summary>
    /// Tests that a bloom-table survives a spreadsheet export → import round trip. Export
    /// gives each table on a page a [table] row followed by one [table cell] row per
    /// visible cell: the table element and every one of its cells go into the hidden
    /// [details] column as JSON, attributes and inline styles verbatim, while each cell's
    /// own content rides in the ordinary columns (the language columns for text, [image
    /// source] for a picture, [video source] for a video). Import rebuilds the table from
    /// that JSON and puts it in place of the table the target page has, then fills the
    /// cells through the same code that fills [page content] rows.
    ///
    /// Copying attributes rather than interpreting them is the point: Bloom never has to
    /// understand a column width or a border matrix, and a book imported from a
    /// spreadsheet renders correctly even if it is published without ever being opened in
    /// the Edit tab, where the read-time CSS relies on the renderer's inline grid styles.
    /// </summary>
    public class SpreadsheetTableTests
    {
        static SpreadsheetTableTests()
        {
            // The package requires us to do this as a way of acknowledging that we
            // accept the terms of the NonCommercial license.
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        // A translation group as a text cell holds one, with the two languages the test
        // book uses plus the lang="z" prototype.
        private static string TextCell(string spanish, string english, string extraAttributes = "")
        {
            return $@"<div class=""bloom-cell"" data-content-type=""text"" {extraAttributes}>
                <div class=""bloom-translationGroup bloom-trailingElement normal-style"">
                    <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" lang=""es"" contenteditable=""true""><p>{spanish}</p></div>
                    <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true""><p></p></div>
                    <div class=""bloom-editable normal-style bloom-contentNational1"" lang=""en"" contenteditable=""true""><p>{english}</p></div>
                </div>
            </div>";
        }

        // A picture cell: a bloom-canvas holding a background image canvas element, as
        // tableEditing.ts builds one.
        private const string imageCell =
            @"<div class=""bloom-cell"" data-content-type=""image"" data-pad=""4px"" data-bg=""#ffeeaa"">
                <div class=""bloom-canvas bloom-has-canvas-element bloom-leadingElement"">
                    <div class=""bloom-canvas-element bloom-backgroundImage"" style=""width:100%;height:100%;"">
                        <div class=""bloom-imageContainer""><img src=""flower.jpg""></img></div>
                    </div>
                </div>
            </div>";

        private const string videoCell =
            @"<div class=""bloom-cell"" data-content-type=""video"">
                <div class=""bloom-videoContainer bloom-leadingElement"">
                    <video><source src=""video/fish.mp4""></source></video>
                </div>
            </div>";

        // A cell holding a nested one-row, two-column table.
        private static string NestedTableCell()
        {
            return $@"<div class=""bloom-cell"" data-content-type=""table"">
                <div class=""bloom-table"" data-column-widths=""fill,fill"" data-row-heights=""fill"" style=""grid-template-columns: 1fr 1fr; --table-column-count: 2;"">
                    {TextCell("Dentro", "Inside")}
                    {TextCell("Tambien", "Also")}
                </div>
            </div>";
        }

        /// <summary>
        /// The test book. `tableMarkup` is what goes in the lower half of the page, so we
        /// can build the book with the table (the book that gets exported, and the
        /// same-book import target), with an empty table of the same shape (proving the
        /// spreadsheet itself carries the content), or with no table at all (proving import
        /// reports and skips).
        /// </summary>
        private static string MakeBook(string tableMarkup)
        {
            return @"
<!DOCTYPE html>

<html>
<head>
</head>

<body data-l1=""es"" data-l2=""en"" data-l3="""">
	<div id=""bloomDataDiv"">
		<div data-book=""bookTitle"" lang=""en"">
			<p>Table round trip</p>
		</div>
	</div>
    <div class=""bloom-page numberedPage customPage A5Portrait side-right bloom-bilingual"" data-page="""" id=""3a71a95a-4b62-4890-80b6-6b5b26f1b78a"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""1"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Just Text"" lang=""en"">
            Just Text
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""split-pane-component marginBox"" style="""">
            <div class=""split-pane-component-inner"">
                <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                    <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" id=""heading-es"" lang=""es"" contenteditable=""true""><p>Mi mesa</p></div>
                    <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true""><p></p></div>
                    <div class=""bloom-editable normal-style bloom-contentNational1"" id=""heading-en"" lang=""en"" contenteditable=""true""><p>My table</p></div>
                </div>
"
                + tableMarkup
                + @"
            </div>
        </div>
    </div>
</body>
</html>
";
        }

        // The table itself: three columns (one fixed at 120px), three rows, a merged cell
        // with the bloom-skip cell it covers, a picture cell, a video cell and a nested
        // table. The inline styles are what the renderer writes and what the read-time CSS
        // needs; the data-* attributes are the durable model.
        private static string MakeTable(bool withContent)
        {
            string Text(string spanish, string english, string extras = "") =>
                withContent ? TextCell(spanish, english, extras) : TextCell("", "", extras);
            var picture = withContent
                ? imageCell
                : imageCell.Replace("flower.jpg", "placeHolder.png");
            var video = withContent
                ? videoCell
                : @"<div class=""bloom-cell"" data-content-type=""video""><div class=""bloom-videoContainer bloom-leadingElement bloom-noVideoSelected""></div></div>";
            var nested = withContent
                ? NestedTableCell()
                : @"<div class=""bloom-cell"" data-content-type=""table""><div class=""bloom-table"" data-column-widths=""fill,fill"" data-row-heights=""fill"" style=""grid-template-columns: 1fr 1fr; --table-column-count: 2;""><div class=""bloom-cell"" data-content-type=""text""></div><div class=""bloom-cell"" data-content-type=""text""></div></div></div>";
            return $@"<div class=""bloom-table bloom-leadingElement"" tabindex=""0""
                     data-column-widths=""120px,fill,hug"" data-row-heights=""fill,fill,fill""
                     data-gap-x=""6px"" data-gap-y=""8px"" data-border-default=""1px solid #333""
                     style=""grid-template-columns: 120px 1fr min-content; grid-template-rows: 1fr 1fr 1fr; --table-column-count: 3; --bg: #eef;"">
                {Text("Uno", "One", @"data-align=""center"" data-corners=""{&quot;radius&quot;:4}""")}
                {picture}
                {video}
                {Text("Ancho", "Wide", @"data-span-x=""2"" style=""--span-x: 2;""")}
                <div class=""bloom-cell bloom-skip"" data-content-type=""text""></div>
                {nested}
                {Text("Tres", "Three")}
                {Text("Cuatro", "Four")}
                {Text("Cinco", "Five")}
            </div>";
        }

        private InternalSpreadsheet _sheetFromExport;
        private HtmlDom _roundtrippedDom; // imported over a copy of the exported book
        private HtmlDom _emptyTableDom; // imported into a book whose table has no content
        private List<string> _warningsForBookWithoutTable;
        private HtmlDom _domWithoutTable;

        private static InternalSpreadsheet ExportBook(string bookHtml)
        {
            var mockLangDisplayNameResolver = new Mock<ILanguageDisplayNameResolver>();
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName("en"))
                .Returns("English");
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName("es"))
                .Returns("Spanish");
            var exporter = new SpreadsheetExporter(mockLangDisplayNameResolver.Object);
            exporter.Params = new SpreadsheetExportParams();
            return exporter.Export(new HtmlDom(bookHtml, true), "fakeImagesFolderpath");
        }

        /// <summary>
        /// Writes the sheet to a real .xlsx and reads it back before importing: only the
        /// written file has the language cells flattened to text the way a real import
        /// sees them.
        /// </summary>
        private static async Task<List<string>> RoundTripThroughFileAndImportAsync(
            InternalSpreadsheet sheetFromExport,
            params HtmlDom[] targets
        )
        {
            using (var tempFile = TempFile.WithExtension("xlsx"))
            {
                sheetFromExport.WriteToFile(tempFile.Path);
                var sheet = InternalSpreadsheet.ReadFromFile(tempFile.Path);
                var warnings = new List<string>();
                foreach (var target in targets)
                    warnings.AddRange(
                        await new TestSpreadsheetImporter(null, target).ImportAsync(sheet)
                    );
                return warnings;
            }
        }

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            var bookWithTable = MakeBook(MakeTable(true));
            var origDom = new HtmlDom(bookWithTable, true);
            _roundtrippedDom = new HtmlDom(bookWithTable, true);
            _emptyTableDom = new HtmlDom(MakeBook(MakeTable(false)), true);
            _domWithoutTable = new HtmlDom(MakeBook(""), true);

            // Sanity: the test books are what we think they are before the round trip.
            AssertThatXmlIn
                .Dom(origDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-table')]",
                    2 // the table and the one nested in it
                );
            AssertThatXmlIn
                .Dom(origDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-skip')]", 1);
            Assert.That(
                origDom.RawDom.InnerXml,
                Does.Contain("Uno"),
                "sanity: the exported book has the cell text"
            );
            Assert.That(
                _emptyTableDom.RawDom.InnerXml,
                Does.Not.Contain("Uno"),
                "sanity: the empty-table target has no cell text of its own"
            );
            AssertThatXmlIn
                .Dom(_domWithoutTable.RawDom)
                .HasNoMatchForXpath("//div[contains(@class,'bloom-table')]");

            _sheetFromExport = ExportBook(bookWithTable);
            await RoundTripThroughFileAndImportAsync(
                _sheetFromExport,
                _roundtrippedDom,
                _emptyTableDom
            );
            _warningsForBookWithoutTable = await RoundTripThroughFileAndImportAsync(
                _sheetFromExport,
                _domWithoutTable
            );
        }

        private HtmlDom GetDom(string target)
        {
            switch (target)
            {
                case "roundtrip":
                    return _roundtrippedDom;
                case "emptytable":
                    return _emptyTableDom;
                default:
                    throw new ArgumentException($"unknown test dom '{target}'");
            }
        }

        private static SafeXmlElement GetTable(HtmlDom dom)
        {
            var table = dom.SafeSelectNodes("//div[contains(@class,'bloom-table')]")
                .Cast<SafeXmlElement>()
                .FirstOrDefault(t => SpreadsheetTables.ParentCellOf(t) == null);
            Assert.That(table, Is.Not.Null, "the page should still have a top-level table");
            return table;
        }

        private static SafeXmlElement GetCell(SafeXmlElement table, int row, int column)
        {
            var cells = SpreadsheetTables.CellsOf(table);
            var columnCount = SpreadsheetTables.ColumnCount(table);
            var index = row * columnCount + column;
            Assert.That(
                index,
                Is.LessThan(cells.Count),
                $"the table should have a cell at row {row}, column {column}"
            );
            return cells[index];
        }

        private static string TextOfCell(SafeXmlElement cell, string lang)
        {
            var editable = cell.SafeSelectNodes(
                    $".//div[contains(@class,'bloom-editable') and @lang='{lang}']"
                )
                .Cast<SafeXmlElement>()
                .FirstOrDefault();
            Assert.That(
                editable,
                Is.Not.Null,
                $"the cell should have a bloom-editable for '{lang}'"
            );
            return editable.InnerText.Trim();
        }

        private List<ContentRow> Rows => _sheetFromExport.ContentRows.ToList();

        private static JObject Details(ContentRow row)
        {
            var content = row.GetCell(InternalSpreadsheet.DetailsColumnLabel).Content;
            Assert.That(content, Is.Not.Null.And.Not.Empty, "the row should have details JSON");
            return JObject.Parse(content);
        }

        [Test]
        public void SheetHasTableRowThenCellRowsInOrder()
        {
            var rows = Rows;
            var tableRowIndex = rows.FindIndex(r =>
                r.MetadataKey == InternalSpreadsheet.TableRowLabel
            );
            Assert.That(tableRowIndex, Is.GreaterThan(0), "the table's row exists");
            Assert.That(
                rows[tableRowIndex - 1].MetadataKey,
                Is.EqualTo(InternalSpreadsheet.PageContentRowLabel),
                "the heading's row comes before the table, as it does on the page"
            );

            // The eight visible cells, row-major, with the nested table's own rows
            // interrupting after the cell that holds it.
            var expected = new[]
            {
                "table-cell:0,0,text",
                "table-cell:0,1,image",
                "table-cell:0,2,video",
                "table-cell:1,0,text",
                "table-cell:1,2,table",
                "nested-table:1,2",
                "table-cell:0,0,text",
                "table-cell:0,1,text",
                "table-cell:2,0,text",
                "table-cell:2,1,text",
                "table-cell:2,2,text",
            };
            var actual = new List<string>();
            for (var i = tableRowIndex + 1; i < rows.Count; i++)
            {
                var details = Details(rows[i]);
                var parent = details["parent"] as JObject;
                if (rows[i].MetadataKey == InternalSpreadsheet.TableRowLabel)
                {
                    Assert.That(parent, Is.Not.Null, "only a nested table follows the first one");
                    actual.Add($"nested-table:{parent["row"]},{parent["col"]}");
                }
                else
                {
                    Assert.That(
                        rows[i].MetadataKey,
                        Is.EqualTo(InternalSpreadsheet.TableCellRowLabel)
                    );
                    actual.Add($"table-cell:{details["row"]},{details["col"]},{details["type"]}");
                }
            }
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void SkippedCellHasNoRowButIsInTheTableDetails()
        {
            var tableRow = Rows.First(r => r.MetadataKey == InternalSpreadsheet.TableRowLabel);
            var cells = (JArray)Details(tableRow)["cells"];
            Assert.That(cells.Count, Is.EqualTo(9), "all nine grid positions are described");
            var skipped = cells
                .OfType<JObject>()
                .Single(c =>
                    ((string)c["attributes"]?["class"] ?? "").Contains(SpreadsheetTables.SkipClass)
                );
            Assert.That((int)skipped["row"], Is.EqualTo(1));
            Assert.That((int)skipped["col"], Is.EqualTo(1));
            Assert.That(
                Rows.Where(r => r.MetadataKey == InternalSpreadsheet.TableCellRowLabel)
                    .Select(Details)
                    .Where(d => d["parent"] == null)
                    .Count(d => (int)d["row"] == 1 && (int)d["col"] == 1),
                Is.EqualTo(0),
                "a covered cell gets no row of its own"
            );
        }

        [Test]
        public void TableDetailsCarryAttributesAndStylesVerbatim()
        {
            var tableRow = Rows.First(r => r.MetadataKey == InternalSpreadsheet.TableRowLabel);
            var attributes = (JObject)Details(tableRow)["attributes"];
            Assert.That((string)Details(tableRow)["kind"], Is.EqualTo("table"));
            Assert.That((string)attributes["data-column-widths"], Is.EqualTo("120px,fill,hug"));
            Assert.That((string)attributes["data-row-heights"], Is.EqualTo("fill,fill,fill"));
            Assert.That((string)attributes["data-gap-x"], Is.EqualTo("6px"));
            Assert.That((string)attributes["data-gap-y"], Is.EqualTo("8px"));
            Assert.That((string)attributes["data-border-default"], Is.EqualTo("1px solid #333"));
            Assert.That((string)attributes["tabindex"], Is.EqualTo("0"));
            var style = (string)attributes["style"];
            Assert.That(style, Does.Contain("grid-template-columns: 120px 1fr min-content"));
            Assert.That(style, Does.Contain("grid-template-rows: 1fr 1fr 1fr"));
            Assert.That(style, Does.Contain("--table-column-count: 3"));
            Assert.That(style, Does.Contain("--bg: #eef"));
        }

        [Test]
        public void CellDetailsCarryPerCellFormatting()
        {
            var tableRow = Rows.First(r => r.MetadataKey == InternalSpreadsheet.TableRowLabel);
            var cells = ((JArray)Details(tableRow)["cells"]).OfType<JObject>().ToList();
            var firstCell = (JObject)cells[0]["attributes"];
            Assert.That((string)firstCell["data-align"], Is.EqualTo("center"));
            Assert.That((string)firstCell["data-corners"], Is.EqualTo("{\"radius\":4}"));
            var pictureCell = (JObject)cells[1]["attributes"];
            Assert.That((string)pictureCell["data-pad"], Is.EqualTo("4px"));
            Assert.That((string)pictureCell["data-bg"], Is.EqualTo("#ffeeaa"));
            var mergedCell = (JObject)cells[3]["attributes"];
            Assert.That((string)mergedCell["data-span-x"], Is.EqualTo("2"));
            Assert.That((string)mergedCell["style"], Does.Contain("--span-x: 2"));
        }

        [Test]
        public void DetailsColumnIsHidden()
        {
            // Like [image source], [details] is machinery, not something a translator
            // should be invited to edit.
            var detailsColumn = _sheetFromExport.GetColumnForTag(
                InternalSpreadsheet.DetailsColumnLabel
            );
            Assert.That(detailsColumn, Is.GreaterThanOrEqualTo(0), "sanity: column exists");
            Assert.That(_sheetFromExport.HiddenColumns, Does.Contain(detailsColumn));
        }

        [Test]
        public void TableContentDoesNotAlsoAppearAsPageContent()
        {
            var pageContentRows = Rows.Where(r =>
                    r.MetadataKey == InternalSpreadsheet.PageContentRowLabel
                )
                .ToList();
            Assert.That(
                pageContentRows.Count,
                Is.EqualTo(1),
                "only the heading, outside the table, makes a [page content] row"
            );
            Assert.That(pageContentRows[0].GetCell("[es]").Content, Does.Contain("Mi mesa"));
            foreach (var row in pageContentRows)
            {
                Assert.That(row.GetCell("[es]").Content, Does.Not.Contain("Uno"));
                Assert.That(
                    row.GetCell(InternalSpreadsheet.ImageSourceColumnLabel).Content,
                    Does.Not.Contain("flower")
                );
                Assert.That(
                    row.GetCell(InternalSpreadsheet.VideoSourceColumnLabel).Content,
                    Is.Empty
                );
            }
        }

        [Test]
        public void CellTextGoesInTheLanguageColumns()
        {
            var cellRows = Rows.Where(r => r.MetadataKey == InternalSpreadsheet.TableCellRowLabel)
                .ToList();
            var firstTextRow = cellRows.First(r => (string)Details(r)["type"] == "text");
            Assert.That(firstTextRow.GetCell("[es]").Content, Does.Contain("Uno"));
            Assert.That(firstTextRow.GetCell("[en]").Content, Does.Contain("One"));

            var imageRow = cellRows.First(r => (string)Details(r)["type"] == "image");
            Assert.That(
                imageRow
                    .GetCell(InternalSpreadsheet.ImageSourceColumnLabel)
                    .Content.Replace('\\', '/'),
                Is.EqualTo("images/flower.jpg")
            );

            var videoRow = cellRows.First(r => (string)Details(r)["type"] == "video");
            Assert.That(
                videoRow.GetCell(InternalSpreadsheet.VideoSourceColumnLabel).Content,
                Is.EqualTo("video/fish.mp4")
            );
        }

        [TestCase("roundtrip")]
        [TestCase("emptytable")]
        public void TableStructureAndFormattingSurvive(string target)
        {
            var table = GetTable(GetDom(target));
            Assert.That(table.GetAttribute("data-column-widths"), Is.EqualTo("120px,fill,hug"));
            Assert.That(table.GetAttribute("data-row-heights"), Is.EqualTo("fill,fill,fill"));
            Assert.That(table.GetAttribute("data-gap-x"), Is.EqualTo("6px"));
            Assert.That(table.GetAttribute("data-gap-y"), Is.EqualTo("8px"));
            Assert.That(table.GetAttribute("data-border-default"), Is.EqualTo("1px solid #333"));
            Assert.That(table.GetAttribute("tabindex"), Is.EqualTo("0"));
            var style = table.GetAttribute("style");
            Assert.That(
                style,
                Does.Contain("grid-template-columns: 120px 1fr min-content"),
                "the renderer's inline grid style is what read-time CSS lays out with"
            );
            Assert.That(style, Does.Contain("--bg: #eef"));
            Assert.That(
                SpreadsheetTables.CellsOf(table).Count,
                Is.EqualTo(9),
                "every grid position, covered ones included, comes back"
            );
        }

        [TestCase("roundtrip")]
        [TestCase("emptytable")]
        public void MergedAndCoveredCellsSurvive(string target)
        {
            var table = GetTable(GetDom(target));
            var merged = GetCell(table, 1, 0);
            Assert.That(merged.GetAttribute("data-span-x"), Is.EqualTo("2"));
            Assert.That(merged.GetAttribute("style"), Does.Contain("--span-x: 2"));
            var covered = GetCell(table, 1, 1);
            Assert.That(
                covered.HasClass(SpreadsheetTables.SkipClass),
                Is.True,
                "the covered cell is still there, and still covered"
            );
            Assert.That(
                covered.InnerXml.Trim(),
                Is.Empty,
                "a covered cell holds nothing, so it is rebuilt empty"
            );
        }

        [TestCase("roundtrip")]
        [TestCase("emptytable")]
        public void TextCellsSurviveInBothLanguages(string target)
        {
            var table = GetTable(GetDom(target));
            Assert.That(TextOfCell(GetCell(table, 0, 0), "es"), Is.EqualTo("Uno"));
            Assert.That(TextOfCell(GetCell(table, 0, 0), "en"), Is.EqualTo("One"));
            Assert.That(TextOfCell(GetCell(table, 1, 0), "es"), Is.EqualTo("Ancho"));
            Assert.That(TextOfCell(GetCell(table, 1, 0), "en"), Is.EqualTo("Wide"));
            Assert.That(TextOfCell(GetCell(table, 2, 0), "es"), Is.EqualTo("Tres"));
            Assert.That(TextOfCell(GetCell(table, 2, 1), "en"), Is.EqualTo("Four"));
            Assert.That(TextOfCell(GetCell(table, 2, 2), "es"), Is.EqualTo("Cinco"));
            Assert.That(
                GetCell(table, 0, 0).GetAttribute("data-align"),
                Is.EqualTo("center"),
                "per-cell formatting survives alongside the text"
            );
        }

        [TestCase("roundtrip")]
        [TestCase("emptytable")]
        public void PictureCellSurvives(string target)
        {
            var cell = GetCell(GetTable(GetDom(target)), 0, 1);
            Assert.That(cell.GetAttribute("data-content-type"), Is.EqualTo("image"));
            var canvas = cell.SafeSelectNodes(".//div[contains(@class,'bloom-canvas')]")
                .Cast<SafeXmlElement>()
                .FirstOrDefault();
            Assert.That(canvas, Is.Not.Null, "a picture cell holds a bloom-canvas");
            var img = cell.SafeSelectNodes(".//img").Cast<SafeXmlElement>().FirstOrDefault();
            Assert.That(img, Is.Not.Null);
            Assert.That(img.GetAttribute("src"), Is.EqualTo("flower.jpg"));
            Assert.That(cell.GetAttribute("data-bg"), Is.EqualTo("#ffeeaa"));
        }

        [TestCase("roundtrip")]
        [TestCase("emptytable")]
        public void VideoCellSurvives(string target)
        {
            var cell = GetCell(GetTable(GetDom(target)), 0, 2);
            Assert.That(cell.GetAttribute("data-content-type"), Is.EqualTo("video"));
            var container = cell.SafeSelectNodes(".//div[contains(@class,'bloom-videoContainer')]")
                .Cast<SafeXmlElement>()
                .FirstOrDefault();
            Assert.That(container, Is.Not.Null, "a video cell holds a bloom-videoContainer");
            Assert.That(
                container.GetAttribute("class"),
                Does.Not.Contain("bloom-noVideoSelected"),
                "the cell has a video again, so it is not marked as having none"
            );
            var source = cell.SafeSelectNodes(".//source").Cast<SafeXmlElement>().FirstOrDefault();
            Assert.That(source, Is.Not.Null, "a video cell's video element has a source");
            // The src is url-encoded, as the importer writes it for a page's video too.
            Assert.That(
                UrlPathString.CreateFromUrlEncodedString(source.GetAttribute("src")).NotEncoded,
                Is.EqualTo("video/fish.mp4")
            );
        }

        [TestCase("roundtrip")]
        [TestCase("emptytable")]
        public void NestedTableSurvivesWithItsText(string target)
        {
            var cell = GetCell(GetTable(GetDom(target)), 1, 2);
            Assert.That(cell.GetAttribute("data-content-type"), Is.EqualTo("table"));
            var nested = SpreadsheetTables.FindNestedTable(cell);
            Assert.That(nested, Is.Not.Null, "the cell still holds a table");
            Assert.That(nested.GetAttribute("data-column-widths"), Is.EqualTo("fill,fill"));
            Assert.That(
                nested.GetAttribute("style"),
                Does.Contain("grid-template-columns: 1fr 1fr")
            );
            var nestedCells = SpreadsheetTables.CellsOf(nested);
            Assert.That(nestedCells.Count, Is.EqualTo(2));
            Assert.That(TextOfCell(nestedCells[0], "es"), Is.EqualTo("Dentro"));
            Assert.That(TextOfCell(nestedCells[0], "en"), Is.EqualTo("Inside"));
            Assert.That(TextOfCell(nestedCells[1], "es"), Is.EqualTo("Tambien"));
            Assert.That(TextOfCell(nestedCells[1], "en"), Is.EqualTo("Also"));
        }

        [Test]
        public void TableStaysWhereItWasOnThePage()
        {
            // Import replaces the table element but keeps its parent, so the table is still
            // the second thing in the page's content, after the heading.
            var table = GetTable(_roundtrippedDom);
            var siblings = table
                .ParentNode.ChildNodes.OfType<SafeXmlElement>()
                .Select(e => e.GetAttribute("class"))
                .ToList();
            Assert.That(siblings.Count, Is.EqualTo(2));
            Assert.That(siblings[0], Does.Contain("bloom-translationGroup"));
            Assert.That(siblings[1], Does.Contain("bloom-table"));
            Assert.That(
                _roundtrippedDom
                    .SafeSelectNodes("//div[@id='heading-es']")
                    .Cast<SafeXmlElement>()
                    .First()
                    .InnerText,
                Does.Contain("Mi mesa"),
                "the page's own content outside the table imported as usual"
            );
        }

        [Test]
        public async Task EditedTranslationLandsInTheRightCellAndLanguage()
        {
            // Change one language cell of one [table cell] row, the way a translator would,
            // and check it lands in that cell and only there.
            var sheet = ExportBook(MakeBook(MakeTable(true)));
            var rowToEdit = sheet
                .ContentRows.Where(r => r.MetadataKey == InternalSpreadsheet.TableCellRowLabel)
                .First(r =>
                    r.GetCell(InternalSpreadsheet.DetailsColumnLabel).Content.Contains("\"row\":2")
                    && r.GetCell(InternalSpreadsheet.DetailsColumnLabel)
                        .Content.Contains("\"col\":1")
                );
            Assert.That(
                rowToEdit.GetCell("[en]").Content,
                Does.Contain("Four"),
                "sanity: this is the cell we think it is before we change it"
            );
            rowToEdit.SetCell(sheet.GetRequiredColumnForLang("en"), "<p>Quatre</p>");

            var target = new HtmlDom(MakeBook(MakeTable(true)), true);
            await RoundTripThroughFileAndImportAsync(sheet, target);

            var table = GetTable(target);
            Assert.That(TextOfCell(GetCell(table, 2, 1), "en"), Is.EqualTo("Quatre"));
            Assert.That(
                TextOfCell(GetCell(table, 2, 1), "es"),
                Is.EqualTo("Cuatro"),
                "the other language of that cell is untouched"
            );
            Assert.That(
                TextOfCell(GetCell(table, 2, 0), "en"),
                Is.EqualTo("Three"),
                "the neighbouring cell is untouched"
            );
        }

        [Test]
        public void ImportIntoPageWithoutTableReportsAndSkips()
        {
            Assert.That(
                _warningsForBookWithoutTable,
                Has.Some.Contains("found no table on the page for it"),
                "importing a table into a page that has none is reported"
            );
            AssertThatXmlIn
                .Dom(_domWithoutTable.RawDom)
                .HasNoMatchForXpath("//div[contains(@class,'bloom-table')]"); // none is invented
            Assert.That(
                _domWithoutTable
                    .SafeSelectNodes("//div[@id='heading-es']")
                    .Cast<SafeXmlElement>()
                    .First()
                    .InnerText,
                Does.Contain("Mi mesa"),
                "the rest of the page still imported"
            );
        }

        [Test]
        public async Task SheetWithoutDetailsColumnLeavesTheBooksTableAlone()
        {
            // A spreadsheet from a book with no table (like any spreadsheet from an older
            // Bloom) has no [details] column, so it says nothing about tables: importing it
            // over a book that has one must not damage it.
            var targetDom = new HtmlDom(MakeBook(MakeTable(true)), true);
            var sheet = ExportBook(MakeBook(""));
            Assert.That(
                sheet.GetColumnForTag(InternalSpreadsheet.DetailsColumnLabel),
                Is.LessThan(0),
                "sanity: this sheet should have no details column"
            );
            var warnings = await RoundTripThroughFileAndImportAsync(sheet, targetDom);
            Assert.That(warnings, Is.Empty);

            var table = GetTable(targetDom);
            Assert.That(TextOfCell(GetCell(table, 0, 0), "es"), Is.EqualTo("Uno"));
            Assert.That(TextOfCell(GetCell(table, 2, 2), "en"), Is.EqualTo("Five"));
            Assert.That(table.GetAttribute("data-column-widths"), Is.EqualTo("120px,fill,hug"));
            Assert.That(
                targetDom
                    .SafeSelectNodes("//div[@id='heading-es']")
                    .Cast<SafeXmlElement>()
                    .First()
                    .InnerText,
                Does.Contain("Mi mesa"),
                "sanity: the rest of the page imported from the table-less sheet"
            );
        }

        [TestCase("")]
        [TestCase("not json {")]
        public async Task TableRowWithUnreadableDetailsLeavesTheBooksTableAlone(string details)
        {
            // The [details] column is present, but someone has blanked or mangled the cell
            // on the [table] row. Bloom must not rebuild the table from nothing (which would
            // erase the book's table); it says so and leaves the table as it was.
            var sheet = ExportBook(MakeBook(MakeTable(true)));
            var tableRow = sheet.ContentRows.First(r =>
                r.MetadataKey == InternalSpreadsheet.TableRowLabel
            );
            Assert.That(
                tableRow.GetCell(InternalSpreadsheet.DetailsColumnLabel).Content,
                Does.Contain("\"kind\""),
                "sanity: the exported details cell describes the table before we spoil it"
            );
            tableRow.SetCell(InternalSpreadsheet.DetailsColumnLabel, details);

            var targetDom = new HtmlDom(MakeBook(MakeTable(true)), true);
            var warnings = await RoundTripThroughFileAndImportAsync(sheet, targetDom);
            Assert.That(warnings, Has.Some.Contains("could not read its [details] cell"));

            var table = GetTable(targetDom);
            Assert.That(TextOfCell(GetCell(table, 0, 0), "es"), Is.EqualTo("Uno"));
            Assert.That(TextOfCell(GetCell(table, 2, 2), "en"), Is.EqualTo("Five"));
            Assert.That(table.GetAttribute("data-column-widths"), Is.EqualTo("120px,fill,hug"));
        }

        [Test]
        public async Task ImportCopiesCellImageAndVideoFilesIntoTheBook()
        {
            using (var spreadsheetFolder = new TemporaryFolder("tableSheetFolder"))
            using (var bookFolder = new TemporaryFolder("tableBookFolder"))
            {
                var imagesFolder = Path.Combine(spreadsheetFolder.Path, "images");
                Directory.CreateDirectory(imagesFolder);
                using (var bitmap = new Bitmap(100, 50))
                    bitmap.Save(Path.Combine(imagesFolder, "flower.jpg"), ImageFormat.Jpeg);
                var videoFolder = Path.Combine(spreadsheetFolder.Path, "video");
                Directory.CreateDirectory(videoFolder);
                RobustFile.WriteAllText(
                    Path.Combine(videoFolder, "fish.mp4"),
                    "not really a video"
                );

                var dom = new HtmlDom(MakeBook(MakeTable(false)), true);
                InternalSpreadsheet sheet;
                using (var tempFile = TempFile.WithExtension("xlsx"))
                {
                    _sheetFromExport.WriteToFile(tempFile.Path);
                    sheet = InternalSpreadsheet.ReadFromFile(tempFile.Path);
                }
                await new TestSpreadsheetImporter(
                    null,
                    dom,
                    spreadsheetFolder.Path,
                    bookFolder.Path
                ).ImportAsync(sheet);

                Assert.That(
                    RobustFile.Exists(Path.Combine(bookFolder.Path, "flower.jpg")),
                    "a picture cell's file lands in the book folder"
                );
                Assert.That(
                    RobustFile.Exists(Path.Combine(bookFolder.Path, "video", "fish.mp4")),
                    "a video cell's file lands in the book's video folder"
                );
                var table = GetTable(dom);
                var img = GetCell(table, 0, 1)
                    .SafeSelectNodes(".//img")
                    .Cast<SafeXmlElement>()
                    .First();
                Assert.That(img.GetAttribute("src"), Is.EqualTo("flower.jpg"));
            }
        }

        [Test]
        public void ExportStripsEditTimeArtifacts()
        {
            // prepare-for-save.ts takes these off before Bloom saves a page; we strip them
            // again so a spreadsheet made from a page that somehow still has them cannot
            // carry one session's selection or anchor names into another book.
            var messyTable = MakeTable(true)
                .Replace(
                    "class=\"bloom-table bloom-leadingElement\"",
                    "class=\"bloom-table bloom-leadingElement table--selected bloom-current-table bloom-pointer-near\" data-table-attached=\"true\""
                )
                .Replace(
                    "class=\"bloom-cell\" data-content-type=\"image\"",
                    "class=\"bloom-cell cell--selected bloom-pulse-fill\" data-content-type=\"image\" data-btable-anchor-name=\"--btable-3\""
                );
            var sheet = ExportBook(MakeBook(messyTable));
            var tableRow = sheet.ContentRows.First(r =>
                r.MetadataKey == InternalSpreadsheet.TableRowLabel
            );
            var details = tableRow.GetCell(InternalSpreadsheet.DetailsColumnLabel).Content;
            Assert.That(
                details,
                Does.Contain("bloom-leadingElement"),
                "sanity: the durable classes are still there"
            );
            foreach (
                var artifact in new[]
                {
                    "table--selected",
                    "bloom-current-table",
                    "bloom-pointer-near",
                    "data-table-attached",
                    "cell--selected",
                    "bloom-pulse-fill",
                    "data-btable-anchor-name",
                }
            )
                Assert.That(details, Does.Not.Contain(artifact));
        }

        /// <summary>
        /// A book whose pages hold nothing but a table, one page per entry in `texts`. A
        /// custom page with a single Table section, or a canvas page with nothing but a
        /// table, is a realistic layout, and such a page has no [page content] row at all,
        /// so its [table] row is the only thing that can bring the importer onto it.
        /// </summary>
        private static string MakeTableOnlyBook(params string[] texts)
        {
            var pages = texts.Select(
                (text, index) =>
                    $@"
    <div class=""bloom-page numberedPage customPage A5Portrait side-right bloom-bilingual"" data-page="""" id=""table-only-page-{index}"" data-page-number=""{index + 1}"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Custom"" lang=""en"">Custom</div>
        <div class=""pageDescription"" lang=""en""></div>
        <div class=""split-pane-component marginBox"" style="""">
            <div class=""split-pane-component-inner"">
                <div class=""bloom-table bloom-leadingElement"" data-column-widths=""{(index == 0 ? "fill,fill" : "80px,fill")}"" data-row-heights=""fill"" style=""grid-template-columns: {(index == 0 ? "1fr 1fr" : "80px 1fr")}; --table-column-count: 2;"">
                    {TextCell(text + " uno", text + " one")}
                    {TextCell(text + " dos", text + " two")}
                </div>
            </div>
        </div>
    </div>"
            );
            return @"
<!DOCTYPE html>

<html>
<head>
</head>

<body data-l1=""es"" data-l2=""en"" data-l3="""">
	<div id=""bloomDataDiv"">
		<div data-book=""bookTitle"" lang=""en""><p>Table only</p></div>
	</div>
"
                + string.Join("\n", pages)
                + @"
</body>
</html>
";
        }

        private static int CountNumberedPages(HtmlDom dom)
        {
            return dom.GetPageElements()
                .Count(p => p.GetAttribute("class").Contains("numberedPage"));
        }

        private static List<SafeXmlElement> TopLevelTablesOfPage(SafeXmlElement page)
        {
            return SpreadsheetTables.TopLevelTables(page);
        }

        [Test]
        public async Task TableOnlyPagesRoundTripIntoTheSameBook()
        {
            // Nothing on these pages makes a [page content] row, so the [table] rows have to
            // advance the importer from page to page by themselves.
            var bookHtml = MakeTableOnlyBook("Alpha", "Beta");
            var sourceDom = new HtmlDom(bookHtml, true);
            Assert.That(CountNumberedPages(sourceDom), Is.EqualTo(2), "sanity: two pages");
            Assert.That(
                sourceDom.GetPageElements().Sum(p => TopLevelTablesOfPage(p).Count),
                Is.EqualTo(2),
                "sanity: one table per page"
            );

            var sheet = ExportBook(bookHtml);
            Assert.That(
                sheet.ContentRows.Count(r =>
                    r.MetadataKey == InternalSpreadsheet.PageContentRowLabel
                ),
                Is.EqualTo(0),
                "sanity: these pages have no content outside their tables"
            );
            Assert.That(
                sheet.ContentRows.Count(r => r.MetadataKey == InternalSpreadsheet.TableRowLabel),
                Is.EqualTo(2)
            );
            // Export puts the page type on the first row it makes for a page, which here is
            // the [table] row; that is what tells import to start a new page.
            var pageTypeColumn = sheet.GetColumnForTag(InternalSpreadsheet.PageTypeColumnLabel);
            Assert.That(pageTypeColumn, Is.GreaterThanOrEqualTo(0));
            foreach (
                var tableRow in sheet.ContentRows.Where(r =>
                    r.MetadataKey == InternalSpreadsheet.TableRowLabel
                )
            )
                Assert.That(tableRow.GetCell(pageTypeColumn).Content, Is.EqualTo("Custom"));

            var target = new HtmlDom(bookHtml, true);
            var warnings = await RoundTripThroughFileAndImportAsync(sheet, target);
            Assert.That(warnings, Is.Empty);

            Assert.That(CountNumberedPages(target), Is.EqualTo(2));
            var pages = target
                .GetPageElements()
                .Where(p => p.GetAttribute("class").Contains("numberedPage"))
                .ToList();
            var firstTable = TopLevelTablesOfPage(pages[0]).Single();
            var secondTable = TopLevelTablesOfPage(pages[1]).Single();
            Assert.That(
                TextOfCell(SpreadsheetTables.CellsOf(firstTable)[0], "es"),
                Is.EqualTo("Alpha uno")
            );
            Assert.That(
                TextOfCell(SpreadsheetTables.CellsOf(firstTable)[1], "en"),
                Is.EqualTo("Alpha two")
            );
            Assert.That(
                TextOfCell(SpreadsheetTables.CellsOf(secondTable)[0], "es"),
                Is.EqualTo("Beta uno"),
                "the second page's table got the second page's rows, not the first's"
            );
            Assert.That(
                firstTable.GetAttribute("data-column-widths"),
                Is.EqualTo("fill,fill"),
                "each page's table keeps its own shape"
            );
            Assert.That(secondTable.GetAttribute("data-column-widths"), Is.EqualTo("80px,fill"));
        }

        [Test]
        public async Task UnreadableDetailsUseUpTheBooksTableSoLaterTablesLandRight()
        {
            // Two pages, a table on each. The first [table] row's details are spoiled; the
            // second's are fine and carry an edit. The first table must be left alone and
            // the second must still receive its own edit, not be skipped or land on the
            // first page's table.
            var bookHtml = MakeTableOnlyBook("Alpha", "Beta");
            var sheet = ExportBook(bookHtml);
            var tableRows = sheet
                .ContentRows.Where(r => r.MetadataKey == InternalSpreadsheet.TableRowLabel)
                .ToList();
            Assert.That(tableRows.Count, Is.EqualTo(2), "sanity: one [table] row per page");
            tableRows[0].SetCell(InternalSpreadsheet.DetailsColumnLabel, "");
            var betaCellRow = sheet.ContentRows.First(r =>
                r.MetadataKey == InternalSpreadsheet.TableCellRowLabel
                && r.GetCell("[es]").Content.Contains("Beta uno")
            );
            betaCellRow.SetCell(sheet.GetRequiredColumnForLang("es"), "<p>Beta edited</p>");

            var target = new HtmlDom(bookHtml, true);
            var warnings = await RoundTripThroughFileAndImportAsync(sheet, target);
            Assert.That(warnings, Has.Some.Contains("could not read its [details] cell"));

            var pages = target
                .GetPageElements()
                .Where(p => p.GetAttribute("class").Contains("numberedPage"))
                .ToList();
            Assert.That(pages.Count, Is.EqualTo(2), "no page was added");
            var firstTable = TopLevelTablesOfPage(pages[0]).Single();
            var secondTable = TopLevelTablesOfPage(pages[1]).Single();
            Assert.That(
                TextOfCell(SpreadsheetTables.CellsOf(firstTable)[0], "es"),
                Is.EqualTo("Alpha uno"),
                "the table with the spoiled row is untouched"
            );
            Assert.That(
                TextOfCell(SpreadsheetTables.CellsOf(secondTable)[0], "es"),
                Is.EqualTo("Beta edited"),
                "the second table still got its own rows"
            );
            Assert.That(secondTable.GetAttribute("data-column-widths"), Is.EqualTo("80px,fill"));
        }

        [Test]
        public async Task UnreadableDetailsBeyondTheBooksTablesAddNoPage()
        {
            // The sheet has two tables; the book has one. The second [table] row's details
            // are spoiled. Import must not add a page for a row it cannot use.
            var sheet = ExportBook(MakeTableOnlyBook("Alpha", "Beta"));
            var tableRows = sheet
                .ContentRows.Where(r => r.MetadataKey == InternalSpreadsheet.TableRowLabel)
                .ToList();
            Assert.That(tableRows.Count, Is.EqualTo(2), "sanity: one [table] row per page");
            tableRows[1].SetCell(InternalSpreadsheet.DetailsColumnLabel, "");

            var target = new HtmlDom(MakeTableOnlyBook("Gamma"), true);
            Assert.That(CountNumberedPages(target), Is.EqualTo(1), "sanity: one page to start");
            var warnings = await RoundTripThroughFileAndImportAsync(sheet, target);
            Assert.That(warnings, Has.Some.Contains("found no table on the page for it"));

            Assert.That(CountNumberedPages(target), Is.EqualTo(1), "no page was added");
            var table = GetTable(target);
            Assert.That(
                TextOfCell(SpreadsheetTables.CellsOf(table)[0], "es"),
                Is.EqualTo("Alpha uno"),
                "the first table still imported"
            );
        }

        [Test]
        public async Task TableOnlyPageImportsOntoAnAddedPage()
        {
            // The target book runs out of pages part way through: the second table has no
            // page to land on, so import must add one, as it does for any other row type.
            var sheet = ExportBook(MakeTableOnlyBook("Alpha", "Beta"));
            var target = new HtmlDom(MakeTableOnlyBook("Gamma"), true);
            Assert.That(CountNumberedPages(target), Is.EqualTo(1), "sanity: one page to start");

            var warnings = await RoundTripThroughFileAndImportAsync(sheet, target);
            Assert.That(warnings, Is.Empty);

            Assert.That(
                CountNumberedPages(target),
                Is.EqualTo(2),
                "a page was added for the second table"
            );
            var pages = target
                .GetPageElements()
                .Where(p => p.GetAttribute("class").Contains("numberedPage"))
                .ToList();
            Assert.That(
                TextOfCell(
                    SpreadsheetTables.CellsOf(TopLevelTablesOfPage(pages[0]).Single())[0],
                    "es"
                ),
                Is.EqualTo("Alpha uno")
            );
            var addedTable = TopLevelTablesOfPage(pages[1]).Single();
            Assert.That(
                TextOfCell(SpreadsheetTables.CellsOf(addedTable)[0], "es"),
                Is.EqualTo("Beta uno")
            );
            Assert.That(
                TextOfCell(SpreadsheetTables.CellsOf(addedTable)[1], "en"),
                Is.EqualTo("Beta two")
            );
            Assert.That(
                addedTable.GetAttribute("data-column-widths"),
                Is.EqualTo("80px,fill"),
                "the added page's table has the shape the spreadsheet described, not the shape of the page it was copied from"
            );
            Assert.That(
                target.RawDom.InnerXml,
                Does.Not.Contain("Gamma"),
                "the target book's own table content was replaced"
            );
        }

        [Test]
        public async Task TableRowForAPageWithNoTableAnywhereReportsAndSkips()
        {
            // A book with no table at all cannot receive one: there is no default page that
            // holds a table, and inventing one would be inventing a shape nothing asked for.
            var sheet = ExportBook(MakeTableOnlyBook("Alpha"));
            var target = new HtmlDom(MakeBook(""), true);
            var warnings = await RoundTripThroughFileAndImportAsync(sheet, target);

            Assert.That(warnings, Has.Some.Contains("found no table on the page for it"));
            AssertThatXmlIn
                .Dom(target.RawDom)
                .HasNoMatchForXpath("//div[contains(@class,'bloom-table')]");
        }

        [Test]
        public void StripEditOnlyStylePropertiesKeepsEverythingElse()
        {
            // A cell's anchor name is minted per session and must not be saved; the grid
            // styles beside it must come through untouched.
            var kept = SpreadsheetTables.StripEditOnlyStyleProperties(
                "anchor-name: --btable-3; --span-x: 2; --hint-top-color: red; padding: 4px"
            );
            Assert.That(kept, Is.EqualTo("--span-x: 2; padding: 4px;"));
            Assert.That(
                SpreadsheetTables.StripEditOnlyStyleProperties("anchor-name: --btable-3"),
                Is.Empty
            );
        }
    }
}
