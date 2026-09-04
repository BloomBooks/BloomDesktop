using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    /// Tests the generic mechanism that lets a thing on a page which the ordinary
    /// [page content] machinery cannot carry get rows of its own in a spreadsheet: a lead
    /// row with a row label of its own, carrying in the hidden [details] column the JSON
    /// needed to put the thing back, optionally followed by more rows that belong to it.
    ///
    /// Nothing here knows what any real kind of such a thing is. The fixture registers a
    /// stub kind of its own (see <see cref="StubObjectKind"/>) and checks what the exporter
    /// and importer do around it: that the [details] column appears and is hidden, that an
    /// object's rows land at the object's own position among the page's rows, that what is
    /// inside the object is not also exported as page content, that a round trip restores
    /// the object, that a page with nowhere to put the object gets a warning naming the row
    /// and is otherwise imported normally, and that a spreadsheet with no [details] column
    /// at all imports exactly as it did before any of this existed.
    /// </summary>
    public class SpreadsheetDetailsTests
    {
        static SpreadsheetDetailsTests()
        {
            // The package requires us to do this as a way of acknowledging that we
            // accept the terms of the NonCommercial license.
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        // What the stub kind's objects look like in a book: a div of its own class holding
        // one translation group per part. The label is the bit of state that only [details]
        // can carry, and the ampersand in it is deliberate: [details] holds JSON that must
        // come back byte for byte, so it must not be XML-escaped on the way through a file.
        private const string stubObjectLabel = "Wheels & Cogs";

        // The same label as it has to be written in the book's XHTML.
        private const string stubObjectLabelEscaped = "Wheels &amp; Cogs";

        private static string StubObject(string firstPart, string secondPart)
        {
            return $@"<div class=""stub-object"" data-label=""{stubObjectLabelEscaped}"">
                {TranslationGroup(firstPart + "-es", firstPart + "-en")}
                {TranslationGroup(secondPart + "-es", secondPart + "-en")}
            </div>";
        }

        private static string TranslationGroup(string spanish, string english)
        {
            return $@"<div class=""bloom-translationGroup bloom-trailingElement normal-style"">
                <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" lang=""es"" contenteditable=""true""><p>{spanish}</p></div>
                <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true""><p></p></div>
                <div class=""bloom-editable normal-style bloom-contentNational1"" lang=""en"" contenteditable=""true""><p>{english}</p></div>
            </div>";
        }

        /// <summary>
        /// The test book: a heading group, then whatever markup the caller wants in the
        /// middle, then a trailing group, all on one page. Putting the object between two
        /// ordinary groups is what lets us see whether its rows land in the right place.
        /// </summary>
        private static string MakeBook(string middleMarkup)
        {
            return MakeBookWithPages(
                TranslationGroup("Encabezado", "Heading")
                    + middleMarkup
                    + TranslationGroup("Pie", "Footing")
            );
        }

        /// <summary>
        /// A book with one "Just Text" page per argument, each page holding just the
        /// markup given for it.
        /// </summary>
        private static string MakeBookWithPages(params string[] pageMarkups)
        {
            var pages = new StringBuilder();
            for (var i = 0; i < pageMarkups.Length; i++)
            {
                pages.Append(
                    $@"
    <div class=""bloom-page numberedPage customPage A5Portrait side-right bloom-bilingual"" data-page="""" id=""3a71a95a-4b62-4890-80b6-6b5b26f1b78{i}"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""{i + 1}"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Just Text"" lang=""en"">
            Just Text
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""split-pane-component marginBox"" style="""">
            <div class=""split-pane-component-inner"">
"
                        + pageMarkups[i]
                        + @"
            </div>
        </div>
    </div>
"
                );
            }
            return @"
<!DOCTYPE html>

<html>
<head>
</head>

<body data-l1=""es"" data-l2=""en"" data-l3="""">
    <div id=""bloomDataDiv"">
        <div data-book=""bookTitle"" lang=""en"">
            <p>Details round trip</p>
        </div>
    </div>"
                + pages
                + @"
</body>
</html>
";
        }

        /// <summary>The book's pages, in order.</summary>
        private static List<SafeXmlElement> PagesOf(HtmlDom dom)
        {
            return dom
                .RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")
                .Cast<SafeXmlElement>()
                .ToList();
        }

        private StubObjectKind _kind;
        private InternalSpreadsheet _sheetFromExport;
        private HtmlDom _emptyObjectDom; // imported into a book whose object has no content
        private List<string> _warningsForBookWithoutObject;
        private HtmlDom _domWithoutObject;

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
        /// sees them, and only it can show us whether the [details] cell survived
        /// unescaped.
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
            // Registration is process-wide, so OneTimeTearDown must undo it or every later
            // spreadsheet test would see this stub kind.
            _kind = new StubObjectKind();
            SpreadsheetObjectKinds.Register(_kind);

            var bookWithObject = MakeBook(StubObject("Uno", "Dos"));
            var origDom = new HtmlDom(bookWithObject, true);
            _emptyObjectDom = new HtmlDom(MakeBook(StubObject("", "")), true);
            _domWithoutObject = new HtmlDom(MakeBook(""), true);

            // Sanity: the test books are what we think they are before the round trip.
            AssertThatXmlIn
                .Dom(origDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'stub-object')]", 1);
            Assert.That(
                origDom.RawDom.InnerXml,
                Does.Contain("Uno-es"),
                "sanity: the exported book has the object's text"
            );
            Assert.That(
                _emptyObjectDom.RawDom.InnerXml,
                Does.Not.Contain("Uno-es"),
                "sanity: the empty-object target has none of the object's text of its own"
            );
            AssertThatXmlIn
                .Dom(_domWithoutObject.RawDom)
                .HasNoMatchForXpath("//div[contains(@class,'stub-object')]");

            _sheetFromExport = ExportBook(bookWithObject);
            await RoundTripThroughFileAndImportAsync(_sheetFromExport, _emptyObjectDom);
            _warningsForBookWithoutObject = await RoundTripThroughFileAndImportAsync(
                _sheetFromExport,
                _domWithoutObject
            );
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_kind != null)
                Assert.That(
                    SpreadsheetObjectKinds.Unregister(_kind),
                    Is.True,
                    "the stub kind should still have been registered at teardown"
                );
        }

        [Test]
        public void Export_MakesTheDetailsColumn_AndHidesIt()
        {
            var detailsColumn = _sheetFromExport.GetColumnForTag(
                InternalSpreadsheet.DetailsColumnLabel
            );
            Assert.That(
                detailsColumn,
                Is.GreaterThanOrEqualTo(0),
                $"the export should have made a {InternalSpreadsheet.DetailsColumnLabel} column"
            );
            Assert.That(
                _sheetFromExport.HiddenColumns,
                Does.Contain(detailsColumn),
                $"the {InternalSpreadsheet.DetailsColumnLabel} column holds machine-made state and should be hidden"
            );
        }

        [Test]
        public void Export_PutsTheObjectsRowsAtTheObjectsPosition()
        {
            var rowLabels = _sheetFromExport.ContentRows.Select(r => r.MetadataKey).ToList();
            Assert.That(
                rowLabels,
                Is.EqualTo(
                    new[]
                    {
                        "[book title]", // from the data div, before any page
                        InternalSpreadsheet.PageContentRowLabel, // the heading group
                        StubObjectKind.LeadLabel,
                        StubObjectKind.PartLabel,
                        StubObjectKind.PartLabel,
                        InternalSpreadsheet.PageContentRowLabel, // the trailing group
                    }
                ),
                "the object's rows belong between the rows of the groups it sits between"
            );
        }

        [Test]
        public void Export_PutsTheKindAndItsStateInTheLeadRowsDetailsCell()
        {
            var leadRow = _sheetFromExport.ContentRows.First(r =>
                r.MetadataKey == StubObjectKind.LeadLabel
            );
            var details = JObject.Parse(
                leadRow.GetCell(InternalSpreadsheet.DetailsColumnLabel).Content
            );
            Assert.That(
                details["kind"]?.ToString(),
                Is.EqualTo(StubObjectKind.KindName),
                "a [details] cell should say what kind of thing it is for"
            );
            Assert.That(details["label"]?.ToString(), Is.EqualTo(stubObjectLabel));
        }

        [Test]
        public void Export_DoesNotAlsoWriteTheObjectsTextAsPageContent()
        {
            var spanishColumn = _sheetFromExport.GetRequiredColumnForLang("es");
            var pageContentRows = _sheetFromExport
                .ContentRows.Where(r => r.MetadataKey == InternalSpreadsheet.PageContentRowLabel)
                .ToList();
            Assert.That(
                pageContentRows.Count,
                Is.EqualTo(2),
                "only the two groups outside the object are page content"
            );
            Assert.That(
                pageContentRows.Select(r => r.GetCell(spanishColumn).Content),
                Is.EquivalentTo(new[] { "<p>Encabezado</p>", "<p>Pie</p>" }),
                "the groups inside the object belong to it, not to the page"
            );

            // Sanity: the object's text really was exported, just not as page content.
            var partRows = _sheetFromExport
                .ContentRows.Where(r => r.MetadataKey == StubObjectKind.PartLabel)
                .ToList();
            Assert.That(
                partRows.Select(r => r.GetCell(spanishColumn).Content),
                Is.EqualTo(new[] { "<p>Uno-es</p>", "<p>Dos-es</p>" })
            );
        }

        [Test]
        public void Import_RestoresTheObjectsTextIntoAnEmptyObject()
        {
            var editables = _emptyObjectDom
                .SafeSelectNodes(
                    "//div[contains(@class,'stub-object')]//div[contains(@class,'bloom-editable') and @lang='es']"
                )
                .Cast<SafeXmlElement>()
                .Select(e => e.InnerText.Trim())
                .ToList();
            Assert.That(
                editables,
                Is.EqualTo(new[] { "Uno-es", "Dos-es" }),
                "the spreadsheet is the authority on what is inside the object"
            );
        }

        [Test]
        public void Import_RestoresTheStateThatOnlyTheDetailsCellCarries()
        {
            var stubObject = _emptyObjectDom
                .SafeSelectNodes("//div[contains(@class,'stub-object')]")
                .Cast<SafeXmlElement>()
                .Single();
            Assert.That(
                stubObject.GetAttribute("data-label"),
                Is.EqualTo(stubObjectLabel),
                "an ampersand in a [details] cell must survive the file unescaped"
            );
        }

        [Test]
        public void Import_StillPutsThePagesOwnContentOnThePage()
        {
            Assert.That(
                _emptyObjectDom.RawDom.InnerXml,
                Does.Contain("Encabezado").And.Contain("Pie"),
                "the rows around the object should import as they always did"
            );
        }

        [Test]
        public void Import_WhenThePageHasNowhereToPutTheObject_ReportsAndSkips()
        {
            Assert.That(
                _warningsForBookWithoutObject,
                Has.Exactly(1).Contains(StubObjectKind.LeadLabel),
                "the user should be told that the object's rows could not be used"
            );
            var warning = _warningsForBookWithoutObject.First(w =>
                w.Contains(StubObjectKind.LeadLabel)
            );
            var leadRowNumber =
                _sheetFromExport.GetIndexOfRow(
                    _sheetFromExport.ContentRows.First(r =>
                        r.MetadataKey == StubObjectKind.LeadLabel
                    )
                ) + 1;
            Assert.That(
                leadRowNumber,
                Is.GreaterThan(1),
                "sanity: the lead row is not the first row of the sheet"
            );
            Assert.That(
                warning,
                Does.StartWith($"Row {leadRowNumber} is"),
                "the warning should name the spreadsheet row the user can go look at"
            );
        }

        [Test]
        public void Import_WhenThePageHasNowhereToPutTheObject_ImportsTheRestOfThePage()
        {
            AssertThatXmlIn
                .Dom(_domWithoutObject.RawDom)
                .HasNoMatchForXpath("//div[contains(@class,'stub-object')]");
            Assert.That(
                _domWithoutObject.RawDom.InnerXml,
                Does.Contain("Encabezado").And.Contain("Pie"),
                "skipping the object must not cost the page its own content"
            );
            Assert.That(
                _domWithoutObject.RawDom.InnerXml,
                Does.Not.Contain("Uno-es"),
                "there was nowhere to put the object, so its text must not have leaked onto the page"
            );
            Assert.That(
                PagesOf(_domWithoutObject).Count,
                Is.EqualTo(1),
                "a lead row in the middle of a page is skipped in place: it must not start a new page and push the footing onto it"
            );
        }

        [Test]
        public async Task Import_WhenTheBookHasNoObject_LaterRowsStillLandOnTheirOwnPages()
        {
            // A spreadsheet whose first page holds nothing but an object, then a page of
            // text, imported into a book that has no such object anywhere. The object's
            // rows have nowhere to go, but they must still hold their place in the page
            // order: the text that follows belongs on the second page, not the first, and
            // the first page must not be thrown away as unused.
            var sheet = ExportBook(
                MakeBookWithPages(StubObject("Uno", "Dos"), TranslationGroup("Segundo", "Second"))
            );
            Assert.That(
                sheet.ContentRows.Select(r => r.MetadataKey).ToList(),
                Is.EqualTo(
                    new[]
                    {
                        "[book title]",
                        StubObjectKind.LeadLabel,
                        StubObjectKind.PartLabel,
                        StubObjectKind.PartLabel,
                        InternalSpreadsheet.PageContentRowLabel,
                    }
                ),
                "sanity: an object-only page followed by a text page"
            );

            var target = new HtmlDom(
                MakeBookWithPages(
                    TranslationGroup("Primero", "First"),
                    TranslationGroup("Viejo", "Old")
                ),
                true
            );
            var warnings = await RoundTripThroughFileAndImportAsync(sheet, target);

            Assert.That(warnings, Has.Exactly(1).Contains(StubObjectKind.LeadLabel));
            var pages = PagesOf(target);
            Assert.That(pages.Count, Is.EqualTo(2), "neither page should have been thrown away");
            Assert.That(
                pages[0].InnerXml,
                Does.Contain("Primero").And.Not.Contain("Segundo"),
                "the first page had nowhere to put the object, so it must be left as it was"
            );
            Assert.That(
                pages[1].InnerXml,
                Does.Contain("Segundo").And.Not.Contain("Viejo"),
                "the text row belongs on the second page"
            );
        }

        [Test]
        public async Task Import_OfAnObjectOnlyPage_IntoABookWithNoObject_KeepsThatPage()
        {
            // The object's rows are the whole spreadsheet. Skipping them must still count
            // as reaching the page they were meant for, or the cleanup at the end of the
            // import would throw that page away as one the spreadsheet never got to.
            var sheet = ExportBook(MakeBookWithPages(StubObject("Uno", "Dos")));
            var target = new HtmlDom(MakeBookWithPages(TranslationGroup("Primero", "First")), true);
            var warnings = await RoundTripThroughFileAndImportAsync(sheet, target);

            Assert.That(warnings, Has.Exactly(1).Contains(StubObjectKind.LeadLabel));
            var pages = PagesOf(target);
            Assert.That(pages.Count, Is.EqualTo(1), "the page must not be thrown away");
            Assert.That(pages[0].InnerXml, Does.Contain("Primero"));
        }

        [Test]
        public async Task Import_OfASheetWithNoDetailsColumn_WorksAsItAlwaysDid()
        {
            var sheet = ExportBook(MakeBook(""));

            // Sanity: a book with none of these objects gets no [details] column at all,
            // which is exactly the shape of every spreadsheet made before the column existed.
            Assert.That(
                sheet.GetColumnForTag(InternalSpreadsheet.DetailsColumnLabel),
                Is.LessThan(0),
                "nothing needed the column, so it should not have been made"
            );
            Assert.That(
                sheet.ContentRows.Select(r => r.MetadataKey).ToList(),
                Is.EqualTo(
                    new[]
                    {
                        "[book title]",
                        InternalSpreadsheet.PageContentRowLabel,
                        InternalSpreadsheet.PageContentRowLabel,
                    }
                )
            );

            var target = new HtmlDom(MakeBook(""), true);
            var warnings = await RoundTripThroughFileAndImportAsync(sheet, target);
            Assert.That(warnings, Is.Empty, string.Join("; ", warnings));
            Assert.That(target.RawDom.InnerXml, Does.Contain("Encabezado").And.Contain("Pie"));
        }

        [Test]
        public void ReadFromFile_LeavesTheDetailsCellExactlyAsWritten()
        {
            // The JSON in a [details] cell is copied verbatim, so text in it that happens to
            // look like one of Excel's _xNNNN_ character escapes must come back as that
            // text, not as the character it would name; and its markup characters must not
            // be XML-escaped. (EPPlus itself still rewrites the doubly-escaped form
            // _x005F_xNNNN_, which Bloom does not try to protect.)
            const string details = "{\"kind\":\"stub\",\"label\":\"_x0041_ & <b>\"}";
            var sheet = new InternalSpreadsheet();
            sheet.AddColumnForTag(
                InternalSpreadsheet.DetailsColumnLabel,
                InternalSpreadsheet.DetailsColumnFriendlyName
            );
            var row = new ContentRow(sheet);
            row.SetCell(InternalSpreadsheet.RowTypeColumnLabel, StubObjectKind.LeadLabel);
            row.SetCell(InternalSpreadsheet.DetailsColumnLabel, details);

            // Sanity: the decoding that ordinary cells get would indeed change this text.
            Assert.That(
                SpreadsheetIO.ReplaceExcelEscapedCharsAndEscapeXmlOnes(details, false),
                Is.Not.EqualTo(details)
            );

            using (var tempFile = TempFile.WithExtension("xlsx"))
            {
                sheet.WriteToFile(tempFile.Path);
                var readBack = InternalSpreadsheet.ReadFromFile(tempFile.Path);
                Assert.That(
                    readBack
                        .ContentRows.Single()
                        .GetCell(InternalSpreadsheet.DetailsColumnLabel)
                        .Content,
                    Is.EqualTo(details)
                );
            }
        }

        [Test]
        public async Task Import_OfAContinuationRowWithNoLeadRow_ReportsIt()
        {
            // A hand-made sheet holding just one part row, as a user would leave behind by
            // deleting or moving the lead row that gave it meaning.
            var sheet = new InternalSpreadsheet();
            var spanishColumn = sheet.AddColumnForLang("es", "Spanish");
            var strandedRow = new ContentRow(sheet);
            strandedRow.SetCell(InternalSpreadsheet.RowTypeColumnLabel, StubObjectKind.PartLabel);
            strandedRow.SetCell(spanishColumn, "<p>Orphan</p>");

            // Sanity: the sheet is the shape we meant to make.
            Assert.That(
                sheet.ContentRows.Select(r => r.MetadataKey).ToList(),
                Is.EqualTo(new[] { StubObjectKind.PartLabel })
            );

            var target = new HtmlDom(MakeBook(StubObject("", "")), true);
            var warnings = await new TestSpreadsheetImporter(null, target).ImportAsync(sheet);
            Assert.That(
                warnings,
                Has.Exactly(1).Contains(StubObjectKind.LeadLabel),
                "the warning should say which lead row the stranded row needed"
            );
            Assert.That(
                target.RawDom.InnerXml,
                Does.Not.Contain("Orphan"),
                "a stranded row's text has no place we could know of, so it must be dropped"
            );
        }

        /// <summary>
        /// A minimal ISpreadsheetObjectKind, existing only to exercise the generic
        /// mechanism. Its objects are divs of class "stub-object" holding one translation
        /// group per part; its lead row carries the object's data-label attribute (the bit
        /// of state no ordinary column could hold) in [details], and each part gets a row
        /// whose language columns hold that part's text.
        /// </summary>
        private class StubObjectKind : ISpreadsheetObjectKind
        {
            /// <summary>The class marking one of this kind's objects in a book.</summary>
            public const string ObjectClass = "stub-object";

            /// <summary>The "kind" this kind's [details] cells claim.</summary>
            public const string KindName = "stub";

            /// <summary>The row label of an object's lead row.</summary>
            public const string LeadLabel = "[stub object]";

            /// <summary>The row label of one part of an object.</summary>
            public const string PartLabel = "[stub part]";

            /// <summary>The "kind" of a part row's [details] cell.</summary>
            public const string PartKindName = "stub-part";

            /// <summary>See <see cref="ISpreadsheetObjectKind.Kind"/>.</summary>
            public string Kind => KindName;

            /// <summary>See <see cref="ISpreadsheetObjectKind.LeadRowLabel"/>.</summary>
            public string LeadRowLabel => LeadLabel;

            /// <summary>A part row, and nothing else, continues an object's family.</summary>
            public bool IsContinuationRow(ContentRow row)
            {
                return row.MetadataKey == PartLabel;
            }

            /// <summary>
            /// The stub objects the page holds in its own right. (Nesting is not something
            /// this stub does, so every one it finds is one of the page's own.)
            /// </summary>
            public List<SafeXmlElement> GetObjectsOnPage(SafeXmlElement page)
            {
                return page.SafeSelectNodes($".//div[contains(@class,'{ObjectClass}')]")
                    .Cast<SafeXmlElement>()
                    .ToList();
            }

            /// <summary>
            /// True if the element has a stub object among its ancestors, so that it belongs
            /// to that object rather than to the page.
            /// </summary>
            public bool IsInsideObject(SafeXmlElement element)
            {
                for (
                    var ancestor = element.ParentNode as SafeXmlElement;
                    ancestor != null;
                    ancestor = ancestor.ParentNode as SafeXmlElement
                )
                {
                    if (ancestor.HasClass(ObjectClass))
                        return true;
                }
                return false;
            }

            /// <summary>
            /// Writes the object's lead row, whose [details] cell holds the label, then one
            /// part row per translation group inside it, each carrying that group's text in
            /// the ordinary language columns.
            /// </summary>
            public void ExportObject(SafeXmlElement obj, SpreadsheetObjectExportContext context)
            {
                var leadRow = new ContentRow(context.Spreadsheet);
                context.SetPageTypeIfNeeded(leadRow);
                leadRow.SetCell(InternalSpreadsheet.RowTypeColumnLabel, LeadLabel);
                leadRow.SetCell(InternalSpreadsheet.PageNumberColumnLabel, context.PageNumber);
                leadRow.SetCell(
                    InternalSpreadsheet.DetailsColumnLabel,
                    new JObject
                    {
                        ["kind"] = KindName,
                        ["label"] = obj.GetAttribute("data-label"),
                    }.ToString(Newtonsoft.Json.Formatting.None)
                );
                leadRow.BackgroundColor = context.ColorForPage;

                var groups = GroupsOf(obj);
                for (var i = 0; i < groups.Count; i++)
                {
                    var partRow = new ContentRow(context.Spreadsheet);
                    partRow.SetCell(InternalSpreadsheet.RowTypeColumnLabel, PartLabel);
                    partRow.SetCell(InternalSpreadsheet.PageNumberColumnLabel, context.PageNumber);
                    partRow.SetCell(
                        InternalSpreadsheet.DetailsColumnLabel,
                        new JObject { ["kind"] = PartKindName, ["index"] = i }.ToString(
                            Newtonsoft.Json.Formatting.None
                        )
                    );
                    partRow.BackgroundColor = context.ColorForPage;
                    context.Exporter.WriteTranslationGroup(
                        groups[i],
                        partRow,
                        context.BookFolderPath
                    );
                }
            }

            /// <summary>
            /// Puts the label back on the target object and each part row's text into the
            /// group at that part's index, warning about a part row that has no group.
            /// </summary>
            public async Task ImportObjectAsync(
                List<ContentRow> rows,
                SpreadsheetObjectImportContext context
            )
            {
                var details = JObject.Parse(
                    rows[0].GetCell(InternalSpreadsheet.DetailsColumnLabel).Content
                );
                context.TargetElement.SetAttribute(
                    "data-label",
                    details["label"]?.ToString() ?? ""
                );
                var groups = GroupsOf(context.TargetElement);
                for (var i = 1; i < rows.Count; i++)
                {
                    context.SetRowInFamilyBeingProcessed(i);
                    var index =
                        (int?)
                            JObject.Parse(
                                rows[i].GetCell(InternalSpreadsheet.DetailsColumnLabel).Content
                            )["index"] ?? -1;
                    if (index < 0 || index >= groups.Count)
                    {
                        context.Warn(
                            $"Row {context.Importer.CurrentRowIndexForMessages} is a {PartLabel} row for a part that is not there, so it was skipped."
                        );
                        continue;
                    }
                    await context.Importer.PutRowInGroupAsync(rows[i], groups[index]);
                }
            }

            /// <summary>The translation groups that are the object's parts.</summary>
            private static List<SafeXmlElement> GroupsOf(SafeXmlElement obj)
            {
                return obj.SafeSelectNodes(".//div[contains(@class,'bloom-translationGroup')]")
                    .Cast<SafeXmlElement>()
                    .ToList();
            }
        }
    }
}
