using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bloom.Book;
using Bloom.SafeXml;
using Bloom.Spreadsheet;
using Moq;
using NUnit.Framework;
using OfficeOpenXml;
using SIL.IO;

namespace BloomTests.Spreadsheet
{
    /// <summary>
    /// Tests that inline images (Word-style images inside text blocks; .bloom-inlineImage
    /// wrappers replicated into every bloom-editable of a translation group) survive a
    /// spreadsheet export → import round trip. Export gives each inline image its own
    /// [inline image] row right after its group's row: the file in the normal [image source]
    /// column, and the geometry parameters (location, displacement, width, aspect) as
    /// readable text in [image details]. Import reconstructs the wrappers from those
    /// parameters — whether importing over the same book or into a book that has no inline
    /// images at all. A spreadsheet without the [image details] column (from an older Bloom)
    /// falls back to preserving whatever the target book already has.
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
            @"<div data-bloom-inline-image-id=""ii-float"" class=""bloom-inlineImage bloom-inlineImageRight bloom-keepFirstInField bloom-preventRemoval"" contenteditable=""false"" style=""--inline-image-width: 40%; --inline-image-aspect-ratio: 800 / 600; --inline-image-offset: 24px;""><img src=""flower.jpg"" alt=""""></img></div>";

        private const string bottomWrapper =
            @"<div data-bloom-inline-image-id=""ii-bottom"" class=""bloom-inlineImage bloom-inlineImageBottom bloom-keepFirstInField bloom-preventRemoval"" contenteditable=""false"" style=""--inline-image-width: 60%; --inline-image-aspect-ratio: 4 / 3;""><img src=""fish.png"" alt=""""></img></div>";

        /// <summary>
        /// The test book, parameterized so we can build it with the inline images (the book
        /// that gets exported, and the same-book import target) or without them (the
        /// "blank book" import target, proving the spreadsheet itself carries the images).
        /// </summary>
        private static string MakeBook(string floatImg, string bottomImg)
        {
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
                + floatImg
                + @"
                        <p></p>
                    </div>

                    <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true"">"
                + floatImg
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
                rows[group1Index + 1].GetCell(InternalSpreadsheet.ImageDetailsColumnLabel).Content,
                Is.EqualTo("right, offset 24px, width 40%, aspect 800/600")
            );
            Assert.That(
                rows[group1Index + 2]
                    .GetCell(InternalSpreadsheet.ImageSourceColumnLabel)
                    .Content.Replace('\\', '/'),
                Is.EqualTo("images/fish.png")
            );
            Assert.That(
                rows[group1Index + 2].GetCell(InternalSpreadsheet.ImageDetailsColumnLabel).Content,
                Is.EqualTo("bottom, width 60%, aspect 4/3")
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
                rows[group1Index + 4].GetCell(InternalSpreadsheet.ImageDetailsColumnLabel).Content,
                Is.EqualTo("right, offset 24px, width 40%, aspect 800/600")
            );
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
            Assert.That(
                style,
                Does.Contain("--inline-image-aspect-ratio: 800 / 600"),
                "aspect ratio survives"
            );
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
            var style = wrapper.GetAttribute("style");
            Assert.That(style, Does.Contain("--inline-image-width: 60%"));
            Assert.That(style, Does.Contain("--inline-image-aspect-ratio: 4 / 3"));
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
        public async Task SpreadsheetWithoutImageDetailsColumnPreservesBookImages()
        {
            // A spreadsheet made from a book with no inline images (like any spreadsheet
            // from an older Bloom) has no [image details] column, so it is not an authority
            // on inline images: importing it over a book that has them must not destroy them.
            var targetDom = new HtmlDom(MakeBook(floatWrapper, bottomWrapper), true);
            var sheet = ExportBook(MakeBook("", ""));
            Assert.That(
                sheet.GetColumnForTag(InternalSpreadsheet.ImageDetailsColumnLabel),
                Is.LessThan(0),
                "sanity: this sheet should have no image-details column"
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
    }
}
