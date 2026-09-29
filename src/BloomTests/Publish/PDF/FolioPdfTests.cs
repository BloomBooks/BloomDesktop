using System.Collections.Generic;
using System.Linq;
using Bloom.Book;
using Bloom.Publish.PDF;
using NUnit.Framework;

namespace BloomTests.Publish.PDF
{
    /// <summary>
    /// Tests of how a folio's PDF is put together from the books it holds: where blank pages go,
    /// and the numbers and sides its pages get.
    /// </summary>
    [TestFixture]
    public class FolioPdfTests
    {
        private static FolioPageSides.PartShape Part(int pageCount, int positionWhenAlone = 0) =>
            new FolioPageSides.PartShape(pageCount, positionWhenAlone);

        [Test]
        public void GetBlankPagesBefore_BookAfterEvenPageCount_NeedsNoBlank()
        {
            var blanks = FolioPageSides.GetBlankPagesBefore(
                new[] { Part(4), Part(6), Part(2) },
                addBlankPages: true
            );
            Assert.That(blanks, Is.EqualTo(new[] { 0, 0, 0 }));
        }

        [Test]
        public void GetBlankPagesBefore_BookAfterOddPageCount_GetsOneBlank()
        {
            // Folio front has 4 pages, then a book of 3 pages ends on a right-hand page, so the next
            // book, whose cover is a right-hand page when printed alone, needs a blank before it.
            var blanks = FolioPageSides.GetBlankPagesBefore(
                new[] { Part(4), Part(3), Part(5), Part(2) },
                addBlankPages: true
            );
            // After the blank: 4 + 3 + 1 = 8, even; the 5-page book then ends at 13, odd.
            Assert.That(blanks, Is.EqualTo(new[] { 0, 0, 1, 1 }));
        }

        [Test]
        public void GetBlankPagesBefore_PartStartingOnLeftWhenAlone_KeepsItsSide()
        {
            // The folio's back matter starts at position 5 when the folio is printed alone, a
            // left-hand page. After 4 + 6 pages it would be at 10, a right-hand page, so it gets a blank.
            var blanks = FolioPageSides.GetBlankPagesBefore(
                new[] { Part(4), Part(6), Part(1, positionWhenAlone: 5) },
                addBlankPages: true
            );
            Assert.That(blanks, Is.EqualTo(new[] { 0, 0, 1 }));
        }

        [Test]
        public void GetBlankPagesBefore_BlanksNotWanted_AddsNone()
        {
            var blanks = FolioPageSides.GetBlankPagesBefore(
                new[] { Part(4), Part(3), Part(5), Part(1, positionWhenAlone: 5) },
                addBlankPages: false
            );
            Assert.That(blanks, Is.EqualTo(new[] { 0, 0, 0, 0 }));
        }

        private static HtmlDom MakeDom(string pages) =>
            new HtmlDom($"<html><head></head><body>{pages}</body></html>");

        [Test]
        public void InsertBlankPageAtStart_PlainPages_InsertsSameSizePageFirst()
        {
            var dom = MakeDom(
                "<div class='bloom-page numberedPage A5Portrait' id='p1'><div class='marginBox'/></div>"
            );
            Assert.That(FolioPdfPartsMaker.GetPages(dom).Count, Is.EqualTo(1), "setup");

            FolioPdfPartsMaker.InsertBlankPageAtStart(dom);

            var pages = FolioPdfPartsMaker.GetPages(dom);
            Assert.That(pages.Count, Is.EqualTo(2));
            Assert.That(pages[0].HasClass(FolioPdfPartsMaker.kBlankPageClass), Is.True);
            Assert.That(pages[0].HasClass("A5Portrait"), Is.True);
            Assert.That(pages[0].HasClass("countPageButDoNotShowNumber"), Is.True);
            Assert.That(pages[1].GetAttribute("id"), Is.EqualTo("p1"));
        }

        [Test]
        public void InsertBlankPageAtStart_FullBleedPages_WrapsBlankInMediaBox()
        {
            var dom = MakeDom(
                "<div class='bloom-mediaBox A5Portrait'><div class='bloom-page A5Portrait' id='p1'/></div>"
            );

            FolioPdfPartsMaker.InsertBlankPageAtStart(dom);

            var boxes = dom.RawDom.SafeSelectElements("/html/body/div");
            Assert.That(boxes.Length, Is.EqualTo(2), "each page should have its own media box");
            Assert.That(boxes[0].GetAttribute("class"), Is.EqualTo("bloom-mediaBox A5Portrait"));
            var blank = (Bloom.SafeXml.SafeXmlElement)boxes[0].FirstChild;
            Assert.That(blank.HasClass(FolioPdfPartsMaker.kBlankPageClass), Is.True);
        }

        [Test]
        public void NumberPagesAcrossFolio_NumbersRunOnAndSidesFollowFolioPosition()
        {
            var folioFront = MakeDom(
                "<div class='bloom-page bloom-frontMatter A5Portrait side-right' id='f1'/>"
                    + "<div class='bloom-page numberedPage A5Portrait side-left' id='toc'/>"
            );
            // A book printed alone would number its first content page 1 and restart numbering there.
            var child = MakeDom(
                "<div class='bloom-page bloom-frontMatter A5Portrait side-right' id='c1'/>"
                    + "<div class='bloom-page numberedPage bloom-startPageNumbering A5Portrait side-left' id='c2'/>"
                    + "<div class='bloom-page numberedPage A5Portrait side-right' id='c3'/>"
            );
            FolioPdfPartsMaker.InsertBlankPageAtStart(child); // pretend the planner asked for one
            Assert.That(FolioPdfPartsMaker.GetPages(child).Count, Is.EqualTo(4), "setup");

            FolioPdfPartsMaker.NumberPagesAcrossFolio(
                new List<(HtmlDom, bool)> { (folioFront, true), (child, false) },
                "",
                false
            );

            var pages = FolioPdfPartsMaker
                .GetPages(folioFront)
                .Concat(FolioPdfPartsMaker.GetPages(child))
                .ToList();
            var sides = pages.Select(p => p.HasClass("side-right") ? "R" : "L").ToArray();
            Assert.That(sides, Is.EqualTo(new[] { "R", "L", "R", "L", "R", "L" }));
            var numbers = pages.Select(p => p.GetAttribute("data-page-number")).ToArray();
            // toc is page 1; the blank counts as 2 without showing it; the child's cover is not
            // numbered; its content pages carry on at 3 and 4 instead of restarting.
            Assert.That(numbers, Is.EqualTo(new[] { "", "1", "", "", "3", "4" }));
            Assert.That(pages[4].HasClass("bloom-startPageNumbering"), Is.False);
        }

        [Test]
        public void NumberPagesAcrossFolio_EachBooksOwnNumbers_KeepsThemButSetsSidesFromFolio()
        {
            var folioFront = MakeDom(
                "<div class='bloom-page bloom-frontMatter A5Portrait' id='f1'/>"
                    + "<div class='bloom-page numberedPage A5Portrait' id='toc'/>"
                    + "<div class='bloom-page numberedPage A5Portrait' id='f3'/>"
            );
            // The numbers the book has when printed alone, as its saved file carries them.
            var child = MakeDom(
                "<div class='bloom-page numberedPage A5Portrait side-right' id='c1' data-page-number='1'/>"
                    + "<div class='bloom-page numberedPage A5Portrait side-left' id='c2' data-page-number='2'/>"
            );

            FolioPdfPartsMaker.NumberPagesAcrossFolio(
                new List<(HtmlDom, bool)> { (folioFront, true), (child, false) },
                "",
                false,
                continuously: false
            );

            var pages = FolioPdfPartsMaker
                .GetPages(folioFront)
                .Concat(FolioPdfPartsMaker.GetPages(child))
                .ToList();
            Assert.That(
                pages.Select(p => p.GetAttribute("data-page-number")),
                Is.EqualTo(new[] { "", "1", "2", "1", "2" })
            );
            // The book's first page is fourth in the folio, a left-hand page.
            Assert.That(
                pages.Select(p => p.HasClass("side-right") ? "R" : "L"),
                Is.EqualTo(new[] { "R", "L", "R", "L", "R" })
            );
        }

        [Test]
        public void NumberPagesAcrossFolio_RightToLeft_FirstPageIsOnTheLeft()
        {
            var folioFront = MakeDom(
                "<div class='bloom-page A5Portrait' id='f1'/><div class='bloom-page A5Portrait' id='f2'/>"
            );

            FolioPdfPartsMaker.NumberPagesAcrossFolio(
                new List<(HtmlDom, bool)> { (folioFront, true) },
                "",
                true
            );

            var pages = FolioPdfPartsMaker.GetPages(folioFront);
            Assert.That(pages[0].HasClass("side-left"), Is.True);
            Assert.That(pages[1].HasClass("side-right"), Is.True);
        }

        [Test]
        public void FillTablesOfContents_RewritesTheListBoxAsTitlesAndFirstPrintedNumbers()
        {
            // The list's language 1 box, styled and holding what the Edit tab last wrote there.
            var folio = MakeDom(
                "<div class='bloom-page bloom-folio-toc' id='toc'><div class='marginBox'>"
                    + "<div class='bloom-translationGroup bloom-folio-toc-list' data-folio-book-ids='b a'>"
                    + "<div class='bloom-editable bloom-content1 TocList-style' lang='en'><p>Old text</p></div>"
                    + "<div class='bloom-editable' lang='fr'><p>Autre</p></div>"
                    + "</div></div></div>"
            );
            var bookA = MakeDom(
                "<div class='bloom-page' data-page-number=''/><div class='bloom-page' data-page-number='3'/>"
            );
            var bookB = MakeDom(
                "<div class='bloom-page' data-page-number=''/><div class='bloom-page' data-page-number='9'/>"
            );

            FolioPdfPartsMaker.FillTablesOfContents(
                new[] { folio },
                new List<(string, string, HtmlDom)>
                {
                    ("a", "Book A", bookA),
                    ("b", "Book B", bookB),
                }
            );

            var box = folio.RawDom.SafeSelectElements("//div[@lang='en']").Single();
            Assert.That(
                box.GetAttribute("class"),
                Does.Contain("TocList-style"),
                "the box keeps its style"
            );
            var lines = box.SafeSelectElements("p");
            Assert.That(lines.Length, Is.EqualTo(2), "one line per book, and the old text is gone");
            string Span(int i, string cls) =>
                lines[i].SafeSelectElements($"span[@class='{cls}']").Single().InnerText;
            // In the list's order, which is not the order the books were given in.
            Assert.That(Span(0, FolioPdfPartsMaker.kTocTitleClass), Is.EqualTo("Book B"));
            Assert.That(Span(0, FolioPdfPartsMaker.kTocPageNumberClass), Is.EqualTo("9"));
            Assert.That(Span(1, FolioPdfPartsMaker.kTocTitleClass), Is.EqualTo("Book A"));
            Assert.That(Span(1, FolioPdfPartsMaker.kTocPageNumberClass), Is.EqualTo("3"));
            Assert.That(
                folio.RawDom.SafeSelectElements("//div[@lang='fr']").Single().InnerText,
                Is.EqualTo("Autre"),
                "other languages' boxes are left alone"
            );
        }

        [Test]
        public void GetFolioBookIds_ReadsEveryTocPageInOrder()
        {
            var dom = MakeDom(
                "<div class='bloom-page bloom-frontMatter' id='cover'/>"
                    + "<div class='bloom-page bloom-folio-toc' id='toc1'><div class='marginBox'>"
                    + "<div class='bloom-translationGroup bloom-folio-toc-list' data-folio-book-ids='a  b'/>"
                    + "</div></div>"
                    + "<div class='bloom-page numberedPage' id='other'><div class='bloom-folio-toc-list' data-folio-book-ids='not-a-toc-page'/></div>"
                    + "<div class='bloom-page bloom-folio-toc' id='toc2'><div class='marginBox'>"
                    + "<div class='bloom-translationGroup bloom-folio-toc-list' data-folio-book-ids='c'/>"
                    + "</div></div>"
            );

            Assert.That(Bloom.Book.Book.GetFolioBookIds(dom), Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(Bloom.Book.Book.GetFolioTocPages(dom).Count(), Is.EqualTo(2));
        }

        [Test]
        public void GetFolioTocPages_BookWithoutTocPage_IsNotAFolio()
        {
            var dom = MakeDom("<div class='bloom-page bloom-folio-tocx numberedPage' id='p1'/>");
            Assert.That(Bloom.Book.Book.GetFolioTocPages(dom), Is.Empty);
        }
    }
}
