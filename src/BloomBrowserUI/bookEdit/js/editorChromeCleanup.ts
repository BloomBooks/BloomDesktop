// Strip, from a CLONE of the .bloom-page element, the chrome that only exists because the page is
// being edited -- so that what we hand C# is the page, not the editor.
//
// This is the one place the editor's chrome is removed before saving; C#
// (HtmlDom.ProcessPageAfterEditing) relies on it. Doing it here also keeps chrome out of what the
// page snapshot compares and sends (see pageSnapshot.ts), where it would make untouched pages look
// edited and cause a save on the way out.
//
// CKEditor's toolbars and qTip's bubbles are appended to the body, outside the page, so they never
// reach the clone. What is inside it: bloom-ui elements, resize handles, cke_ classes, and qTip's
// bookkeeping attributes, which churn between runs (the number in "qtip-0" depends on the order
// bubbles happen to be created). Books saved in the past may still contain those attributes.
//
// Nothing here may touch the live page; the caller passes a detached deep copy of it.
export function removeEditorChromeFromClone(clonedPage: HTMLElement) {
    for (const element of Array.from(
        clonedPage.querySelectorAll(".bloom-ui, .ui-resizable-handle"),
    )) {
        element.remove();
    }

    // Only qtip-* values, so that an aria-describedby someone put there on purpose survives.
    for (const element of Array.from(
        clonedPage.querySelectorAll(
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

    // The ids paper.js leaves on the SVG Comical draws for speech bubbles. The SVG itself IS saved
    // (the reader has no Comical to redraw bubbles), but these ids get a fresh GUID on every
    // redraw, so any page with a bubble would look edited on every visit. Safe to drop: nothing
    // references them (no url(#...) or href="#..."), and they are not even unique.
    for (const element of Array.from(
        clonedPage.querySelectorAll("svg.comical-generated [id]"),
    )) {
        element.removeAttribute("id");
    }

    // The classes CKEditor adds to each editable it attaches to (cke_editable, cke_focus, ...).
    for (const element of Array.from(
        clonedPage.querySelectorAll("[class*='cke_']"),
    )) {
        const kept = Array.from(element.classList).filter(
            (c) => !c.startsWith("cke_"),
        );
        if (kept.length === 0) element.removeAttribute("class");
        else element.setAttribute("class", kept.join(" "));
    }
}
