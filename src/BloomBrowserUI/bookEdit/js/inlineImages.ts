// Inline (Word-style) images: an image that lives *inside* a bloom-editable, docked left,
// right, as a full-width band, or at the bottom, with the text of that editable wrapping
// around it. See INLINE-IMAGES-PLAN.md for the design and its rationale; the layout rules
// are in content/bookLayout/inlineImages.less and the edit-time affordances in
// bookEdit/css/editMode.less.
//
// Two facts shape everything in this file:
//
// 1. The markup has to be inside each bloom-editable, because a CSS float can only wrap
//    the text of the block it is in. So every language's editable in a translation group
//    carries its own copy of the wrapper, and they are kept identical by JS: the copy in
//    the first visible language's editable is canonical, and normalizeInlineImages()
//    stamps it onto the siblings at page load, while syncInlineImagesFromEditable() does
//    so after every edit. (CSS then shows the image in only one visible editable, so the
//    reader sees the picture once; see inlineImages.less.)
//
// 2. Position is geometry, never DOM anchoring. Sibling editables hold different text with
//    different paragraph structure, so "after the second paragraph" cannot be carried
//    across languages, but "docked right, 40% wide, 120px down" renders the same in all of
//    them. So the wrapper never moves within the text: it is always the editable's first
//    child, or its last child when docked at the bottom -- the one DOM move there is. All
//    the rest of the state is a dock class plus the three custom properties below, which
//    makes (class list + style attribute) the complete, copyable state of an inline image.
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import { kBlockElementSelector } from "../bloomField/BloomField";
import { createValidXhtmlUniqueId } from "./xhtmlIdUtils";

export const kInlineImageClass = "bloom-inlineImage";

export const kInlineImageLeftClass = "bloom-inlineImageLeft";
export const kInlineImageRightClass = "bloom-inlineImageRight";
export const kInlineImageMiddleClass = "bloom-inlineImageMiddle";
export const kInlineImageBottomClass = "bloom-inlineImageBottom";

// The four docks. Exactly one is on a wrapper at any time.
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

// Edit-time only (a bloom-ui-ish marker; it is stripped from the copies we stamp onto
// sibling editables, and from the canonical copy when it is serialized). The interaction
// layer puts this on the wrapper the user has selected, and the undo gate reads it.
export const kInlineImageSelectedClass = "bloom-inlineImage-selected";

// Raised on the translation group (and bubbling) after an undo has replaced its wrappers, so
// that whatever was attached to the old elements can be rebuilt. See inlineImageUndo.
export const kInlineImagesRestoredEvent = "bloom-inlineImagesRestored";

// Raised on the translation group (and bubbling) when a new picture has landed in an inline
// image, with the wrapper as event.detail. The image chooser round trip goes out through C#
// and comes back into changeImage, so this is the only point at which the code that started
// the operation can learn it finished -- which matters because that code owns the selection,
// and the undo of the change (or of the insert that preceded it) is only reachable while the
// wrapper is selected. See handleInlineImageChanged.
export const kInlineImageChangedEvent = "bloom-inlineImageChanged";

// The identity of an inline image, shared by its copy in every language's editable. A text
// block may hold any number of inline images, so "the wrapper in this editable" is not a
// thing: sync, normalize and undo all match copies up by this value. It has to be an
// attribute rather than an id, because TranslationGroupManager strips the id from an
// editable it clones for a new language (TranslationGroupManager.cs:998) -- whereas nothing
// in the C# sweeps touches an unknown data-*, and StripOutText only removes p/br/u/b/i
// elements and text nodes, so the wrapper and this attribute survive into the new language
// with the value intact, which is exactly what we need.
export const kInlineImageIdAttr = "data-bloom-inline-image-id";

// Custom properties carrying the geometry. See inlineImages.less for what each does.
export const kInlineImageWidthVar = "--inline-image-width";
export const kInlineImageOffsetVar = "--inline-image-offset";
export const kInlineImageAspectRatioVar = "--inline-image-aspect-ratio";

// Classes owned by BloomField.ts, which already knows how to protect an embedded image:
// keepFirstInField keeps the required <p> after the image, and preventRemoval undoes a
// ctrl+a DEL that would otherwise take the image with it.
export const kKeepFirstInFieldClass = "bloom-keepFirstInField";
export const kPreventRemovalClass = "bloom-preventRemoval";

export const kDefaultInlineImageWidth = "40%";
// A new wrapper holds a placeholder, which never loads, so there is no natural size to
// take a ratio from. Without one the wrapper would have no height at all, and the user
// would have nothing to see or click. 4/3 is the usual photo shape and is replaced by the
// real ratio as soon as a real image loads.
export const kDefaultInlineImageAspectRatio = "4 / 3";

const kEditableSelector = ".bloom-editable";
const kInlineImageSelector = "." + kInlineImageClass;

// The imgs whose load handler we have already installed. Kept out here rather than marked
// on the element, so that nothing about it can end up in the saved HTML; weak so that it
// doesn't hold onto the images of pages we have navigated away from.
const imagesWithLoadHandler = new WeakSet<HTMLImageElement>();

/**
 * The bloom-editable children of a translation group, in DOM order. (Only direct
 * children: a translation group nested in a canvas element still owns just its own.)
 */
export function getEditables(translationGroup: HTMLElement): HTMLElement[] {
    return Array.from(translationGroup.children).filter((child) =>
        child.classList.contains("bloom-editable"),
    ) as HTMLElement[];
}

const isVisible = (editable: HTMLElement): boolean =>
    editable.classList.contains("bloom-visibility-code-on");

/**
 * The inline images of one bloom-editable, in DOM order -- which is also the order the
 * reader sees them in, and the order sync replicates to the other languages.
 */
export function getInlineImagesInEditable(
    editable: HTMLElement,
): HTMLElement[] {
    return Array.from(
        editable.querySelectorAll(":scope > " + kInlineImageSelector),
    ) as HTMLElement[];
}

/**
 * The FIRST inline image of one bloom-editable, or null. A block may hold any number of them,
 * so prefer getInlineImagesInEditable (or a lookup by id) unless you genuinely mean "the
 * first" or you already know there is only one.
 */
export function getInlineImageInEditable(
    editable: HTMLElement,
): HTMLElement | null {
    return getInlineImagesInEditable(editable)[0] ?? null;
}

/**
 * The FIRST inline image of the group's canonical editable (see
 * getCanonicalInlineImageEditable), or null if the group has none. As above: with several
 * images in a block this is "the first", not "the image", so reach for getInlineImages or
 * hasInlineImages when that is what you mean.
 */
export function getInlineImage(
    translationGroup: HTMLElement,
): HTMLElement | null {
    const editable = getCanonicalInlineImageEditable(translationGroup);
    return editable ? getInlineImageInEditable(editable) : null;
}

/**
 * The identity of one inline image. The same value appears on this image's copy in every
 * language's editable, which is what lets sync, normalize and undo tell "this image" from
 * "the other image in the same block". Undefined only for a wrapper built by hand (an old
 * book, or the test.pug page), which insertInlineImage never produces.
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
 * Every inline image wrapper anywhere in the container (all languages, so this includes
 * the copies that CSS is hiding). Useful for whole-page passes.
 */
export function getInlineImages(container: HTMLElement): HTMLElement[] {
    return Array.from(
        container.querySelectorAll(
            kEditableSelector + " > " + kInlineImageSelector,
        ),
    ) as HTMLElement[];
}

/**
 * The editable whose set of inline images is canonical: the one the user is looking at, and
 * therefore the one whose images, geometry and order win when we sync. Preference is
 * bloom-contentFirst (which the appearance system assigns when it is in charge of
 * visibility), then bloom-content1, then whichever editable has any -- matching both the CSS
 * that decides where images show and bloomEditing.ts's SetupThingsSensitiveToStyleChanges.
 * Returns null if no editable in the group has an inline image.
 */
export function getCanonicalInlineImageEditable(
    translationGroup: HTMLElement,
): HTMLElement | null {
    const withImages = getEditables(translationGroup).filter(
        (editable) => getInlineImagesInEditable(editable).length > 0,
    );
    if (withImages.length === 0) return null;
    return (
        withImages.find((e) => e.classList.contains("bloom-contentFirst")) ??
        withImages.find((e) => e.classList.contains("bloom-content1")) ??
        withImages[0]
    );
}

/**
 * The editable whose copy of the image the reader sees, using the same precedence as the
 * show-once CSS: a visible bloom-contentFirst, else a visible bloom-content1, else the
 * first visible editable. Undefined if the group has no visible editable at all (which
 * happens for the language-prototype-only groups on template pages).
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
 * Builds a new inline image wrapper: a contenteditable=false island holding a placeholder
 * image, docked right at the default width. Not attached to anything; see insertInlineImage.
 * Pass the id when building the copies of one image for the sibling editables, so that all
 * of them share an identity; omit it to mint a new one.
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
    // The island is not text. CKEditor tolerates such islands inside the fields it
    // manages -- the format cog is one (StyleEditor.ts) -- and the talking book tool skips
    // them when adding audio markup (audioRecording.ts).
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
 * Adds a new inline image to a translation group, putting a copy in *every* bloom-editable
 * child -- including the lang="z" prototype. That is deliberate: when Bloom later adds a
 * language to this group, MakeElementWithLanguageForOneGroup clones an existing editable
 * and StripOutText empties the text out of the clone, so a copy in the prototype means the
 * new language inherits the image with no C# involvement (TranslationGroupManagerTests
 * covers that cloning). Returns the copy in the editable the reader sees, which is the one
 * the caller will want to work with (e.g. to open the image chooser on it).
 */
export function insertInlineImage(translationGroup: HTMLElement): HTMLElement {
    recordInlineImageUndoPoint(translationGroup);
    // One identity, shared by the copy we put in each language's editable.
    const id = createValidXhtmlUniqueId();
    const editables = getEditables(translationGroup);
    editables.forEach((editable) => {
        ensureEditableHasAParagraph(editable);
        // A new image joins the end of the floating cluster, so it appears after the images
        // already there rather than jumping in front of them.
        editable.insertBefore(
            makeInlineImageWrapper(id),
            getFloatingClusterEnd(editable),
        );
    });
    const preferred = getFirstVisibleEditable(translationGroup) ?? editables[0];
    return getInlineImageById(preferred, id)!;
}

/**
 * Deletes ONE inline image -- the one this wrapper is a copy of -- from every bloom-editable
 * of its group, which is what "delete this image" means for a feature whose copies are
 * per-language. Other images in the same block are left alone.
 *
 * Takes the wrapper the user acted on, not the translation group: with several images in a
 * block, a group-level remove could only guess which one was meant. Throws if handed
 * something that is not an inline image, rather than silently deleting the wrong thing.
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
        // An id is missing only for hand-written markup, which by definition has no copies
        // to match; then all we can do is remove the one we were given.
        if (id) getInlineImageById(editable, id)?.remove();
    });
    if (!id) wrapper.remove();
}

/**
 * Moves an inline image to a different dock. For the three floating docks this is purely a
 * class change unless the image is coming back from the bottom, which is what makes dragging
 * safe to replicate across languages; the bottom dock moves it to the trailing cluster. Also
 * keeps bloom-keepFirstInField consistent: BloomField uses that class to decide whether the
 * field's required <p> goes after the images (floating docks) or before them (bottom dock),
 * so a bottom-docked wrapper must not carry it.
 * The caller is responsible for the follow-up syncInlineImagesFromEditable().
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
 * Copies this editable's inline images onto its sibling editables, so that every language
 * shows the same images, the same way, in the same order. Call it after anything that
 * changes an image: choosing a different picture, dragging, resizing, changing the dock.
 *
 * This editable is the authority for the whole set. Copies are matched up by
 * kInlineImageIdAttr, so an image whose geometry changed is updated in place, one that is new
 * here is added there, and one that is gone from here is removed there. Sibling text is never
 * touched. It is idempotent, so callers may be generous with it, and it strips transient UI
 * (bloom-ui children, the selected-state class, temporary ids) so none of that is replicated
 * or saved.
 *
 * Does nothing if this editable has no inline images, since that is indistinguishable from
 * "this editable is not the one being edited"; deletion goes through removeInlineImage.
 */
export function syncInlineImagesFromEditable(editable: HTMLElement): void {
    const sources = getInlineImagesInEditable(editable);
    if (sources.length === 0) return;
    const translationGroup = getTranslationGroupOf(editable);
    if (!translationGroup) return;

    const wantedIds = new Set(
        sources.map((source) => getInlineImageId(source)),
    );
    getEditables(translationGroup).forEach((sibling) => {
        if (sibling === editable) return;
        ensureEditableHasAParagraph(sibling);
        // Anything here that the source no longer has is gone.
        getInlineImagesInEditable(sibling).forEach((existing) => {
            if (!wantedIds.has(getInlineImageId(existing))) existing.remove();
        });
        // Stamp each source copy over its counterpart, or add it if there isn't one. Note
        // that we rebuild rather than patch: the whole state of an inline image is its class
        // list and style attribute, so a fresh copy is both simpler and exactly right.
        sources.forEach((source) => {
            const clone = makeSerializedCopy(source);
            const id = getInlineImageId(source);
            const existing = id ? getInlineImageById(sibling, id) : null;
            if (existing) existing.replaceWith(clone);
            else sibling.appendChild(clone);
        });
        // ...and put them in the source's order, in the right cluster.
        arrangeInlineImages(sibling, sources);
    });
}

/**
 * Makes every editable of the group agree about its inline image, by finding the canonical
 * copy (see getInlineImage) and stamping it onto the others. This is what fixes up a group
 * where the copies have diverged, or where some editable has no copy at all -- for
 * instance a language added by an older Bloom, or an editable a user managed to delete the
 * image out of. A no-op for a group with no inline image.
 */
export function normalizeInlineImages(translationGroup: HTMLElement): void {
    const canonicalEditable = getCanonicalInlineImageEditable(translationGroup);
    if (!canonicalEditable) return;
    syncInlineImagesFromEditable(canonicalEditable);
}

/**
 * Page-load setup for every translation group in the container that has an inline image:
 * normalize the copies, and arrange for the real image's dimensions to be recorded (and
 * the page's overflow re-checked) once it loads. Called from SetupElements.
 */
export function setupInlineImages(container: HTMLElement): void {
    // Undo snapshots hold element references, and this is a freshly set up DOM, so anything
    // recorded before now could only restore into detached elements. (The page-id check does
    // this too when the page really changed, but a re-setup of the same page needs it as well.)
    clearInlineImageUndoState();
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
 * Call when the image inside an inline image wrapper has been replaced (a different
 * picture chosen). The old natural dimensions no longer apply, so we drop the recorded
 * aspect ratio and let the new image's load supply a new one; meanwhile the new src has to
 * reach the other languages' copies. bloomEditing.ts's changeImageInfo calls this for any
 * img that turns out to be inside an inline image.
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
    // Tell the interaction layer, which started this and owns the selection, that the
    // picture has arrived. It needs to re-assert selection on the wrapper, because the
    // chooser round trip can leave the focus in the text, and undo of this change (or of
    // the insert that led to it) is only reachable while the wrapper is selected.
    wrapper.dispatchEvent(
        new CustomEvent(kInlineImageChangedEvent, {
            bubbles: true,
            detail: wrapper,
        }),
    );
}

// --- undo --------------------------------------------------------------------
//
// Inline-image operations need an undo layer of their own. CKEditor's stack cannot see
// changes we make to the DOM programmatically, and the image-operation layer
// (ImageUndoManager.ts) only knows how to put one img's src and crop back: it knows nothing
// about a wrapper that exists once per language and has to be restored in all of them at
// once. So this is a third small layer built on the same shape as ImageUndoManager --
// snapshot stack, two-phase prepare/commit, cleared on page change, gated on the relevant
// thing being active, and no redo.
//
// A snapshot is the whole inline-image state of one translation group: for every one of its
// editables, the serialized markup of all its inline images, in order. That is deliberately
// coarse: a single operation may touch every editable (sync stamps all of them) and any number
// of images in each, so restoring the group wholesale is both simpler and more robust than
// trying to reverse individual mutations. The markup carries each image's identity, dock and
// geometry, and the list order carries their order, so a snapshot needs nothing else.

type InlineImageEditableSnapshot = {
    editable: HTMLElement;
    // The markup of each inline image, in the order they appeared. Empty when this editable
    // had none.
    wrapperHtmls: string[];
};

type InlineImageUndoItem = {
    translationGroup: HTMLElement;
    editables: InlineImageEditableSnapshot[];
};

const inlineImageUndoStack: InlineImageUndoItem[] = [];
let pendingInlineImageUndo: InlineImageUndoItem | undefined;
let pageIdForInlineImageUndo: string | undefined;

/**
 * Captures the undo state for an operation that is about to happen but might not complete
 * (a drag the user may abandon, an image change that may fail). Pair it with
 * commitPendingInlineImageUndo once the change has actually landed, or
 * discardPendingInlineImageUndo if it didn't. Cheap enough to call at drag start.
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
 * Pushes the state captured by prepareInlineImageUndo, now that the operation really
 * happened. Ignores a pending snapshot belonging to some other translation group, so a
 * mismatched or superseded operation cannot push a misleading undo point.
 */
export function commitPendingInlineImageUndo(element: HTMLElement): void {
    clearInlineImageUndoOnPageChange();
    const translationGroup = getTranslationGroupOf(element);
    if (
        pendingInlineImageUndo &&
        pendingInlineImageUndo.translationGroup === translationGroup
    ) {
        inlineImageUndoStack.push(pendingInlineImageUndo);
    }
    pendingInlineImageUndo = undefined;
}

/** Throws away a prepared snapshot, for an operation that turned out not to happen. */
export function discardPendingInlineImageUndo(): void {
    pendingInlineImageUndo = undefined;
}

/**
 * Records an undo point for an operation that definitely changes something (insert, remove,
 * a completed dock change). Prepare and commit in one call; use the two-phase pair instead
 * when the operation might not complete. Takes the translation group, or any element in one.
 */
export function recordInlineImageUndoPoint(element: HTMLElement): void {
    prepareInlineImageUndo(element);
    commitPendingInlineImageUndo(element);
}

/**
 * Forgets all inline-image undo state. Called when something happens that we cannot undo,
 * so that undo can't reach back past it and restore a state that never followed from what
 * the user sees now.
 */
export function clearInlineImageUndoState(): void {
    inlineImageUndoStack.length = 0;
    pendingInlineImageUndo = undefined;
}

/**
 * Whether the workspace undo command should route to this layer. Normally it takes two
 * things: something recorded, and an inline image in the *same* translation group being the
 * active thing. That gate matters, because this layer is tried ahead of CKEditor and without
 * it an old inline-image snapshot would shadow the text the user typed a moment ago. It
 * mirrors canUndoImageOperation's requirement that an image container be active, and adds
 * the same-group check, so we can never restore a block the user isn't working in.
 */
export function inlineImageCanUndo(): boolean {
    clearInlineImageUndoOnPageChange();
    const top = inlineImageUndoStack[inlineImageUndoStack.length - 1];
    if (!top) return false;
    const activeWrapper = getActiveInlineImage();
    if (activeWrapper) {
        return getTranslationGroupOf(activeWrapper) === top.translationGroup;
    }
    // There is one case where insisting on an active inline image would be wrong: the user
    // deleted an image and then pressed ctrl+z. The image they were working on is gone, so if
    // we said no here, deleting an inline image could never be undone at all. We can recognize
    // that case -- the snapshot holds more images than the group does now -- and then it is
    // enough that the caret is still in the block we would be restoring into.
    // (Cost: if the user typed in that same block after deleting, we go first and their
    // typing needs a second ctrl+z. These stacks are independent and cannot be ordered
    // against each other; see the comment on the chain in workspaceRoot.handleUndo.)
    const countInSnapshot = top.editables.reduce(
        (total, snapshot) => total + snapshot.wrapperHtmls.length,
        0,
    );
    const countNow = getEditables(top.translationGroup).reduce(
        (total, editable) => total + getInlineImagesInEditable(editable).length,
        0,
    );
    if (countInSnapshot > countNow) {
        const focused = getElementWithFocusOrSelection();
        return !!focused && top.translationGroup.contains(focused);
    }
    return false;
}

/**
 * Undoes the most recent inline-image operation by restoring its snapshot to every editable
 * of the translation group, and re-checks overflow since the image's size may have changed.
 * Returns false if there was nothing to undo. There is no redo, matching the
 * image-operation layer.
 */
export function inlineImageUndo(): boolean {
    clearInlineImageUndoOnPageChange();
    const undoItem = inlineImageUndoStack.pop();
    if (!undoItem) return false;
    // Restoring replaces the wrapper elements, which would drop the selection and so make a
    // second ctrl+z unreachable (the gate needs an active inline image). So carry the
    // selection over to the restored copy of the same image -- by id, since the block may hold
    // several and re-selecting the wrong one would be worse than re-selecting none.
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
    // Undo is the one operation that replaces the very wrapper the user is working on (sync
    // and normalize only ever rewrite the siblings). So anything holding a reference to it,
    // or anything it was hosting -- drag handles, the hover button cluster, all of which are
    // bloom-ui and therefore not part of a snapshot -- is now stale. This event is how the
    // interaction layer knows to re-derive from the DOM.
    undoItem.translationGroup.dispatchEvent(
        new CustomEvent(kInlineImagesRestoredEvent, { bubbles: true }),
    );
    return true;
}

/**
 * The "before" half of undo for a change of the image inside an inline image. Returns true
 * if this img is in fact in an inline image, in which case this layer has taken charge and
 * the caller must not also record an image-operation undo point: that layer would restore
 * one language's src and leave the other languages showing the new picture.
 */
export function prepareInlineImageUndoForImageChange(
    img: HTMLElement,
): boolean {
    if (!img.closest(kInlineImageSelector)) return false;
    prepareInlineImageUndo(img);
    return true;
}

/**
 * The "after" half: pushes what prepareInlineImageUndoForImageChange captured, now that the
 * new image is really in place. Returns true if this img is in an inline image, as above.
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

function takeInlineImageSnapshot(
    translationGroup: HTMLElement,
): InlineImageUndoItem {
    return {
        translationGroup,
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
    // Rebuild them all, then place them: appending first and arranging afterwards means the
    // cluster anchors are computed against the finished set rather than a half-built one.
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

// The inline image the user is working on, if any: the one the interaction layer has marked
// as selected, else one that contains the focus or the caret (which is how an island the
// user clicked into shows up before there is any selection UI).
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

// Snapshots hold element references, so they are only meaningful for the page they were
// taken on. Same approach (and same data-page-id check) as
// ImageUndoManager.clearImageOperationUndoOnPageChange.
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
function getTranslationGroupsWithInlineImages(
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

// Records the image's real shape so that layout (and therefore the overflow checker) is
// right, and re-checks overflow, because an image that just got taller can push the text
// past the bottom of the block. A placeholder never loads, so it keeps the default ratio.
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
        // Each language's copy has its own img, so each records its own ratio; there is
        // nothing to sync here.
        if (editable) OverflowChecker.AdjustSizeOrMarkOverflowSoon(editable);
    });
}

function setAspectRatioFromNaturalSize(
    wrapper: HTMLElement,
    img: HTMLImageElement,
): void {
    if (!img.naturalWidth || !img.naturalHeight) return; // not loaded (or a placeholder)
    wrapper.style.setProperty(
        kInlineImageAspectRatioVar,
        `${img.naturalWidth} / ${img.naturalHeight}`,
    );
}

// A copy of the wrapper as it should be persisted and replicated: no transient UI, no
// selected state, and no ids (a change-image round trip puts a temporary id on the img,
// and duplicating ids across the languages' copies would be worse than useless).
// Note that this strips the `id` attribute only. kInlineImageIdAttr is a data-* attribute and
// deliberately survives: it is what makes this copy recognizable as the same image in another
// language, and every copy of one image is meant to share it.
function makeSerializedCopy(wrapper: HTMLElement): HTMLElement {
    const clone = wrapper.cloneNode(true) as HTMLElement;
    clone.classList.remove(kInlineImageSelectedClass);
    clone.querySelectorAll(".bloom-ui").forEach((e) => e.remove());
    clone.removeAttribute("id");
    clone.querySelectorAll("[id]").forEach((e) => e.removeAttribute("id"));
    return clone;
}

// SLOT MODEL
// An editable's children run: the floating images (left/right/middle) in a leading cluster,
// then the text, then the bottom-docked images in a trailing cluster, then any bloom-ui (the
// format cog). Within a cluster, DOM order is the images' order, and sync replicates it. The
// wrappers never move relative to the text beyond that, which is what keeps a position
// meaningful across languages whose text is entirely different.

// The node a new or returning floating image should be inserted before: the first child that
// is neither a floating inline image nor bloom-ui, i.e. where the text starts. Null when the
// editable has no content yet, in which case appending is right.
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

// The node a bottom-docked image should be inserted before: the trailing bloom-ui run, so the
// images stay after all the text but the format cog stays last. Null when there is no such
// run, in which case appending puts it at the end.
function getBottomClusterEnd(editable: HTMLElement): Node | null {
    const children = Array.from(editable.children);
    for (let i = children.length - 1; i >= 0; i--) {
        if (!children[i].classList.contains("bloom-ui")) {
            return children[i].nextSibling;
        }
    }
    return editable.firstChild;
}

// Is this wrapper already ahead of all the text, i.e. in the leading cluster?
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

// Is this wrapper already after all the text, i.e. in the trailing cluster?
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

// Puts one wrapper in the cluster its dock calls for. A wrapper already in the right cluster
// is left exactly where it is: switching between left, right and middle must not reshuffle an
// image past its neighbors, since the order within a cluster is the images' order.
function moveToDockCluster(editable: HTMLElement, wrapper: HTMLElement): void {
    if (wrapper.classList.contains(kInlineImageBottomClass)) {
        if (isInBottomCluster(wrapper)) return;
        editable.insertBefore(wrapper, getBottomClusterEnd(editable));
        return;
    }
    if (isInFloatingCluster(wrapper)) return;
    editable.insertBefore(wrapper, getFloatingClusterEnd(editable));
}

// Puts this editable's images into the same order and clusters as the source list, which is
// the authority for both. Inserting each in turn before a fixed anchor reproduces the source
// order; the floating cluster goes first so that the bottom cluster's anchor is computed once
// the text boundary has settled.
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

// An editable whose only content is the non-editable island would give the user nowhere to
// type. BloomField.EnsureParagraphsPresent does this at page load; we do it here too
// because we insert wrappers after that has run.
//
// "Has somewhere to type" means any block element, not just a <p>: converted content can
// legitimately hold a real heading and no paragraph, and appending an empty paragraph under
// that heading would give the reader a blank line that then persists into the saved page.
// We borrow BloomField's own selector so the two cannot disagree about what counts.
// Note that for every dock except bottom the wrapper carries bloom-keepFirstInField, and
// BloomField deliberately still insists on a real trailing <p> in that case (the paragraph
// the text wraps around), so it will add one itself; this only decides whether we add one
// first. The bottom dock is where it actually shows.
function ensureEditableHasAParagraph(editable: HTMLElement): void {
    if (editable.querySelector(kBlockElementSelector)) return;
    editable.appendChild(document.createElement("p"));
}
