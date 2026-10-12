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
using SIL.Windows.Forms.ClearShare;

namespace BloomTests.Spreadsheet
{
    /// <summary>
    /// Tests that inline images survive exporting a book to a spreadsheet and importing it
    /// again. Inline images are pictures inside a text block, as in Word; each one is a
    /// .bloom-inlineImage wrapper, copied into every bloom-editable of its translation group.
    /// The export gives each inline image its own [inline image] row right after its group's
    /// row. The image file goes in the ordinary [image source] column, and the side it is
    /// docked to, how far down it is pushed, and its width go as JSON in the hidden [details]
    /// column. The JSON has no aspect ratio, because the importer measures the image file.
    /// The import rebuilds the wrappers from those values, both when importing over the same
    /// book and when importing into a book that has no inline images at all. When a
    /// spreadsheet has no [details] column (because an older Bloom made it), the import keeps
    /// whatever inline images the target book already has.
    /// </summary>
    public class SpreadsheetInlineImageTests
    {
        static SpreadsheetInlineImageTests()
        {
            // The package requires us to do this as a way of acknowledging that we
            // accept the terms of the NonCommercial license.
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        // A right-docked wrapper and a bottom-docked wrapper, as makeInlineImageWrapper and
        // insertInlineImage (inlineImages.ts) produce them. The test book puts both in every
        // editable of the group, including the lang="z" prototype.
        private const string floatWrapper =
            @"<div data-bloom-inline-image-id=""ii-float"" data-inline-image-offset-basedon=""380,300"" class=""bloom-inlineImage bloom-inlineImageRight bloom-keepFirstInField bloom-preventRemoval"" contenteditable=""false"" style=""--inline-image-width: 40%; --inline-image-aspect-ratio: 800 / 600; --inline-image-offset: 24px;""><img class=""bloom-transparent"" src=""flower.jpg"" alt=""""></img></div>";

        private const string bottomWrapper =
            @"<div data-bloom-inline-image-id=""ii-bottom"" class=""bloom-inlineImage bloom-inlineImageBottom bloom-keepFirstInField bloom-preventRemoval"" contenteditable=""false"" style=""--inline-image-width: 60%; --inline-image-aspect-ratio: 4 / 3;""><img src=""fish.png"" alt=""""></img></div>";

        /// <summary>
        /// Builds the test book, either with the inline images or without them. The book with
        /// them is the one we export, and also the target when we import over the same book.
        /// The book without them is the "blankbook" import target, which shows that the
        /// spreadsheet itself carries the images.
        /// </summary>
        /// <param name="imageOnlyImg">the second group's picture. It defaults to the first
        /// group's right-docked picture, since most callers want both groups to look the same.
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

            // The first group's two inline images come directly after its row, in the order
            // they appear in the editable.
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

            // The second group, which has a picture and no text, comes next, followed by its
            // one inline image.
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
            // Like [image source], [details] is there for Bloom to read back, and a
            // translator has no reason to edit it.
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
            // The import measures the aspect ratio from the image file. These imports run
            // with null folders, so there is no file to measure and the property is left off,
            // and the CSS falls back to the image's natural ratio.
            // ImportMeasuresAspectRatioFromImageFile tests an import that has the files.
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

            // The right-docked wrapper must be the editable's first child, before the text.
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
            // the end of the block, so the <p> belongs before it. With the class on, BloomField
            // adds a blank paragraph after the picture, which shows as a blank line under the
            // text. setInlineImageDock (inlineImages.ts) removes the class for the same reason.
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
            // The es cell exports as [blank] because the picture has no text, and the importer
            // normally deletes an editable whose cell is blank. It must keep an editable whose
            // group has an inline image.
            var editable = GetEditable(GetDom(target), "imageOnlyGroup-es");
            GetFloatWrapper(editable);
        }

        [TestCase("roundtrip")]
        [TestCase("roundtrip")]
        [TestCase("blankbook")]
        public void OffsetKeepsTheBlockSizeItWasMeasuredIn(string target)
        {
            // The offset is a fixed distance, and the block it is imported into is often a
            // different size (another page size, another layout, another book). Without the
            // size of the block it was measured in, adjustInlineImageOffsetsIfBlockSizeChanged
            // cannot scale the offset, so the picture keeps an offset meant for the other
            // layout and can push the text after it off the end of the block.
            var wrapper = GetFloatWrapper(GetEditable(GetDom(target), "groupWithTextAndImages-es"));
            // Sanity check: the offset that this block size belongs to came through.
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
            // The person chooses whether a picture's white is transparent from the image's menu
            // (the "image" section of canvasControlRegistry, which canvas elements also use),
            // and the choice is stored as a class on the img. If the import lost it, a picture
            // that showed the page's background would sit in a white box.
            var editable = GetEditable(GetDom(target), "groupWithTextAndImages-es");
            var floatImg = GetFloatWrapper(editable).ChildNodes.OfType<SafeXmlElement>().First();
            Assert.That(floatImg.GetAttribute("class"), Does.Contain("bloom-transparent"));
            // Nobody made that choice for the bottom picture, and the import must not add one.
            // With neither class the page's background decides.
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
            // The [details] JSON has no aspect ratio. We never stretch images, so the image
            // file determines it, and the importer measures the file after copying it into
            // the book.
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
                // As in OneTimeSetUp, write a real .xlsx and read it back. Only then are the
                // language cells reduced to plain text the way a real import sees them.
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

        [Test]
        public async Task ImportPutsTheImageFileMetadataOnTheImg()
        {
            // Bloom copies an image file's copyright, creator and license onto the img element
            // so that the credits and the UI can read them without opening every file, and the
            // ordinary [image] import fills them in as it copies the file
            // (CopyImageFileToDestination). The inline-image import has to do the same, because
            // nothing else on this path does, and a command-line import is never followed by
            // the pass that brings a book up to date in the editor.
            using (var spreadsheetFolder = new TemporaryFolder("inlineImageCreditsSheetFolder"))
            using (var bookFolder = new TemporaryFolder("inlineImageCreditsBookFolder"))
            {
                var imagesFolder = Path.Combine(spreadsheetFolder.Path, "images");
                Directory.CreateDirectory(imagesFolder);
                var flowerPath = Path.Combine(imagesFolder, "flower.jpg");
                using (var bitmap = new Bitmap(100, 50))
                    bitmap.Save(flowerPath, ImageFormat.Jpeg);
                using (var bitmap = new Bitmap(30, 60))
                    bitmap.Save(Path.Combine(imagesFolder, "fish.png"), ImageFormat.Png);
                var metadata = new Metadata
                {
                    CopyrightNotice = "Copyright 2026 Emilia Example",
                    Creator = "Emilia Example",
                    License = CreativeCommonsLicense.FromLicenseUrl(
                        "http://creativecommons.org/licenses/by/4.0/"
                    ),
                };
                metadata.Write(flowerPath);
                Assert.That(
                    Bloom.RobustFileIO.MetadataFromFile(flowerPath).CopyrightNotice,
                    Is.EqualTo("Copyright 2026 Emilia Example"),
                    "sanity: the test image has metadata for the import to find"
                );

                var dom = new HtmlDom(MakeBook("", ""), true);
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

                var img = GetFloatWrapper(GetEditable(dom, "groupWithTextAndImages-es"))
                    .ChildNodes.OfType<SafeXmlElement>()
                    .First();
                Assert.That(img.Name, Is.EqualTo("img"), "sanity: the wrapper holds the picture");
                Assert.That(
                    img.GetAttribute("data-copyright"),
                    Is.EqualTo("Copyright 2026 Emilia Example")
                );
                Assert.That(img.GetAttribute("data-creator"), Is.EqualTo("Emilia Example"));
                Assert.That(img.GetAttribute("data-license"), Is.EqualTo("cc-by"));
            }
        }

        // A book with a picture inside an image description, and a text block that has one
        // too, so the sheet gets a [details] column and the import builds inline images from
        // its rows. Bloom no longer offers Insert Image inside an image description, so a book
        // like this was made before that change or edited by hand. Either way the picture is
        // the person's, and an import must not throw it away.
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
            // An image description is exported as a cell on its image's row instead of as a
            // row of its own, so no [inline image] rows can follow it, and the sheet says
            // nothing about pictures in it. The group has to keep the pictures the book gave it.
            var bookHtml = MakeBookWithPictureInImageDescription();
            var targetDom = new HtmlDom(bookHtml, true);
            var sheet = ExportBook(bookHtml);
            Assert.That(
                sheet.GetColumnForTag(InternalSpreadsheet.DetailsColumnLabel),
                Is.GreaterThanOrEqualTo(0),
                "sanity: this sheet has the details column, so it is the authority"
            );

            await RoundTripThroughFileAndImportAsync(sheet, targetDom);

            // Sanity check: the text block's own picture came through, so the import did
            // rebuild inline images on this page.
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
            // This test shows why pictures can only travel in [inline image] rows, which is not
            // obvious from the code. A cell holds MarkedUpText (paragraphs, and bold, italic
            // and underline runs; see SpreadsheetIO), so writing the file drops any other
            // element and reading it cannot bring one back. So markup in a cell cannot move a
            // picture to another book, and an import from a file cannot write old wrappers into
            // an editable either.
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
            // The language cell holds the block's text. A picture in the block has a row of its
            // own, so its markup should not appear in the language cell too. Markup in the cell
            // would also cause trouble for code that reads the sheet without going through a
            // file, as the importer's own tests do. That code would write the cell back into
            // the editable as it is, and StampInlineImages skips an editable that already has
            // pictures, so a change made in the [inline image] rows would be ignored. Through a
            // real file the markup never arrives at all; see PictureMarkupCannotTravelInACell.
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
            // A person can edit the [details] cell (the column is hidden, but it is not
            // locked), and the width and offset go straight into the wrapper's style attribute.
            // The browser ignores a value that is not a length, which leaves the picture at
            // some default that would surprise the person, and a value with a semicolon in it
            // adds declarations of its own to the wrapper's style. So the import rejects a
            // value it cannot use, warns about it, and gives the picture the default.
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
            // A spreadsheet made from a book with no inline images has no [details] column,
            // just like any spreadsheet from an older Bloom, so it says nothing about inline
            // images. Importing it over a book that has them must keep them.
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
            // A person deletes a block's pictures through the spreadsheet by removing the
            // block's [inline image] rows. The import rewrites the language editables from
            // their cells, so their copies go along with the old text. It does not rewrite the
            // lang="z" prototype, so the prototype's copy has to be removed separately. If it
            // stayed, nobody would see it until a language was added to the collection and
            // got a copy of it (TranslationGroupManager clones the prototype).
            //
            // The sheet here still has the second group's picture, so it has a [details]
            // column and the import builds inline images from its rows. Those rows say the
            // first group has none.
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
            // The group the sheet does have a picture for still has it.
            Assert.That(
                GetWrappers(GetEditable(targetDom, "imageOnlyGroup-es")).Count,
                Is.EqualTo(1)
            );
        }

        [Test]
        public async Task ALanguageTheSheetLacksFollowsTheSheetsPictures()
        {
            // The target book has a French editable the sheet has no column for, so the import
            // does not rewrite it. Its pictures still have to match the sheet, which here says
            // the block has none. A copy left in French would show up whenever French is shown,
            // and its id would match nothing else in the block.
            var frenchEditable =
                "<div class=\"bloom-editable normal-style\" id=\"groupWithTextAndImages-fr\" lang=\"fr\" contenteditable=\"true\">"
                + floatWrapper
                + "<p>Un chien très courageux.</p></div>";
            var englishEditableStart =
                "<div class=\"bloom-editable normal-style bloom-contentNational1\" id=\"groupWithTextAndImages-en\"";
            var bookHtml = MakeBook(floatWrapper, bottomWrapper);
            Assert.That(bookHtml, Does.Contain(englishEditableStart), "sanity: insertion point");
            var targetDom = new HtmlDom(
                bookHtml.Replace(englishEditableStart, frenchEditable + englishEditableStart),
                true
            );
            var sheet = ExportBook(MakeBook("", "", floatWrapper));
            Assert.That(
                sheet.Languages,
                Does.Not.Contain("fr"),
                "sanity: the sheet has no French column"
            );
            Assert.That(
                GetWrappers(GetEditable(targetDom, "groupWithTextAndImages-fr")).Count,
                Is.EqualTo(1),
                "sanity: French starts with a picture"
            );

            await RoundTripThroughFileAndImportAsync(sheet, targetDom);

            var french = GetEditable(targetDom, "groupWithTextAndImages-fr");
            Assert.That(french.InnerText, Does.Contain("Un chien très courageux."));
            Assert.That(GetWrappers(french).Count, Is.EqualTo(0));
        }

        [Test]
        public async Task WarningsNameTheInlineImageRowTheyAreAbout()
        {
            // The import handles an [inline image] row while it is still on its group's row, and
            // a group can have several picture rows. The warning has to give the picture's own
            // row number, or the person cannot tell which one to fix.
            var sheet = ExportBook(MakeBook(floatWrapper, bottomWrapper));
            var rows = sheet.ContentRows.ToList();
            var imageRows = rows.Where(r =>
                    r.MetadataKey == InternalSpreadsheet.InlineImageRowLabel
                )
                .ToList();
            var groupRow = rows[rows.IndexOf(imageRows[0]) - 1];
            Assert.That(
                groupRow.MetadataKey,
                Is.EqualTo(InternalSpreadsheet.PageContentRowLabel),
                "sanity: the picture rows follow their group's row"
            );
            // Use the block's second picture, whose row is not the one right after the group's
            // row.
            var badRow = imageRows[1];
            badRow.SetCell(
                InternalSpreadsheet.DetailsColumnLabel,
                "{\"kind\":\"inline-image\",\"location\":\"bottom\",\"width\":\"wide\"}"
            );
            var targetDom = new HtmlDom(MakeBook("", ""), true);

            var warnings = await new TestSpreadsheetImporter(null, targetDom).ImportAsync(sheet);

            var badRowNumber = sheet.GetIndexOfRow(badRow) + 1;
            var groupRowNumber = sheet.GetIndexOfRow(groupRow) + 1;
            Assert.That(badRowNumber, Is.Not.EqualTo(groupRowNumber), "sanity");
            var widthWarning = warnings.Single(w => w.Contains("\"wide\""));
            Assert.That(widthWarning, Does.Contain($"row {badRowNumber}."));
        }

        [Test]
        public async Task ImportDropsABlockSizeItCannotRead()
        {
            // The editor scales the offset by comparing offsetBasedOn with the block's size, so
            // the import drops a hand-edited value it cannot parse and warns about it. The
            // offset itself is still usable and stays.
            var sheet = ExportBook(MakeBook(floatWrapper, bottomWrapper));
            var row = sheet.ContentRows.First(r =>
                r.MetadataKey == InternalSpreadsheet.InlineImageRowLabel
            );
            row.SetCell(
                InternalSpreadsheet.DetailsColumnLabel,
                "{\"kind\":\"inline-image\",\"location\":\"right\",\"offset\":\"24px\",\"offsetBasedOn\":\"about A5\",\"width\":\"40%\"}"
            );
            var targetDom = new HtmlDom(MakeBook("", ""), true);

            var warnings = await new TestSpreadsheetImporter(null, targetDom).ImportAsync(sheet);

            var wrapper = GetFloatWrapper(GetEditable(targetDom, "groupWithTextAndImages-es"));
            Assert.That(wrapper.GetAttribute("style"), Does.Contain("--inline-image-offset: 24px"));
            Assert.That(wrapper.GetAttribute("data-inline-image-offset-basedon"), Is.Null.Or.Empty);
            Assert.That(warnings.Count(w => w.Contains("\"about A5\"")), Is.EqualTo(1));
        }
    }
}
