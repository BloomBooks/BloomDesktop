using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bloom.Book;
using Bloom.SafeXml;
using Bloom.Spreadsheet;
using Moq;
using NUnit.Framework;
using OfficeOpenXml;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Spreadsheet
{
    /// <summary>
    /// Tests that inline images (Word-style images inside text blocks; .bloom-inlineImage
    /// wrappers replicated into every bloom-editable of a translation group) survive a
    /// spreadsheet export → import round trip. Export gives each inline image its own
    /// [inline image] row right after its group's row: the file in the normal [image source]
    /// column, and the geometry (location, displacement, width) as JSON in the hidden
    /// [details] column, which future canvas-element rows are meant to share. The aspect
    /// ratio is not in the JSON: the importer measures the image file itself. Import
    /// reconstructs the wrappers from those parameters — whether importing over the same
    /// book or into a book that has no inline images at all. A spreadsheet without the
    /// [details] column (from an older Bloom) falls back to preserving whatever the target
    /// book already has.
    /// </summary>
    public class SpreadsheetInlineImageTests
    {
        static SpreadsheetInlineImageTests()
        {
            // The package requires us to do this as a way of acknowledging that we
            // accept the terms of the NonCommercial license.
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        // A floating (right-docked) wrapper and a bottom-docked wrapper, exactly as
        // makeInlineImageWrapper/insertInlineImage (inlineImages.ts) produce them, in every
        // editable of the group including the lang="z" prototype.
        private const string floatWrapper =
            @"<div data-bloom-inline-image-id=""ii-float"" data-inline-image-offset-basedon=""380,300"" class=""bloom-inlineImage bloom-inlineImageRight bloom-keepFirstInField bloom-preventRemoval"" contenteditable=""false"" style=""--inline-image-width: 40%; --inline-image-aspect-ratio: 800 / 600; --inline-image-offset: 24px;""><img class=""bloom-transparent"" src=""flower.jpg"" alt=""""></img></div>";

        private const string bottomWrapper =
            @"<div data-bloom-inline-image-id=""ii-bottom"" class=""bloom-inlineImage bloom-inlineImageBottom bloom-keepFirstInField bloom-preventRemoval"" contenteditable=""false"" style=""--inline-image-width: 60%; --inline-image-aspect-ratio: 4 / 3;""><img src=""fish.png"" alt=""""></img></div>";

        /// <summary>
        /// The test book, parameterized so we can build it with the inline images (the book
        /// that gets exported, and the same-book import target) or without them (the
        /// "blank book" import target, proving the spreadsheet itself carries the images).
        /// </summary>
        /// <param name="imageOnlyImg">the second group's picture; defaults to the first
        /// group's floating one, since most callers want both groups to look the same.
        /// Pass it separately to build a book (or a sheet) where only one group has a
        /// picture.</param>
        private static string MakeBook(
            string floatImg,
            string bottomImg,
            string imageOnlyImg = null
        )
        {
            imageOnlyImg = imageOnlyImg ?? floatImg;
            return @"
<!DOCTYPE html>

<html>
<head>
</head>

<body data-l1=""es"" data-l2="""" data-l3="""">
	<div id=""bloomDataDiv"">
		<div data-book=""bookTitle"" lang=""en"">
			<p>Inline image round trip</p>
		</div>
	</div>
    <div class=""bloom-page numberedPage customPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""3a71a95a-4b62-4890-80b6-6b5b26f1b78a"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""1"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Just Text"" lang=""en"">
            Just Text
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""split-pane-component marginBox"" style="""">
            <div class=""split-pane-component-inner"">
                <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                    <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" id=""groupWithTextAndImages-es"" lang=""es"" contenteditable=""true"">"
                + floatImg
                + @"
                        <p>Un perro muy valiente.</p>"
                + bottomImg
                + @"
                    </div>

                    <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true"">"
                + floatImg
                + @"
                        <p></p>"
                + bottomImg
                + @"
                    </div>

                    <div class=""bloom-editable normal-style bloom-contentNational1"" id=""groupWithTextAndImages-en"" lang=""en"" contenteditable=""true"">"
                + floatImg
                + @"
                        <p>A very bold dog.</p>"
                + bottomImg
                + @"
                    </div>
                </div>

                <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                    <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" id=""imageOnlyGroup-es"" lang=""es"" contenteditable=""true"">"
                + imageOnlyImg
                + @"
                        <p></p>
                    </div>

                    <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true"">"
                + imageOnlyImg
                + @"
                        <p></p>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body>
</html>
";
        }

        private InternalSpreadsheet _sheetFromExport;
        private HtmlDom _roundtrippedDom; // imported over a copy of the exported book
        private HtmlDom _blankBookDom; // imported into a book with no inline images

        private static InternalSpreadsheet ExportBook(string bookHtml)
        {
            var mockLangDisplayNameResolver = new Mock<ILanguageDisplayNameResolver>();
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName("en"))
                .Returns("English");
            var exporter = new SpreadsheetExporter(mockLangDisplayNameResolver.Object);
            exporter.Params = new SpreadsheetExportParams();
            return exporter.Export(new HtmlDom(bookHtml, true), "fakeImagesFolderpath");
        }

        private static async Task<InternalSpreadsheet> RoundTripThroughFileAndImportAsync(
            InternalSpreadsheet sheetFromExport,
            params HtmlDom[] targets
        )
        {
            using (var tempFile = TempFile.WithExtension("xlsx"))
            {
                sheetFromExport.WriteToFile(tempFile.Path);
                var sheet = InternalSpreadsheet.ReadFromFile(tempFile.Path);
                foreach (var target in targets)
                    await new TestSpreadsheetImporter(null, target).ImportAsync(sheet);
                return sheet;
            }
        }

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            var bookWithImages = MakeBook(floatWrapper, bottomWrapper);
            var origDom = new HtmlDom(bookWithImages, true);
            _roundtrippedDom = new HtmlDom(bookWithImages, true);
            _blankBookDom = new HtmlDom(MakeBook("", ""), true);

            // Sanity: the test books are what we think they are before the round trip.
            AssertThatXmlIn
                .Dom(origDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-inlineImage')]",
                    8
                );
            AssertThatXmlIn
                .Dom(_blankBookDom.RawDom)
                .HasNoMatchForXpath("//div[contains(@class,'bloom-inlineImage')]");

            _sheetFromExport = ExportBook(bookWithImages);
            await RoundTripThroughFileAndImportAsync(
                _sheetFromExport,
                _roundtrippedDom,
                _blankBookDom
            );
        }

        private HtmlDom GetDom(string target)
        {
            switch (target)
            {
                case "roundtrip":
                    return _roundtrippedDom;
                case "blankbook":
                    return _blankBookDom;
                default:
                    throw new ArgumentException($"unknown test dom '{target}'");
            }
        }

        private SafeXmlElement GetEditable(HtmlDom dom, string id)
        {
            var editable =
                dom.SafeSelectNodes($"//div[@id='{id}']").FirstOrDefault() as SafeXmlElement;
            Assert.That(editable, Is.Not.Null, $"editable '{id}' should survive the import");
            return editable;
        }

        private static List<SafeXmlElement> GetWrappers(SafeXmlElement editable)
        {
            return editable
                .ChildNodes.OfType<SafeXmlElement>()
                .Where(e => (" " + e.GetAttribute("class") + " ").Contains(" bloom-inlineImage "))
                .ToList();
        }

        private SafeXmlElement GetFloatWrapper(SafeXmlElement editable)
        {
            var wrapper = GetWrappers(editable)
                .FirstOrDefault(e => e.GetAttribute("class").Contains("bloom-inlineImageRight"));
            Assert.That(
                wrapper,
                Is.Not.Null,
                $"'{editable.GetAttribute("id")}' should have a right-docked inline image"
            );
            return wrapper;
        }

        [Test]
        public void SheetHasInlineImageRowsAfterTheirGroupRow()
        {
            var rows = _sheetFromExport.ContentRows.ToList();
            var group1Index = rows.FindIndex(r =>
                r.GetCell("[es]").Content.Contains("Un perro muy valiente.")
            );
            Assert.That(group1Index, Is.GreaterThanOrEqualTo(0), "first group's row exists");

            // First group: two inline images, in stacking order, directly after its row.
            Assert.That(
                rows[group1Index + 1].MetadataKey,
                Is.EqualTo(InternalSpreadsheet.InlineImageRowLabel)
            );
            Assert.That(
                rows[group1Index + 2].MetadataKey,
                Is.EqualTo(InternalSpreadsheet.InlineImageRowLabel)
            );
            Assert.That(
                rows[group1Index + 1]
                    .GetCell(InternalSpreadsheet.ImageSourceColumnLabel)
                    .Content.Replace('\\', '/'),
                Is.EqualTo("images/flower.jpg")
            );
            Assert.That(
                rows[group1Index + 1].GetCell(InternalSpreadsheet.DetailsColumnLabel).Content,
                Is.EqualTo(
                    "{\"kind\":\"inline-image\",\"location\":\"right\",\"offset\":\"24px\",\"offsetBasedOn\":\"380,300\",\"width\":\"40%\",\"transparency\":\"transparent\"}"
                )
            );
            Assert.That(
                rows[group1Index + 2]
                    .GetCell(InternalSpreadsheet.ImageSourceColumnLabel)
                    .Content.Replace('\\', '/'),
                Is.EqualTo("images/fish.png")
            );
            Assert.That(
                rows[group1Index + 2].GetCell(InternalSpreadsheet.DetailsColumnLabel).Content,
                Is.EqualTo("{\"kind\":\"inline-image\",\"location\":\"bottom\",\"width\":\"60%\"}")
            );

            // Second group (image-only): its row follows, then its one inline image.
            Assert.That(
                rows[group1Index + 3].MetadataKey,
                Is.EqualTo(InternalSpreadsheet.PageContentRowLabel)
            );
            Assert.That(
                rows[group1Index + 4].MetadataKey,
                Is.EqualTo(InternalSpreadsheet.InlineImageRowLabel)
            );
            Assert.That(
                rows[group1Index + 4].GetCell(InternalSpreadsheet.DetailsColumnLabel).Content,
                Is.EqualTo(
                    "{\"kind\":\"inline-image\",\"location\":\"right\",\"offset\":\"24px\",\"offsetBasedOn\":\"380,300\",\"width\":\"40%\",\"transparency\":\"transparent\"}"
                )
            );
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

        [TestCase("roundtrip", "groupWithTextAndImages-es", "Un perro muy valiente.")]
        [TestCase("roundtrip", "groupWithTextAndImages-en", "A very bold dog.")]
        [TestCase("blankbook", "groupWithTextAndImages-es", "Un perro muy valiente.")]
        [TestCase("blankbook", "groupWithTextAndImages-en", "A very bold dog.")]
        public void FloatingImageSurvivesWithGeometry(
            string target,
            string editableId,
            string expectedText
        )
        {
            var editable = GetEditable(GetDom(target), editableId);
            Assert.That(editable.InnerText, Does.Contain(expectedText));

            var wrapper = GetFloatWrapper(editable);
            var classes = wrapper.GetAttribute("class");
            Assert.That(classes, Does.Contain("bloom-inlineImage"));
            Assert.That(classes, Does.Contain("bloom-inlineImageRight"), "location survives");
            Assert.That(classes, Does.Contain("bloom-keepFirstInField"));
            Assert.That(classes, Does.Contain("bloom-preventRemoval"));
            Assert.That(wrapper.GetAttribute("contenteditable"), Is.EqualTo("false"));
            Assert.That(
                wrapper.GetAttribute("data-bloom-inline-image-id"),
                Is.Not.Null.And.Not.Empty,
                "the rebuilt wrapper needs an id for edit-time sync"
            );

            var style = wrapper.GetAttribute("style");
            Assert.That(style, Does.Contain("--inline-image-width: 40%"), "width survives");
            // The aspect ratio is measured from the image file on import. These imports run
            // with null folders, so there is no file to measure and the property is omitted;
            // the CSS then falls back to the image's natural ratio. See
            // ImportMeasuresAspectRatioFromImageFile for the with-files case.
            Assert.That(style, Does.Not.Contain("--inline-image-aspect-ratio"));
            Assert.That(
                style,
                Does.Contain("--inline-image-offset: 24px"),
                "displacement survives"
            );

            var img = wrapper.ChildNodes.OfType<SafeXmlElement>().FirstOrDefault();
            Assert.That(img?.Name, Is.EqualTo("img"));
            Assert.That(
                img.GetAttribute("src"),
                Is.EqualTo("flower.jpg"),
                "src points at the book folder again after import"
            );

            // The floating wrapper must be at the top of the editable, before the text.
            var elementChildren = editable.ChildNodes.OfType<SafeXmlElement>().ToList();
            Assert.That(
                elementChildren.First().GetAttribute("class"),
                Does.Contain("bloom-inlineImageRight"),
                "floating wrapper is the first child"
            );
        }

        [TestCase("roundtrip", "groupWithTextAndImages-es")]
        [TestCase("roundtrip", "groupWithTextAndImages-en")]
        [TestCase("blankbook", "groupWithTextAndImages-es")]
        [TestCase("blankbook", "groupWithTextAndImages-en")]
        public void BottomImageSurvivesAsLastChild(string target, string editableId)
        {
            var editable = GetEditable(GetDom(target), editableId);
            var elementChildren = editable.ChildNodes.OfType<SafeXmlElement>().ToList();
            var wrapper = elementChildren.Last();
            Assert.That(
                wrapper.GetAttribute("class"),
                Does.Contain("bloom-inlineImageBottom"),
                "bottom wrapper is the last child"
            );
            // BloomField reads bloom-keepFirstInField to decide whether the field's required
            // empty <p> goes after the pictures or before them. A bottom-docked picture is at
            // the end of the block, so the <p> belongs before it; with the class on, field
            // setup appended a blank paragraph after the picture (a blank line under the text).
            // setInlineImageDock (inlineImages.ts) takes it off for the same reason.
            Assert.That(
                wrapper.GetAttribute("class"),
                Does.Not.Contain("bloom-keepFirstInField"),
                "a bottom-docked wrapper must not keep the paragraph after it"
            );
            var style = wrapper.GetAttribute("style");
            Assert.That(style, Does.Contain("--inline-image-width: 60%"));
            Assert.That(
                style,
                Does.Not.Contain("--inline-image-offset"),
                "a bottom-docked image has no displacement"
            );
            var img = wrapper.ChildNodes.OfType<SafeXmlElement>().FirstOrDefault();
            Assert.That(img?.GetAttribute("src"), Is.EqualTo("fish.png"));
        }

        [TestCase("roundtrip")]
        [TestCase("blankbook")]
        public void ImageIdsAgreeAcrossEditablesAndDifferBetweenImages(string target)
        {
            var dom = GetDom(target);
            var es = GetEditable(dom, "groupWithTextAndImages-es");
            var en = GetEditable(dom, "groupWithTextAndImages-en");
            var esWrappers = GetWrappers(es);
            var enWrappers = GetWrappers(en);
            Assert.That(esWrappers.Count, Is.EqualTo(2), "sanity: float + bottom in es");
            Assert.That(enWrappers.Count, Is.EqualTo(2), "sanity: float + bottom in en");
            for (var i = 0; i < 2; i++)
            {
                Assert.That(
                    esWrappers[i].GetAttribute("data-bloom-inline-image-id"),
                    Is.EqualTo(enWrappers[i].GetAttribute("data-bloom-inline-image-id")),
                    "copies of one image share one id across the group's editables"
                );
            }
            Assert.That(
                esWrappers[0].GetAttribute("data-bloom-inline-image-id"),
                Is.Not.EqualTo(esWrappers[1].GetAttribute("data-bloom-inline-image-id")),
                "different images have different ids"
            );
        }

        [TestCase("roundtrip")]
        [TestCase("blankbook")]
        public void ImageOnlyEditableIsNotDeleted(string target)
        {
            // The es cell exports as the blank-content indicator (the image contributes no
            // text), and the importer normally deletes an editable whose cell is blank. An
            // editable whose group has an inline image must survive that.
            var editable = GetEditable(GetDom(target), "imageOnlyGroup-es");
            GetFloatWrapper(editable);
        }

        [TestCase("roundtrip")]
        [TestCase("roundtrip")]
        [TestCase("blankbook")]
        public void OffsetKeepsTheBlockSizeItWasMeasuredIn(string target)
        {
            // The offset is an absolute distance, and the block it lands in is very often a
            // different size (another page size, another layout, another book). Dropping the
            // size it was measured against leaves the editor nothing to re-measure from
            // (adjustInlineImageOffsetsIfBlockSizeChanged), so the picture keeps a displacement
            // from the other layout and pushes the text after it off the end of the block.
            var wrapper = GetFloatWrapper(GetEditable(GetDom(target), "groupWithTextAndImages-es"));
            // Sanity check: the offset that baseline describes really did come through.
            Assert.That(wrapper.GetAttribute("style"), Does.Contain("--inline-image-offset: 24px"));
            Assert.That(
                wrapper.GetAttribute("data-inline-image-offset-basedon"),
                Is.EqualTo("380,300")
            );
        }

        [TestCase("roundtrip")]
        [TestCase("blankbook")]
        public void TransparencySurvivesTheRoundTrip(string target)
        {
            // Whether a picture's white is transparent is a choice the person made from the
            // image's own menu (the "image" section of canvasControlRegistry, shared with
            // canvas elements), and it is held as a class on the img. Dropping it turns a
            // picture that was drawn on the page's background into one sitting in a white box.
            var editable = GetEditable(GetDom(target), "groupWithTextAndImages-es");
            var floatImg = GetFloatWrapper(editable).ChildNodes.OfType<SafeXmlElement>().First();
            Assert.That(floatImg.GetAttribute("class"), Does.Contain("bloom-transparent"));
            // The bottom picture made no such choice, and must not acquire one: with neither
            // class the page's background decides.
            var bottomImg = GetWrappers(editable)
                .Last()
                .ChildNodes.OfType<SafeXmlElement>()
                .First();
            Assert.That(bottomImg.GetAttribute("class") ?? "", Does.Not.Contain("bloom-"));
        }

        [TestCase("blankbook")]
        public void PrototypeEditableGetsImages(string target)
        {
            // The lang="z" prototype editable carries a copy of each inline image, so a
            // language added later inherits it (see insertInlineImage in inlineImages.ts).
            var group = GetEditable(GetDom(target), "groupWithTextAndImages-es").ParentNode;
            var prototype = group
                .ChildNodes.OfType<SafeXmlElement>()
                .FirstOrDefault(e => e.GetAttribute("lang") == "z");
            Assert.That(prototype, Is.Not.Null, "the group should still have a z prototype");
            Assert.That(GetWrappers(prototype).Count, Is.EqualTo(2));
        }

        [TestCase("roundtrip")]
        [TestCase("blankbook")]
        public void NoWrapperIsDuplicated(string target)
        {
            var dom = GetDom(target);
            Assert.That(
                GetWrappers(GetEditable(dom, "groupWithTextAndImages-es")).Count,
                Is.EqualTo(2)
            );
            Assert.That(
                GetWrappers(GetEditable(dom, "groupWithTextAndImages-en")).Count,
                Is.EqualTo(2)
            );
            Assert.That(GetWrappers(GetEditable(dom, "imageOnlyGroup-es")).Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ImportMeasuresAspectRatioFromImageFile()
        {
            // The [details] JSON carries no aspect ratio; we never stretch images, so the
            // file itself is the authority and the importer measures it after copying it
            // into the book.
            using (var spreadsheetFolder = new TemporaryFolder("inlineImageSheetFolder"))
            using (var bookFolder = new TemporaryFolder("inlineImageBookFolder"))
            {
                var imagesFolder = Path.Combine(spreadsheetFolder.Path, "images");
                Directory.CreateDirectory(imagesFolder);
                using (var bitmap = new Bitmap(100, 50))
                    bitmap.Save(Path.Combine(imagesFolder, "flower.jpg"), ImageFormat.Jpeg);
                using (var bitmap = new Bitmap(30, 60))
                    bitmap.Save(Path.Combine(imagesFolder, "fish.png"), ImageFormat.Png);

                var dom = new HtmlDom(MakeBook("", ""), true);
                // Like the shared setup, go through a real .xlsx: only the written file has
                // the language cells flattened to text the way a real import sees them.
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

                var editable = GetEditable(dom, "groupWithTextAndImages-es");
                var floatStyle = GetFloatWrapper(editable).GetAttribute("style");
                Assert.That(floatStyle, Does.Contain("--inline-image-aspect-ratio: 100 / 50"));
                var bottomStyle = GetWrappers(editable).Last().GetAttribute("style");
                Assert.That(bottomStyle, Does.Contain("--inline-image-aspect-ratio: 30 / 60"));

                Assert.That(
                    RobustFile.Exists(Path.Combine(bookFolder.Path, "flower.jpg")),
                    "the image file lands in the book folder"
                );
            }
        }

        // A book with a picture inside an image description, alongside a text block that has
        // one too (so the sheet gets its [details] column and IS the authority on inline
        // images). Bloom no longer offers Add Image inside an image description, so this is a
        // book made before that or edited by hand -- and either way the picture is the
        // person's and must not be thrown away by an import.
        private static string MakeBookWithPictureInImageDescription()
        {
            return @"
<!DOCTYPE html>
<html>
<head>
</head>
<body data-l1=""es"" data-l2="""" data-l3="""">
    <div id=""bloomDataDiv"">
        <div data-book=""bookTitle"" lang=""en""><p>Picture in a description</p></div>
    </div>
    <div class=""bloom-page numberedPage customPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""4b71a95a-4b62-4890-80b6-6b5b26f1b78b"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""1"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Image"" lang=""en"">
            Basic Text &amp; Image
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""split-pane-component marginBox"" style="""">
            <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" id=""textGroup-es"" lang=""es"" contenteditable=""true"">"
                + floatWrapper
                + @"
                    <p>Un perro muy valiente.</p>
                </div>
            </div>
            <div class=""bloom-canvas"">
                <img src=""placeHolder.png""></img>
                <div class=""bloom-translationGroup bloom-imageDescription bloom-trailingElement"" data-default-languages=""auto"">
                    <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" id=""description-es"" lang=""es"" contenteditable=""true"">"
                + bottomWrapper
                + @"
                        <p>Un pez que salta.</p>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body>
</html>
";
        }

        [Test]
        public async Task ImportKeepsAPictureInAnImageDescription()
        {
            // An image description is exported as a cell on its image's row, not as a group
            // row of its own, so no [inline image] rows can follow it and the sheet has
            // nothing to say about pictures in it. That is not the same as saying it has
            // none: the group has to keep what the book gave it.
            var bookHtml = MakeBookWithPictureInImageDescription();
            var targetDom = new HtmlDom(bookHtml, true);
            var sheet = ExportBook(bookHtml);
            Assert.That(
                sheet.GetColumnForTag(InternalSpreadsheet.DetailsColumnLabel),
                Is.GreaterThanOrEqualTo(0),
                "sanity: this sheet has the details column, so it is the authority"
            );

            await RoundTripThroughFileAndImportAsync(sheet, targetDom);

            // Sanity check: the text block's own picture came through, so the import really
            // did rebuild inline images on this page.
            Assert.That(GetWrappers(GetEditable(targetDom, "textGroup-es")).Count, Is.EqualTo(1));
            var description = GetEditable(targetDom, "description-es");
            Assert.That(description.InnerText, Does.Contain("Un pez que salta."));
            Assert.That(
                GetWrappers(description).Select(w => w.GetAttribute("data-bloom-inline-image-id")),
                Is.EquivalentTo(new[] { "ii-bottom" }),
                "the description keeps the picture the book gave it"
            );
        }

        [Test]
        public async Task PictureMarkupCannotTravelInACell()
        {
            // Why the [inline image] rows are the only carrier, and worth pinning because it is
            // not obvious from the code: a cell holds MarkedUpText -- paragraphs, and bold,
            // italic and underline runs (see SpreadsheetIO) -- so writing the file drops any
            // other element and reading it cannot bring one back. Markup in a cell therefore
            // cannot move a picture to another book, and an import from a file was never at risk
            // of writing old wrappers into an editable either.
            var sheet = ExportBook(MakeBookWithPictureInImageDescription());
            var row = sheet.ContentRows.First(r =>
                r.MetadataKey == InternalSpreadsheet.ImageDescriptionRowLabel
            );
            row.SetCell("[es]", bottomWrapper + "<p>Un pez que salta.</p>");
            Assert.That(
                row.GetCell("[es]").Content,
                Does.Contain("bloom-inlineImage"),
                "sanity: the cell holds the markup before the file is written"
            );
            var targetDom = new HtmlDom(
                MakeBookWithPictureInImageDescription()
                    .Replace(bottomWrapper, "")
                    .Replace("Un pez que salta.", ""),
                true
            );

            var readBack = await RoundTripThroughFileAndImportAsync(sheet, targetDom);

            Assert.That(
                readBack
                    .ContentRows.First(r =>
                        r.MetadataKey == InternalSpreadsheet.ImageDescriptionRowLabel
                    )
                    .GetCell("[es]")
                    .Content,
                Does.Not.Contain("bloom-inlineImage"),
                "the file cannot hold the wrapper"
            );
            var description = GetEditable(targetDom, "description-es");
            Assert.That(description.InnerText, Does.Contain("Un pez que salta."));
            Assert.That(
                GetWrappers(description),
                Is.Empty,
                "so no picture arrives in a book that had none"
            );
        }

        [Test]
        public void LanguageCellsCarryNoPictureMarkup()
        {
            // The cell is the block's text. A picture in the block has a row of its own, so its
            // markup has no business in the language cell as well. In a cell it is also a trap
            // for anything that reads the sheet without going through a file, as the importer's
            // own tests do: the cell would be written back verbatim, and StampInlineImages
            // leaves alone an editable that already has pictures, so a change made through the
            // picture rows would be ignored. Through a real file the markup never arrives at all
            // -- see PictureMarkupCannotTravelInACell.
            var content = _sheetFromExport
                .ContentRows.First(r =>
                    r.GetCell("[es]").Content.Contains("Un perro muy valiente.")
                )
                .GetCell("[es]")
                .Content;
            Assert.That(content, Does.Not.Contain("bloom-inlineImage"));
            Assert.That(content, Does.Not.Contain("flower.jpg"));
        }

        [Test]
        public async Task ImportRejectsGeometryItCannotUse()
        {
            // A person can edit the [details] cell -- it is hidden, not locked -- and these two
            // values go straight into the wrapper's style attribute. A value that is not a
            // length would either be ignored by the browser (leaving a picture at some default
            // that surprises them) or, with a semicolon in it, add declarations of its own to
            // the wrapper's style. So an unusable value is refused, with a warning, and the
            // picture gets the default.
            var sheet = ExportBook(MakeBook(floatWrapper, bottomWrapper));
            var row = sheet.ContentRows.First(r =>
                r.MetadataKey == InternalSpreadsheet.InlineImageRowLabel
            );
            row.SetCell(
                InternalSpreadsheet.DetailsColumnLabel,
                "{\"kind\":\"inline-image\",\"location\":\"right\",\"offset\":\"12px; display: none\",\"width\":\"wide\"}"
            );
            var targetDom = new HtmlDom(MakeBook("", ""), true);

            await RoundTripThroughFileAndImportAsync(sheet, targetDom);

            var wrapper = GetFloatWrapper(GetEditable(targetDom, "groupWithTextAndImages-es"));
            var style = wrapper.GetAttribute("style");
            Assert.That(style, Does.Not.Contain("display"));
            Assert.That(style, Does.Not.Contain("wide"));
            Assert.That(style, Does.Contain("--inline-image-width: 40%"), "the default width");
            Assert.That(style, Does.Not.Contain("--inline-image-offset"));
        }

        [Test]
        public async Task SpreadsheetWithoutDetailsColumnPreservesBookImages()
        {
            // A spreadsheet made from a book with no inline images (like any spreadsheet
            // from an older Bloom) has no [details] column, so it is not an authority
            // on inline images: importing it over a book that has them must not destroy them.
            var targetDom = new HtmlDom(MakeBook(floatWrapper, bottomWrapper), true);
            var sheet = ExportBook(MakeBook("", ""));
            Assert.That(
                sheet.GetColumnForTag(InternalSpreadsheet.DetailsColumnLabel),
                Is.LessThan(0),
                "sanity: this sheet should have no details column"
            );
            await RoundTripThroughFileAndImportAsync(sheet, targetDom);

            var editable = GetEditable(targetDom, "groupWithTextAndImages-es");
            Assert.That(editable.InnerText, Does.Contain("Un perro muy valiente."));
            var wrappers = GetWrappers(editable);
            Assert.That(wrappers.Count, Is.EqualTo(2), "both images preserved");
            Assert.That(
                wrappers.Select(w => w.GetAttribute("data-bloom-inline-image-id")),
                Is.EquivalentTo(new[] { "ii-float", "ii-bottom" }),
                "the book's own wrappers survive untouched"
            );
        }

        [Test]
        public async Task ASheetWithNoRowsForABlockClearsItsImages()
        {
            // Removing a block's [inline image] rows is how a person deletes its pictures
            // through the spreadsheet. The language editables are rewritten from their cells,
            // so their copies go with the text; the lang="z" prototype is not, and its copy
            // used to stay -- invisible, so nothing showed the picture was still there, until
            // a language was added to the collection and inherited it (TranslationGroupManager
            // clones the prototype).
            //
            // The sheet here still carries the second group's picture, so it has a [details]
            // column and IS the authority on inline images; it just says the first group has
            // none.
            var targetDom = new HtmlDom(MakeBook(floatWrapper, bottomWrapper, floatWrapper), true);
            var sheet = ExportBook(MakeBook("", "", floatWrapper));
            Assert.That(
                sheet.GetColumnForTag(InternalSpreadsheet.DetailsColumnLabel),
                Is.GreaterThanOrEqualTo(0),
                "sanity: this sheet has the details column, so it is the authority"
            );
            Assert.That(
                sheet.ContentRows.Count(r =>
                    r.MetadataKey == InternalSpreadsheet.InlineImageRowLabel
                ),
                Is.EqualTo(1),
                "sanity: one image row, and it belongs to the second group"
            );

            await RoundTripThroughFileAndImportAsync(sheet, targetDom);

            AssertThatXmlIn
                .Dom(targetDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='groupWithTextAndImages-es']/div[contains(@class,'bloom-inlineImage')]",
                    0
                );
            var group = GetEditable(targetDom, "groupWithTextAndImages-es").ParentNode;
            var prototype = group
                .ChildNodes.OfType<SafeXmlElement>()
                .FirstOrDefault(e => e.GetAttribute("lang") == "z");
            Assert.That(prototype, Is.Not.Null, "sanity: the group still has a z prototype");
            Assert.That(
                GetWrappers(prototype).Count,
                Is.EqualTo(0),
                "the prototype's copies go too, or the picture comes back with the next language"
            );
            // ...and the group the sheet DOES have a picture for still has it.
            Assert.That(
                GetWrappers(GetEditable(targetDom, "imageOnlyGroup-es")).Count,
                Is.EqualTo(1)
            );
        }
    }
}
