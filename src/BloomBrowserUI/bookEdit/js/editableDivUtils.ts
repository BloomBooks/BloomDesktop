/// <reference path="../../typings/jquery/jquery.d.ts" />
import { get } from "../../utils/bloomApi";

interface qtipInterface extends JQuery {
    qtip(options: string): JQuery;
}

// If the current selection is an insertion point in editableDiv (which MUST be a div!), return the index of the selection,
// as a character offset within the text of editableDiv. If the selection is not in editableDiv, return -1.
export class EditableDivUtils {
    public static getElementSelectionIndex(editableDiv: HTMLElement): number {
        const page: HTMLIFrameElement | null = <HTMLIFrameElement | null>(
            parent.window.document.getElementById("page")
        );
        if (!page || !page.contentWindow) return -1; // unit testing? Anyway there is no selection, so not in editableDiv.

        const selection = page.contentWindow.getSelection();
        if (!selection || !selection.anchorNode) return -1;
        const active = $(selection.anchorNode)
            .closest("div")
            .get(0);
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
        atStart: boolean
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
                        atStart
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
        gearIcon: JQuery
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
            collision: "fit"
        });

        // unless we're debugging, the dialog html should be initially created with visibility set to 'hidden'
        dialogBox.css("visibility", "visible");

        if (dialogBox.is(".ui-draggable")) {
            EditableDivUtils.adjustDraggableOptionsForScaleBug(
                dialogBox,
                scale
            );
        }
    }

    // from http://stackoverflow.com/questions/105034/create-guid-uuid-in-javascript
    public static createUuid(): string {
        // http://www.ietf.org/rfc/rfc4122.txt
        const s: string[] = [];
        const hexDigits = "0123456789abcdef";
        for (let i = 0; i < 36; i++) {
            s[i] = hexDigits.substr(Math.floor(Math.random() * 0x10), 1);
        }
        s[14] = "4"; // bits 12-15 of the time_hi_and_version field to 0010
        s[19] = hexDigits.substr((s[19].charCodeAt(0) & 0x3) | 0x8, 1); // bits 6-7 of the clock_seq_hi_and_reserved to 01
        s[8] = s[13] = s[18] = s[23] = "-";

        const uuid = s.join("");
        return uuid;
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
    public static getPageScale(): number {
        const page = this.getPage();

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
        const styleString = page
            .find("div#page-scaling-container")
            .attr("style");
        const searchData = /transform: *scale\(([0-9.]*)/.exec(styleString);
        if (searchData) {
            scale = parseFloat(searchData[1]);
        }
        return scale;
    }

    public static adjustDraggableOptionsForScaleBug(
        dialogBox: JQuery,
        scale: number
    ) {
        dialogBox.draggable({
            // BL-4293 the 'start' and 'drag' functions here work around a known bug in jqueryui.
            // fix adapted from majcherek2048's about 2/3 down this page https://bugs.jqueryui.com/ticket/3740.
            // If we upgrade our jqueryui to a version that doesn't have this bug (1.10.3 or later?),
            // we'll need to back out this change.
            start: function(event, ui) {
                $(this).data("startingScrollTop", $("html").scrollTop());
                $(this).data("startingScrollLeft", $("html").scrollLeft());
            },
            drag: function(event, ui) {
                ui.position.top =
                    (ui.position.top - $(this).data("startingScrollTop")) /
                    scale;
                ui.position.left =
                    (ui.position.left - $(this).data("startingScrollLeft")) /
                    scale;
            }
        });
    }

    public static pasteImageCredits() {
        const activeElement = document.activeElement;
        get("image/imageCreditsForWholeBook", result => {
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
    // See BL-12391.
    // Return the bookmarks for each editable div, so that we can restore the selection after
    // modifying the divs.
    // Changes to this logic may need to be reflected in audioRecording.ts' cleanUpCkEditorHtml.
    public static doCkEditorCleanup(
        editableDivs: HTMLDivElement[],
        createBookMarks: boolean
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

                            bookmarksForEachEditable[
                                index
                            ] = ckeditorSelection.createBookmarks(true);
                        } catch (e) {
                            console.error("createBookmarks failed");
                            console.error(e);
                            bookmarksForEachEditable[index] = {};
                        }
                    }
                }

                const ckEditorData = ckeditorOfThisBox.getData();
                if (ckEditorData !== div.innerHTML) {
                    this.safelyReplaceContentWithCkEditorData(
                        div,
                        ckEditorData
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
        ckEditorData: string
    ) {
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

    // I don't know why cdEditor's getData() converts paragraphs with only a <br>
    // in them to contain &nbsp; instead. But when it does, we get various issues
    // with extra spaces (which can also cause other toolbox markup issues).
    // Note, this method works to clean up paragraphs which have only a ckeditor bookmark in them, too.
    public static fixUpEmptyishParagraphs(element: HTMLElement) {
        element.querySelectorAll("p").forEach(p => {
            const pChildNodes = Array.from(p.childNodes);
            if (pChildNodes.length < 1 || pChildNodes.length > 2) {
                return; // (continue)
            }

            const childTextNodes = pChildNodes.filter(
                n => n.nodeName === "#text"
            );

            if (
                childTextNodes.length !== 1 ||
                childTextNodes[0].textContent !== "\u00A0"
            ) {
                return; // (continue)
            }

            const childSpanNodes = pChildNodes.filter(
                n => n.nodeName === "SPAN"
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

    public static restoreSelectionFromCkEditorBookmarks(
        editableDivs: HTMLDivElement[],
        ckEditorBookmarks: object[]
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
                            span => {
                                span.remove();
                            }
                        );
                    }
                }
            });
        }
    }

    // This is just a helpful debugging tool.
    public static logElementsInnerHtml(elements: HTMLElement[]) {
        elements.forEach((div, index) => {
            console.log(
                `   [${index}]: ${div.innerHTML.replace(/\u200b/g, "ZWSP")}`
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
                parentEditable,
                "isVisible(): Unexpected span that is not inside a bloom-editable. span = " +
                    elem
            );
            elemToCheck = parentEditable || elem;
        }
        // elemToCheck is typically a bloom-editable. Originally, we were just looking to consider
        // which languages were hidden. But with drag-activity, there are cases where a containing
        // text-over-picture element is hidden. That's two levels above the bloom-editable.
        // Looking up four levels should be enough, and may make this computationally less
        // expensive than looking all the way up to the document. (I think getComputedStyle is
        // quite slow.)
        for (let i = 0; i < 4; i++) {
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
