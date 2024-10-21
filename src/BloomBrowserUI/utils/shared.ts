// This file is just a place to put items which are needed by both the page editing bundle and the toolbox bundle,
// and which don't seem to fit elsewhere, such as in utils files in a particular tool.
// Its first  item is ckeditableSelector which was originally exported by bloomEditing.ts to be used by toolbox.ts.
// But somehow that caused significantly inflated build times and bundle sizes. Thus, this file was created...

// We want to attach ckeditor to the contenteditable="true" class="bloom-content1"
// also to contenteditable="true" and class="bloom-content2" or class="bloom-content3"
// as well as Equation-style (Used by ArithmeticTemplate.pug, these are language neutral and don't have a content language)
// attachToCkEditor will skip any element with class="bloom-userCannotModifyStyles" (which might be on the translationGroup)
// Update 1 Feb 2022: JohnT: in dealing with BL-10893 it became clear that ckEditor should also be attached to
// contenteditable things with class bloom-contentNational2, as it had previously been made to attach to bloom-contentNational1,
// though the comment was not updated. It is NOT clear to me exactly what principle is meant to really govern what elements
// get ckeditor attached (which, at least, allows them to have styled text). There is presumably some reason not to
// just attach it to everything that is contenteditable or everything with bloom-visibility-code-on, but unfortunately
// we did not comment that. Possibly there are special fields (ISBN comes to mind) in xmatter that should not be styled?
export const ckeditableSelector =
    ".bloom-content1[contenteditable='true'],.bloom-content2[contenteditable='true']," +
    ".bloom-content3[contenteditable='true'],.bloom-contentNational1[contenteditable='true']," +
    ".bloom-contentNational2[contenteditable='true'],.Equation-style[contenteditable='true']";

export function getPageIFrame(): HTMLIFrameElement {
    return parent.window.document.getElementById("page") as HTMLIFrameElement;
}

// The body of the editable page, a root for searching for document content.
export function getPageIframeBody(): HTMLElement | null {
    const page = getPageIFrame();
    if (!page || !page.contentWindow) return null;
    return page.contentWindow.document.body;
}
