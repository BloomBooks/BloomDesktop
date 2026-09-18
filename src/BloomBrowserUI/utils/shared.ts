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

// The iframe (in the workspace root document) that holds the page being edited.
// We deliberately look in `parent` rather than `window.top`. Every frame that asks this
// question (the toolbox frame and the page frame itself) is a direct child of the workspace
// root, so parent and top are the same document there, and `parent` is what the rest of the
// edit-mode code (e.g. workspaceFrames.ts) has always used. Using `parent` also keeps the
// answer local: a caller running in some deeper frame gets null (no "page" sibling) rather
// than silently reaching past its own workspace up to the outermost document.
// Note: typed non-null for the convenience of the many callers that legitimately assume the
// frame is there, but it really can be null (unit tests, or before the frame is created).
export function getPageIFrame(): HTMLIFrameElement {
    return parent.window.document.getElementById("page") as HTMLIFrameElement;
}

// The document of the editable page. Use this (rather than the body) when you need
// document-level APIs such as getElementById or documentElement.
export function getPageIframeDocument(): Document | null {
    const page = getPageIFrame();
    // ENHANCE: What if the iframe is there but hasn't finished loading yet? (Which will wipe contentWindow.document)
    if (!page || !page.contentWindow) return null;
    return page.contentWindow.document;
}

// The body of the editable page, a root for searching for document content.
export function getPageIframeBody(): HTMLElement | null {
    return getPageIframeDocument()?.body ?? null;
}

export function getBloomPageElement(): HTMLElement | null {
    return getPageIframeBody()?.querySelector(
        ".bloom-page",
    ) as HTMLElement | null;
}

/**
 * Is the page currently being edited part of the front or back matter?
 * Some callers (e.g. deciding whether the canvas tool may be used) want to treat a custom
 * page as not being xmatter, so we support an override for that. Could be just a boolean,
 * but using an object with a named field makes it clearer what the argument is for where it
 * is used.
 * Returns false if there is no page yet, or (improbably) it has no classes.
 * (The class attribute, unlike some others we put on the page, is never URL-encoded.)
 */
export function isXmatterPage(
    args: { returnFalseForCustomPage: boolean } = {
        returnFalseForCustomPage: false,
    },
): boolean {
    const pageClasses = getBloomPageElement()?.getAttribute("class");
    if (!pageClasses) return false;
    if (
        args.returnFalseForCustomPage &&
        pageClasses.includes("bloom-customLayout")
    ) {
        return false;
    }
    return (
        pageClasses.includes("bloom-frontMatter") ||
        pageClasses.includes("bloom-backMatter")
    );
}

// We saw one failure where the page iframe and its body already existed, but the editable
// .bloom-page element had not been inserted yet when other React code tried to read it.
// That is only a single observed case, so we do not know how often it happens, but the ordering
// strongly suggests a race between iframe population and code that assumes the live page DOM is
// fully ready as soon as the iframe body exists. Use this helper when the caller truly needs the
// editable page root, rather than treating body readiness as proof that .bloom-page is ready.
export function whenBloomPageIsReady(
    onReady: (page: HTMLElement) => void,
): () => void {
    let disposed = false;
    let observer: MutationObserver | undefined;

    const disconnectObserver = () => {
        observer?.disconnect();
        observer = undefined;
    };

    const notifyIfReady = (): boolean => {
        const page = getBloomPageElement();
        if (!page || disposed) {
            return false;
        }

        disconnectObserver();
        onReady(page);
        return true;
    };

    if (notifyIfReady()) {
        return () => {
            disposed = true;
        };
    }

    const body = getPageIframeBody();
    if (body) {
        observer = new MutationObserver(() => {
            notifyIfReady();
        });
        observer.observe(body, {
            childList: true,
            subtree: true,
        });
    }

    return () => {
        disposed = true;
        disconnectObserver();
    };
}

//if this is ever changed, be sure to also change it in bloomUI.less
export const animateStyleName: string = "bloom-animationPreview";
