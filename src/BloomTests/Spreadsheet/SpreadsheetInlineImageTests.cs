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
    /// spreadsheet export → import round trip. The spreadsheet itself cannot represent
    /// them — language cells are flattened to formatted text runs when written to xlsx —
    /// so the importer is responsible for preserving the wrappers that are already in the
    /// book: re-stamping them into each editable it overwrites, and declining to delete an
    /// editable whose only content is an image.
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

        private const string inlineImageTestBook =
            @"
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
            + floatWrapper
            + @"
                        <p>Un perro muy valiente.</p>"
            + bottomWrapper
            + @"
                    </div>

                    <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true"">"
            + floatWrapper
            + @"
                        <p></p>"
            + bottomWrapper
            + @"
                    </div>

                    <div class=""bloom-editable normal-style bloom-contentNational1"" id=""groupWithTextAndImages-en"" lang=""en"" contenteditable=""true"">"
            + floatWrapper
            + @"
                        <p>A very bold dog.</p>"
            + bottomWrapper
            + @"
                    </div>
                </div>

                <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                    <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" id=""imageOnlyGroup-es"" lang=""es"" contenteditable=""true"">"
            + floatWrapper
            + @"
                        <p></p>
                    </div>

                    <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true"">"
            + floatWrapper
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

        private HtmlDom _roundtrippedDom;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            var origDom = new HtmlDom(inlineImageTestBook, true);
            _roundtrippedDom = new HtmlDom(inlineImageTestBook, true); // will get imported into

            // Sanity: the test book is what we think it is before the round trip.
            AssertThatXmlIn
                .Dom(origDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-inlineImage')]",
                    8
                );
            AssertThatXmlIn
                .Dom(origDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@id='groupWithTextAndImages-es']", 1);

            var mockLangDisplayNameResolver = new Mock<ILanguageDisplayNameResolver>();
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName("en"))
                .Returns("English");
            var exporter = new SpreadsheetExporter(mockLangDisplayNameResolver.Object);
            exporter.Params = new SpreadsheetExportParams();
            var sheetFromExport = exporter.Export(origDom, "fakeImagesFolderpath");
            using (var tempFile = TempFile.WithExtension("xlsx"))
            {
                sheetFromExport.WriteToFile(tempFile.Path);
                var sheet = InternalSpreadsheet.ReadFromFile(tempFile.Path);
                var importer = new TestSpreadsheetImporter(null, _roundtrippedDom);
                await importer.ImportAsync(sheet);
            }
        }

        private SafeXmlElement GetEditable(string id)
        {
            var editable =
                _roundtrippedDom.SafeSelectNodes($"//div[@id='{id}']").FirstOrDefault()
                as SafeXmlElement;
            Assert.That(editable, Is.Not.Null, $"editable '{id}' should survive the round trip");
            return editable;
        }

        private SafeXmlElement GetWrapper(SafeXmlElement editable, string imageId)
        {
            var wrapper = editable
                .ChildNodes.OfType<SafeXmlElement>()
                .FirstOrDefault(e => e.GetAttribute("data-bloom-inline-image-id") == imageId);
            Assert.That(
                wrapper,
                Is.Not.Null,
                $"inline image '{imageId}' should survive in '{editable.GetAttribute("id")}'"
            );
            return wrapper;
        }

        [TestCase("groupWithTextAndImages-es", "Un perro muy valiente.")]
        [TestCase("groupWithTextAndImages-en", "A very bold dog.")]
        public void FloatingImageSurvivesWithGeometry(string editableId, string expectedText)
        {
            var editable = GetEditable(editableId);
            Assert.That(editable.InnerText, Does.Contain(expectedText));

            var wrapper = GetWrapper(editable, "ii-float");
            var classes = wrapper.GetAttribute("class");
            Assert.That(classes, Does.Contain("bloom-inlineImage"));
            Assert.That(classes, Does.Contain("bloom-inlineImageRight"), "dock class survives");
            Assert.That(classes, Does.Contain("bloom-keepFirstInField"));
            Assert.That(classes, Does.Contain("bloom-preventRemoval"));
            Assert.That(wrapper.GetAttribute("contenteditable"), Is.EqualTo("false"));

            var style = wrapper.GetAttribute("style");
            Assert.That(style, Does.Contain("--inline-image-width: 40%"), "width survives");
            Assert.That(
                style,
                Does.Contain("--inline-image-aspect-ratio: 800 / 600"),
                "aspect ratio survives"
            );
            Assert.That(style, Does.Contain("--inline-image-offset: 24px"), "offset survives");

            var img = wrapper.ChildNodes.OfType<SafeXmlElement>().FirstOrDefault();
            Assert.That(img?.Name, Is.EqualTo("img"));
            Assert.That(img.GetAttribute("src"), Is.EqualTo("flower.jpg"));

            // The floating wrapper must still be at the top of the editable, before the text.
            var elementChildren = editable.ChildNodes.OfType<SafeXmlElement>().ToList();
            Assert.That(
                elementChildren.First().GetAttribute("data-bloom-inline-image-id"),
                Is.EqualTo("ii-float"),
                "floating wrapper stays first child"
            );
        }

        [TestCase("groupWithTextAndImages-es")]
        [TestCase("groupWithTextAndImages-en")]
        public void BottomImageSurvivesAsLastChild(string editableId)
        {
            var editable = GetEditable(editableId);
            var wrapper = GetWrapper(editable, "ii-bottom");
            Assert.That(
                wrapper.GetAttribute("class"),
                Does.Contain("bloom-inlineImageBottom"),
                "bottom dock class survives"
            );
            var elementChildren = editable.ChildNodes.OfType<SafeXmlElement>().ToList();
            Assert.That(
                elementChildren.Last().GetAttribute("data-bloom-inline-image-id"),
                Is.EqualTo("ii-bottom"),
                "bottom wrapper stays last child"
            );
        }

        [Test]
        public void ImageOnlyEditableIsNotDeleted()
        {
            // The es cell exports as the blank-content indicator (the image contributes no
            // text), and the importer normally deletes an editable whose cell is blank. An
            // editable that holds an inline image must survive that.
            var editable = GetEditable("imageOnlyGroup-es");
            GetWrapper(editable, "ii-float");
        }

        [Test]
        public void NoWrapperIsDuplicated()
        {
            foreach (
                var id in new[]
                {
                    "groupWithTextAndImages-es",
                    "groupWithTextAndImages-en",
                    "imageOnlyGroup-es",
                }
            )
            {
                var editable = GetEditable(id);
                var wrapperIds = editable
                    .ChildNodes.OfType<SafeXmlElement>()
                    .Where(e => (e.GetAttribute("class") ?? "").Contains("bloom-inlineImage"))
                    .Select(e => e.GetAttribute("data-bloom-inline-image-id"))
                    .ToList();
                Assert.That(
                    wrapperIds,
                    Is.Unique,
                    $"'{id}' should not gain duplicate inline images"
                );
            }
        }
    }
}
