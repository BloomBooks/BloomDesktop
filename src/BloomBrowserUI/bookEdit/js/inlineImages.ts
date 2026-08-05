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
 * The inline image wrapper inside one bloom-editable, if it has one. (v1 allows at most
 * one per editable, so this is the only accessor callers should need.)
 */
export function getInlineImageInEditable(
    editable: HTMLElement,
): HTMLElement | null {
    return editable.querySelector(
        ":scope > " + kInlineImageSelector,
    ) as HTMLElement | null;
}

/**
 * The canonical inline image of a translation group: the copy the user is looking at and
 * therefore the one whose state wins when we sync. Preference is bloom-contentFirst (which
 * the appearance system assigns when it is in charge of visibility), then bloom-content1,
 * then whichever editable has a copy -- matching both the CSS that decides where the image
 * shows and bloomEditing.ts's SetupThingsSensitiveToStyleChanges. Returns null if no
 * editable in the group has an inline image.
 */
export function getInlineImage(
    translationGroup: HTMLElement,
): HTMLElement | null {
    const editable = getCanonicalInlineImageEditable(translationGroup);
    return editable ? getInlineImageInEditable(editable) : null;
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
 * The editable holding the canonical copy (see getInlineImage), or null if no editable in
 * the group has one.
 */
export function getCanonicalInlineImageEditable(
    translationGroup: HTMLElement,
): HTMLElement | null {
    const withImage = getEditables(translationGroup).filter(
        (editable) => !!getInlineImageInEditable(editable),
    );
    if (withImage.length === 0) return null;
    return (
        withImage.find((e) => e.classList.contains("bloom-contentFirst")) ??
        withImage.find((e) => e.classList.contains("bloom-content1")) ??
        withImage[0]
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
 * image, docked right at the default width. Not attached to anything; see
 * insertInlineImage.
 */
export function makeInlineImageWrapper(): HTMLElement {
    const wrapper = document.createElement("div");
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
    const editables = getEditables(translationGroup);
    editables.forEach((editable) => {
        const existing = getInlineImageInEditable(editable);
        if (existing) existing.remove(); // v1: at most one per editable
        editable.insertBefore(makeInlineImageWrapper(), editable.firstChild);
        ensureEditableHasAParagraph(editable);
    });
    const preferred = getFirstVisibleEditable(translationGroup) ?? editables[0];
    return getInlineImageInEditable(preferred)!;
}

/**
 * Removes the inline image from every bloom-editable of the group (including hidden
 * languages and the prototype), which is what "delete this image" means for a feature
 * whose copies are per-language.
 */
export function removeInlineImage(translationGroup: HTMLElement): void {
    recordInlineImageUndoPoint(translationGroup);
    getEditables(translationGroup).forEach((editable) => {
        getInlineImageInEditable(editable)?.remove();
    });
}

/**
 * Moves an inline image to a different dock. Only the bottom dock changes the DOM slot
 * (last child instead of first); the other three are purely a class change, which is what
 * makes dragging safe to replicate across languages. Also keeps bloom-keepFirstInField
 * consistent: BloomField uses that class to decide whether the field's required <p> goes
 * after the image (first-child docks) or before it (bottom dock), so a bottom-docked
 * wrapper must not carry it.
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
    if (editable) moveToDockSlot(editable, wrapper);
}

/**
 * Copies this editable's inline image onto its sibling editables, so that every language
 * shows the same image in the same place. Call it after anything that changes the wrapper:
 * choosing a different image, dragging, resizing, changing the dock.
 *
 * It rewrites only the wrapper in each sibling; their text is untouched. It is idempotent,
 * so callers may be generous with it, and it strips transient UI (bloom-ui children, the
 * selected-state class, temporary ids) so none of that gets replicated or saved.
 *
 * Does nothing if this editable has no inline image; removal is removeInlineImage's job,
 * not something to infer from an absence.
 */
export function syncInlineImagesFromEditable(editable: HTMLElement): void {
    const source = getInlineImageInEditable(editable);
    if (!source) return;
    const translationGroup = editable.parentElement;
    if (!translationGroup) return;

    const isBottomDocked = source.classList.contains(kInlineImageBottomClass);
    getEditables(translationGroup).forEach((sibling) => {
        if (sibling === editable) return;
        const clone = makeSerializedCopy(source);
        const existing = getInlineImageInEditable(sibling);
        if (existing) {
            existing.replaceWith(clone);
            // A dock change may mean the wrapper now belongs in the other slot.
            moveToDockSlot(sibling, clone);
        } else {
            // Give the sibling its <p> before we add the wrapper, so that a
            // bottom-docked wrapper still ends up last.
            ensureEditableHasAParagraph(sibling);
            if (isBottomDocked) sibling.appendChild(clone);
            else sibling.insertBefore(clone, sibling.firstChild);
            moveToDockSlot(sibling, clone);
        }
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
                const wrapper = getInlineImageInEditable(editable);
                if (wrapper) wireUpImage(wrapper);
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
// editables, the serialized wrapper and which slot it was in, or nothing when that editable
// had no wrapper. That is deliberately coarse: an operation may touch every editable (sync
// stamps all of them), and restoring the group wholesale is both simpler and more robust
// than trying to reverse each individual mutation.

type InlineImageEditableSnapshot = {
    editable: HTMLElement;
    // The wrapper's markup, or undefined if this editable had no inline image.
    wrapperHtml?: string;
    dockedAtBottom: boolean;
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
    // deleted the image and then pressed ctrl+z. There is nothing left to select, so if we
    // said no here, deleting an inline image could never be undone at all. We can recognize
    // that case though -- the snapshot has an image and the group no longer does -- and then
    // it is enough that the caret is still in the block we would be restoring into.
    // (Cost: if the user typed in that same block after deleting, we go first and their
    // typing needs a second ctrl+z. These stacks are independent and cannot be ordered
    // against each other; see the comment on the chain in workspaceRoot.handleUndo.)
    const snapshotHadAnImage = top.editables.some(
        (snapshot) => !!snapshot.wrapperHtml,
    );
    if (snapshotHadAnImage && !getInlineImage(top.translationGroup)) {
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
    // selection over to the restored copy in the same editable.
    const wasSelected = document.querySelector(
        kInlineImageSelector + "." + kInlineImageSelectedClass,
    ) as HTMLElement | null;
    const editableThatWasSelected = wasSelected?.closest(
        kEditableSelector,
    ) as HTMLElement | null;
    undoItem.editables.forEach((snapshot) => {
        restoreInlineImageSnapshot(snapshot);
    });
    if (editableThatWasSelected) {
        getInlineImageInEditable(editableThatWasSelected)?.classList.add(
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
        editables: getEditables(translationGroup).map((editable) => {
            const wrapper = getInlineImageInEditable(editable);
            return {
                editable,
                wrapperHtml: wrapper
                    ? makeSerializedCopy(wrapper).outerHTML
                    : undefined,
                dockedAtBottom:
                    !!wrapper &&
                    wrapper.classList.contains(kInlineImageBottomClass),
            };
        }),
    };
}

function restoreInlineImageSnapshot(
    snapshot: InlineImageEditableSnapshot,
): void {
    const editable = snapshot.editable;
    getInlineImageInEditable(editable)?.remove();
    if (snapshot.wrapperHtml) {
        const template = document.createElement("template");
        template.innerHTML = snapshot.wrapperHtml;
        const wrapper = template.content.firstElementChild as HTMLElement;
        if (snapshot.dockedAtBottom) editable.appendChild(wrapper);
        else editable.insertBefore(wrapper, editable.firstChild);
        moveToDockSlot(editable, wrapper);
        wireUpImage(wrapper);
    }
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
function makeSerializedCopy(wrapper: HTMLElement): HTMLElement {
    const clone = wrapper.cloneNode(true) as HTMLElement;
    clone.classList.remove(kInlineImageSelectedClass);
    clone.querySelectorAll(".bloom-ui").forEach((e) => e.remove());
    clone.removeAttribute("id");
    clone.querySelectorAll("[id]").forEach((e) => e.removeAttribute("id"));
    return clone;
}

// Puts the wrapper in the slot its dock calls for: last child for the bottom dock, first
// child otherwise. For the bottom dock we go before any trailing bloom-ui elements (the
// format cog lives at the end of the editable), so the image really is the last content.
function moveToDockSlot(editable: HTMLElement, wrapper: HTMLElement): void {
    if (wrapper.classList.contains(kInlineImageBottomClass)) {
        const lastContent = Array.from(editable.children)
            .filter((c) => c !== wrapper && !c.classList.contains("bloom-ui"))
            .pop();
        if (lastContent && lastContent.nextSibling !== wrapper) {
            lastContent.after(wrapper);
        }
        return;
    }
    if (editable.firstChild !== wrapper) {
        editable.insertBefore(wrapper, editable.firstChild);
    }
}

// An editable whose only content is the non-editable island would give the user nowhere to
// type. BloomField.EnsureParagraphsPresent does this at page load; we do it here too
// because we insert wrappers after that has run.
function ensureEditableHasAParagraph(editable: HTMLElement): void {
    if (editable.querySelector("p")) return;
    editable.appendChild(document.createElement("p"));
}
