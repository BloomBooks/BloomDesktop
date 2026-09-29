using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SafeXml;
using L10NSharp;

namespace Bloom.Publish.PDF
{
    /// <summary>
    /// Thrown when a folio cannot be published as a PDF. The message is for the user: it names
    /// the books that stop it and says what is wrong with them. It is not a problem in Bloom, so it
    /// is shown in an ordinary message box (see PublishPdfApi), not reported as one.
    /// </summary>
    public class FolioPublishingException : ApplicationException
    {
        /// <summary>The same message as HTML, one problem to a line.</summary>
        public readonly string MessageHtml;

        public FolioPublishingException(string intro, IEnumerable<string> problems)
            : base(intro + Environment.NewLine + string.Join(Environment.NewLine, problems))
        {
            MessageHtml =
                System.Net.WebUtility.HtmlEncode(intro)
                + "<br/>"
                + string.Join("<br/>", problems.Select(System.Net.WebUtility.HtmlEncode));
        }
    }

    /// <summary>
    /// One HTML document that becomes one section of a folio's PDF: the folio's own pages before
    /// the books it holds, one book it holds, or the folio's own back matter. Each is rendered on
    /// its own, from its own book's folder and with its own book's stylesheets, and the PDFs are
    /// then joined (see PdfMaker).
    /// </summary>
    public class FolioPdfPart
    {
        /// <summary>The book whose pages these are.</summary>
        public Book.Book Book;

        /// <summary>The DOM to render, as Book.GetDomForPrinting made it, plus any blank page added
        /// at its start and the folio's page numbers and sides.</summary>
        public HtmlDom Dom;
    }

    /// <summary>
    /// Works out where a folio needs blank pages so that each of its pages is on the side it has
    /// when its own book is printed alone.
    /// </summary>
    public static class FolioPageSides
    {
        /// <summary>
        /// A section of the folio, described by how many pages it has and the position (from 0)
        /// that its first page has when its own book is printed alone. Even positions are
        /// right-hand pages in a left-to-right book.
        /// </summary>
        public struct PartShape
        {
            public int PageCount;
            public int PositionWhenPrintedAlone;

            public PartShape(int pageCount, int positionWhenPrintedAlone)
            {
                PageCount = pageCount;
                PositionWhenPrintedAlone = positionWhenPrintedAlone;
            }
        }

        /// <summary>
        /// How many blank pages go before each part. When addBlankPages is true, a part whose
        /// first page would otherwise fall on the other side from the one it has when printed
        /// alone gets one blank page before it; when false, no part gets any. The blank pages
        /// themselves count as pages of the folio.
        /// </summary>
        public static int[] GetBlankPagesBefore(IReadOnlyList<PartShape> parts, bool addBlankPages)
        {
            var result = new int[parts.Count];
            var position = 0;
            for (var i = 0; i < parts.Count; i++)
            {
                if (addBlankPages && (position - parts[i].PositionWhenPrintedAlone) % 2 != 0)
                    result[i] = 1;
                position += result[i] + parts[i].PageCount;
            }
            return result;
        }
    }

    /// <summary>
    /// Builds the sections of a folio's PDF (see FolioPdfPart): checks that every book the folio
    /// lists can be published with it, makes each section's DOM, adds the blank pages that keep
    /// every page on its own side, and numbers the pages and sets their sides across the whole folio.
    /// </summary>
    public class FolioPdfPartsMaker
    {
        private readonly Book.Book _folio;
        private readonly BookCollection _collection;
        private readonly BookServer _bookServer;
        private readonly Layout _pageLayout;
        private readonly PublishModel.BookletPortions _portion;

        /// <summary>
        /// The class of a page added to keep the following pages on their own side.
        /// </summary>
        public const string kBlankPageClass = "bloom-folio-blank";

        public FolioPdfPartsMaker(
            Book.Book folio,
            BookCollection collection,
            BookServer bookServer,
            Layout pageLayout,
            PublishModel.BookletPortions portion
        )
        {
            _folio = folio;
            _collection = collection;
            _bookServer = bookServer;
            _pageLayout = pageLayout;
            _portion = portion;
        }

        /// <summary>
        /// Make the sections of the folio's PDF, in order. Throws FolioPublishingException, naming
        /// the books, when any book the folio lists is missing, cannot be read, is itself a folio,
        /// or differs from the folio in page size, orientation or full bleed.
        /// </summary>
        public List<FolioPdfPart> MakeParts()
        {
            var children = GetChildBooks();
            CheckChildrenMatchFolio(children);

            var settings = _folio.BookInfo.PublishSettings.Folio;
            var includeBackgroundColors = _folio.UserPrefs.IncludeBackgroundColors;
            var parts = new List<FolioPdfPart>();
            var positionsWhenAlone = new List<int>();

            // The folio's own pages before the books: its front matter and its content pages,
            // which include the table of contents unless that is not to be printed.
            var front = _folio.GetDomForPrinting(_portion, _pageLayout);
            var folioPagesBeforeBackMatter = 0;
            foreach (var page in GetPages(front))
            {
                if (
                    page.HasClass("bloom-backMatter")
                    || (
                        !settings.ShowTableOfContents && page.HasClass(Book.Book.kFolioTocPageClass)
                    )
                )
                    RemovePage(page);
                else
                    folioPagesBeforeBackMatter++;
            }
            parts.Add(new FolioPdfPart { Book = _folio, Dom = front });
            positionsWhenAlone.Add(0);

            foreach (var child in children)
            {
                // This sets bloom-content1 and the like on the right languages. It happens anyway once
                // a book has been opened in the Edit tab, but a book that never has been shows every
                // language's text without it.
                child.UpdateEditableAreasOfElement(child.OurHtmlDom);
                // Each book is printed whole, covers included, as its own PDF would be. A booklet's
                // inside pages leave out covers, but these covers are inside pages of the folio.
                var dom = child.GetDomForPrinting(
                    PublishModel.BookletPortions.AllPagesNoBooklet,
                    _pageLayout,
                    includeBackgroundColors
                );
                // Where the first page this folio prints of the book sits when the book is printed
                // alone: its cover, or, when its front matter is left out, its first content page.
                var positionWhenAlone = 0;
                if (!settings.KeepEachBooksXmatter)
                {
                    foreach (var page in GetPages(dom))
                    {
                        if (IsXmatter(page))
                        {
                            if (page.HasClass("bloom-frontMatter"))
                                positionWhenAlone++;
                            RemovePage(page);
                        }
                    }
                }
                // A book with nothing left to print takes no pages.
                if (!GetPages(dom).Any())
                    continue;
                parts.Add(new FolioPdfPart { Book = child, Dom = dom });
                positionsWhenAlone.Add(positionWhenAlone);
            }

            var back = _folio.GetDomForPrinting(_portion, _pageLayout);
            foreach (var page in GetPages(back))
            {
                if (!page.HasClass("bloom-backMatter"))
                    RemovePage(page);
            }
            if (GetPages(back).Any())
            {
                parts.Add(new FolioPdfPart { Book = _folio, Dom = back });
                positionsWhenAlone.Add(folioPagesBeforeBackMatter);
            }

            var shapes = parts
                .Select(
                    (part, i) =>
                        new FolioPageSides.PartShape(
                            GetPages(part.Dom).Count,
                            positionsWhenAlone[i]
                        )
                )
                .ToList();
            var blanks = FolioPageSides.GetBlankPagesBefore(shapes, settings.AddBlankPages);
            for (var i = 0; i < parts.Count; i++)
            {
                for (var b = 0; b < blanks[i]; b++)
                    InsertBlankPageAtStart(parts[i].Dom);
            }

            NumberPagesAcrossFolio(
                parts.Select(part => (part.Dom, part.Book == _folio)).ToList(),
                _folio.CollectionSettings.CharactersForDigitsForPageNumbers,
                _folio.IsPrimaryLanguageRtl,
                settings.NumberContinuously
            );
            FillTablesOfContents(
                parts.Where(part => part.Book == _folio).Select(part => part.Dom),
                parts
                    .Where(part => part.Book != _folio)
                    .Select(part => (part.Book.ID, part.Book.TitleBestForUserDisplay, part.Dom))
                    .ToList()
            );
            return parts;
        }

        /// <summary>
        /// The books the folio lists, in its order. Throws FolioPublishingException when any is
        /// missing from the collection, cannot be read, or is itself a folio.
        /// </summary>
        private List<Book.Book> GetChildBooks()
        {
            var bookInfos = _collection.GetBookInfos().ToList();
            var titles = Book.Book.GetFolioBookTitles(_folio.OurHtmlDom);
            var problems = new List<string>();
            var children = new List<Book.Book>();
            foreach (var id in _folio.GetFolioBookIds())
            {
                var info = bookInfos.FirstOrDefault(b => b.Id == id);
                if (info == null)
                {
                    // Named by the last title the table of contents page recorded for it. A book
                    // chosen before titles were recorded can only be named by its id.
                    problems.Add(
                        string.Format(
                            LocalizationManager.GetString(
                                "Folio.BookNoLongerInCollection",
                                "\"{0}\" is no longer in this collection.",
                                "{0} is the title of a book."
                            ),
                            titles.TryGetValue(id, out var title) ? title : id
                        )
                    );
                    continue;
                }
                var child = _bookServer.GetBookFromBookInfo(info);
                if (child is ErrorBook || child.HasFatalError)
                {
                    problems.Add(
                        string.Format(
                            LocalizationManager.GetString(
                                "PublishTab.Folio.BookCannotBeRead",
                                "\"{0}\" cannot be read.",
                                "{0} is the name of a book's folder."
                            ),
                            System.IO.Path.GetFileName(info.FolderPath)
                        )
                    );
                    continue;
                }
                if (child.IsFolio)
                {
                    problems.Add(
                        string.Format(
                            LocalizationManager.GetString(
                                "PublishTab.Folio.BookIsFolio",
                                "\"{0}\" is itself a folio.",
                                "{0} is the title of a book."
                            ),
                            child.TitleBestForUserDisplay
                        )
                    );
                    continue;
                }
                children.Add(child);
            }
            ThrowIfProblems(problems);
            return children;
        }

        /// <summary>
        /// Throw FolioPublishingException, naming each book and what differs, when any book has a
        /// different page size, orientation or full-bleed setting from the folio's PDF. One PDF
        /// must have one page size to be imposed as a booklet and to be trimmed and bound, and a
        /// book shown at a size it was not made for reflows with nobody having checked the result.
        /// </summary>
        private void CheckChildrenMatchFolio(List<Book.Book> children)
        {
            var problems = new List<string>();
            var folioSize = _pageLayout.SizeAndOrientation;
            foreach (var child in children)
            {
                var childSize = child.GetLayout().SizeAndOrientation;
                if (
                    childSize.PageSizeName != folioSize.PageSizeName
                    || childSize.IsLandScape != folioSize.IsLandScape
                )
                {
                    problems.Add(
                        string.Format(
                            LocalizationManager.GetString(
                                "PublishTab.Folio.BookSizeDiffers",
                                "\"{0}\" is {1}, but this folio is {2}.",
                                "{0} is the title of a book. {1} and {2} are page sizes and orientations, such as A5Portrait."
                            ),
                            child.TitleBestForUserDisplay,
                            childSize.ToString(),
                            folioSize.ToString()
                        )
                    );
                }
                if (child.FullBleed != _folio.FullBleed)
                {
                    problems.Add(
                        string.Format(
                            _folio.FullBleed
                                ? LocalizationManager.GetString(
                                    "PublishTab.Folio.BookNotFullBleed",
                                    "\"{0}\" is not set up for full bleed printing, but this folio is.",
                                    "{0} is the title of a book."
                                )
                                : LocalizationManager.GetString(
                                    "PublishTab.Folio.BookFullBleed",
                                    "\"{0}\" is set up for full bleed printing, but this folio is not.",
                                    "{0} is the title of a book."
                                ),
                            child.TitleBestForUserDisplay
                        )
                    );
                }
            }
            ThrowIfProblems(problems);
        }

        private static void ThrowIfProblems(List<string> problems)
        {
            if (problems.Count == 0)
                return;
            var intro = LocalizationManager.GetString(
                "PublishTab.Folio.CannotMakePdf",
                "Bloom cannot make a PDF of this folio:"
            );
            throw new FolioPublishingException(intro, problems);
        }

        /// <summary>
        /// The pages of a printing DOM, in order. Under full bleed each page is wrapped in a media
        /// box, so they are not all children of the body.
        /// </summary>
        public static List<SafeXmlElement> GetPages(HtmlDom dom)
        {
            return dom
                .RawDom.SafeSelectElements(
                    "//div[contains(concat(' ', @class, ' '), ' bloom-page ')]"
                )
                .ToList();
        }

        /// <summary>
        /// Remove a page from a printing DOM, and its full-bleed media box if it has one.
        /// </summary>
        private static void RemovePage(SafeXmlElement page)
        {
            var toRemove =
                page.ParentNode is SafeXmlElement parent && parent.HasClass("bloom-mediaBox")
                    ? parent
                    : page;
            toRemove.ParentNode.RemoveChild(toRemove);
        }

        /// <summary>
        /// Put an empty page before the first page of a printing DOM, at the same size, and inside
        /// a media box like the others if they have one. It counts as a page for numbering but
        /// shows no number.
        /// </summary>
        public static void InsertBlankPageAtStart(HtmlDom dom)
        {
            var firstPage = GetPages(dom).First();
            var doc = dom.RawDom;
            var pageSizeClass = firstPage
                .GetAttribute("class")
                .Split(' ')
                .First(x => x.EndsWith("Portrait") || x.EndsWith("Landscape"));
            var blank = doc.CreateElement("div");
            blank.SetAttribute(
                "class",
                $"bloom-page {kBlankPageClass} countPageButDoNotShowNumber {pageSizeClass}"
            );
            blank.SetAttribute("id", Guid.NewGuid().ToString());
            var marginBox = doc.CreateElement("div");
            marginBox.SetAttribute("class", "marginBox");
            blank.AppendChild(marginBox);

            if (firstPage.ParentNode is SafeXmlElement box && box.HasClass("bloom-mediaBox"))
            {
                var newBox = doc.CreateElement("div");
                newBox.SetAttribute("class", box.GetAttribute("class"));
                newBox.AppendChild(blank);
                box.ParentNode.InsertBefore(newBox, box);
            }
            else
            {
                firstPage.ParentNode.InsertBefore(blank, firstPage);
            }
        }

        /// <summary>
        /// The class of the span holding a book's title in a table of contents entry.
        /// </summary>
        public const string kTocTitleClass = "bloom-folio-toc-title";

        /// <summary>
        /// The class of the span holding a book's first page number in a table of contents entry.
        /// </summary>
        public const string kTocPageNumberClass = "bloom-folio-toc-page-number";

        /// <summary>
        /// The class of one line of a printed table of contents: a book's title, then its number.
        /// </summary>
        public const string kTocEntryClass = "bloom-folio-toc-entry";

        /// <summary>
        /// Rewrite the text of each table of contents list in `folioDoms` (the language 1 box of the
        /// list's translation group) as one line per book it names: the book's title and the first
        /// page number printed in that book, which the pages must already have (see
        /// NumberPagesAcrossFolio). The box keeps its own classes and so its style. `books` gives
        /// each held book's id, title and printing DOM.
        /// </summary>
        internal static void FillTablesOfContents(
            IEnumerable<HtmlDom> folioDoms,
            IReadOnlyList<(string id, string title, HtmlDom dom)> books
        )
        {
            var byId = books.GroupBy(b => b.id).ToDictionary(g => g.Key, g => g.First());
            foreach (var dom in folioDoms)
            {
                var tocPages = GetPages(dom).Where(p => p.HasClass(Book.Book.kFolioTocPageClass));
                foreach (var tocPage in tocPages)
                {
                    foreach (
                        var list in tocPage.SafeSelectElements(
                            $".//div[contains(concat(' ', @class, ' '), ' {Book.Book.kFolioTocListClass} ')]"
                        )
                    )
                    {
                        var box = list.SafeSelectElements(
                                ".//div[contains(concat(' ', @class, ' '), ' bloom-editable ') and contains(concat(' ', @class, ' '), ' bloom-content1 ')]"
                            )
                            .FirstOrDefault();
                        if (box == null)
                            continue;
                        foreach (var child in box.ChildNodes.ToArray())
                            box.RemoveChild(child);
                        var ids = list.GetAttribute(Book.Book.kFolioBookIdsAttribute)
                            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var id in ids)
                        {
                            if (!byId.TryGetValue(id, out var book))
                                continue;
                            var firstNumber =
                                GetPages(book.dom)
                                    .Select(p => p.GetAttribute("data-page-number"))
                                    .FirstOrDefault(n => !string.IsNullOrEmpty(n))
                                ?? "";
                            var line = box.OwnerDocument.CreateElement("p");
                            line.SetAttribute("class", kTocEntryClass);
                            AddSpan(line, kTocTitleClass, book.title);
                            AddSpan(line, kTocPageNumberClass, firstNumber);
                            box.AppendChild(line);
                        }
                    }
                }
            }
        }

        private static void AddSpan(SafeXmlElement line, string className, string text)
        {
            var span = line.OwnerDocument.CreateElement("span");
            span.SetAttribute("class", className);
            span.InnerText = text;
            line.AppendChild(span);
        }

        private static bool IsXmatter(SafeXmlElement page) =>
            page.HasClass("bloom-frontMatter") || page.HasClass("bloom-backMatter");

        /// <summary>
        /// Number the folio's pages and set each page's side from its position in the folio.
        /// `parts` are the folio's printing DOMs in order, each saying whether it holds the folio's
        /// own pages. When `continuously`, the numbers run on through the whole folio, and a held
        /// book's own restart of numbering is ignored, so its numbers carry on from the book before.
        /// Otherwise the folio's own pages are numbered as one sequence, and each held book keeps
        /// the numbers it has when printed alone.
        /// </summary>
        internal static void NumberPagesAcrossFolio(
            IReadOnlyList<(HtmlDom dom, bool isFolioOwnPages)> parts,
            string charactersForDigits,
            bool languageIsRightToLeft,
            bool continuously = true
        )
        {
            var allPages = new List<SafeXmlElement>();
            var folioOwnPages = new List<SafeXmlElement>();
            foreach (var part in parts)
            {
                var pages = GetPages(part.dom);
                if (part.isFolioOwnPages)
                    folioOwnPages.AddRange(pages);
                else if (continuously)
                {
                    foreach (var page in pages)
                        page.RemoveClass("bloom-startPageNumbering");
                }
                allPages.AddRange(pages);
            }
            HtmlDom.UpdatePageNumberAndSideClassOfPages(
                charactersForDigits,
                languageIsRightToLeft,
                continuously ? allPages : folioOwnPages
            );
            if (!continuously)
            {
                // That set the folio's own pages' sides as if they stood alone; set every page's
                // side from its place in the whole folio.
                for (var i = 0; i < allPages.Count; i++)
                    HtmlDom.UpdateSideClass(allPages[i], i, languageIsRightToLeft);
            }
        }
    }
}
