/// <reference path="../../typings/jquery/jquery.d.ts" />
import { BloomApi } from "../../utils/bloomApi";

interface qtipInterface extends JQuery {
    qtip(options: string): JQuery;
}

export class EditableDivUtils {
    public static getElementSelectionIndex(element: HTMLElement): number {
        const page: HTMLIFrameElement | null = <HTMLIFrameElement | null>(
            parent.window.document.getElementById("page")
        );
        if (!page || !page.contentWindow) return -1; // unit testing?

        const selection = page.contentWindow.getSelection();
        if (!selection || !selection.anchorNode) return -1;
        const active = $(selection.anchorNode)
            .closest("div")
            .get(0);
        if (active != element) return -1; // huh??
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
                (offset == len && (i == node.childNodes.length - 1 || !atStart))
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
        const myOptionValue = "left+" + pxAdjToScale + " top-" + pxAdjToScale;

        // Set the dialog 30px (adjusted for 'scale') to the right and up from the gear icon.
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

    public static getPageFrame(): HTMLIFrameElement | null {
        return <HTMLIFrameElement | null>(
            window.top.document.getElementById("page")
        );
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
        BloomApi.get("image/imageCreditsForWholeBook", result => {
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
}
