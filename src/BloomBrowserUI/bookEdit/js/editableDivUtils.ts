/// <reference path="../../typings/jquery/jquery.d.ts" />
import { get, postString } from "../../utils/bloomApi";
import $ from "jquery";

interface qtipInterface extends JQuery {
    qtip(options: string): JQuery;
}

// If the current selection is an insertion point in editableDiv (which MUST be a div!), return the index of the selection,
// as a character offset within the text of editableDiv. If the selection is not in editableDiv, return -1.
export class EditableDivUtils {
    public static normalizeBloomLineBreakSpansInElement(root: Element): void {
        Array.from(root.querySelectorAll("span.bloom-linebreak")).forEach(
            (span) => {
                if (span.hasChildNodes()) {
                    const fragment = document.createDocumentFragment();
                    while (span.firstChild) {
                        postString(
                            "common/logger/writeEvent",
                            `Found content inside a bloom-linebreak span: ${span.firstChild.textContent}. Moving it out.`,
                        );
                        fragment.appendChild(span.firstChild);
                    }
                    span.parentNode?.insertBefore(fragment, span);
                }
            },
        );
    }

    public static getElementSelectionIndex(editableDiv: HTMLElement): number {
        const page: HTMLIFrameElement | null = <HTMLIFrameElement | null>(
            parent.window.document.getElementById("page")
        );
        if (!page || !page.contentWindow) return -1; // unit testing? Anyway there is no selection, so not in editableDiv.

        const selection = page.contentWindow.getSelection();
        if (!selection || !selection.anchorNode) return -1;
        const active = $(selection.anchorNode).closest("div").get(0);
        if (active != editableDiv) return -1; // the selection is not in editableDiv at all
        if (!active || selection.rangeCount == 0) {
            return -1;
        }
        const myRange = selection.getRangeAt(0).cloneRange();
        myRange.setStart(active, 0);
        return myRange.toString().length;
    }

    public static selectAtOffset(node: Node, offset: number): void {
        const page: HTMLIFrameElement | null = <HTMLIFrameElement | null>(
            parent.window.document.getElementById("page")
        );
        if (!page || !page.contentWindow) return;
        const iframeWindow: Window = page.contentWindow;
        const selection1 = iframeWindow.getSelection();
        if (selection1) {
            const range = iframeWindow.document.createRange();
            range.setStart(node, offset);
            range.setEnd(node, offset);
            selection1.removeAllRanges();
            selection1.addRange(range);
        }
    }

    /**
     * Make a selection in the specified node at the specified offset.
     * If divBrCount is >=0, we expect to make the selection offset characters into node itself
     * (typically the root div). After traversing offset characters, we will try to additionally
     * traverse divBrCount <br> elements.
     * @param node
     * @param offset
     */
    public static makeSelectionIn(
        node: Node,
        offset: number,
        divBrCount: number,
        atStart: boolean,
    ): boolean {
        if (node.nodeType === 3) {
            // drilled down to a text node. Make the selection.
            EditableDivUtils.selectAtOffset(node, offset);
            return true;
        }

        let i = 0;
        let childNode;
        let len;

        for (; i < node.childNodes.length && offset >= 0; i++) {
            childNode = node.childNodes[i];
            len = childNode.textContent.length;
            if (divBrCount >= 0 && len == offset) {
                // We want the selection after childNode itself, plus if possible an additional divBrCount <br> elements
                for (
                    i++;
                    i < node.childNodes.length &&
                    divBrCount > 0 &&
                    !node.childNodes[i].textContent;
                    i++
                ) {
                    if ((node.childNodes[i] as Element).localName === "br")
                        divBrCount--;
                }
                // We want the selection in node itself, before childNode[i].
                EditableDivUtils.selectAtOffset(node, i);
                return true;
            }
            // If it's at the end of a child (that is not the last child) we have a choice whether to put it at the
            // end of that node or the start of the following one. For some reason the IP is invisible if
            // placed at the end of the preceding one, so prefer the start of the following one, which is why
            // we generally call this routine with atStart true.
            // (But, of course, if it is the last node we must be able to put the IP at the very end.)
            // When trying to do a precise restore, we pass atStart carefully, as it may control
            // whether we end up before or after some <br>s
            if (
                offset < len ||
                (offset === len &&
                    (i === node.childNodes.length - 1 || !atStart))
            ) {
                if (
                    EditableDivUtils.makeSelectionIn(
                        childNode,
                        offset,
                        -1,
                        atStart,
                    )
                ) {
                    return true;
                }
            }
            offset -= len;
        }
        // Somehow we failed. Maybe the node it should go in has no text?
        // See if we can put it at the right position (or as close as possible) in an earlier node.
        // Not sure exactly what case required this...possibly markup included some empty spans?
        for (i--; i >= 0; i--) {
            childNode = node.childNodes[i];
            len = childNode.textContent.length;
            if (EditableDivUtils.makeSelectionIn(childNode, len, -1, atStart)) {
                return true;
            }
        }
        // can't select anywhere (maybe this has no text-node children? Hopefully the caller can find
        // an equivalent place in an adjacent node).
        return false;
    }

    // Positions the dialog box so that it is completely visible, so that it does not extend below the
    // current viewport. Method takes into consideration zoom factor. If the dialog is draggable,
    // it also modifies the draggable options to account for a scrolling bug in jqueryui.
    // @param dialogBox
    public static positionDialogAndSetDraggable(
        dialogBox: JQuery,
        gearIcon: JQuery,
    ): void {
        // A zoom on the body affects offset but not outerHeight, which messes things up if we don't account for it.
        const scale =
            dialogBox[0].getBoundingClientRect().height /
            dialogBox[0].offsetHeight;
        const adjustmentFactor = 30;
        const pxAdjToScale = (adjustmentFactor / scale).toFixed(); // rounded to nearest integer
        const myOptionValue =
            "left+" + pxAdjToScale + " center-" + pxAdjToScale;

        // Set the dialog 30px (adjusted for 'scale') to the right and somewhat up from the gear icon.
        // If it won't fit there for some reason, .position() will 'fit' it in by moving it away from the viewport edges.
        dialogBox.position({
            my: myOptionValue,
            at: "right top",
            of: gearIcon,
            collision: "fit",
        });

        // unless we're debugging, the dialog html should be initially created with visibility set to 'hidden'
        dialogBox.css("visibility", "visible");

        if (dialogBox.is(".ui-draggable")) {
            EditableDivUtils.adjustDraggableOptionsForScaleBug(
                dialogBox,
                scale,
            );
        }
    }

    // from http://stackoverflow.com/questions/105034/create-guid-uuid-in-javascript
    public static createUuid(): string {
        // http://www.ietf.org/rfc/rfc4122.txt
        return crypto.randomUUID
            ? crypto.randomUUID()
            : // The string "10000000-1000-4000-8000-100000000000" is a template for the UUID.
              // The 4 is never changed, but the 1, 0, and 8 are replaced with random hex digits,
              // with 1 and 8 having special meaning and effects due to the XOR and shift operations.
              "10000000-1000-4000-8000-100000000000".replace(/[018]/g, (c) =>
                  (
                      +c ^
                      (crypto.getRandomValues(new Uint8Array(1))[0] &
                          (15 >> (+c / 4)))
                  ).toString(16),
              );
    }

    public static getPageFrame(): HTMLIFrameElement | null {
        const doc = window.top?.document;
        return doc ? <HTMLIFrameElement>doc.getElementById("page") : null;
    }

    // The body of the editable page, a root for searching for document content.
    public static getPage(): JQuery {
        const page = this.getPageFrame();
        if (!page || !page.contentWindow) return $();
        return $(page.contentWindow.document.body);
    }

    // look for an existing transform:scale setting and extract the scale. If not found, use 1.0 as starting point.
    // If target is supplied, we only want a scale based on the page scaler if the target is inside it.
    public static getPageScale(target?: HTMLElement): number {
        const page = this.getPage();
        let scaler: HTMLElement | undefined = undefined;
        const getScaler = () => page.find("div#page-scaling-container").get(0);
        if (target) {
            scaler = getScaler();
            if (!scaler || !scaler.contains(target)) {
                return 1.0;
            }
        }

        // With full bleed, we have a transform on the page in addition to the possible scaling using the zoom control.
        // This calculation gets the scale experimentally. Because offsetWidth is an integer,
        // it can be slightly less accurate than just reading the scale from the page-scaling-container style as we do below.
        // That's why I made this two code paths rather than just changing everything to use the upper calc.
        // I also tried getting the scale from the page's transform and multiplying it by the one on the page-scaling-container.
        // But due to rounding before multiplying, it actually ended up with less precision than this.
        if (page.hasClass("bloom-fullBleed")) {
            const bloomPage = page.find("div.bloom-page")?.get()[0];
            if (!bloomPage || !bloomPage.offsetWidth) {
                return 1.0;
            }
            return (
                // Both values include padding and borders (though I don't think we have either)
                // as long as box-sizing is set to the default (content-box).
                bloomPage.getBoundingClientRect().width / bloomPage.offsetWidth
            );
        }

        let scale = 1.0;
        if (page.length === 0) return scale;
        const styleString =
            (scaler ?? getScaler())?.getAttribute("style") ?? "";
        const searchData = /transform: *scale\(([0-9.]*)/.exec(styleString);
        if (searchData) {
            scale = parseFloat(searchData[1]);
        }
        return scale;
    }

    public static adjustDraggableOptionsForScaleBug(
        dialogBox: JQuery,
        scale: number,
    ) {
        dialogBox.draggable({
            // BL-4293 the 'start' and 'drag' functions here work around a known bug in jqueryui.
            // fix adapted from majcherek2048's about 2/3 down this page https://bugs.jqueryui.com/ticket/3740.
            // If we upgrade our jqueryui to a version that doesn't have this bug (1.10.3 or later?),
            // we'll need to back out this change.
            start: function (event, ui) {
                $(this).data("startingScrollTop", $("html").scrollTop());
                $(this).data("startingScrollLeft", $("html").scrollLeft());
            },
            drag: function (event, ui) {
                ui.position.top =
                    (ui.position.top - $(this).data("startingScrollTop")) /
                    scale;
                ui.position.left =
                    (ui.position.left - $(this).data("startingScrollLeft")) /
                    scale;
            },
        });
    }

    public static pasteImageCredits() {
        const activeElement = document.activeElement;
        get("image/imageCreditsForWholeBook", (result) => {
            const data = result.data;
            if (!data) return; // nothing to insert: no images apparently...

            // This is a global method, called from an href attribute of an <a> element.
            // document.activeElement must be that <a> element, which is owned by a qtip-content
            // class div element, which in turn is owned by a qtip class element.  The editable
            // div element to which the qtip bubble is attached has an aria-describedby attribute
            // that refers to the div.qtip's id.
            if (activeElement == null || activeElement.parentElement == null)
                return;
            const bubble = activeElement.parentElement.parentElement;
            if (bubble == null) return;
            const query =
                "[aria-describedby='" + bubble.getAttribute("id") + "']";
            let artists: Element | null = null;
            const credits = document.querySelectorAll(query);
            if (credits.length > 0) {
                artists = credits[0];
            } else {
                if (
                    activeElement.getAttribute("data-book") ==
                        "originalContributions" &&
                    activeElement.getAttribute("contenteditable") == "true" &&
                    activeElement.getAttribute("role") == "textbox" &&
                    activeElement.getAttribute("aria-label") == "false"
                ) {
                    // If we're coming from a tab in a source-bubble instead of a pure
                    // hint-bubble, then the activeElement is the actual text-box we
                    // want to insert into.  I don't know why it isn't the <a> element.
                    // It must be some difference in how source bubbles and hint bubbles
                    // work.
                    artists = activeElement;
                }
            }
            if (artists !== null) {
                // We found where to insert the credits.  If there's a better way to add this
                // information, I'd be happy to learn what it is.  data is a string consisting
                // of one or more <p> elements properly terminated by </p> and separated by
                // newlines.
                const d2 = document.createElement("div");
                d2.innerHTML = data;
                const paras = d2.getElementsByTagName("p");
                // Note that when the p element is appended to the div element, it gets removed from the list.
                while (paras.length > 0) {
                    artists.appendChild(paras[0]);
                }
            }
        });
        // Reposition all language tips, not just the tip for this item because sometimes the edit moves other controls.
        setTimeout(() => {
            (<qtipInterface>$("div[data-hasqtip]")).qtip("reposition");
        }, 100); // make sure the DOM has the inserted text before we try to reposition qtips
    }

    // Get the cleaned up data (getData()) from ckeditor, rather than just the raw html.
    // Specifically, we want it to remove the zero-width space characters that ckeditor inserts.
    // See BL-12391. Note that getData() only removes the filling char ckeditor is actively
    // tracking; an orphaned one survives it, so we also strip stray filling chars explicitly
    // (see removeCkEditorFillingChars and BL-16490).
    // Return the bookmarks for each editable div, so that we can restore the selection after
    // modifying the divs.
    // Changes to this logic may need to be reflected in audioRecording.ts' cleanUpCkEditorHtml.
    public static doCkEditorCleanup(
        editableDivs: HTMLDivElement[],
        createBookMarks: boolean,
    ): object[] {
        const bookmarksForEachEditable: object[] = [];
        editableDivs.forEach((div, index) => {
            const ckeditorOfThisBox = (<any>div).bloomCkEditor;
            if (ckeditorOfThisBox) {
                if (createBookMarks) {
                    const ckeditorSelection = ckeditorOfThisBox.getSelection();
                    if (ckeditorSelection) {
                        try {
                            // console.log("doCkEditorCleanup, before createBookmarks: ");
                            // EditableDivUtils.logElementsInnerHtml([div]);

                            bookmarksForEachEditable[index] =
                                ckeditorSelection.createBookmarks(true);
                        } catch (e) {
                            console.error("createBookmarks failed");
                            console.error(e);
                            bookmarksForEachEditable[index] = {};
                        }
                    }
                }

                // Strip stray filling chars before comparing: if the live DOM has an
                // orphaned filling char that getData() didn't remove, the stripped data
                // will differ from div.innerHTML and trigger the replacement that removes it.
                const ckEditorData =
                    EditableDivUtils.removeCkEditorFillingChars(
                        ckeditorOfThisBox.getData(),
                    );
                if (ckEditorData !== div.innerHTML) {
                    this.safelyReplaceContentWithCkEditorData(
                        div,
                        ckEditorData,
                    );
                }
            }
        });

        // console.log("doCkEditorCleanup, final result: ");
        // EditableDivUtils.logElementsInnerHtml(editableDivs);

        return bookmarksForEachEditable;
    }

    // public for unit testing
    public static safelyReplaceContentWithCkEditorData(
        div: HTMLDivElement,
        ckEditorData: string,
    ) {
        // Belt-and-suspenders for callers that pass getData() directly (e.g.
        // audioRecording.cleanUpCkEditorHtml): make sure we never write an
        // orphaned ckeditor filling char into the DOM. See BL-16490.
        ckEditorData =
            EditableDivUtils.removeCkEditorFillingChars(ckEditorData);

        let needToRemoveInitialParagraph = false;
        let divChildNodes = Array.from(div.childNodes);
        if (
            divChildNodes.length > 0 &&
            EditableDivUtils.isNodeCkEditorBookmark(divChildNodes[0])
        ) {
            // For some reason, if the bookmark span is the first thing in the div,
            // ckeditor wraps it in a p tag and adds a nbsp which introduces an empty paragraph.
            // Make sure we don't do that.
            needToRemoveInitialParagraph = true;
        }

        // console.log("safelyReplaceContentWithCkEditorData, before getData replacement: ");
        // EditableDivUtils.logElementsInnerHtml([div]);

        div.innerHTML = ckEditorData;

        // console.log("safelyReplaceContentWithCkEditorData, after getData replacement: ");
        // EditableDivUtils.logElementsInnerHtml([div]);

        if (needToRemoveInitialParagraph) {
            // Be very specific in what we change here. (Don't break some scenario we don't understand.)
            // Only if the div starts with a bookmark and ckeditor wraps that in a p and adds a nbsp.
            // e.g.       <span id="cke_bm_49C" style="display: none;">&nbsp;</span>
            // becomes <p><span id="cke_bm_49C" style="display: none;">&nbsp;</span>&nbsp;</p>
            divChildNodes = Array.from(div.childNodes);
            if (divChildNodes.length > 0 && divChildNodes[0].nodeName === "P") {
                const pChildNodes = Array.from(divChildNodes[0].childNodes);
                if (
                    pChildNodes.length === 2 &&
                    EditableDivUtils.isNodeCkEditorBookmark(pChildNodes[0]) &&
                    pChildNodes[1].nodeName === "#text" &&
                    pChildNodes[1].textContent === "\u00A0"
                ) {
                    div.replaceChild(pChildNodes[0], divChildNodes[0]);

                    // console.log(
                    //     "safelyReplaceContentWithCkEditorData, after needToRemoveInitialParagraph change: "
                    // );
                    // EditableDivUtils.logElementsInnerHtml([div]);
                }
            }
        }

        EditableDivUtils.fixUpEmptyishParagraphs(div);
    }

    private static isNodeCkEditorBookmark(node: Node): boolean {
        return node.nodeName === "SPAN" && node["id"].startsWith("cke_bm_");
    }

    // CKEditor 4 inserts a "filling char" (U+200B ZERO WIDTH SPACE) at the caret on
    // WebKit/Blink to keep the cursor navigable near inline-element boundaries and
    // before <br>. It removes the one it is tracking, but when the decodable/leveled
    // reader rewrites a box's innerHTML out from under ckeditor, that filling char is
    // orphaned: getData() no longer strips it, so it gets saved and corrupts reader
    // word matching (the analyzer treats U+200B as a word split while the highlighter
    // does not). Strip any such stray filling chars. See BL-16490.
    // We deliberately remove only U+200B; U+200C (ZWNJ) and U+200D (ZWJ) are legitimate
    // in some scripts and must be preserved.
    public static removeCkEditorFillingChars(html: string): string {
        const fillingChar = String.fromCharCode(0x200b); // U+200B ZERO WIDTH SPACE
        return html.split(fillingChar).join("");
    }

    // I don't know why cdEditor's getData() converts paragraphs with only a <br>
    // in them to contain &nbsp; instead. But when it does, we get various issues
    // with extra spaces (which can also cause other toolbox markup issues).
    // Note, this method works to clean up paragraphs which have only a ckeditor bookmark in them, too.
    public static fixUpEmptyishParagraphs(element: HTMLElement) {
        element.querySelectorAll("p").forEach((p) => {
            const pChildNodes = Array.from(p.childNodes);
            if (pChildNodes.length < 1 || pChildNodes.length > 2) {
                return; // (continue)
            }

            const childTextNodes = pChildNodes.filter(
                (n) => n.nodeName === "#text",
            );

            if (
                childTextNodes.length !== 1 ||
                childTextNodes[0].textContent !== "\u00A0"
            ) {
                return; // (continue)
            }

            const childSpanNodes = pChildNodes.filter(
                (n) => n.nodeName === "SPAN",
            );

            if (
                childSpanNodes.length === 0 ||
                (childSpanNodes.length === 1 &&
                    childSpanNodes[0]["id"].startsWith("cke_bm_"))
            ) {
                p.replaceChild(document.createElement("br"), childTextNodes[0]);
            }
        });
    }

    // A ckeditor bookmark is a hidden span inserted at the insertion point, so creating one
    // SPLITS the text node the user is typing in, and removing it again (which is what
    // selectBookmarks does) leaves the two halves as separate, adjacent text nodes.
    // The characters are all still there, and the DOM inspector's text view looks right, but
    // Chromium shapes the paragraph's text as a single run and then hands out the resulting
    // glyphs per text node. When a ligature straddles the boundary - very easy in SIL fonts
    // such as Andika and Charis, which ligate ff, fl and ffl - that split lands in the middle
    // of a glyph cluster and Chromium loses glyphs: letters the user typed simply stop being
    // painted (and the caret draws in the wrong place) until something re-renders the
    // paragraph, e.g. changing the font or reloading the page. Type "overflow", then insert a
    // second "f" before the "f", pause for the markup timer, then type any other letter: the
    // "fl" vanishes (BL-16717).
    // So whenever we take bookmarks out again, put the text back the way we found it.
    // Note that repairing the DOM is not on its own enough to repair the display: see
    // mergeAdjacentTextNodeRuns() for why we rebuild the text rather than call normalize().
    // Preserving the insertion point is our job too. The DOM spec does require text-node
    // merging to keep live ranges - including the selection - on the same characters, but we
    // don't want the user's cursor to depend on the browser getting that right, so we save and
    // restore it ourselves.
    // Callers should avoid making bookmarks at all when nothing is going to rewrite the box;
    // this is the repair for when we genuinely needed one.
    public static mergeTextNodesSplitByBookmarks(element: HTMLElement): void {
        if (!EditableDivUtils.hasAdjacentTextNodes(element)) {
            return; // nothing to merge; leave the selection strictly alone
        }

        const selection = element.ownerDocument.defaultView?.getSelection();
        // Only our own box's selection is ours to restore.
        const isOurs =
            selection?.anchorNode &&
            selection.focusNode &&
            element.contains(selection.anchorNode) &&
            element.contains(selection.focusNode);
        const anchor = isOurs
            ? EditableDivUtils.saveablePosition(
                  selection!.anchorNode!,
                  selection!.anchorOffset,
              )
            : undefined;
        const focus = isOurs
            ? EditableDivUtils.saveablePosition(
                  selection!.focusNode!,
                  selection!.focusOffset,
              )
            : undefined;

        EditableDivUtils.mergeAdjacentTextNodeRuns(element);

        if (!selection || !anchor || !focus) {
            return;
        }
        const restoredAnchor = EditableDivUtils.positionForCharacterOffset(
            anchor.element,
            anchor.characterOffset,
        );
        const restoredFocus = EditableDivUtils.positionForCharacterOffset(
            focus.element,
            focus.characterOffset,
        );
        if (!restoredAnchor || !restoredFocus) {
            return; // no text to put it in; better to leave the selection where it landed
        }
        // setBaseAndExtent rather than a Range, so a backwards selection stays backwards.
        selection.setBaseAndExtent(
            restoredAnchor.node,
            restoredAnchor.offset,
            restoredFocus.node,
            restoredFocus.offset,
        );
    }

    // Merge each run of adjacent sibling text nodes back into one.
    //
    // Merging the DOM back together is only half the repair: Chromium goes on painting the
    // paragraph's old, glyph-dropped text afterwards. The DOM ends up correct - the inspector
    // shows one string, and the layout even measures correctly - while the screen still shows
    // "overfow" for a paragraph reading "overfflow", until something else forces a repaint.
    //
    // The obvious cure, rebuilding each run as a brand-new text node (nothing stale is cached
    // against a node that didn't exist a moment ago), is NOT usable here: replacing a text node
    // collapses any live Range whose boundaries were inside it, and Bloom's reader-tool and
    // Talking Book highlights are exactly that - live Ranges over these text nodes, registered
    // by the markup pass just before we run. Destroying them makes the highlighting silently
    // disappear from the paragraph the user is typing in (the failure mode
    // textHighlightManager.hasDeadRanges() exists to detect).
    //
    // So: merge with normalize(), which the DOM spec requires to keep live ranges on the same
    // characters, and then make Chromium re-shape the merged node by writing its own text back
    // into it - appending a character and immediately removing it again. Both edits are at the
    // very end of the node, and per the spec only boundaries strictly beyond the edit point
    // move, so no live range is disturbed; and because it happens inside one synchronous task,
    // the extra character is never painted.
    //
    // Deliberately NOT done by toggling the element's display: the parent of a bare text node
    // can be the .bloom-editable itself, and hiding a focused contenteditable blurs it - the
    // user's next keystrokes would go nowhere - besides leaving a stray style="" attribute
    // behind in the saved book.
    private static mergeAdjacentTextNodeRuns(element: HTMLElement): void {
        const doc = element.ownerDocument;
        const parentsWithRuns = new Set<Element>();
        const walker = doc.createTreeWalker(element, NodeFilter.SHOW_TEXT);
        for (let node = walker.nextNode(); node; node = walker.nextNode()) {
            if (node.nextSibling?.nodeType === Node.TEXT_NODE) {
                // A text node always has a parent element here: we are walking inside element.
                parentsWithRuns.add(node.parentElement!);
            }
        }

        parentsWithRuns.forEach((parent) => {
            parent.normalize();
            for (
                let child = parent.firstChild;
                child;
                child = child.nextSibling
            ) {
                if (child.nodeType === Node.TEXT_NODE) {
                    const text = child as Text;
                    text.insertData(text.length, " ");
                    text.deleteData(text.length - 1, 1);
                }
            }
        });
    }

    // Does element contain two text nodes that are siblings, as removing a bookmark leaves
    // behind? (An empty text node next to another one counts: merging folds it away.)
    private static hasAdjacentTextNodes(element: HTMLElement): boolean {
        const walker = element.ownerDocument.createTreeWalker(
            element,
            NodeFilter.SHOW_TEXT,
        );
        for (let node = walker.nextNode(); node; node = walker.nextNode()) {
            if (node.nextSibling?.nodeType === Node.TEXT_NODE) {
                return true;
            }
        }
        return false;
    }

    // Express the DOM position (container, offset) in a way that survives merging text nodes:
    // the element it is in, plus how many characters of that element's text precede it.
    // A (node, offset) pair does not survive, and a character offset within the whole box
    // does not identify the position uniquely - the end of one paragraph and the start of the
    // next are the same number of characters in, but they are different places to be typing.
    // normalize() never moves text out of the element it is in, so this stays exact.
    private static saveablePosition(
        container: Node,
        offset: number,
    ): { element: Element; characterOffset: number } | undefined {
        const element =
            container.nodeType === Node.ELEMENT_NODE
                ? (container as Element)
                : container.parentElement;
        if (!element) {
            return undefined;
        }
        const range = element.ownerDocument.createRange();
        range.setStart(element, 0);
        range.setEnd(container, offset);
        // toString() gives just the characters of the text nodes in the range, which is
        // exactly what positionForCharacterOffset() counts back through.
        return { element, characterOffset: range.toString().length };
    }

    // The inverse of saveablePosition(): the text node and offset within it that
    // characterOffset characters of text into root land on.
    private static positionForCharacterOffset(
        root: Node,
        characterOffset: number,
    ): { node: Text; offset: number } | undefined {
        const walker = root.ownerDocument!.createTreeWalker(
            root,
            NodeFilter.SHOW_TEXT,
        );
        let remaining = characterOffset;
        let lastTextNode: Text | undefined;
        for (
            let node = walker.nextNode() as Text | null;
            node;
            node = walker.nextNode() as Text | null
        ) {
            // <= so that an offset at the very end of a text node stays in that node rather
            // than falling through to the start of the next one.
            if (remaining <= node.length) {
                return { node, offset: remaining };
            }
            remaining -= node.length;
            lastTextNode = node;
        }
        // Shouldn't happen: we counted these same characters a moment ago. But if something
        // did change, the end of the text is the least surprising place to be.
        return lastTextNode
            ? { node: lastTextNode, offset: lastTextNode.length }
            : undefined;
    }

    public static restoreSelectionFromCkEditorBookmarks(
        editableDivs: HTMLDivElement[],
        ckEditorBookmarks: object[],
    ) {
        if (ckEditorBookmarks.length) {
            editableDivs.forEach((div, index) => {
                const ckeditorOfThisBox = (<any>div).bloomCkEditor;
                if (ckeditorOfThisBox) {
                    try {
                        ckeditorOfThisBox
                            .getSelection()
                            .selectBookmarks(ckEditorBookmarks[index]);
                    } catch (e) {
                        // I don't understand why this throws sometimes.
                        // But we don't want to crash or lose the user's work.
                        // Or even inform the user.
                        //
                        // I think when this happens, it is mostly (always?) because
                        // the bookmarks aren't in the DOM. But we'll play it
                        // safe and remove any which are there.
                        // (That's what a successful call to selectBookmarks does.)
                        div.querySelectorAll("span[id^='cke_bm_']").forEach(
                            (span) => {
                                span.remove();
                            },
                        );
                    }
                    EditableDivUtils.mergeTextNodesSplitByBookmarks(div);
                }
            });
        }
    }

    // This is just a helpful debugging tool.
    public static logElementsInnerHtml(elements: HTMLElement[]) {
        elements.forEach((div, index) => {
            console.log(
                `   [${index}]: ${div.innerHTML.replace(/\u200b/g, "ZWSP")}`,
            );
        });
    }

    public static isInHiddenLanguageBlock(elem: Element) {
        // Spans (and probably other inline elements?) can have display=inline even if they're inside a div that's display=none
        let elemToCheck: Element | null = elem;

        if (elem.tagName === "SPAN") {
            const parentEditable = elem.closest(".bloom-editable");

            // Really not wanting this scenario to happen, because we may get inaccurate results, but...
            // We ought to be able to continue on without anything terrible happening
            console.assert(
                !!parentEditable,
                "isVisible(): Unexpected span that is not inside a bloom-editable. span = " +
                    elem,
            );
            elemToCheck = parentEditable || elem;
        }
        // elemToCheck is typically a bloom-editable. Originally, this check was used to consider
        // which languages were hidden. But with drag-activity, there are cases where a containing
        // text-over-picture element is hidden. Checking a small number of ancestors is usually
        // enough, and less expensive than looking all the way up to the document.
        // (I think getComputedStyle is quite slow.)
        return EditableDivUtils.isInDisplayNone(elemToCheck);
    }

    public static isInDisplayNone(elem: Element, maxLevelsToCheck = 4) {
        let elemToCheck: Element | null = elem;

        for (let i = 0; i < maxLevelsToCheck; i++) {
            const style = window.getComputedStyle(elemToCheck);
            if (style.display === "none") {
                return true;
            }
            elemToCheck = elemToCheck.parentElement;
            if (!elemToCheck) {
                return false;
            }
        }
        return false;
    }
}
