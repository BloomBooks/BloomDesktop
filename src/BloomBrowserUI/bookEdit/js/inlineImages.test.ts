import { describe, it, expect, beforeEach, afterAll } from "vitest";
import {
    getTestRoot,
    cleanTestRoot,
    removeTestRoot,
} from "../../utils/testHelper";
import {
    clearInlineImageUndoState,
    commitPendingInlineImageUndo,
    discardPendingInlineImageUndo,
    getEditables,
    getInlineImage,
    getInlineImageById,
    getInlineImageId,
    getInlineImageInEditable,
    getInlineImages,
    getInlineImagesInEditable,
    handleInlineImageChanged,
    kInlineImageChangedEvent,
    kInlineImageLeftClass,
    kInlineImageMiddleClass,
    inlineImageCanUndo,
    noteInlineImageBlockWasEdited,
    inlineImageUndo,
    insertInlineImage,
    kInlineImageBottomClass,
    kInlineImageClass,
    kInlineImageRightClass,
    kInlineImageWidthVar,
    kInlineImageSelectedClass,
    kInlineImagesRestoredEvent,
    kKeepFirstInFieldClass,
    normalizeInlineImages,
    prepareInlineImageUndo,
    prepareInlineImageUndoForImageChange,
    recordInlineImageUndoPoint,
    removeInlineImage,
    setInlineImageDock,
    setupInlineImages,
    syncInlineImagesFromEditable,
} from "./inlineImages";

// Each group is built inside its own .bloom-page with a fresh data-page-id, both because
// that is how it looks in Bloom and because the undo layer keys its "did the page change?"
// check on that id, so a new one per test gives each test a clean undo stack.
let pageCounter = 0;

// Builds a translation group, one editable per entry. `id` names it and puts it on the page
// beside any group already there, which is what a test about two blocks of one page needs;
// without it the page is rebuilt with this as its only group.
function makeTranslationGroup(
    editables: { lang: string; classes?: string; content?: string }[],
    id?: string,
): HTMLElement {
    const root = getTestRoot();
    const groupHtml =
        `<div class="bloom-translationGroup" id="${id ?? "group"}">` +
        editables
            .map(
                (e) =>
                    `<div class="bloom-editable ${e.classes ?? ""}" lang="${e.lang}">${
                        e.content ?? ""
                    }</div>`,
            )
            .join("") +
        `</div>`;
    const page = id ? root.querySelector(".bloom-page") : null;
    if (page) page.insertAdjacentHTML("beforeend", groupHtml);
    else
        root.innerHTML =
            `<div class="bloom-page" data-page-id="test-page-${++pageCounter}">` +
            groupHtml +
            `</div>`;
    return root.querySelector("#" + (id ?? "group")) as HTMLElement;
}

// Stands in for the page's CKEditor: whether it holds text editing it could undo, and `index`,
// CKEditor's own name for where it stands in its stack of snapshots (one per edit, going down
// as edits are undone). The real thing is a global the page's CKEditor puts up; absent, as in
// these tests unless a test says otherwise, it reads as "nothing to undo".
const fakeCkeditorUndoManager = {
    undoable: () => fakeCkeditorUndoManager.state,
    index: undefined as number | undefined,
    state: false,
};

function setCkeditorUndoable(
    undoable: boolean | undefined,
    index?: number,
): void {
    const global = globalThis as unknown as { CKEDITOR?: unknown };
    if (undoable === undefined) {
        delete global.CKEDITOR;
        return;
    }
    // One manager per editable, kept across calls, because that is what the real thing does and
    // what makes two of its positions comparable.
    fakeCkeditorUndoManager.state = undoable;
    fakeCkeditorUndoManager.index = index;
    global.CKEDITOR = {
        currentInstance: { undoManager: fakeCkeditorUndoManager },
    };
}

// The undo layer only fires when an inline image is the active thing, which in the real
// editor is the interaction layer's selected class.
const select = (wrapper: HTMLElement) =>
    wrapper.classList.add(kInlineImageSelectedClass);

// Stands in for the user's caret sitting in a block's text.
function putCaretIn(editable: HTMLElement): void {
    const selection = document.getSelection()!;
    selection.removeAllRanges();
    const range = document.createRange();
    range.selectNodeContents(editable.querySelector("p")!);
    range.collapse(true);
    selection.addRange(range);
}

const editableFor = (group: HTMLElement, lang: string) =>
    group.querySelector(`[lang="${lang}"]`) as HTMLElement;

describe("inlineImages", () => {
    beforeEach(() => {
        cleanTestRoot();
        // A caret left in a removed element would leak into the next test's undo gate.
        document.getSelection()?.removeAllRanges();
    });
    afterAll(removeTestRoot);

    describe("insertInlineImage", () => {
        it("puts a wrapper in every editable, including the lang=z prototype", () => {
            const group = makeTranslationGroup([
                {
                    lang: "en",
                    classes: "bloom-content1 bloom-visibility-code-on",
                    content: "<p>English</p>",
                },
                { lang: "fr", content: "<p>French</p>" },
                { lang: "z", content: "<p></p>" },
            ]);
            // Sanity check: nothing there before we start.
            expect(getInlineImages(group).length).toBe(0);

            const returned = insertInlineImage(group);

            expect(getInlineImages(group).length).toBe(3);
            ["en", "fr", "z"].forEach((lang) => {
                const wrapper = getInlineImageInEditable(
                    editableFor(group, lang),
                );
                expect(
                    wrapper,
                    `expected a wrapper in the ${lang} editable`,
                ).not.toBeNull();
                expect(wrapper!.getAttribute("contenteditable")).toBe("false");
                expect(
                    wrapper!.classList.contains(kInlineImageRightClass),
                ).toBe(true);
                expect(
                    wrapper!.classList.contains(kKeepFirstInFieldClass),
                ).toBe(true);
                expect(wrapper!.querySelector("img")!.getAttribute("src")).toBe(
                    "placeHolder.png",
                );
                // The wrapper goes first so text wraps around it; BloomField keeps the <p> after it.
                expect(editableFor(group, lang).firstElementChild).toBe(
                    wrapper,
                );
            });
            // The caller gets back the copy the reader is looking at.
            expect(returned).toBe(
                getInlineImageInEditable(editableFor(group, "en")),
            );
        });

        it("gives an editable with no paragraph one, so there is somewhere to type", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "" },
            ]);
            insertInlineImage(group);
            expect(editableFor(group, "en").querySelectorAll("p").length).toBe(
                1,
            );
        });

        // A heading is a block too (BloomField's kBlockElementSelector), so a box holding one
        // already has somewhere to type and must not collect an empty paragraph under it --
        // that would show as a blank line, and persist once the page is saved.
        it("does not add a paragraph to an editable that holds only a heading", () => {
            const group = makeTranslationGroup([
                {
                    lang: "en",
                    classes: "bloom-content1",
                    content: "<h1>A heading</h1>",
                },
            ]);
            const editable = editableFor(group, "en");
            // Sanity check: a heading and no paragraph is the case under test.
            expect(editable.querySelectorAll("h1").length).toBe(1);
            expect(editable.querySelectorAll("p").length).toBe(0);

            insertInlineImage(group);

            expect(editable.querySelectorAll("p").length).toBe(0);
            expect(editable.querySelector("h1")!.textContent).toBe("A heading");
        });
    });

    describe("syncInlineImagesFromEditable", () => {
        it("stamps the wrapper onto siblings without touching their text", () => {
            const group = makeTranslationGroup([
                {
                    lang: "en",
                    classes: "bloom-content1 bloom-visibility-code-on",
                    content: "<p>English text</p>",
                },
                { lang: "fr", content: "<p>Texte français</p>" },
            ]);
            const source = editableFor(group, "en");
            const sibling = editableFor(group, "fr");
            insertInlineImage(group);
            // Make the canonical copy different from the others.
            const wrapper = getInlineImageInEditable(source)!;
            wrapper.style.setProperty("--inline-image-width", "25%");
            wrapper.style.setProperty("--inline-image-offset", "120px");
            // Sanity check: the sibling has not got that yet.
            expect(
                getInlineImageInEditable(sibling)!.style.getPropertyValue(
                    "--inline-image-width",
                ),
            ).toBe("40%");

            syncInlineImagesFromEditable(source);

            const stamped = getInlineImageInEditable(sibling)!;
            expect(stamped.getAttribute("style")).toBe(
                wrapper.getAttribute("style"),
            );
            expect(sibling.querySelector("p")!.textContent).toBe(
                "Texte français",
            );
        });

        // Hand-edited markup, or a paste from another program, can hold a wrapper with no
        // data-bloom-inline-image-id. Everything here pairs copies up by that id, so without
        // one the sibling's copy could not be recognized and a second was appended beside it
        // -- once per language per page setup, for as long as the book was open.
        it("does not multiply a wrapper that arrived without an id", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            const source = editableFor(group, "en");
            const sibling = editableFor(group, "fr");
            insertInlineImage(group);
            getInlineImages(group).forEach((wrapper) =>
                wrapper.removeAttribute("data-bloom-inline-image-id"),
            );
            // Sanity check: one each, and no identity to pair them by.
            expect(getInlineImagesInEditable(source).length).toBe(1);
            expect(getInlineImagesInEditable(sibling).length).toBe(1);
            expect(
                getInlineImageId(getInlineImageInEditable(source)!),
            ).toBeUndefined();

            syncInlineImagesFromEditable(source);
            syncInlineImagesFromEditable(source);
            syncInlineImagesFromEditable(source);

            expect(getInlineImagesInEditable(source).length).toBe(1);
            expect(getInlineImagesInEditable(sibling).length).toBe(1);
            // ...and it has been given an identity, shared by both copies, so the next
            // operation can pair them.
            const id = getInlineImageId(getInlineImageInEditable(source)!);
            expect(id).toBeTruthy();
            expect(getInlineImageId(getInlineImageInEditable(sibling)!)).toBe(
                id,
            );
        });

        it("is idempotent", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
                { lang: "z", content: "<p></p>" },
            ]);
            insertInlineImage(group);
            const source = editableFor(group, "en");

            syncInlineImagesFromEditable(source);
            const afterFirst = group.innerHTML;
            syncInlineImagesFromEditable(source);

            expect(group.innerHTML).toBe(afterFirst);
        });

        it("adds a missing wrapper to a sibling that has none", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            insertInlineImage(group);
            const sibling = editableFor(group, "fr");
            getInlineImageInEditable(sibling)!.remove();
            // Sanity check: it really is gone.
            expect(getInlineImageInEditable(sibling)).toBeNull();

            syncInlineImagesFromEditable(editableFor(group, "en"));

            expect(getInlineImageInEditable(sibling)).not.toBeNull();
            expect(sibling.firstElementChild!.classList).toContain(
                kInlineImageClass,
            );
        });

        it("puts a bottom-docked wrapper last in each sibling", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p><p>c</p>" },
            ]);
            insertInlineImage(group);
            const source = editableFor(group, "en");
            setInlineImageDock(
                getInlineImageInEditable(source)!,
                kInlineImageBottomClass,
            );
            // Sanity check: the dock change moved the canonical copy to the end.
            expect(source.lastElementChild).toBe(
                getInlineImageInEditable(source),
            );

            syncInlineImagesFromEditable(source);

            const sibling = editableFor(group, "fr");
            const stamped = getInlineImageInEditable(sibling)!;
            expect(sibling.lastElementChild).toBe(stamped);
            // A bottom-docked wrapper must not claim to keep first in field, or BloomField
            // would put the field's required <p> after it.
            expect(stamped.classList.contains(kKeepFirstInFieldClass)).toBe(
                false,
            );
            expect(sibling.querySelectorAll("p").length).toBe(2);
        });

        it("moves a sibling's wrapper back to the first slot when the dock leaves bottom", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            insertInlineImage(group);
            const source = editableFor(group, "en");
            const sibling = editableFor(group, "fr");
            setInlineImageDock(
                getInlineImageInEditable(source)!,
                kInlineImageBottomClass,
            );
            syncInlineImagesFromEditable(source);
            // Sanity check: the sibling's copy is at the end before we dock it elsewhere.
            expect(sibling.lastElementChild).toBe(
                getInlineImageInEditable(sibling),
            );

            setInlineImageDock(
                getInlineImageInEditable(source)!,
                kInlineImageRightClass,
            );
            syncInlineImagesFromEditable(source);

            expect(sibling.firstElementChild).toBe(
                getInlineImageInEditable(sibling),
            );
        });

        it("strips edit-time-only markup from the copies it stamps", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            insertInlineImage(group);
            const source = editableFor(group, "en");
            const wrapper = getInlineImageInEditable(source)!;
            wrapper.classList.add(kInlineImageSelectedClass);
            wrapper.insertAdjacentHTML(
                "beforeend",
                '<div class="bloom-ui" id="inlineImageButtons">buttons</div>',
            );
            // A change-image round trip leaves a temporary id on the img.
            wrapper.querySelector("img")!.setAttribute("id", "tempImageId");

            syncInlineImagesFromEditable(source);

            const stamped = getInlineImageInEditable(editableFor(group, "fr"))!;
            expect(stamped.querySelector(".bloom-ui")).toBeNull();
            expect(stamped.classList.contains(kInlineImageSelectedClass)).toBe(
                false,
            );
            expect(stamped.querySelector("[id]")).toBeNull();
            // ...and the original keeps its UI; syncing is not a cleanup pass on the source.
            expect(wrapper.querySelector(".bloom-ui")).not.toBeNull();
        });

        it("does nothing when the editable has no inline image", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            insertInlineImage(group);
            const before = group.innerHTML;

            // The 'fr' copy exists, so use an editable we deliberately emptied instead.
            const other = editableFor(group, "fr");
            getInlineImageInEditable(other)!.remove();
            const afterRemoval = group.innerHTML;
            syncInlineImagesFromEditable(other);

            expect(group.innerHTML).toBe(afterRemoval);
            expect(group.innerHTML).not.toBe(before); // sanity check on the test itself
        });
    });

    describe("normalizeInlineImages", () => {
        it("prefers the bloom-contentFirst copy", () => {
            const group = makeTranslationGroup([
                {
                    lang: "en",
                    classes: "bloom-content1 bloom-contentSecond",
                    content: "<p>a</p>",
                },
                {
                    lang: "fr",
                    classes: "bloom-content2 bloom-contentFirst",
                    content: "<p>b</p>",
                },
            ]);
            insertInlineImage(group);
            getInlineImageInEditable(
                editableFor(group, "fr"),
            )!.style.setProperty("--inline-image-width", "15%");

            expect(getInlineImage(group)).toBe(
                getInlineImageInEditable(editableFor(group, "fr")),
            );
            normalizeInlineImages(group);

            expect(
                getInlineImageInEditable(
                    editableFor(group, "en"),
                )!.style.getPropertyValue("--inline-image-width"),
            ).toBe("15%");
        });

        it("falls back to bloom-content1 when no editable has bloom-contentFirst", () => {
            const group = makeTranslationGroup([
                { lang: "fr", classes: "bloom-content2", content: "<p>b</p>" },
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            insertInlineImage(group);
            getInlineImageInEditable(
                editableFor(group, "en"),
            )!.style.setProperty("--inline-image-width", "15%");

            normalizeInlineImages(group);

            expect(
                getInlineImageInEditable(
                    editableFor(group, "fr"),
                )!.style.getPropertyValue("--inline-image-width"),
            ).toBe("15%");
        });

        it("falls back to the first editable that has a copy", () => {
            const group = makeTranslationGroup([
                { lang: "es", content: "<p>c</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            insertInlineImage(group);
            getInlineImageInEditable(
                editableFor(group, "es"),
            )!.style.setProperty("--inline-image-width", "15%");

            normalizeInlineImages(group);

            expect(
                getInlineImageInEditable(
                    editableFor(group, "fr"),
                )!.style.getPropertyValue("--inline-image-width"),
            ).toBe("15%");
        });

        // Turning language 1 off on the page leaves its editable in the DOM with a copy
        // nobody can see. Preferring that copy stamped it over the language the person was
        // actually working in.
        it("prefers a visible language over a hidden bloom-content1", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                {
                    lang: "fr",
                    classes: "bloom-content2 bloom-visibility-code-on",
                    content: "<p>b</p>",
                },
            ]);
            insertInlineImage(group);
            getInlineImageInEditable(
                editableFor(group, "fr"),
            )!.style.setProperty("--inline-image-width", "15%");
            // Sanity check: the two copies really do disagree before we normalize.
            expect(
                getInlineImageInEditable(
                    editableFor(group, "en"),
                )!.style.getPropertyValue("--inline-image-width"),
            ).toBe("40%");

            normalizeInlineImages(group);

            expect(
                getInlineImageInEditable(
                    editableFor(group, "en"),
                )!.style.getPropertyValue("--inline-image-width"),
            ).toBe("15%");
        });

        it("does nothing to a group with no inline image", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            const before = group.innerHTML;
            normalizeInlineImages(group);
            expect(group.innerHTML).toBe(before);
        });
    });

    describe("removeInlineImage", () => {
        it("clears the image from every editable", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
                { lang: "z", content: "<p></p>" },
            ]);
            const wrapper = insertInlineImage(group);
            // Sanity check: all three have one before we remove.
            expect(getInlineImages(group).length).toBe(3);

            removeInlineImage(wrapper);

            expect(getInlineImages(group).length).toBe(0);
            expect(getInlineImage(group)).toBeNull();
            // The text survives.
            expect(editableFor(group, "fr").textContent).toBe("b");
        });

        it("removes only the image it was given, in every language", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            const first = insertInlineImage(group);
            const second = insertInlineImage(group);
            const firstId = getInlineImageId(first);
            const secondId = getInlineImageId(second);
            // Sanity check: two distinct images, both in both languages.
            expect(firstId).not.toBe(secondId);
            expect(getInlineImages(group).length).toBe(4);

            removeInlineImage(second);

            expect(getInlineImages(group).length).toBe(2);
            getEditables(group).forEach((editable) => {
                expect(
                    getInlineImageById(editable, firstId!),
                    "the untouched image should survive in every language",
                ).not.toBeNull();
                expect(getInlineImageById(editable, secondId!)).toBeNull();
            });
        });

        it("throws rather than guess when handed something that is not an inline image", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            insertInlineImage(group);
            // Passing the translation group used to mean "remove them all"; with several
            // images per block that could only guess, so it must fail loudly instead.
            expect(() => removeInlineImage(group)).toThrow();
            expect(getInlineImages(group).length).toBe(1);
        });
    });

    // A text block may hold any number of inline images (requirement from live testing,
    // 2026-08-05). Copies are matched across languages by kInlineImageIdAttr, and the order
    // within a cluster is the images' order.
    describe("several images in one block", () => {
        it("gives each image its own identity, in every language, in insertion order", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
                { lang: "z", content: "<p></p>" },
            ]);

            const first = insertInlineImage(group);
            const second = insertInlineImage(group);

            expect(getInlineImages(group).length).toBe(6);
            const ids = [getInlineImageId(first)!, getInlineImageId(second)!];
            expect(ids[0]).not.toBe(ids[1]);
            getEditables(group).forEach((editable) => {
                // Same two ids, in the same order, in every language.
                expect(
                    getInlineImagesInEditable(editable).map(getInlineImageId),
                ).toEqual(ids);
            });
        });

        it("keeps each image's geometry and dock separate when syncing", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            const first = insertInlineImage(group);
            const second = insertInlineImage(group);
            const source = editableFor(group, "en");
            const sibling = editableFor(group, "fr");

            first.style.setProperty("--inline-image-width", "20%");
            first.style.setProperty("--inline-image-offset", "10px");
            setInlineImageDock(second, kInlineImageLeftClass);
            second.style.setProperty("--inline-image-width", "70%");
            second.style.setProperty("--inline-image-offset", "200px");
            syncInlineImagesFromEditable(source);

            const [stampedFirst, stampedSecond] =
                getInlineImagesInEditable(sibling);
            expect(getInlineImageId(stampedFirst)).toBe(
                getInlineImageId(first),
            );
            expect(
                stampedFirst.style.getPropertyValue("--inline-image-width"),
            ).toBe("20%");
            expect(
                stampedFirst.style.getPropertyValue("--inline-image-offset"),
            ).toBe("10px");
            expect(
                stampedFirst.classList.contains(kInlineImageRightClass),
            ).toBe(true);
            expect(
                stampedSecond.style.getPropertyValue("--inline-image-width"),
            ).toBe("70%");
            expect(
                stampedSecond.style.getPropertyValue("--inline-image-offset"),
            ).toBe("200px");
            expect(
                stampedSecond.classList.contains(kInlineImageLeftClass),
            ).toBe(true);
        });

        it("clusters floating images before the text and bottom-docked ones after it", () => {
            const group = makeTranslationGroup([
                {
                    lang: "en",
                    classes: "bloom-content1",
                    content: "<p>one</p><p>two</p>",
                },
                { lang: "fr", content: "<p>un</p>" },
            ]);
            const floating = insertInlineImage(group);
            const bottom = insertInlineImage(group);
            const source = editableFor(group, "en");

            setInlineImageDock(bottom, kInlineImageBottomClass);
            syncInlineImagesFromEditable(source);

            [source, editableFor(group, "fr")].forEach((editable) => {
                const children = Array.from(editable.children);
                const floatingCopy = getInlineImageById(
                    editable,
                    getInlineImageId(floating)!,
                )!;
                const bottomCopy = getInlineImageById(
                    editable,
                    getInlineImageId(bottom)!,
                )!;
                expect(children.indexOf(floatingCopy)).toBe(0);
                expect(children.indexOf(bottomCopy)).toBe(children.length - 1);
                // The text is still between them, and unharmed.
                expect(editable.querySelectorAll("p").length).toBeGreaterThan(
                    0,
                );
            });
            expect(editableFor(group, "fr").textContent).toBe("un");
        });

        it("switching between floating docks does not reorder the images", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            const first = insertInlineImage(group);
            const second = insertInlineImage(group);
            const editable = editableFor(group, "en");
            // Sanity check on the starting order.
            expect(getInlineImagesInEditable(editable)).toEqual([
                first,
                second,
            ]);

            setInlineImageDock(first, kInlineImageLeftClass);
            setInlineImageDock(first, kInlineImageMiddleClass);

            expect(getInlineImagesInEditable(editable)).toEqual([
                first,
                second,
            ]);
        });

        it("sync is still idempotent with several images", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            insertInlineImage(group);
            const second = insertInlineImage(group);
            setInlineImageDock(second, kInlineImageBottomClass);
            const source = editableFor(group, "en");

            syncInlineImagesFromEditable(source);
            const afterFirst = group.innerHTML;
            syncInlineImagesFromEditable(source);

            expect(group.innerHTML).toBe(afterFirst);
        });

        it("undo restores the whole set, in order", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            const first = insertInlineImage(group);
            const second = insertInlineImage(group);
            const idsBefore = [
                getInlineImageId(first)!,
                getInlineImageId(second)!,
            ];
            putCaretIn(editableFor(group, "en"));

            removeInlineImage(second);
            // Sanity check: one image left in each of the two languages.
            expect(getInlineImages(group).length).toBe(2);

            expect(inlineImageCanUndo()).toBe(true);
            expect(inlineImageUndo()).toBe(true);

            getEditables(group).forEach((editable) => {
                expect(
                    getInlineImagesInEditable(editable).map(getInlineImageId),
                ).toEqual(idsBefore);
            });
            expect(editableFor(group, "fr").textContent).toBe("b");
        });
    });

    describe("handleInlineImageChanged", () => {
        it("syncs the new picture to the siblings and announces it", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            const wrapper = insertInlineImage(group);
            const img = wrapper.querySelector("img")!;
            // A stale ratio from the old picture must not survive the change.
            wrapper.style.setProperty("--inline-image-aspect-ratio", "3 / 2");
            let wrapperFromEvent: unknown;
            document.addEventListener(kInlineImageChangedEvent, (e) => {
                wrapperFromEvent = (e as CustomEvent).detail;
            });

            img.setAttribute("src", "flower.jpg");
            handleInlineImageChanged(img);

            expect(
                wrapper.style.getPropertyValue("--inline-image-aspect-ratio"),
            ).toBe("");
            expect(
                getInlineImageInEditable(editableFor(group, "fr"))!
                    .querySelector("img")!
                    .getAttribute("src"),
            ).toBe("flower.jpg");
            expect(wrapperFromEvent).toBe(wrapper);
        });
    });

    describe("undo", () => {
        it("undoes an insert, back to no image in any editable", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
                { lang: "z", content: "<p></p>" },
            ]);

            select(insertInlineImage(group));
            // Sanity check: the insert really happened and is undoable.
            expect(getInlineImages(group).length).toBe(3);
            expect(inlineImageCanUndo()).toBe(true);

            expect(inlineImageUndo()).toBe(true);

            expect(getInlineImages(group).length).toBe(0);
            // The text is untouched by the round trip.
            expect(editableFor(group, "fr").textContent).toBe("b");
        });

        // Deleting the image leaves nothing to select, so this is the one case where the gate
        // falls back to "is the caret still in that block". Without it, deleting an inline
        // image could never be undone at all.
        it("undoes a remove, restoring the image to every editable", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            removeInlineImage(insertInlineImage(group));
            // Sanity check: they really are gone, and nothing is selected.
            expect(getInlineImages(group).length).toBe(0);
            putCaretIn(editableFor(group, "en"));

            expect(inlineImageCanUndo()).toBe(true);
            expect(inlineImageUndo()).toBe(true);

            expect(getInlineImages(group).length).toBe(2);
            expect(
                getInlineImageInEditable(editableFor(group, "fr")),
            ).not.toBeNull();
            expect(editableFor(group, "fr").textContent).toBe("b");
        });

        // The other half of that fallback. Once the person has typed in the block, their typing
        // is the most recent thing they did, so ctrl+z belongs to ckeditor: going first here
        // would bring the picture back BEFORE the typing, which is not the order anything
        // happened in. These two stacks cannot be merged, so this is how they are ordered.
        it("declines the removed-image case once the user has typed in that block", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            removeInlineImage(insertInlineImage(group));
            const editable = editableFor(group, "en");
            putCaretIn(editable);
            // Sanity check: with nothing typed since, this is the case that says yes.
            expect(inlineImageCanUndo()).toBe(true);

            editable.querySelector("p")!.textContent = "a and some more words";

            expect(inlineImageCanUndo()).toBe(false);
        });

        // Same order-of-operations question as the test above, but the edit leaves the text
        // alone: bolding a word changes only the markup. CKEditor has an undo point for it,
        // so ctrl+z belongs to CKEditor, and going first here would bring the picture back
        // while the bolding stood.
        it("declines the removed-image case once the user has changed formatting", () => {
            const group = makeTranslationGroup([
                {
                    lang: "en",
                    classes: "bloom-content1",
                    content: "<p>some words</p>",
                },
            ]);
            removeInlineImage(insertInlineImage(group));
            const editable = editableFor(group, "en");
            putCaretIn(editable);
            // Sanity check: with nothing changed since, this is the case that says yes.
            expect(inlineImageCanUndo()).toBe(true);

            const paragraph = editable.querySelector("p")!;
            paragraph.innerHTML = "<strong>some</strong> words";
            // Sanity check: the text really is unchanged, so only the markup can tell.
            expect(paragraph.textContent).toBe("some words");

            expect(inlineImageCanUndo()).toBe(false);
        });

        // Selecting a picture does not stop the person typing: the caret stays in the block, and
        // the picture keeps its handles. So the selected picture must not win ctrl+z forever --
        // once they have typed, their typing is the most recent thing they did. Right-clicking
        // the text is the way to get into this state without meaning to, since the menu selects
        // nothing and leaves the picture as it was.
        it("declines with a picture still selected once the person has typed in the block", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            select(insertInlineImage(group));
            const editable = editableFor(group, "en");
            // Sanity check: with nothing typed since, the selected picture is what ctrl+z is for.
            expect(inlineImageCanUndo()).toBe(true);

            editable.querySelector("p")!.textContent = "a and some more words";

            expect(inlineImageCanUndo()).toBe(false);
        });

        // The content comparison cannot see an edit that undid itself -- typing a word and
        // deleting it again leaves the markup identical -- but CKEditor has two undo points for
        // it, and they are newer than ours. So the editing itself is reported, and that is what
        // hands ctrl+z over.
        it("declines after an edit that left the content exactly as it was", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            select(insertInlineImage(group));
            const editable = editableFor(group, "en");
            expect(inlineImageCanUndo()).toBe(true);

            // What the page reports for any typing in the block, however it ends up, and
            // CKEditor holding the undo points for it.
            noteInlineImageBlockWasEdited(editable);
            setCkeditorUndoable(true);

            expect(inlineImageCanUndo()).toBe(false);
            setCkeditorUndoable(undefined);
        });

        // The report of an edit must not be a one-way latch. Once the person has undone their
        // text editing, CKEditor has nothing left and the picture operation from before it is
        // the next thing back -- so it has to be reachable again, or ctrl+z simply stops
        // working and the insert can never be taken back.
        it("comes back once the text edits have been undone", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            select(insertInlineImage(group));
            noteInlineImageBlockWasEdited(editableFor(group, "en"));
            setCkeditorUndoable(true);
            // Sanity check: their editing goes first while CKEditor still holds it.
            expect(inlineImageCanUndo()).toBe(false);

            setCkeditorUndoable(false);

            expect(inlineImageCanUndo()).toBe(true);
            setCkeditorUndoable(undefined);
        });

        // "Is there anything to undo" is not the same question as "is any of it newer than
        // this snapshot". With text editing on BOTH sides of the picture operation, undoing the
        // newer half leaves CKEditor still holding the older half -- and answering the first
        // question there made every ctrl+z go to CKEditor, so it undid text from before the
        // picture operation while the picture change stood.
        it("comes back with older typing still behind it", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            const editable = editableFor(group, "en");
            // Typing first, so CKEditor holds one snapshot when ours is taken.
            noteInlineImageBlockWasEdited(editable);
            setCkeditorUndoable(true, 0);

            select(insertInlineImage(group));

            // Then typing that leaves the content exactly as it was, which only the report can
            // see, and which CKEditor takes a snapshot of.
            noteInlineImageBlockWasEdited(editable);
            setCkeditorUndoable(true, 1);
            // Sanity check: their newer typing goes first.
            expect(inlineImageCanUndo()).toBe(false);

            // Ctrl+Z takes that typing back; the older typing is still there to take back.
            setCkeditorUndoable(true, 0);

            expect(inlineImageCanUndo()).toBe(true);
            setCkeditorUndoable(undefined);
        });

        // The report names a block, and the undo point for that block need not be the top of
        // the stack: a picture operation in another block can be sitting on top of it. Marking
        // only the top left the older one looking untouched, so undoing the newer one exposed
        // a picture from before an edit the person has made since.
        it("marks the block's own undo point, not just the top of the stack", () => {
            const first = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            const second = makeTranslationGroup(
                [
                    {
                        lang: "en",
                        classes: "bloom-content1",
                        content: "<p>b</p>",
                    },
                ],
                "group2",
            );
            select(insertInlineImage(first));
            const secondWrapper = insertInlineImage(second);
            // An edit in the FIRST block, reported while the second block's operation is on
            // top. It leaves the content as it was, so only the report can know about it.
            noteInlineImageBlockWasEdited(editableFor(first, "en"));
            setCkeditorUndoable(true);

            // Undo the second block's insert, which is the top of the stack.
            select(secondWrapper);
            expect(inlineImageUndo()).toBe(true);

            // Now the first block's operation is on top, and it is older than that edit.
            putCaretIn(editableFor(first, "en"));
            expect(inlineImageCanUndo()).toBe(false);
            setCkeditorUndoable(undefined);
        });

        // SetupElements runs on a PIECE of the page whenever a canvas element is added (and
        // for the image description tool), and setupInlineImages goes with it. Clearing the
        // whole stack there took away the undo for a picture move the user had just made in a
        // block that setup never touched.
        it("survives a setup of another part of the page", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            const wrapper = insertInlineImage(group);
            recordInlineImageUndoPoint(group);
            wrapper.style.setProperty("--inline-image-width", "80%");
            syncInlineImagesFromEditable(editableFor(group, "en"));
            select(wrapper);
            // Sanity check: there is something to undo before the setup.
            expect(inlineImageCanUndo()).toBe(true);

            // Something elsewhere on the page gets set up. It holds no inline images, which is
            // the case that matters: the picture's own group is untouched.
            const elsewhere = document.createElement("div");
            group.closest(".bloom-page")!.appendChild(elsewhere);
            setupInlineImages(elsewhere);

            expect(inlineImageCanUndo()).toBe(true);
            expect(inlineImageUndo()).toBe(true);
            expect(
                getInlineImageInEditable(
                    editableFor(group, "en"),
                )!.style.getPropertyValue("--inline-image-width"),
            ).toBe("40%");
        });

        // The other side of it: a page frame rebuilt under us leaves every recorded element
        // detached, and restoring into those would put the picture nowhere the user can see.
        it("drops an undo point whose group has left the document", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            const wrapper = insertInlineImage(group);
            recordInlineImageUndoPoint(group);
            wrapper.style.setProperty("--inline-image-width", "80%");
            select(wrapper);
            // Sanity check: recorded, and reachable.
            expect(inlineImageCanUndo()).toBe(true);

            const page = group.closest(".bloom-page") as HTMLElement;
            group.remove();
            setupInlineImages(page);

            expect(inlineImageCanUndo()).toBe(false);
        });

        it("undoes a dock and size change in every editable at once", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            insertInlineImage(group);
            const source = editableFor(group, "en");
            const sibling = editableFor(group, "fr");

            recordInlineImageUndoPoint(group);
            const wrapper = getInlineImageInEditable(source)!;
            wrapper.style.setProperty("--inline-image-width", "80%");
            setInlineImageDock(wrapper, kInlineImageBottomClass);
            syncInlineImagesFromEditable(source);
            // Sanity check: the change landed in the sibling too.
            expect(sibling.lastElementChild).toBe(
                getInlineImageInEditable(sibling),
            );
            expect(
                getInlineImageInEditable(sibling)!.style.getPropertyValue(
                    "--inline-image-width",
                ),
            ).toBe("80%");

            select(getInlineImageInEditable(source)!);
            expect(inlineImageUndo()).toBe(true);

            [source, sibling].forEach((editable) => {
                const restored = getInlineImageInEditable(editable)!;
                expect(
                    restored.style.getPropertyValue("--inline-image-width"),
                ).toBe("40%");
                expect(
                    restored.classList.contains(kInlineImageRightClass),
                ).toBe(true);
                expect(
                    restored.classList.contains(kInlineImageBottomClass),
                ).toBe(false);
                // Back in the first slot, with the text after it.
                expect(editable.firstElementChild).toBe(restored);
            });
        });

        it("keeps the selection on the restored image, so a second undo is reachable", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            const source = editableFor(group, "en");
            select(insertInlineImage(group));
            // Two more operations on top of the insert.
            recordInlineImageUndoPoint(group);
            getInlineImageInEditable(source)!.style.setProperty(
                "--inline-image-width",
                "60%",
            );
            recordInlineImageUndoPoint(group);
            getInlineImageInEditable(source)!.style.setProperty(
                "--inline-image-width",
                "80%",
            );

            expect(inlineImageUndo()).toBe(true);
            expect(
                getInlineImageInEditable(source)!.style.getPropertyValue(
                    "--inline-image-width",
                ),
            ).toBe("60%");
            // The restored wrapper is a new element, but it inherits the selection...
            expect(
                getInlineImageInEditable(source)!.classList.contains(
                    kInlineImageSelectedClass,
                ),
            ).toBe(true);
            // ...so the next undo is still routed to us.
            expect(inlineImageCanUndo()).toBe(true);
            expect(inlineImageUndo()).toBe(true);
            expect(
                getInlineImageInEditable(source)!.style.getPropertyValue(
                    "--inline-image-width",
                ),
            ).toBe("40%");
        });

        it("announces the restore, so the interaction layer can rebuild what it attached", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            select(insertInlineImage(group));
            let eventsSeen = 0;
            let groupFromEvent: EventTarget | null = null;
            // Listening at the document proves it bubbles, which is what lets the interaction
            // layer use one listener for the whole page.
            document.addEventListener(kInlineImagesRestoredEvent, (e) => {
                eventsSeen++;
                groupFromEvent = e.target;
            });

            inlineImageUndo();

            expect(eventsSeen).toBe(1);
            expect(groupFromEvent).toBe(group);
        });

        // Undo replaces the wrappers, so the copy that was selected is gone -- and when the
        // operation it took back was the insert that created that picture, there is no copy of
        // it left to hand the selection to. Requiring a selected picture made the second ctrl+z
        // in a row fall through to text undo.
        it("allows a second undo after the first one left nothing selected", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            const first = insertInlineImage(group);
            // An earlier operation on the first picture: this is what the second ctrl+z has to
            // reach. It is deliberately never selected -- the user moves on to the new picture.
            prepareInlineImageUndo(group);
            first.style.setProperty(kInlineImageWidthVar, "80%");
            commitPendingInlineImageUndo(group);
            select(insertInlineImage(group));
            // Sanity check: two pictures, and the newer insert is what undo takes back first.
            expect(getInlineImages(group).length).toBe(2);

            expect(inlineImageUndo()).toBe(true);
            expect(getInlineImages(group).length).toBe(1);
            // Nothing is selected now: the picture that was is the one undo took away.
            expect(
                group.querySelector("." + kInlineImageSelectedClass),
            ).toBeNull();
            // The caret is where selecting a picture leaves it: in the block's text.
            putCaretIn(editableFor(group, "en"));

            expect(inlineImageCanUndo()).toBe(true);
            expect(inlineImageUndo()).toBe(true);
            expect(
                getInlineImageInEditable(
                    editableFor(group, "en"),
                )!.style.getPropertyValue(kInlineImageWidthVar),
            ).toBe("40%");
        });

        it("declines when nothing has been recorded", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            const wrapper = insertInlineImage(group);
            select(wrapper);
            // Sanity check: the only reason it would say yes is the recorded insert.
            expect(inlineImageCanUndo()).toBe(true);

            clearInlineImageUndoState();

            expect(inlineImageCanUndo()).toBe(false);
            expect(inlineImageUndo()).toBe(false);
        });

        it("declines when no inline image is the active thing", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            insertInlineImage(group);

            // Recorded, but nothing selected: undo must fall through to ckeditor rather than
            // shadowing whatever the user did most recently.
            expect(inlineImageCanUndo()).toBe(false);
        });

        it("declines when the active inline image is in a different translation group", () => {
            const root = getTestRoot();
            root.innerHTML =
                `<div class="bloom-page" data-page-id="test-page-${++pageCounter}">` +
                `<div class="bloom-translationGroup" id="groupA">` +
                `<div class="bloom-editable bloom-content1" lang="en"><p>a</p></div></div>` +
                `<div class="bloom-translationGroup" id="groupB">` +
                `<div class="bloom-editable bloom-content1" lang="en"><p>b</p></div></div>` +
                `</div>`;
            const groupA = root.querySelector("#groupA") as HTMLElement;
            const groupB = root.querySelector("#groupB") as HTMLElement;
            insertInlineImage(groupB);
            clearInlineImageUndoState();

            // The recorded operation is in A, but the user is working on B's image.
            recordInlineImageUndoPoint(groupA);
            select(getInlineImage(groupB)!);

            expect(inlineImageCanUndo()).toBe(false);
        });

        it("records on commit but not on discard", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            select(insertInlineImage(group));
            clearInlineImageUndoState();

            const image = getInlineImages(group)[0];

            prepareInlineImageUndo(group);
            discardPendingInlineImageUndo();
            commitPendingInlineImageUndo(group);
            expect(inlineImageCanUndo()).toBe(false);

            // Something has to actually change between prepare and commit: on this two-phase
            // path the change has already landed by the time we commit, so a snapshot that
            // still describes the group means the operation ended where it began, and that is
            // deliberately not recorded.
            prepareInlineImageUndo(group);
            image.style.setProperty(kInlineImageWidthVar, "55%");
            commitPendingInlineImageUndo(group);
            expect(inlineImageCanUndo()).toBe(true);
        });

        it("does not record an operation that ended where it began", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            select(insertInlineImage(group));
            clearInlineImageUndoState();

            // What a drag reverted by the fit-or-revert rule leaves behind: the gesture ran, so
            // it prepared and committed, but the picture is back where it started. An undo point
            // there would do nothing visible, and would make the NEXT undo take back a change
            // the person had stopped thinking about.
            prepareInlineImageUndo(group);
            commitPendingInlineImageUndo(group);

            expect(inlineImageCanUndo()).toBe(false);
        });

        it("forgets everything when the page changes", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            select(insertInlineImage(group));
            // Sanity check: recorded and reachable on this page.
            expect(inlineImageCanUndo()).toBe(true);

            group
                .closest(".bloom-page")!
                .setAttribute("data-page-id", "some-other-page");

            expect(inlineImageCanUndo()).toBe(false);
        });

        it("takes charge of undo only for images that really are inline images", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            const wrapper = insertInlineImage(group);
            const inlineImg = wrapper.querySelector("img") as HTMLElement;
            const ordinaryImg = document.createElement("img");
            group.closest(".bloom-page")!.appendChild(ordinaryImg);

            // This is what tells bloomEditing's changeImage which undo layer owns the change.
            expect(prepareInlineImageUndoForImageChange(inlineImg)).toBe(true);
            expect(prepareInlineImageUndoForImageChange(ordinaryImg)).toBe(
                false,
            );
        });
    });
});
