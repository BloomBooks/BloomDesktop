/// <reference path="../../typings/jquery/jquery.d.ts" />

export class EditableDivUtils {

  static getElementSelectionIndex(element: HTMLElement): number {

    var page:HTMLIFrameElement = <HTMLIFrameElement>parent.window.document.getElementById('page');
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

  static WaitForCKEditorReady(window: Window, targetBox: any, callback: (target: any) => any) {
    var editorInstances = (<any>window).CKEDITOR.instances;
    for (var i = 1; ; i++) {
      var instance = editorInstances['editor' + i];
      if (instance == null) {
        break; // if we get here all instances are ready
      }
      if (!instance.instanceReady) {
        instance.on('instanceReady', e => callback(targetBox));
        return;
      }
    }
  }

  // Positions the dialog box so that it is completely visible, so that it does not extend below the
  // current viewport.
  // @param dialogBox
  static positionInViewport(dialogBox: JQuery): void {

    // get the current size and position of the dialogBox
    var elem: HTMLElement = dialogBox[0];
    var top = elem.offsetTop;
    var height = elem.offsetHeight;

    // get the top of the dialogBox in relation to the top of its containing elements
    while (elem.offsetParent) {
      elem = <HTMLElement>elem.offsetParent;
      top += elem.offsetTop;
    }

    // diff is the portion of the dialogBox that is below the viewport
    var diff = (top + height) - (window.pageYOffset + window.innerHeight);
    if (diff > 0) {
      var offset = dialogBox.offset();

      // the extra 30 pixels is for padding
      dialogBox.offset({ left: offset.left, top: offset.top - diff - 30 });
    }
  }
}