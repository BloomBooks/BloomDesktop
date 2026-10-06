// Strip, from a CLONE of the editing page, the chrome that only exists because the page is being
// edited -- so that what we hand C# is the page, not the editor.
//
// This is the one place the editor's chrome is removed before saving; C#
// (HtmlDom.ProcessPageAfterEditing) relies on it. Doing it here also keeps chrome out of what the
// page snapshot compares and sends (see pageSnapshot.ts), where it would make untouched pages look
// edited and cause a save on the way out.
//
// Besides the chrome itself (CKEditor's toolbars and qTip's bubbles, which are big and whose inline
// styles change while they animate; bloom-ui elements; cke_ classes), qTip's bookkeeping attributes
// churn between runs: the number in "qtip-0" depends on the order bubbles happen to be created.
// Books saved in the past may still contain them.
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

    // The ids paper.js leaves on the SVG Comical draws for speech bubbles. The SVG itself IS saved
    // (the reader has no Comical to redraw bubbles), but these ids get a fresh GUID on every
    // redraw, so any page with a bubble would look edited on every visit. Safe to drop: nothing
    // references them (no url(#...) or href="#..."), and they are not even unique.
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
