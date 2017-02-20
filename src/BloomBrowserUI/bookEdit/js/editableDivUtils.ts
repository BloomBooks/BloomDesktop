/// <reference path="../../typings/jquery/jquery.d.ts" />

export class EditableDivUtils {

    static getElementSelectionIndex(element: HTMLElement): number {

        var page: HTMLIFrameElement = <HTMLIFrameElement>parent.window.document.getElementById('page');
        if (!page) return -1; // unit testing?

        var selection = page.contentWindow.getSelection();
        var active = $(selection.anchorNode).closest('div').get(0);
        if (active != element) return -1; // huh??
        if (!active || selection.rangeCount == 0) {
            return -1;
        }
        var myRange = selection.getRangeAt(0).cloneRange();
        myRange.setStart(active, 0);
        return myRange.toString().length;
    }

    static selectAtOffset(node: Node, offset: number): void {

        var iframeWindow: Window = (<HTMLIFrameElement>parent.window.document.getElementById('page')).contentWindow;

        var range = iframeWindow.document.createRange();
        range.setStart(node, offset);
        range.setEnd(node, offset);
        var selection1 = iframeWindow.getSelection();
        selection1.removeAllRanges();
        selection1.addRange(range);
    }

    /**
     * Make a selection in the specified node at the specified offset.
     * If divBrCount is >=0, we expect to make the selection offset characters into node itself
     * (typically the root div). After traversing offset characters, we will try to additionally
     * traverse divBrCount <br> elements.
     * @param node
     * @param offset
     */
    static makeSelectionIn(node: Node, offset: number, divBrCount: number, atStart: boolean): boolean {

        if (node.nodeType === 3) {
            // drilled down to a text node. Make the selection.
            EditableDivUtils.selectAtOffset(node, offset);
            return true;
        }

        var i = 0;
        var childNode;
        var len;

        for (; i < node.childNodes.length && offset >= 0; i++) {
            childNode = node.childNodes[i];
            len = childNode.textContent.length;
            if (divBrCount >= 0 && len == offset) {
                // We want the selection after childNode itself, plus if possible an additional divBrCount <br> elements
                for (i++;
                    i < node.childNodes.length && divBrCount > 0 && node.childNodes[i].textContent.length == 0;
                    i++) {
                    if (node.childNodes[i].localName === 'br') divBrCount--;
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
            if (offset < len || (offset == len && (i == node.childNodes.length - 1 || !atStart))) {
                if (EditableDivUtils.makeSelectionIn(childNode, offset, -1, atStart)) {
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
    static positionDialogAndSetDraggable(dialogBox: JQuery, gearIconOffset: JQueryCoordinates): void {
        // console.log('gear offset: left=' + gearIconOffset.left + '; top=' + gearIconOffset.top);
        // A zoom on the body affects offset but not outerHeight, which messes things up if we don't account for it.
        var scale = dialogBox[0].getBoundingClientRect().height / dialogBox[0].offsetHeight;
        // console.log('  scale is ' + scale);
        var pxAdjToScale = 30 / scale;

        // Initially set the dialog 30px (adjusted for 'scale') to the right and up from the gear icon.
        dialogBox.offset({ left: gearIconOffset.left + pxAdjToScale, top: gearIconOffset.top - pxAdjToScale });
        console.log('dialogBox offset is: left=' + dialogBox.offset().left + '; top=' + dialogBox.offset().top);

        EditableDivUtils.adjustDialogInViewport(dialogBox, scale);
        // console.log('dialogBox offset after adjustment for viewport is: left=' +
        //     dialogBox.offset().left + '; top=' + dialogBox.offset().top);

        // unless we're debugging, the dialog html should be initially created with visibility set to 'hidden'
        dialogBox.css('visibility', 'visible');

        if (dialogBox.is('.ui-draggable')) {
            EditableDivUtils.adjustDraggableOptionsForScaleBug(dialogBox, scale);
        }
    }

    static getTotalOffsetOfHtmlElement(uiElement: HTMLElement): JQueryCoordinates {
        var top = uiElement.offsetTop;
        var left = uiElement.offsetLeft;
        var elem = uiElement;

        // get the top left corner of the dialogBox in relation to the top left of its containing elements
        while (elem.offsetParent) {
            elem = <HTMLElement>elem.offsetParent;
            top += elem.offsetTop;
            left += elem.offsetLeft;
        }
        return { top, left };
    }

    static adjustDialogInViewport(dialogBox: JQuery, scale: number) {
        // I'm having a terrible time getting this method to do anything reasonable if scale != 1.0
        // get the current size and position of the dialogBox
        var elem: HTMLElement = dialogBox[0];
        var offset = EditableDivUtils.getTotalOffsetOfHtmlElement(elem);
        var top = offset.top / scale;
        var left = offset.left / scale;
        var height = elem.offsetHeight;
        var width = elem.offsetWidth;

        var dlgOffsetTop = dialogBox.offset().top * scale;

        // diffY is supposed to be the portion of the dialogBox that is below the viewport
        var diffY = ((top + height) - (window.pageYOffset * scale + window.innerHeight));
        if (diffY > 0) {
            // the extra 30 is for padding between the bottom of the dialog and the bottom of the viewport
            dlgOffsetTop -= (diffY - 30) * scale;
        }

        var dlgOffsetLeft = dialogBox.offset().left * scale;

        var diffX = ((left + width) - (window.pageXOffset * scale + window.innerWidth));
        if (diffX > 0) {
            dlgOffsetLeft += (diffX * scale);
        }

        dialogBox.offset({ left: dlgOffsetLeft, top: dlgOffsetTop });
    }

    static adjustDraggableOptionsForScaleBug(dialogBox: JQuery, scale: number) {
        dialogBox.draggable({
            // BL-4293 the 'start' and 'drag' functions here work around a known bug in jqueryui.
            // fix adapted from majcherek2048's about 2/3 down this page https://bugs.jqueryui.com/ticket/3740.
            // If we upgrade our jqueryui to a version that doesn't have this bug (1.10.3 or later?),
            // we'll need to back out this change.
            start: function (event, ui) {
                $(this).data('startingScrollTop', $('html').scrollTop());
                $(this).data('startingScrollLeft', $('html').scrollLeft());
            },
            drag: function (event, ui) {
                ui.position.top = (ui.position.top - $(this).data('startingScrollTop')) / scale;
                ui.position.left = (ui.position.left - $(this).data('startingScrollLeft')) / scale;
            }
        });
    }
}
