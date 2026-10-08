// Inline (Word-style) images are pictures placed inside a bloom-editable, with the text of
// that editable wrapping around them. A picture can be docked on the left, on the right, as a
// full-width band across the middle, or at the bottom. The layout rules are in
// content/bookLayout/inlineImages.less, and the handles and outlines shown while editing are
// in bookEdit/css/editMode.less.
//
// Two things about the design explain most of the code in this file:
//
// 1. The picture's markup has to be inside each bloom-editable, because a CSS float only
//    wraps the text of the element it is in. So every language's editable in a translation
//    group has its own copy of the wrapper, and this code keeps the copies identical. The
//    copy in the editable chosen by getCanonicalInlineImageEditable is the one that counts:
//    normalizeInlineImages() copies it onto the other editables when the page loads, and
//    syncInlineImagesFromEditable() does the same after every edit. CSS then shows the
//    picture in only one of the visible editables, so the reader sees it once (see
//    inlineImages.less).
//
// 2. Where a picture sits is stored as measurements: which side it is docked to, how wide
//    it is, and how far down it is. A position in the text wouldn't work, because each
//    language's editable has different text and different paragraphs, so "after the second
//    paragraph" can't be carried from one language to another. "Docked right, 40% wide,
//    120px down" looks the same in all of them. That is why the wrapper always stays at the
//    start of the editable, or at the end when docked at the bottom; moving it between
//    those two places is the only time it moves. Everything else about the picture is one
//    of the dock classes plus the three custom properties defined below, so the wrapper's
//    class list and style attribute together hold everything needed to copy it.
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import { kBlockElementSelector } from "../bloomField/BloomField";
import { createValidXhtmlUniqueId } from "./xhtmlIdUtils";

export const kInlineImageClass = "bloom-inlineImage";

export const kInlineImageLeftClass = "bloom-inlineImageLeft";
export const kInlineImageRightClass = "bloom-inlineImageRight";
export const kInlineImageMiddleClass = "bloom-inlineImageMiddle";
export const kInlineImageBottomClass = "bloom-inlineImageBottom";

// The four dock classes. A wrapper always has exactly one of them.
export const kInlineImageDockClasses = [
    kInlineImageLeftClass,
    kInlineImageRightClass,
    kInlineImageMiddleClass,
    kInlineImageBottomClass,
];

export type InlineImageDock =
    | typeof kInlineImageLeftClass
    | typeof kInlineImageRightClass
    | typeof kInlineImageMiddleClass
    | typeof kInlineImageBottomClass;

// Marks the wrapper the user has selected. It is used only while editing: makeSerializedCopy
// removes it, so it is never copied to the other languages or saved. The code that handles
// clicking on inline images adds it, and inlineImageCanUndo reads it.
export const kInlineImageSelectedClass = "bloom-inlineImage-selected";

// Dispatched on the translation group, and bubbling, after an undo has replaced its wrappers,
// so that anything attached to the old elements can be rebuilt. See inlineImageUndo.
export const kInlineImagesRestoredEvent = "bloom-inlineImagesRestored";

// Dispatched from the wrapper, and bubbling, when a new picture has been put in an inline
// image; event.detail is the wrapper. Choosing a picture goes out to C# and comes back into
// changeImage, so this event is the only way the code that opened the image chooser learns
// that it finished. That code manages which wrapper is selected, and it needs to know,
// because undoing the change (or the insert before it) only works while the wrapper is
// selected. See handleInlineImageChanged.
export const kInlineImageChangedEvent = "bloom-inlineImageChanged";

// Identifies one inline image. Its copy in every language's editable has the same value. A
// text block can hold any number of inline images, so there may be no single "wrapper in this
// editable", and sync, normalize and undo all pair up the copies by this value. It has to be
// a data attribute rather than an id, because TranslationGroupManager removes the id from an
// editable it clones for a new language (TranslationGroupManager.cs:998). None of the C# code
// that cleans up book markup touches a data attribute it does not know, and StripOutText only
// removes p/br/u/b/i elements and text nodes, so the wrapper and this attribute reach the new
// language with the value unchanged.
export const kInlineImageIdAttr = "data-bloom-inline-image-id";

// Custom properties that hold the picture's size and position. See inlineImages.less for what
// each one does.
export const kInlineImageWidthVar = "--inline-image-width";
export const kInlineImageOffsetVar = "--inline-image-offset";
export const kInlineImageAspectRatioVar = "--inline-image-aspect-ratio";

/**
 * The size of the block that --inline-image-offset was measured against, as "width,height" in
 * layout pixels. The width is a percentage of the block and the aspect ratio is a ratio, but
 * the offset is a distance in pixels, so it is the only value that has to change when the
 * block changes size. The block changes size when the book is shown at another page size, when
 * a different layout is chosen for the page, and when a pane is dragged in Change Layout.
 * Storing the size the offset was measured against lets
 * adjustInlineImageOffsetsIfBlockSizeChanged work out the offset for the new size.
 *
 * bloom-canvas does the same for canvas elements with data-imgsizebasedon. This name follows
 * that one, and like it the attribute must be all lowercase to be a valid data-* attribute.
 */
export const kInlineImageOffsetBasedOnAttr = "data-inline-image-offset-basedon";

// Classes that BloomField.ts uses to protect an embedded image. With bloom-keepFirstInField it
// keeps the required <p> after the image, and with bloom-preventRemoval it undoes a ctrl+a DEL
// that would otherwise delete the image too.
export const kKeepFirstInFieldClass = "bloom-keepFirstInField";
export const kPreventRemovalClass = "bloom-preventRemoval";

export const kDefaultInlineImageWidth = "40%";
// A new wrapper holds a placeholder, which never loads, so there is no natural size to take
// a ratio from. Without a ratio the wrapper would have no height, and the user would have
// nothing to see or click. 4/3 is a common photo shape, and the real ratio replaces it as
// soon as a real image loads.
export const kDefaultInlineImageAspectRatio = "4 / 3";

const kEditableSelector = ".bloom-editable";
const kInlineImageSelector = "." + kInlineImageClass;

// The imgs that already have our load handler. We track them here instead of marking the
// element, so that nothing about it can end up in the saved HTML. It is a WeakSet so that it
// doesn't keep the images of pages we have left in memory.
const imagesWithLoadHandler = new WeakSet<HTMLImageElement>();

/**
 * The bloom-editable children of a translation group, in DOM order. Only direct children
 * count, so a translation group inside a canvas element inside this one is not included.
 */
export function getEditables(translationGroup: HTMLElement): HTMLElement[] {
    return Array.from(translationGroup.children).filter((child) =>
        child.classList.contains("bloom-editable"),
    ) as HTMLElement[];
}

const isVisible = (editable: HTMLElement): boolean =>
    editable.classList.contains("bloom-visibility-code-on");

/**
 * The inline images of one bloom-editable, in DOM order. That is also the order the reader
 * sees them in, and the order syncInlineImagesFromEditable copies to the other languages.
 */
export function getInlineImagesInEditable(
    editable: HTMLElement,
): HTMLElement[] {
    return Array.from(
        editable.querySelectorAll(":scope > " + kInlineImageSelector),
    ) as HTMLElement[];
}

/**
 * The FIRST inline image of one bloom-editable, or null. A block can hold any number of them,
 * so use getInlineImagesInEditable (or getInlineImageById) unless you want the first one or
 * already know there is only one.
 */
export function getInlineImageInEditable(
    editable: HTMLElement,
): HTMLElement | null {
    return getInlineImagesInEditable(editable)[0] ?? null;
}

/**
 * The FIRST inline image in the editable that getCanonicalInlineImageEditable picks, or null
 * if the group has none. When a block holds several images this returns only the first, so
 * use getInlineImages or hasInlineImages when you want all of them or just need to know
 * whether there are any.
 */
export function getInlineImage(
    translationGroup: HTMLElement,
): HTMLElement | null {
    const editable = getCanonicalInlineImageEditable(translationGroup);
    return editable ? getInlineImageInEditable(editable) : null;
}

/**
 * Records the block size this image's offset was measured against. Call it wherever the offset
 * has just been set, so that if the block later changes size the offset can be recalculated.
 */
export function recordInlineImageOffsetBaseline(
    wrapper: HTMLElement,
    editable: HTMLElement,
): void {
    wrapper.setAttribute(
        kInlineImageOffsetBasedOnAttr,
        `${editable.clientWidth},${editable.clientHeight}`,
    );
}

/**
 * The block size this image's offset was measured against, or undefined for an image saved
 * by a Bloom that did not record it. That is not an error; the caller records the current
 * size and leaves the offset alone.
 */
export function getInlineImageOffsetBaseline(
    wrapper: HTMLElement,
): { widthLayoutPx: number; heightLayoutPx: number } | undefined {
    const parts = (
        wrapper.getAttribute(kInlineImageOffsetBasedOnAttr) ?? ""
    ).split(",");
    if (parts.length !== 2) return undefined;
    const widthLayoutPx = parseFloat(parts[0]);
    const heightLayoutPx = parseFloat(parts[1]);
    if (!(widthLayoutPx > 0) || !(heightLayoutPx > 0)) return undefined;
    return { widthLayoutPx, heightLayoutPx };
}

/**
 * The kInlineImageIdAttr value of one inline image. This image's copy in every language's
 * editable has the same value, which is how sync, normalize and undo tell it apart from other
 * images in the same block. It is undefined only for a wrapper written by hand (an old book,
 * or the test.pug page); insertInlineImage always sets it.
 */
export function getInlineImageId(wrapper: HTMLElement): string | undefined {
    return wrapper.getAttribute(kInlineImageIdAttr) ?? undefined;
}

/** This editable's copy of a particular image, or null if it hasn't got one. */
export function getInlineImageById(
    editable: HTMLElement,
    id: string,
): HTMLElement | null {
    return editable.querySelector(
        `:scope > ${kInlineImageSelector}[${kInlineImageIdAttr}="${id}"]`,
    ) as HTMLElement | null;
}

/** Whether any editable of the group holds an inline image. */
export function hasInlineImages(translationGroup: HTMLElement): boolean {
    return getEditables(translationGroup).some(
        (editable) => getInlineImagesInEditable(editable).length > 0,
    );
}

/**
 * Every inline image wrapper anywhere in the container, in all languages, including the
 * copies that CSS hides. Use it for code that has to visit every inline image on a page.
 */
export function getInlineImages(container: HTMLElement): HTMLElement[] {
    return Array.from(
        container.querySelectorAll(
            kEditableSelector + " > " + kInlineImageSelector,
        ),
    ) as HTMLElement[];
}

/**
 * The editable whose inline images the other editables should copy. It is the one the user is
 * looking at, so when we sync, its images, their sizes and positions, and their order replace
 * those in the other editables. A visible editable is always chosen over a hidden one. Among
 * editables that are equally visible, the order of preference is bloom-contentFirst (which the
 * appearance system adds when it controls visibility), then bloom-content1, then the first
 * editable that has any images. This matches both the CSS that decides where images show and
 * bloomEditing.ts's SetupThingsSensitiveToStyleChanges.
 * Returns null if no editable in the group has an inline image.
 */
export function getCanonicalInlineImageEditable(
    translationGroup: HTMLElement,
): HTMLElement | null {
    const withImages = getEditables(translationGroup).filter(
        (editable) => getInlineImagesInEditable(editable).length > 0,
    );
    if (withImages.length === 0) return null;
    // Turning language 1 off on a page leaves its bloom-editable in the DOM, holding a copy
    // nobody can see. That copy must not be chosen, or normalizeInlineImages would copy it over
    // the language the person has been working in and undo what they just did. A group whose
    // only copies are in hidden editables still has to be normalized (a template page, or a
    // page where every language is off), so we fall back to the hidden ones.
    const visibleWithImages = withImages.filter(isVisible);
    const candidates =
        visibleWithImages.length > 0 ? visibleWithImages : withImages;
    return (
        candidates.find((e) => e.classList.contains("bloom-contentFirst")) ??
        candidates.find((e) => e.classList.contains("bloom-content1")) ??
        candidates[0]
    );
}

/**
 * The editable whose copy of the image the reader sees. It uses the same order of preference
 * as the CSS in inlineImages.less that shows the image in only one editable: a visible
 * bloom-contentFirst, else a visible bloom-content1, else the first visible editable. Returns
 * undefined if the group has no visible editable, which happens on template pages where the
 * group holds only the lang="z" prototype.
 */
export function getFirstVisibleEditable(
    translationGroup: HTMLElement,
): HTMLElement | undefined {
    const visibles = getEditables(translationGroup).filter(isVisible);
    return (
        visibles.find((e) => e.classList.contains("bloom-contentFirst")) ??
        visibles.find((e) => e.classList.contains("bloom-content1")) ??
        visibles[0]
    );
}

/**
 * Gives a wrapper a kInlineImageIdAttr if it has none, and returns the value. Every lookup that
 * pairs a wrapper with its copies in the other languages matches on that attribute, so a
 * wrapper without it can't be paired. syncInlineImagesFromEditable would keep it and add
 * another copy beside it every time the page is set up, and arrangeInlineImages would never
 * put it in place. makeInlineImageWrapper always creates an id, so this is for markup that
 * came from somewhere else, such as hand-edited HTML or a paste from another program.
 */
export function ensureInlineImageId(wrapper: HTMLElement): string {
    const existing = getInlineImageId(wrapper);
    if (existing) return existing;
    const id = createValidXhtmlUniqueId();
    wrapper.setAttribute(kInlineImageIdAttr, id);
    return id;
}

/**
 * Builds a new inline image wrapper. It is a contenteditable=false div holding a placeholder
 * image, docked right at the default width. It is not added to the document; see
 * insertInlineImage. When building the copies of one image for the other editables, pass the
 * same id for each so they all share it; leave it out to create a new one.
 */
export function makeInlineImageWrapper(id?: string): HTMLElement {
    const wrapper = document.createElement("div");
    wrapper.setAttribute(kInlineImageIdAttr, id ?? createValidXhtmlUniqueId());
    wrapper.classList.add(
        kInlineImageClass,
        kInlineImageRightClass,
        kKeepFirstInFieldClass,
        kPreventRemovalClass,
    );
    // The wrapper is not text. CKEditor accepts contenteditable=false elements inside the
    // fields it manages (the format cog in StyleEditor.ts is one), and the talking book tool
    // skips them when adding audio markup (audioRecording.ts).
    wrapper.setAttribute("contenteditable", "false");
    wrapper.style.setProperty(kInlineImageWidthVar, kDefaultInlineImageWidth);
    wrapper.style.setProperty(
        kInlineImageAspectRatioVar,
        kDefaultInlineImageAspectRatio,
    );
    const img = document.createElement("img");
    // We no longer ship a placeHolder.png file; the src is just how Bloom marks an image
    // as "not chosen yet" (see bloomImages.ts), and CSS draws the flower.
    img.setAttribute("src", "placeHolder.png");
    img.setAttribute("alt", "");
    wrapper.appendChild(img);
    return wrapper;
}

/**
 * Adds a new inline image to a translation group, putting a copy in every bloom-editable
 * child, including the lang="z" prototype. When Bloom later adds a language to this group,
 * MakeElementWithLanguageForOneGroup clones an existing editable and StripOutText removes the
 * text from the clone, so a copy in the prototype gives the new language the image without
 * any C# changes (TranslationGroupManagerTests covers that cloning). Returns the copy in the
 * editable the reader sees, which is the one the caller will want to work with (for example,
 * to open the image chooser on it).
 */
export function insertInlineImage(translationGroup: HTMLElement): HTMLElement {
    recordInlineImageUndoPoint(translationGroup);
    // The copies we put in each language's editable all get this one id.
    const id = createValidXhtmlUniqueId();
    const editables = getEditables(translationGroup);
    editables.forEach((editable) => {
        ensureEditableHasAParagraph(editable);
        // A new image goes after the floating images already at the start of the editable,
        // so it appears after them instead of in front of them.
        editable.insertBefore(
            makeInlineImageWrapper(id),
            getFloatingClusterEnd(editable),
        );
    });
    const preferred = getFirstVisibleEditable(translationGroup) ?? editables[0];
    return getInlineImageById(preferred, id)!;
}

/**
 * Deletes the inline image this wrapper is a copy of, removing its copy from every
 * bloom-editable of its group. Other images in the same block are left alone.
 *
 * It takes the wrapper the user acted on instead of the translation group, because when a
 * block has several images, a function given only the group could only guess which one was
 * meant. It throws if given something that is not an inline image, so that it can't quietly
 * delete the wrong thing.
 */
export function removeInlineImage(wrapper: HTMLElement): void {
    const id = getInlineImageId(wrapper);
    const translationGroup = getTranslationGroupOf(wrapper);
    if (!wrapper.classList.contains(kInlineImageClass) || !translationGroup) {
        throw new Error(
            "removeInlineImage requires an inline image wrapper inside a translation group",
        );
    }
    recordInlineImageUndoPoint(translationGroup);
    getEditables(translationGroup).forEach((editable) => {
        // Only hand-written markup lacks an id, and it has no copies to match, so in that case
        // we remove just the wrapper we were given.
        if (id) getInlineImageById(editable, id)?.remove();
    });
    if (!id) wrapper.remove();
}

/**
 * Moves an inline image to a different dock. Switching among left, right and middle only
 * changes the class, unless the image is coming back from the bottom; that is why a drag can
 * be copied safely to the other languages. Docking at the bottom moves the wrapper to the
 * end of the editable, after the text. This also adds or removes bloom-keepFirstInField:
 * BloomField uses that class to decide whether the field's required <p> goes after the images
 * (left, right, middle) or before them (bottom), so a bottom-docked wrapper must not have it.
 * The caller must call syncInlineImagesFromEditable() afterwards.
 */
export function setInlineImageDock(
    wrapper: HTMLElement,
    dock: InlineImageDock,
): void {
    kInlineImageDockClasses.forEach((c) => wrapper.classList.remove(c));
    wrapper.classList.add(dock);
    if (dock === kInlineImageBottomClass) {
        wrapper.classList.remove(kKeepFirstInFieldClass);
    } else {
        wrapper.classList.add(kKeepFirstInFieldClass);
    }
    const editable = wrapper.parentElement;
    if (editable) moveToDockCluster(editable, wrapper);
}

/**
 * Copies this editable's inline images onto the other editables in its translation group, so
 * that every language shows the same images, placed the same way, in the same order. Call it
 * after anything that changes an image, such as choosing a different picture, dragging,
 * resizing, or changing the dock.
 *
 * The other editables end up with exactly this editable's set of images. Copies are paired up
 * by kInlineImageIdAttr, so an image whose size or position changed is replaced with the new
 * version, an image that is new here is added to them, and an image that is gone from here is
 * removed from them. Their text is left alone. Calling it twice does no harm, so callers can
 * call it whenever in doubt. It removes bloom-ui children, the selected class and temporary
 * ids from the copies, so none of those are copied or saved.
 *
 * It does nothing if this editable has no inline images, because there is no way to tell
 * that apart from "this is not the editable being edited". Deleting an image goes through
 * removeInlineImage.
 */
export function syncInlineImagesFromEditable(editable: HTMLElement): void {
    const sources = getInlineImagesInEditable(editable);
    if (sources.length === 0) return;
    const translationGroup = getTranslationGroupOf(editable);
    if (!translationGroup) return;

    // Everything below pairs copies up by id, so first give an id to any wrapper that lacks
    // one. See ensureInlineImageId for what goes wrong otherwise.
    sources.forEach(ensureInlineImageId);
    const wantedIds = new Set(
        sources.map((source) => getInlineImageId(source)),
    );
    getEditables(translationGroup).forEach((sibling) => {
        if (sibling === editable) return;
        ensureEditableHasAParagraph(sibling);
        // Remove any image the source editable no longer has.
        getInlineImagesInEditable(sibling).forEach((existing) => {
            if (!wantedIds.has(getInlineImageId(existing))) existing.remove();
        });
        // Replace each image's copy here with a fresh copy of the source, or add one if there
        // isn't one. Everything about an inline image is in its class list and style
        // attribute, so a fresh copy is simpler than updating the old one and gives the same
        // result.
        sources.forEach((source) => {
            const clone = makeSerializedCopy(source);
            const id = getInlineImageId(source);
            const existing = id ? getInlineImageById(sibling, id) : null;
            if (existing) existing.replaceWith(clone);
            else sibling.appendChild(clone);
        });
        // Put them in the source's order, at the start or end of the editable as each dock
        // requires.
        arrangeInlineImages(sibling, sources);
    });
}

/**
 * Makes every editable of the group hold the same inline images, by copying the images of the
 * editable that getCanonicalInlineImageEditable picks onto the others. This repairs a group
 * where the copies differ, or where some editable has no copy at all, for instance a language
 * added by an older Bloom, or an editable the user managed to delete the image from. It does
 * nothing for a group with no inline image.
 */
export function normalizeInlineImages(translationGroup: HTMLElement): void {
    const canonicalEditable = getCanonicalInlineImageEditable(translationGroup);
    if (!canonicalEditable) return;
    syncInlineImagesFromEditable(canonicalEditable);
}

/**
 * Sets up, when the page loads, every translation group in the container that has an inline
 * image. It normalizes the copies, and arranges for each picture's real shape to be recorded
 * and the page's overflow checked again once the picture loads. Called from SetupElements.
 */
export function setupInlineImages(container: HTMLElement): void {
    // Undo snapshots hold references to elements, so setup has to throw away the ones that can
    // no longer be restored. That happens on a different page (the page id check), and when
    // the page frame is rebuilt, where the page id is the same but every element is new (the
    // isConnected check). Setting up only PART of the page must NOT throw anything away.
    // CanvasElementManager calls SetupElements with just the bloom-canvas it added, and the
    // image description tool calls it with just its container. The groups there keep their
    // editables, and clearing everything would mean the user could no longer undo their last
    // picture move, without any sign of why.
    clearInlineImageUndoOnPageChange();
    dropInlineImageUndoStateForDetachedGroups();
    getTranslationGroupsWithInlineImages(container).forEach(
        (translationGroup) => {
            normalizeInlineImages(translationGroup);
            getEditables(translationGroup).forEach((editable) => {
                getInlineImagesInEditable(editable).forEach(wireUpImage);
            });
        },
    );
}

/**
 * Call this when the image inside an inline image wrapper has been replaced because the user
 * chose a different picture. The old picture's shape no longer applies, so we remove the
 * recorded aspect ratio and let the new picture set one when it loads. The new src also has
 * to be copied to the other languages. bloomEditing.ts's changeImageInfo calls this for any
 * img that is inside an inline image.
 */
export function handleInlineImageChanged(img: HTMLElement): void {
    const wrapper = img.closest(kInlineImageSelector) as HTMLElement | null;
    if (!wrapper) return;
    const editable = wrapper.closest(kEditableSelector) as HTMLElement | null;
    if (!editable) return;
    wrapper.style.removeProperty(kInlineImageAspectRatioVar);
    wireUpImage(wrapper);
    syncInlineImagesFromEditable(editable);
    OverflowChecker.AdjustSizeOrMarkOverflowSoon(editable);
    // Tell the code that opened the image chooser, which also manages the selection, that the
    // picture has arrived. It needs to select the wrapper again, because the trip through the
    // image chooser can leave the focus in the text, and undoing this change (or the insert
    // before it) only works while the wrapper is selected.
    wrapper.dispatchEvent(
        new CustomEvent(kInlineImageChangedEvent, {
            bubbles: true,
            detail: wrapper,
        }),
    );
}

// --- undo --------------------------------------------------------------------
//
// Inline image operations need their own undo stack. CKEditor's undo can't see changes our
// code makes to the DOM, and ImageUndoManager.ts only knows how to restore one img's src and
// crop. It knows nothing about a wrapper that has a copy in every language, all of which
// have to be restored together. So this is a third small undo stack, built the same way as
// ImageUndoManager: a stack of snapshots, a prepare step and a commit step, cleared when the
// page changes, offered only when the thing it applies to is active, and with no redo.
//
// A snapshot records, for every editable in one translation group, the markup of all its
// inline images, in order. Restoring all of that is simpler and more reliable than reversing
// individual changes, because one operation can change every editable (sync rewrites all of
// them) and any number of images in each. The markup holds each image's id, dock, size and
// position, and the list holds their order, so a snapshot needs nothing else.

type InlineImageEditableSnapshot = {
    editable: HTMLElement;
    // The markup of each inline image, in the order they appeared. Empty when this editable
    // had none.
    wrapperHtmls: string[];
};

type InlineImageUndoItem = {
    translationGroup: HTMLElement;
    editables: InlineImageEditableSnapshot[];
    // The group's content when the snapshot was taken, so we can tell whether the person has
    // edited since. inlineImageCanUndo uses that to decide whether ctrl+z goes to this stack
    // or to CKEditor.
    contentAtSnapshot: string;
    // Set when the page reports typing in this block (noteInlineImageBlockWasEdited). The
    // comparison with contentAtSnapshot can't see an edit that left the content as it was,
    // such as a word typed and then deleted, yet CKEditor holds undo points for both steps.
    // This flag makes us let CKEditor undo those first; see hasEditedSinceInlineImageSnapshot.
    wasEditedSinceSnapshot: boolean;
    // Where CKEditor's undo stack stood when the snapshot was taken, so that typing done after
    // this operation can be told apart from typing done before it. Undefined when there was
    // no CKEditor to ask.
    ckeditorUndoAtSnapshot?: CkeditorUndoPosition;
};

// Where CKEditor stands in its own stack of undo snapshots, and which editable's stack that is.
// `index` is CKEditor's own name for the position. It goes up by one for each edit and down as
// edits are undone. Two positions can only be compared when they came from the same
// undoManager, because every editable has its own and each counts from zero.
// `historyIsFull` says the stack holds as many snapshots as CKEditor keeps (20); from then on
// the position stops going up.
type CkeditorUndoPosition = {
    undoManager: unknown;
    index: number;
    historyIsFull: boolean;
};

const inlineImageUndoStack: InlineImageUndoItem[] = [];
let pendingInlineImageUndo: InlineImageUndoItem | undefined;
let pageIdForInlineImageUndo: string | undefined;

/**
 * Takes a snapshot for an operation that is about to happen but might not complete, such as a
 * drag the user may abandon or an image change that may fail. Follow it with
 * commitPendingInlineImageUndo once the change has happened, or discardPendingInlineImageUndo
 * if it didn't. It is fast enough to call when a drag starts.
 * Takes the translation group, or any element inside one.
 */
export function prepareInlineImageUndo(element: HTMLElement): void {
    clearInlineImageUndoOnPageChange();
    const translationGroup = getTranslationGroupOf(element);
    pendingInlineImageUndo = translationGroup
        ? takeInlineImageSnapshot(translationGroup)
        : undefined;
}

/**
 * Pushes the snapshot taken by prepareInlineImageUndo, now that the operation has happened.
 * It ignores a pending snapshot that belongs to some other translation group, so that an
 * operation that was replaced by another one, or that was for a different group, can't push
 * a misleading undo point.
 *
 * If the group now looks exactly as the snapshot recorded it, the snapshot is dropped. A drag
 * comes here whenever the pointer moved at all, but a move that doesn't fit is put back where
 * it was, so a drag into a full side, or past the level of another image, ends exactly where it
 * began. Pushing that snapshot would give the person an Undo that visibly does nothing, and
 * the Undo after it would take back a change they had stopped thinking about.
 */
export function commitPendingInlineImageUndo(element: HTMLElement): void {
    clearInlineImageUndoOnPageChange();
    const translationGroup = getTranslationGroupOf(element);
    if (
        pendingInlineImageUndo &&
        pendingInlineImageUndo.translationGroup === translationGroup &&
        // This check only makes sense here, where the change has already happened, so a
        // snapshot that still matches the group means the operation ended where it began.
        // recordInlineImageUndoPoint runs BEFORE its change, when the snapshot always matches.
        !isInlineImageSnapshotStillTrue(pendingInlineImageUndo)
    ) {
        inlineImageUndoStack.push(pendingInlineImageUndo);
    }
    pendingInlineImageUndo = undefined;
}

/**
 * Whether every image in the group looks exactly as this snapshot recorded it. The wrapper
 * markup holds each image's id, dock, size and position, so comparing the markup compares
 * everything an undo would restore.
 */
function isInlineImageSnapshotStillTrue(item: InlineImageUndoItem): boolean {
    const now = takeInlineImageSnapshot(item.translationGroup);
    if (now.editables.length !== item.editables.length) return false;
    return now.editables.every((current, i) => {
        const then = item.editables[i];
        return (
            current.editable === then.editable &&
            current.wrapperHtmls.length === then.wrapperHtmls.length &&
            current.wrapperHtmls.every(
                (html, j) => html === then.wrapperHtmls[j],
            )
        );
    });
}

/** Throws away a prepared snapshot, for an operation that turned out not to happen. */
export function discardPendingInlineImageUndo(): void {
    pendingInlineImageUndo = undefined;
}

/**
 * Records an undo point for an operation that will definitely change something (insert,
 * remove, a completed dock change). It does the work of prepareInlineImageUndo and
 * commitPendingInlineImageUndo in one call; use those two instead when the operation might
 * not complete. Takes the translation group, or any element in one.
 */
export function recordInlineImageUndoPoint(element: HTMLElement): void {
    prepareInlineImageUndo(element);
    const translationGroup = getTranslationGroupOf(element);
    // This pushes the snapshot directly instead of calling commitPendingInlineImageUndo. The
    // change has not happened yet, so that function's check for whether anything changed
    // would find nothing and throw the snapshot away.
    if (
        pendingInlineImageUndo &&
        pendingInlineImageUndo.translationGroup === translationGroup
    ) {
        inlineImageUndoStack.push(pendingInlineImageUndo);
    }
    pendingInlineImageUndo = undefined;
}

/**
 * Forgets all inline image undo state. Call it when something happens that we cannot undo,
 * so that undo can't go back past it and restore a state that does not fit with what the
 * user sees now.
 */
export function clearInlineImageUndoState(): void {
    inlineImageUndoStack.length = 0;
    pendingInlineImageUndo = undefined;
}

/**
 * Forgets the undo state for translation groups that are no longer in the document, which is
 * what happens when the page frame is rebuilt. A snapshot restores into the same elements it
 * recorded, so a snapshot of a detached group could only restore into elements that are not
 * shown.
 */
function dropInlineImageUndoStateForDetachedGroups(): void {
    for (let i = inlineImageUndoStack.length - 1; i >= 0; i--) {
        if (!inlineImageUndoStack[i].translationGroup.isConnected) {
            inlineImageUndoStack.splice(i, 1);
        }
    }
    if (pendingInlineImageUndo?.translationGroup.isConnected === false) {
        pendingInlineImageUndo = undefined;
    }
}

/**
 * Whether the workspace undo command should go to this undo stack. Normally that needs a
 * recorded snapshot, and an active inline image in the SAME translation group. The check
 * matters because workspaceRoot.handleUndo tries this stack before CKEditor, and without the
 * check an old inline image snapshot would be undone instead of the text the user typed a
 * moment ago. It is like canUndoImageOperation's requirement that an image container be
 * active, with the added requirement of the same group, so we never restore a block the user
 * isn't working in.
 */
export function inlineImageCanUndo(): boolean {
    clearInlineImageUndoOnPageChange();
    const top = inlineImageUndoStack[inlineImageUndoStack.length - 1];
    if (!top) return false;
    // ctrl+z should undo whatever the person edited last, and an edit made after the snapshot
    // is newer than it. If we undid the snapshot first, the picture would go back to how it was
    // before their edit, out of the order things happened in. That is the one mistake these
    // separate undo stacks can make (see the comment on the if/else chain in
    // workspaceRoot.handleUndo). This applies even when a picture is selected: selecting one
    // leaves the caret in the text, so the person can still type, and right-clicking the text
    // gets into that state without their meaning to, because the menu leaves the picture
    // selected.
    if (hasEditedSinceInlineImageSnapshot(top)) return false;
    const activeWrapper = getActiveInlineImage();
    if (activeWrapper) {
        return getTranslationGroupOf(activeWrapper) === top.translationGroup;
    }
    // Requiring an active inline image would be wrong when the user's last action left no
    // picture selected. Two actions do that. Deleting an image removes the one they were
    // working on, so requiring a selection would mean a deleted inline image could never be
    // undone. Undo itself rebuilds the wrappers, and when it has just undone the insert that
    // created the selected picture, there is no copy left to select, so a second ctrl+z in a
    // row would not reach this stack. In this case we require only that the caret is still in
    // the block we would restore.
    const focused = getElementWithFocusOrSelection();
    return !!focused && top.translationGroup.contains(focused);
}

/**
 * Whether the person has made edits that are newer than this snapshot and so have to be undone
 * before it. We check two ways, because each one misses some edits.
 *
 * Comparing the content with contentAtSnapshot catches an edit nobody reported, including one
 * that changes only markup (bolding a word). Once the edit is undone the content matches
 * again, so this check stops saying there is a newer edit.
 *
 * wasEditedSinceSnapshot catches an edit that left the content the same, such as a word typed
 * and deleted again, which the comparison can't see. That flag is never cleared, so if we
 * used it alone the picture operation could never be undone, and once CKEditor had nothing
 * left to undo, ctrl+z would do nothing at all. So the flag only makes us wait while CKEditor
 * still holds typing, and comparing CKEditor's position now with its position when we took
 * the snapshot tells us whether any of that typing is newer than the snapshot. See
 * hasNewerTypingThanInlineImageSnapshot.
 */
function hasEditedSinceInlineImageSnapshot(item: InlineImageUndoItem): boolean {
    if (
        item.wasEditedSinceSnapshot &&
        hasNewerTypingThanInlineImageSnapshot(item)
    )
        return true;
    return (
        getInlineImageUndoContentFingerprint(item.translationGroup) !==
        item.contentAtSnapshot
    );
}

/**
 * Whether the typing CKEditor holds includes any typed after this snapshot was taken.
 *
 * Asking only whether CKEditor has anything to undo gives the wrong answer when the person
 * typed both before and after the picture operation. After the newer typing is undone,
 * CKEditor still holds the older typing, so ctrl+z would keep going to CKEditor, which would
 * undo text from before the picture operation while the picture change stayed. So we compare
 * CKEditor's position with the one recorded when the snapshot was taken.
 *
 * Positions from two different editables each count from zero, so they can't be compared.
 * When the person has moved to another editable since the snapshot, or there was no CKEditor
 * to ask at either time, all we can check is whether CKEditor holds anything at all.
 */
function hasNewerTypingThanInlineImageSnapshot(
    item: InlineImageUndoItem,
): boolean {
    const now = getCkeditorUndoPosition();
    const atSnapshot = item.ckeditorUndoAtSnapshot;
    if (now && atSnapshot && now.undoManager === atSnapshot.undoManager) {
        // CKEditor keeps at most 20 snapshots, and once it has that many its position stops
        // going up: saving a new one drops the oldest and then appends, so the newest ends up
        // at the same position as the previous newest. In a block with that much typing, an
        // unchanged position no longer means nothing was saved since our snapshot, so we have
        // to assume there was typing. Undoing that typing moves the position below ours, so
        // this does not keep returning true forever.
        if (now.index === atSnapshot.index) return now.historyIsFull;
        return now.index > atSnapshot.index;
    }
    return ckeditorHasSomethingToUndo();
}

/**
 * Where the focused editable's CKEditor stands in its undo stack, or undefined if there is no
 * CKEditor or it does not say. CKEditor has no public API for this, so we read it from the
 * page's global CKEDITOR object, relying on the same undocumented internals as
 * editablePage.ts's ckeditorCanUndo.
 */
function getCkeditorUndoPosition(): CkeditorUndoPosition | undefined {
    const undoManager = getCkeditorUndoManager();
    const index = undoManager?.index;
    if (!undoManager || typeof index !== "number") return undefined;
    const kept = undoManager.snapshots?.length ?? 0;
    const limit = undoManager.limit ?? 0;
    return {
        undoManager,
        index,
        historyIsFull: !!limit && kept >= limit,
    };
}

/**
 * Whether CKEditor has any text editing it could undo. Like editablePage.ts's ckeditorCanUndo,
 * it returns false when there is no CKEditor.
 */
function ckeditorHasSomethingToUndo(): boolean {
    return !!getCkeditorUndoManager()?.undoable?.();
}

function getCkeditorUndoManager(): CkeditorUndoManager | undefined {
    return (
        globalThis as unknown as {
            CKEDITOR?: { currentInstance?: { undoManager?: unknown } };
        }
    ).CKEDITOR?.currentInstance?.undoManager as CkeditorUndoManager | undefined;
}

// The parts of CKEditor's undo manager that this file reads: whether it has anything to undo,
// its position, the snapshots it holds, and the most it will hold.
type CkeditorUndoManager = {
    undoable?: () => boolean;
    index?: number;
    snapshots?: unknown[];
    limit?: number;
};

/**
 * Reports that the person has just edited the text of a block, so that ctrl+z goes to
 * CKEditor instead of to an inline image operation from before the edit. Call it for any
 * editing in a bloom-editable; it only has an effect when the same group has an inline image
 * undo point waiting. Takes the editable, or anything inside one.
 */
export function noteInlineImageBlockWasEdited(element: HTMLElement): void {
    const translationGroup = getTranslationGroupOf(element);
    if (!translationGroup) return;
    // Mark every undo point for this block, including ones below the top of the stack. An
    // operation in another block can be on top of one in this block, and once it is undone,
    // the one below would bring back a picture from before this edit.
    inlineImageUndoStack.forEach((item) => {
        if (item.translationGroup === translationGroup)
            item.wasEditedSinceSnapshot = true;
    });
    if (pendingInlineImageUndo?.translationGroup === translationGroup)
        pendingInlineImageUndo.wasEditedSinceSnapshot = true;
}

/**
 * Undoes the most recent inline image operation by restoring its snapshot to every editable
 * of the translation group, and checks overflow again, since the image's size may have
 * changed. Returns false if there was nothing to undo. Like ImageUndoManager, there is no
 * redo.
 */
export function inlineImageUndo(): boolean {
    clearInlineImageUndoOnPageChange();
    const undoItem = inlineImageUndoStack.pop();
    if (!undoItem) return false;
    // Restoring replaces the wrapper elements, which would lose the selection, and then a
    // second ctrl+z might not reach this stack (inlineImageCanUndo looks for an active inline
    // image). So we select the restored copy of the same image. We find it by id, because the
    // block may hold several images, and selecting the wrong one would be worse than
    // selecting none.
    const wasSelected = document.querySelector(
        kInlineImageSelector + "." + kInlineImageSelectedClass,
    ) as HTMLElement | null;
    const selectedId = wasSelected ? getInlineImageId(wasSelected) : undefined;
    const editableThatWasSelected = wasSelected?.closest(
        kEditableSelector,
    ) as HTMLElement | null;
    undoItem.editables.forEach((snapshot) => {
        restoreInlineImageSnapshot(snapshot);
    });
    if (editableThatWasSelected && selectedId) {
        getInlineImageById(editableThatWasSelected, selectedId)?.classList.add(
            kInlineImageSelectedClass,
        );
    }
    // Undo is the only operation that replaces the wrapper the user is working on; sync and
    // normalize only replace the copies in the other editables. So anything that held a
    // reference to the old wrapper is now out of date, and so is anything that was inside it,
    // such as the drag handles and the buttons shown on hover, which are bloom-ui and so are
    // not in the snapshot. This event tells the code that handles clicks and drags on inline
    // images to look them up in the DOM again.
    undoItem.translationGroup.dispatchEvent(
        new CustomEvent(kInlineImagesRestoredEvent, { bubbles: true }),
    );
    return true;
}

/**
 * Takes the undo snapshot before the picture inside an inline image is changed. Returns true
 * if this img is inside an inline image. In that case this file handles the undo, and the
 * caller must not also record an ImageUndoManager undo point, because that would restore
 * only one language's src and leave the other languages showing the new picture.
 */
export function prepareInlineImageUndoForImageChange(
    img: HTMLElement,
): boolean {
    if (!img.closest(kInlineImageSelector)) return false;
    prepareInlineImageUndo(img);
    return true;
}

/**
 * Pushes the snapshot prepareInlineImageUndoForImageChange took, now that the new picture is
 * in place. Returns true if this img is inside an inline image, as above.
 */
export function commitInlineImageUndoForImageChange(img: HTMLElement): boolean {
    if (!img.closest(kInlineImageSelector)) return false;
    commitPendingInlineImageUndo(img);
    return true;
}

// --- internals ---------------------------------------------------------------

const getTranslationGroupOf = (element: HTMLElement): HTMLElement | undefined =>
    (element.closest(".bloom-translationGroup") as HTMLElement | null) ??
    undefined;

/**
 * The markup of a translation group, leaving out the inline images. Comparing this with its
 * value when a snapshot was taken tells us whether the person has edited since, and that
 * decides whether ctrl+z undoes the picture change or the edit (see inlineImageCanUndo).
 *
 * It includes the markup as well as the characters, because bolding a word leaves the text the
 * same but is an edit CKEditor has an undo point for. If we missed it, we would undo the
 * picture first and leave the bolding in place. Elements with bloom-ui are removed so that a
 * toolbar or a format cog appearing does not count as an edit.
 *
 * The contents of the wrappers are left out too, because an operation that adds or removes a
 * picture also adds or removes whatever is inside it, and that must not count as an edit.
 */
function getInlineImageUndoContentFingerprint(
    translationGroup: HTMLElement,
): string {
    return getEditables(translationGroup)
        .map((editable) => {
            const copy = editable.cloneNode(true) as HTMLElement;
            copy.querySelectorAll(kInlineImageSelector).forEach((wrapper) =>
                wrapper.remove(),
            );
            copy.querySelectorAll(".bloom-ui").forEach((e) => e.remove());
            return copy.innerHTML;
        })
        .join("|");
}

function takeInlineImageSnapshot(
    translationGroup: HTMLElement,
): InlineImageUndoItem {
    return {
        translationGroup,
        contentAtSnapshot:
            getInlineImageUndoContentFingerprint(translationGroup),
        wasEditedSinceSnapshot: false,
        ckeditorUndoAtSnapshot: getCkeditorUndoPosition(),
        editables: getEditables(translationGroup).map((editable) => ({
            editable,
            wrapperHtmls: getInlineImagesInEditable(editable).map(
                (wrapper) => makeSerializedCopy(wrapper).outerHTML,
            ),
        })),
    };
}

function restoreInlineImageSnapshot(
    snapshot: InlineImageEditableSnapshot,
): void {
    const editable = snapshot.editable;
    getInlineImagesInEditable(editable).forEach((wrapper) => wrapper.remove());
    // Add all the wrappers first and put them in order afterwards, so that arrangeInlineImages
    // works out where the text starts and ends with every wrapper present.
    const restored = snapshot.wrapperHtmls.map((html) => {
        const template = document.createElement("template");
        template.innerHTML = html;
        const wrapper = template.content.firstElementChild as HTMLElement;
        editable.appendChild(wrapper);
        wireUpImage(wrapper);
        return wrapper;
    });
    arrangeInlineImages(editable, restored);
    OverflowChecker.AdjustSizeOrMarkOverflowSoon(editable);
}

/**
 * Removes bloom-inlineImage-selected from every inline image in the given DOM.
 *
 * bloom-inlineImage-selected is only for editing, but it can't be a bloom-ui class, because it
 * is on the wrapper that is part of the book, and removing bloom-ui elements would remove the
 * picture too. So nothing else removes it before saving. If the book were saved with a picture
 * selected, the class would go into the book's HTML, and from there into spreadsheet exports
 * and published books. removeEditingDebris removes the other things that only matter while
 * editing, such as origami-layout-mode and the textBox-identifier labels, for the same
 * reason, so removeEditingDebris is where this should be called.
 */
export function clearInlineImageSelection(container: HTMLElement): void {
    container
        .querySelectorAll(
            kInlineImageSelector + "." + kInlineImageSelectedClass,
        )
        .forEach((wrapper) =>
            wrapper.classList.remove(kInlineImageSelectedClass),
        );
}
// The inline image the user is working on, if any. That is the one marked with
// kInlineImageSelectedClass, or else one that contains the focus or the caret, which is how a
// wrapper the user clicked into shows up before anything has marked it selected.
function getActiveInlineImage(): HTMLElement | undefined {
    const selected = document.querySelector(
        kInlineImageSelector + "." + kInlineImageSelectedClass,
    ) as HTMLElement | null;
    if (selected) return selected;
    return (
        (getElementWithFocusOrSelection()?.closest(
            kInlineImageSelector,
        ) as HTMLElement | null) ?? undefined
    );
}

// Where the user is: the focused element, or failing that the element holding the caret.
function getElementWithFocusOrSelection(): HTMLElement | undefined {
    const active = document.activeElement as HTMLElement | null;
    // document.body is what we get when nothing in the page has focus; it tells us nothing.
    if (active && active !== document.body) return active;
    const anchorNode = document.getSelection()?.anchorNode;
    const anchorElement =
        anchorNode instanceof Element ? anchorNode : anchorNode?.parentElement;
    return (anchorElement as HTMLElement | null) ?? undefined;
}

// Snapshots hold references to elements, so they only work on the page they were taken on.
// This uses the same data-page-id check as ImageUndoManager.clearImageOperationUndoOnPageChange.
function clearInlineImageUndoOnPageChange(): void {
    const currentPageId =
        (
            document.getElementsByClassName("bloom-page")[0] as
                | HTMLElement
                | undefined
        )?.getAttribute("data-page-id") ?? undefined;
    if (pageIdForInlineImageUndo !== currentPageId) {
        clearInlineImageUndoState();
        pageIdForInlineImageUndo = currentPageId;
    }
}

// All the translation groups in (or equal to) the container that hold at least one inline
// image.
export function getTranslationGroupsWithInlineImages(
    container: HTMLElement,
): HTMLElement[] {
    const groups = new Set<HTMLElement>();
    getInlineImages(container).forEach((wrapper) => {
        const group = wrapper.closest(
            ".bloom-translationGroup",
        ) as HTMLElement | null;
        if (group) groups.add(group);
    });
    return Array.from(groups);
}

// Records the image's real shape so that the layout, and so the overflow check, is right.
// It also checks overflow again when the image loads, because an image that just got taller
// can push the text past the bottom of the block. A placeholder never loads, so it keeps the
// default ratio.
function wireUpImage(wrapper: HTMLElement): void {
    const img = wrapper.querySelector("img") as HTMLImageElement | null;
    if (!img) return;
    setAspectRatioFromNaturalSize(wrapper, img);
    if (imagesWithLoadHandler.has(img)) return;
    imagesWithLoadHandler.add(img);
    img.addEventListener("load", () => {
        setAspectRatioFromNaturalSize(wrapper, img);
        const editable = wrapper.closest(
            kEditableSelector,
        ) as HTMLElement | null;
        if (editable) OverflowChecker.AdjustSizeOrMarkOverflowSoon(editable);
    });
}

/**
 * Writes the picture's real shape onto the wrapper, and onto this picture's copies in the
 * group's other editables.
 *
 * Each copy has its own img and could set its own ratio when it loads, but only the copy in
 * the language being shown does so reliably. A copy's load can happen before the src changed,
 * or not happen at the moment sync copies the markup across. A copy with no ratio uses
 * kDefaultInlineImageAspectRatio and has the wrong shape. The copy where this matters is the
 * lang="z" prototype: it is hidden, so nobody sees the wrong shape, and it is what
 * TranslationGroupManager clones when a language is added to the collection later. Choosing a
 * 274x300 picture without this code left "en" at "274 / 300" and "z" at "".
 */
function setAspectRatioFromNaturalSize(
    wrapper: HTMLElement,
    img: HTMLImageElement,
): void {
    if (!img.naturalWidth || !img.naturalHeight) return; // not loaded (or a placeholder)
    const ratio = `${img.naturalWidth} / ${img.naturalHeight}`;
    wrapper.style.setProperty(kInlineImageAspectRatioVar, ratio);
    const id = getInlineImageId(wrapper);
    const group = getTranslationGroupOf(wrapper);
    if (!id || !group) return;
    getEditables(group).forEach((editable) => {
        getInlineImagesInEditable(editable)
            .filter((each) => getInlineImageId(each) === id)
            .forEach((each) =>
                each.style.setProperty(kInlineImageAspectRatioVar, ratio),
            );
    });
}

// A copy of the wrapper as it should be saved and copied to other languages. It has no
// bloom-ui elements, no selected class, and no id attributes. Changing the image puts a
// temporary id on the img, and the same id on every language's copy would make the ids
// non-unique.
// Only the `id` attribute is removed. kInlineImageIdAttr is a data-* attribute and stays,
// because it is what identifies this copy as the same image in another language, and every
// copy of one image must have the same value.
function makeSerializedCopy(wrapper: HTMLElement): HTMLElement {
    const clone = wrapper.cloneNode(true) as HTMLElement;
    clone.classList.remove(kInlineImageSelectedClass);
    clone.querySelectorAll(".bloom-ui").forEach((e) => e.remove());
    clone.removeAttribute("id");
    clone.querySelectorAll("[id]").forEach((e) => e.removeAttribute("id"));
    return clone;
}

// An editable's children come in this order: the images docked left, right or middle, then
// the text, then the images docked at the bottom, then any bloom-ui elements (the format cog).
// Within each group of images, DOM order is the order of the images, and sync copies it to
// the other languages. The wrappers do not move within the text, which is why a position
// means the same thing in languages whose text is completely different.

// The node to insert a left, right or middle image before. It is the first child that is
// neither such an image nor bloom-ui, which is where the text starts. Null when the editable
// has no content yet, in which case appending is right.
function getFloatingClusterEnd(editable: HTMLElement): Node | null {
    const firstContent = Array.from(editable.children).find(
        (child) =>
            !child.classList.contains("bloom-ui") &&
            !(
                child.classList.contains(kInlineImageClass) &&
                !child.classList.contains(kInlineImageBottomClass)
            ),
    );
    return firstContent ?? null;
}

// The node to insert a bottom-docked image before. It is the first of the bloom-ui elements
// at the end of the editable, so the images come after all the text and the format cog stays
// last. Null when the editable does not end with bloom-ui elements, in which case appending
// puts the image at the end.
function getBottomClusterEnd(editable: HTMLElement): Node | null {
    const children = Array.from(editable.children);
    for (let i = children.length - 1; i >= 0; i--) {
        if (!children[i].classList.contains("bloom-ui")) {
            return children[i].nextSibling;
        }
    }
    return editable.firstChild;
}

// Whether this wrapper is already before all the text.
const isInFloatingCluster = (wrapper: HTMLElement): boolean => {
    let sibling = wrapper.previousElementSibling;
    while (sibling) {
        const isFloatingImage =
            sibling.classList.contains(kInlineImageClass) &&
            !sibling.classList.contains(kInlineImageBottomClass);
        if (!isFloatingImage && !sibling.classList.contains("bloom-ui")) {
            return false;
        }
        sibling = sibling.previousElementSibling;
    }
    return true;
};

// Whether this wrapper is already after all the text.
const isInBottomCluster = (wrapper: HTMLElement): boolean => {
    let sibling = wrapper.nextElementSibling;
    while (sibling) {
        const isBottomImage =
            sibling.classList.contains(kInlineImageClass) &&
            sibling.classList.contains(kInlineImageBottomClass);
        if (!isBottomImage && !sibling.classList.contains("bloom-ui")) {
            return false;
        }
        sibling = sibling.nextElementSibling;
    }
    return true;
};

// Moves one wrapper before or after the text, as its dock requires. A wrapper already on the
// correct side of the text is left exactly where it is, because switching between left, right
// and middle must not move an image past its neighbors; their DOM order is the images' order.
function moveToDockCluster(editable: HTMLElement, wrapper: HTMLElement): void {
    if (wrapper.classList.contains(kInlineImageBottomClass)) {
        if (isInBottomCluster(wrapper)) return;
        editable.insertBefore(wrapper, getBottomClusterEnd(editable));
        return;
    }
    if (isInFloatingCluster(wrapper)) return;
    editable.insertBefore(wrapper, getFloatingClusterEnd(editable));
}

// Puts this editable's images in the same order as the sources list, each before or after the
// text as its dock requires. Inserting each one in turn before the same node reproduces the
// order of the list. The left, right and middle images are placed first, so that
// getBottomClusterEnd runs after they have stopped moving.
function arrangeInlineImages(
    editable: HTMLElement,
    sources: HTMLElement[],
): void {
    const idsFor = (wantBottom: boolean) =>
        sources
            .filter(
                (source) =>
                    source.classList.contains(kInlineImageBottomClass) ===
                    wantBottom,
            )
            .map((source) => getInlineImageId(source))
            .filter((id): id is string => !!id);

    const floatingAnchor = getFloatingClusterEnd(editable);
    idsFor(false).forEach((id) => {
        const wrapper = getInlineImageById(editable, id);
        if (wrapper) editable.insertBefore(wrapper, floatingAnchor);
    });
    const bottomAnchor = getBottomClusterEnd(editable);
    idsFor(true).forEach((id) => {
        const wrapper = getInlineImageById(editable, id);
        if (wrapper) editable.insertBefore(wrapper, bottomAnchor);
    });
}

// If an editable's only content were the contenteditable=false wrapper, the user would have
// nowhere to type, so this adds a paragraph. BloomField.EnsureParagraphsPresent does the same
// when the page loads; we do it here too because we insert wrappers after that has run.
//
// Any block element gives the user somewhere to type, so a <p> is not required. Converted
// content can hold a heading and no paragraph, and adding an empty paragraph under that
// heading would give the reader a blank line that stays in the saved page. We use
// BloomField's own kBlockElementSelector so the two cannot disagree about what counts.
// For every dock except bottom the wrapper has bloom-keepFirstInField, and in that case
// BloomField still requires a <p> after it (the paragraph the text wraps around) and adds one
// itself; this only decides whether we add one first. So this only makes a difference for
// the bottom dock.
function ensureEditableHasAParagraph(editable: HTMLElement): void {
    if (editable.querySelector(kBlockElementSelector)) return;
    editable.appendChild(document.createElement("p"));
}
