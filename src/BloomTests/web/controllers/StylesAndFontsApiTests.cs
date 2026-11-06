using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.SafeXml;
using Bloom.web.controllers;
using BloomTests.Book;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    [TestFixture]
    public class StylesAndFontsApiTests : BookTestsBase
    {
        /// <summary>
        /// The ISBN field on the credits page is language-independent (lang="*"). When the user
        /// gives it a font, StyleEditor writes a [lang="*"] rule, and the Fonts table used to leave
        /// that font out (BL-16851). The font applies only to that field, so it must get a row of
        /// its own rather than replacing the style's font for the book's languages.
        /// </summary>
        [Test]
        public void GetDataRows_LangStarFieldHasUserFont_ShowsItsOwnRow()
        {
            var bookSelection = SetUpBookWithIsbnFont("Charis SIL", "Andika", "Charis SIL");
            var book = bookSelection.CurrentSelection;
            var creditsPage = book.OurHtmlDom.SelectSingleNode(
                "//div[contains(@class,'bloom-page') and @data-xmatter-page='credits']"
            );

            var rows = new StylesAndFontsApi(bookSelection).GetDataRows("current");

            var creditsRows = rows.Where(r => r.style == "Credits-Page").ToList();
            var isbnRow = creditsRows.SingleOrDefault(r => r.fontName == "ABeeZee");
            Assert.That(isbnRow, Is.Not.Null, "the ISBN field's font should have a row");
            Assert.That(isbnRow.languageName, Is.EqualTo("(no language)"));
            Assert.That(isbnRow.pageId, Is.EqualTo(creditsPage.GetAttribute("id")));
            var englishRow = creditsRows.SingleOrDefault(r => r.languageTag == "en");
            Assert.That(englishRow, Is.Not.Null, "English credits text should still have a row");
            Assert.That(
                englishRow.fontName,
                Is.EqualTo("Andika"),
                "the [lang=\"*\"] rule must not change the font shown for English"
            );
            Assert.That(
                rows.Where(r => r.fontName == "ABeeZee").Count(),
                Is.EqualTo(1),
                "ABeeZee is used only by the ISBN field"
            );

            var otherRows = new StylesAndFontsApi(bookSelection).GetDataRows("other");
            Assert.That(
                otherRows.Any(r => r.fontName == "ABeeZee"),
                Is.False,
                "the ISBN field belongs with the current languages, not the other ones"
            );
        }

        /// <summary>
        /// When every collection language uses the same font, the style gets one row labeled
        /// "(all)". The ISBN field's row must not also be labeled "(all)": that would claim its
        /// font is used by every language, and the dialog keys its rows by style and language name,
        /// so the two rows would collide.
        /// </summary>
        [Test]
        public void GetDataRows_LangStarFieldHasUserFont_AllLanguagesShareFont_RowsAreDistinct()
        {
            var bookSelection = SetUpBookWithIsbnFont("Andika", "Andika", "Andika");

            var rows = new StylesAndFontsApi(bookSelection).GetDataRows("current");

            var creditsRows = rows.Where(r => r.style == "Credits-Page").ToList();
            var allRow = creditsRows.SingleOrDefault(r => r.languageName == "(all)");
            Assert.That(
                allRow,
                Is.Not.Null,
                "test setup: the collection's languages should share one font, giving an (all) row. Rows: "
                    + string.Join(
                        "; ",
                        creditsRows.Select(r => $"{r.languageTag}/{r.languageName}/{r.fontName}")
                    )
            );
            Assert.That(allRow.fontName, Is.EqualTo("Andika"));
            var isbnRow = creditsRows.SingleOrDefault(r => r.fontName == "ABeeZee");
            Assert.That(isbnRow, Is.Not.Null, "the ISBN field's font should have a row");
            Assert.That(isbnRow.languageName, Is.EqualTo("(no language)"));
            Assert.That(
                creditsRows.Select(r => r.languageName),
                Is.Unique,
                "each Credits Page row needs its own language name; the dialog keys rows by it"
            );
        }

        /// <summary>
        /// The real xmatter leaves the ISBN field empty. An empty field shows no text, so its font
        /// is not used and must not get a row.
        /// </summary>
        [Test]
        public void GetDataRows_LangStarFieldHasUserFontButIsEmpty_HasNoRow()
        {
            var bookSelection = SetUpBookWithIsbnFont(
                "Charis SIL",
                "Andika",
                "Charis SIL",
                isbn: null
            );
            var rules = HtmlDom
                .GetUserModifiedStyleElement(bookSelection.CurrentSelection.OurHtmlDom.Head)
                .InnerText;
            Assert.That(
                rules,
                Does.Contain("ABeeZee"),
                "test setup: the ISBN style should still have its font rule"
            );

            var rows = new StylesAndFontsApi(bookSelection).GetDataRows("current");

            Assert.That(rows.Any(r => r.fontName == "ABeeZee"), Is.False);
            Assert.That(
                rows.Any(r => r.style == "Credits-Page" && r.languageTag == "en"),
                Is.True,
                "test setup: the credits page's English text should still have a row"
            );
        }

        /// <summary>
        /// Make a book whose credits page has the language-independent ISBN field, with a
        /// [lang="*"] rule giving that field's style the font ABeeZee, and select it.
        /// The collection's languages are xyz, en and fr, each with text on a content page.
        /// </summary>
        /// <param name="xyzFont">the collection's default font for xyz (Language 1)</param>
        /// <param name="enFont">the collection's default font for English (Language 2)</param>
        /// <param name="frFont">the collection's default font for French (Language 3)</param>
        /// <param name="isbn">the ISBN to put in the field, or null to leave it empty</param>
        private BookSelection SetUpBookWithIsbnFont(
            string xyzFont,
            string enFont,
            string frFont,
            string isbn = "978-0-00-000000-2"
        )
        {
            SetDom(
                // Loading the book adds the real xmatter, whose credits page has the ISBN field.
                @"<div class='bloom-page numberedPage' id='11111111-1111-4111-8111-111111111111' data-page-number='1'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable normal-style bloom-visibility-code-on' lang='xyz'>Some text</div>
                        <div class='bloom-editable normal-style bloom-visibility-code-on' lang='en'>Some text</div>
                        <div class='bloom-editable normal-style bloom-visibility-code-on' lang='fr'>Some text</div>
                    </div>
                </div>",
                "<style type='text/css' title='userModifiedStyles'>.Credits-Page-style[lang=\"*\"] { font-family: ABeeZee !important; }</style>"
            );
            var book = CreateBook();
            // Selecting the book writes defaultLangStyles.css from these.
            book.CollectionSettings.Language1.FontName = xyzFont;
            book.CollectionSettings.Language2.FontName = enFont;
            book.CollectionSettings.Language3.FontName = frFont;
            var bookSelection = new BookSelection();
            bookSelection.SelectBook(book);
            Assert.That(
                new List<string>
                {
                    book.CollectionSettings.Language1Tag,
                    book.CollectionSettings.Language2Tag,
                    book.CollectionSettings.Language3Tag,
                },
                Is.EqualTo(new[] { "xyz", "en", "fr" }),
                "test setup: unexpected collection languages"
            );
            Assert.That(
                book.OurHtmlDom.GetDefaultFontsForLanguages(book.FolderPath),
                Is.EquivalentTo(
                    new Dictionary<string, string>
                    {
                        { "xyz", xyzFont },
                        { "en", enFont },
                        { "fr", frFont },
                    }
                ),
                "test setup: defaultLangStyles.css should give each language its font"
            );
            var isbnField = (SafeXmlElement)
                book.OurHtmlDom.SelectSingleNode(
                    "//div[contains(@class,'bloom-page') and @data-xmatter-page='credits']"
                        + "//div[contains(@class,'Credits-Page-style') and @lang='*' and @data-book='ISBN']"
                );
            Assert.That(
                isbnField,
                Is.Not.Null,
                "test setup: the credits page should have the language-independent ISBN field"
            );
            Assert.That(
                isbnField.InnerText.Trim(),
                Is.Empty,
                "test setup: the xmatter's ISBN field should start out empty"
            );
            if (isbn != null)
                isbnField.InnerText = isbn;
            return bookSelection;
        }
    }
}
