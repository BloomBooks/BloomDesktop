// Strip, from a CLONE of the editing page, the chrome that only exists because the page is being
// edited -- so that what we hand C# is the page, not the editor.
//
// Everything here is something HtmlDom.ProcessPageAfterEditing already removes on the C# side, and
// still does; this is not a replacement for it. What it changes is what we SEND, and that matters
// because of how the page snapshot decides to send anything at all: it posts whenever the gathered
// string differs from the last one it sent (see pageSnapshot.ts). Chrome in that string therefore
// made pages look edited when nobody had touched them --  C# would hold a snapshot, conclude there
// were unsaved changes, and save on the way out. Measured on one book, this is the difference
// between six of its eight pages re-saving themselves on every visit and none of them doing so.
//
// The offenders, in the order they were found:
//   * CKEditor’s toolbars and qTip’s bubbles, which those libraries append to the document body.
//     Big (they were 20 KB of a 26 KB page) and restless: a bubble fades in and slides into place,
//     so its inline style changes several times a second while it appears.
//   * bloom-ui elements inside the page -- the image buttons, the format cog.
//   * the cke_ classes CKEditor puts on each editable as it attaches.
//   * qTip’s bookkeeping attributes. These are the ones that churn between RUNS rather than
//     within one: the number in "qtip-0" is handed out in the order the bubbles happen to be
//     created, so it rarely matches the number the box was saved with. BloomHintBubbles has long
//     noted the wart -- "we unfortunately save in the file the qtip attributes that get added like
//     aria-describedby=qtip-0 and has-qtip=true" -- and BookData._attributesNotToCopy already
//     refuses to copy them into the data div, calling them "junk that gets left behind by UI".
//
// This is also the cleanup EditingModel.GetCleanCurrentPageFromBodyAndCss asks for in its
// "Enhance: it would be nice if ALL the cleanup happened in one place, probably the Javascript
// method that retrieves the page content".
//
// Nothing here may touch the live page; the caller passes a detached deep copy of document.body.
export function removeEditorChromeFromClone(cloneOfBody: HTMLElement) {
    for (const element of Array.from(
        cloneOfBody.querySelectorAll(".bloom-ui, .ui-resizable-handle"),
    )) {
        element.remove();
    }

    // CKEditor’s floating toolbars and qTip’s bubbles. Matching CKEditor by the "cke" class
    // rather than the id, because ids beginning "cke_" are also used for bookmark spans INSIDE the
    // text, which must not be removed here. bloomQtipUtils.cleanupBubbles() removes the same
    // div.qtip elements from the live page.
    for (const element of Array.from(
        cloneOfBody.querySelectorAll(".cke, div.qtip"),
    )) {
        element.remove();
    }

    // Only qtip-* values, so that an aria-describedby someone put there on purpose survives.
    for (const element of Array.from(
        cloneOfBody.querySelectorAll(
            "[aria-describedby], [data-hasqtip], [ariasecondary-describedby]",
        ),
    )) {
        if (element.getAttribute("aria-describedby")?.startsWith("qtip-"))
            element.removeAttribute("aria-describedby");
        if (
            element
                .getAttribute("ariasecondary-describedby")
                ?.startsWith("qtip-")
        )
            element.removeAttribute("ariasecondary-describedby");
        element.removeAttribute("data-hasqtip");
    }

    // The ids paper.js leaves on the SVG Comical draws for the speech bubbles. Unlike everything
    // else here this markup IS saved -- the SVG is what draws the bubbles in the reader, which has
    // no Comical to redraw them -- but the ids are regenerated with a fresh GUID every time the
    // SVG is, so an otherwise identical redraw produced a different page and any page with a
    // bubble looked edited on every visit, forever. On one test book that was five or six
    // snapshots per page visit, all of them this.
    //
    // Safe to drop rather than stabilise: nothing inside the SVG references them (no url(#...),
    // no href="#..."), the GUID appears nowhere else in the page, and they are not even unique --
    // "...outlineShape 1 1" occurs twice in one SVG. They are debris, not identifiers.
    for (const element of Array.from(
        cloneOfBody.querySelectorAll("svg.comical-generated [id]"),
    )) {
        element.removeAttribute("id");
    }

    // The classes CKEditor adds to each editable it attaches to (cke_editable, cke_focus, ...).
    for (const element of Array.from(
        cloneOfBody.querySelectorAll("[class*='cke_']"),
    )) {
        const kept = Array.from(element.classList).filter(
            (c) => !c.startsWith("cke_"),
        );
        if (kept.length === 0) element.removeAttribute("class");
        else element.setAttribute("class", kept.join(" "));
    }
}
