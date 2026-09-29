# Folio: publishing a collection's books as one book

Status: proposal, not started. Decisions so far are recorded in each section; remaining questions
are at the end.

## What exists today

A book whose HTML has `<meta name="folio" content="true">` is a folio (`Book.IsFolio`,
`src/BloomExe/Book/Book.cs:3035`). When it is published as a PDF, `Book.GetDomForPrinting` calls
`AddChildBookContentsToFolio` (`Book.cs:4718`), which copies the content pages of every other
non-folio book in the collection into the folio's DOM after the folio's last content page. That
DOM is then rendered as one PDF by the normal pipeline in `PublishModel.MakeFinalHtmlForPdfMaker`
and `PdfMaker.MakePdf`. There is no UI; the meta tag has to be added by hand.

## Problems with that approach

1. **Credits and licensing are dropped.** Child front and back matter are skipped, so each book's
   title page, copyright, license and image credits never reach the PDF. That breaks CC-BY
   attribution.
2. **Child styling is lost.** `HtmlDom.AddStylesheetFromAnotherBook` links only template
   stylesheets. Format-dialog styles (`userModifiedStyles`), `customBookStyles.css` and each
   child's `appearance.css` are ignored, so every child page takes the folio's fonts, sizes,
   theme and colors.
3. **Pages land on the wrong side.** `side-left`/`side-right` are stored per page, computed within
   the child (`HtmlDom.UpdatePageNumberAndSideClassOfPages`), and never recomputed for the folio.
   A book that follows an odd number of pages gets its gutter margins on the wrong edge.
4. **Page size is forced without checking.** `SizeAndOrientation.UpdatePageSizeAndOrientationClasses`
   applies the folio's layout to every page. A child made at another size reflows with nothing
   checking for overflow, and canvas elements move.
5. **No choice of books or order.** Every non-folio book is included, in folder-name order from
   `NaturalSortComparer`, which SIL-LEAD SHRP depends on (`NaturalSortComparer.cs:80`).
6. **Broken books.** `BookServer.GetBookFromBookInfo` returns an `ErrorBook` for a book that fails
   to load; nothing handles that.
7. **Image paths need special cases.** Image URLs are rewritten to `../<childFolder>/...`, and
   `BloomServer` serves any rooted path that exists (`BloomServer.cs:1251`) to make that work.
   Only `img` and background images are rewritten.
8. **One huge DOM.** All pages of all books, with full-resolution images, go through one WebView2
   print job.
9. **Per-book publishing rules run against the folio, not the child.**
   `PublishHelper.RemovePagesByFeatureSystem` and `RemoveClassesAndAttrsToDisableFeatures` are
   called with the folio as the book, so a child with a subscription feature is judged by the
   folio's settings.
10. **Full bleed mismatch.** A child that is not full bleed, inside a full-bleed folio, gets the
    bleed page box with nothing in the bleed area (and the reverse loses the bleed).
11. **PDF only.** BloomPub, ePUB and Bloom Library upload see only the folio's own pages.
    Uploading a folio would publish just the wrapper.
12. **Nothing marks where one book ends.** No title page, no divider, no table of contents.

Team Collections are out of scope for this work, because that feature is about to change a lot.

## Proposed design

### 1. Folio options

The folio carries these settings, each with a default:

| Setting | Default | Alternative |
|---|---|---|
| Child front and back matter | **Keep all of it**: each child appears exactly as its own PDF would, covers included | **Chapters of one book**: strip all xmatter from every child; only the folio's xmatter is printed |
| Page numbers | **Continuous** across the whole folio | Each book keeps the numbers it has when printed alone |
| Blank pages | **Add a blank** where needed so every page is on the side it has when its book is printed alone | Pack books with no blanks |
| Table of contents | **Show** the table of contents pages in the published book | Leave them out; they still choose the books (section 6) |

With the defaults, the folio PDF is the child PDFs glued together, plus the folio's own cover and
whatever pages the folio itself provides (such as the table of contents, section 6).

Under "chapters of one book" the children's credits are ignored along with the rest of their
xmatter. The user is expected not to fill them in; the folio's own credits page covers the whole
book.

The options appear in a new Folio section of Book Settings
(`bookEdit/bookAndPageSettings/BookSettingsConfigrPages.tsx`), shown only when the book is a folio.
They are saved in the folio's `meta.json` through the existing `book/settings` API
(`BookSettingsApi.cs`).

### 2. Render each book separately, then merge the PDFs

Instead of one merged DOM, make one HTML document per part and one PDF per document:

- the folio's front part: its front matter and any folio content pages (the table of contents);
- one part per child book, served from **that child's own folder** with **its own stylesheets**,
  built by the same code that prepares a single book (`GetDomForPrinting` +
  `MakeFinalHtmlForPdfMaker`), so feature removal, AI-content removal, game "play" mode and
  full-bleed markup all run against the right book;
- the folio's back part: its back matter.

Then:

1. render each part with `WebView2PdfMaker`, and remove the extra full-bleed blank pages per part
   (`AddMetadataAndRemoveBlankPagesIfNecessary` compares against that part's `HtmlPageCount`);
2. concatenate the part PDFs with PdfSharp (already a dependency);
3. run Ghostscript once on the merged file (compression, CMYK profile);
4. add the folio's metadata;
5. run booklet imposition once on the merged file.

This removes problems 2, 7, 8 and 9 outright: each part renders exactly as the child book would by
itself. The `../<childFolder>/` rewriting and the rooted-path case in `BloomServer` can be deleted.

**Booklet imposition works unchanged** because every layouter (`SideFoldBookletLayouter`,
`CutLandscapeLayout`, ...) takes one finished PDF and doesn't care how it was produced. The merge
has to happen before imposition, and all parts must have the same page size (section 5).

The booklet portions need care:

- `BookletPortions.BookletCover` renders only the folio's covers; no child parts.
- `BookletPortions.BookletPages` renders the folio's non-cover pages and **every child part as
  `AllPagesNoBooklet`**. `GetDomForPrinting` deletes every page with the `cover` class in the
  `BookletPages` pass; under the default "keep all xmatter" option the child covers are inside
  pages of the folio and must survive.
- The Calendar layout makes no sense for a folio; hide it.

**Cost.** `WebView2PdfMaker` is launched once per HTML file
(`MakePdfUsingExternalPdfMakerProgram.SetArguments`). Launching it once per book adds a startup
per book. Better: teach `WebView2PdfMaker` to accept a list of input/output pairs and render them
in one WebView2 session. Progress reads "Book 3 of 40", and cancel is checked between parts. A
single very long child could be split further by page range; nothing downstream would change.

**Fonts.** Each part embeds its own font subsets, so the merged file carries duplicates. Measure
whether the Ghostscript pass on the merged file removes them before doing anything about it.

### 3. Keep each page on the side it has when printed alone (default)

A page's side when its book is printed alone is given by its position among that book's printed
pages. Under the default "keep all xmatter" option every child starts with its front cover, on a
right-hand page. Under "chapters of one book" a child starts with its first content page, on the
side it would have after its own front matter.

Before each child part, if the next position in the folio is on the other side, insert one blank
page. The blank is a real empty `bloom-page` at the start of that part's HTML, so it gets the right
size and bleed. With the "pack books" option no blanks are inserted.

In both cases, recompute `side-left`/`side-right` from each page's real position in the folio
(the rule in `HtmlDom.UpdateSideClass`, including right-to-left), so margins match the physical
side. Page counts per part are known before rendering, from the DOM, because one HTML page is one
PDF page after the full-bleed fix-up.

Inserted blanks count toward the booklet total, so they can change how many sheets get printed.

### 4. Page numbers

Numbers are assigned before rendering by setting `data-page-number` in each part's DOM, using the
collection's digits (`HtmlDom.GetNumberStringRepresentation`) and the counting rule Bloom already
uses inside one book (`HtmlDom.GetPageNumberOfPage`), applied across the whole folio. With the
"each book keeps its own" option, each child part is left as it would be when printed alone.

### 5. One page size per folio

Every child must have the same page size, orientation and full-bleed setting as the folio's PDF.
If any child differs, the PDF is not made, and the Publish tab lists the books that differ and
what differs. Reasons:

- every booklet layouter takes one `PaperTarget` and assumes every page has the same size;
- a printer trims and binds one size;
- rendering a child at another size reflows its pages and moves canvas elements, and nobody would
  have checked the result.

Because every child is rendered at its own size, which is the folio's size, no overflow check is
needed at publish time.

### 6. The Folio template and its table of contents pages

A new **Folio** template book, under `src/content/templates/template books/`, is how a user starts
a folio. It comes with one table of contents page. That page does two jobs: it is where the user
chooses the folio's books, and it is the table of contents in the published book.

The page reuses the book grid. A link grid page (`src/BloomBrowserUI/bookEdit/js/linkGrid.ts`)
holds `bloom-bookButton` elements with `data-bloom-book-id`, and double-clicking it opens
`BookGridSetupDialog`, which shows every book in the collection (`BookSourcesList`) beside the
chosen ones in order (`BookTargetList`). `AppBuilderChooseBooksDialog` already reuses
`BookGridSetup` for the Reading App Builder with `targetLabel="books-in-app"`. The table of
contents page does the same with a new `targetLabel` such as `"books-in-folio"`:

- in the Edit tab it lists the chosen books in order; double-clicking opens the dialog;
- the chosen books and their order live in the page's DOM, as in a link grid, so they travel with
  the book and are saved by ordinary page saving;
- in the PDF each entry shows the book's title and its first page number, which are known before
  rendering (section 4). With the "leave out the table of contents" option, these pages are not
  printed, though they still decide which books go in.

**More than one table of contents page.** Extra pages exist only because a long list may not fit
on one page; they don't divide the folio into parts. The template's page is also available from
Add Page, so a user whose list doesn't fit can add more. The folio's books are the books on all its table of
contents pages, in page order and then in list order within each page. A book can be on only one
of them: the dialog leaves out books already chosen on another page. All table of contents pages
print where they sit in the folio, before the first child book.

**Folios aren't choosable.** The dialog leaves out other folios, so a folio can't contain one.

**What makes a book a folio.** Today the HTML `folio` meta tag is the source of truth and
`meta.json`'s `folio` is a copy, kept because `meta.json` is faster to read than the HTML
(the comment at `Book.cs:1196`). Replace the meta tag with a rule: a book is a folio if it has at
least one table of contents page. `meta.json` keeps its `folio` copy, updated the same way, so the
collection can tell which books are folios without opening their HTML. The meta tag is then no use
and goes away, along with the `tags.txt` conversion in `Book.ConvertTagsToMetaData`. A book someone
marked as a folio by hand (SHRP's, from 2013) stops gathering books; its owners would make a new
folio from the template.

A missing or broken child (`ErrorBook`) is reported by name and the PDF is not made; failing is
better than a PDF with a book silently missing.

### 7. Other publish targets

For now, BloomPub, ePUB and Bloom Library upload of a folio show a message that folios publish as
PDF only. The next section covers what a single-document folio would need.

## CSS `@scope`, and when it would be needed

With separate rendering (section 2), each book keeps its own document and stylesheets, so the PDF
path needs no style isolation at all.

Isolation matters only if a folio must become **one HTML document**: a BloomPub or ePUB of a folio.
The CSS feature for that is the `@scope` at-rule (CSS Cascading and Inheritance Level 6): each
child's stylesheets would be inlined inside
`@scope ([data-folio-book="<bookInstanceId>"]) { ... }`, wrapped around that child's pages.
Things to know before going that way:

- `@scope` needs Chromium 118. Bloom's minimum WebView2 is 112
  (`WebView2Browser.kMinimumWebView2Version`), and Bloom Player runs in Android WebViews that may
  be older still.
- `@scope` doesn't raise specificity. An unscoped folio rule with a stronger selector still wins
  over a child's scoped rule; scope proximity only breaks ties.
- Many Bloom rules select on body classes (`.publishingWithFullBleed`, `.pdfPublishMode`, the
  right-to-left class). A prototype must check whether such selectors still match inside `@scope`
  when the body is outside the scope root.
- `@font-face` and `@page` can't be scoped.
- `appearance.css` already defines its variables on `.bloom-page` rather than `:root`, because Bloom
  Player polyfills scoped styles (`AppearanceSettings.cs:682`). Bloom Player's polyfill is the first
  thing to study before writing anything new.

Recommendation: don't build this until someone needs a folio as a BloomPub.

## Validation

### The test collection

All checks use a collection built from scratch by a script in `src/BloomE2E/scripts/`, using the
e2e helpers (`makeBookFromTemplate`, `setPageSize`, `setCopyrightHolder`, ...). The same code builds
the collection for live checks during development and for the e2e specs, and none of it depends on
a developer's own collections. For live checks the script writes to a gitignored folder under
`output/` and Bloom is launched from the worktree on that collection (`run-bloom` skill). The e2e
specs build what they need at run time through `collectionSpec`, as `src/BloomE2E/README.md` asks.

Each book is there for a reason and has as few pages as that reason needs:

| Book | Checks |
|---|---|
| 3 content pages | odd page count, so the next book needs a blank |
| Format-dialog font size, a different appearance theme and cover color | each book's styling survives |
| CC-BY license, image credits, spaces and non-ASCII in folder and image names | credits reach the PDF; image paths resolve |
| a canvas element page and a game page | publishing rules run against each book |
| a second folio | not offered in the book chooser |
| a book with broken HTML | reported by name, no PDF |
| another page size; another full-bleed setting | PDF refused, book named |
| enough books to overflow one table of contents page | second table of contents page |

### What is checked in the PDF

- page count and the size of every page, including inserted blanks;
- text by page: page numbers, table of contents titles and numbers, each book's credits;
- page side, from text positions: the gutter margin is on the correct edge;
- booklet: sheet count, and each sheet holds pages n and total + 1 − n;
- styling: pages rasterized with the Ghostscript Bloom ships, compared with the same books
  published alone. That also confirms the default output is each book's own PDF glued together.

### Layers

1. **C# unit tests (`BloomTests`)** for the code that decides where blanks go and what side and
   number each page gets, across every option combination, right-to-left, and each booklet
   portion; PDF merging with small PdfSharp-made PDFs; the same-size check.
2. **Front-end tests**: vitest for the table of contents page's book list; the component-test
   harness for `BookGridSetup` with `"books-in-folio"`, showing folios and books already on another
   table of contents page are left out.
3. **Live checks** in a running Bloom on the test collection while building, driven through the
   API (`publish/pdf/simple`, `pages`, `cover` in `PublishPdfApi.cs`).
4. **E2E specs**, run once at the end of each phase: a small folio made from the template, books
   chosen through the dialog, published as a booklet; then "chapters of one book" with the table
   of contents hidden. These join the nightly suite.
5. **Scale check, last of all**: the script copies one book to about 40 books with full-resolution
   images; measure time and peak memory with the production bundle, against the current single-DOM
   code on the same collection. It is a spec that runs only when asked for, never in the nightly
   e2e suite.

## Phases

1. **Separate rendering and merge** (section 2), taking the book list from table of contents pages
   written by hand in test fixtures, plus the side rule (section 3),
   numbering (section 4) and the same-size check (section 5), with the options at their defaults.
   Delete `AddChildBookContentsToFolio`, the image-path rewriting and the `BloomServer` rooted-path
   case. Unit tests in `BloomTests` for part planning: blank insertion, sides, numbering,
   right-to-left, both xmatter options. One e2e test that publishes a small folio (three short
   books, one needing a blank) as a booklet.
2. **Folio template and table of contents pages** (section 6): the template, the page,
   `BookGridSetup` reuse, the "has a table of contents page" rule replacing the meta tag, the
   printed table of contents, strings in `DistFiles/localization/en/`.
3. **Book Settings Folio section and the non-default options** (section 1): "chapters of one
   book", per-book numbering, packing without blanks, leaving out the table of contents.
4. Messages for the other publish targets (section 7).
5. Update `template.starter.nothingautomatic` in `Template Starter/ReadMe-en.md`, which still calls
   the feature "forthcoming", and write the docs page.
6. Scale check (Validation, layer 5).

## Open questions

None at present.
