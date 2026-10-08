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

// Each group is built inside its own .bloom-page with a new data-page-id. That is how it looks
// in Bloom, and the inline image undo code clears its stack when that id changes, so a new id
// per test gives each test an empty undo stack.
let pageCounter = 0;

// Builds a translation group with one editable per entry. Passing `id` gives the group that id
// and adds it to the page beside any group already there, for tests that need two blocks on
// one page. Without `id`, the page is rebuilt with this as its only group.
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

// A fake of the page's CKEditor undo manager. `undoable` says whether it holds text editing it
// could undo, and `index` is CKEditor's own name for its position in its stack of snapshots,
// which goes up by one per edit and down as edits are undone. The real manager shows that its
// stack is full by having as many `snapshots` as its `limit`, and from then on the position
// stops going up. The real one is reached through the page's global CKEDITOR object. When
// that global is missing, as it is in these tests unless a test sets it, the code under test
// treats it as having nothing to undo.
const fakeCkeditorUndoManager = {
    undoable: () => fakeCkeditorUndoManager.state,
    index: undefined as number | undefined,
    state: false,
    snapshots: [] as unknown[],
    limit: 20,
};

function setCkeditorUndoable(
    undoable: boolean | undefined,
    index?: number,
    historyIsFull = false,
): void {
    const global = globalThis as unknown as { CKEDITOR?: unknown };
    if (undoable === undefined) {
        delete global.CKEDITOR;
        return;
    }
    // Keep the same manager object across calls. The real one belongs to an editable and
    // stays the same, and the code under test only compares two positions when they come from
    // the same manager.
    fakeCkeditorUndoManager.state = undoable;
    fakeCkeditorUndoManager.index = index;
    fakeCkeditorUndoManager.snapshots = new Array(
        historyIsFull ? fakeCkeditorUndoManager.limit : 1,
    ).fill(undefined);
    global.CKEDITOR = {
        currentInstance: { undoManager: fakeCkeditorUndoManager },
    };
}

// inlineImageCanUndo normally needs an active inline image, which in the real editor means
// one with kInlineImageSelectedClass.
const select = (wrapper: HTMLElement) =>
    wrapper.classList.add(kInlineImageSelectedClass);

// Puts the caret in a block's text, as if the user had clicked there.
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
        // A caret left in a removed element would affect inlineImageCanUndo in the next test.
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
                // The wrapper goes first so the text wraps around it; BloomField keeps the <p>
                // after it.
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
        // already has somewhere to type and must not get an empty paragraph under it. That
        // paragraph would show as a blank line, and stay there once the page is saved.
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
            // Make the source copy different from the others.
            const wrapper = getInlineImageInEditable(source)!;
            wrapper.style.setProperty("--inline-image-width", "25%");
            wrapper.style.setProperty("--inline-image-offset", "120px");
            // Sanity check: the other copy does not have that yet.
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
        // data-bloom-inline-image-id. Sync pairs copies up by that id, so without one the
        // other editable's copy can't be recognized, and another copy would be added beside
        // it in each language every time the page was set up, for as long as the book was open.
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
            // Sanity check: one each, and no id to pair them by.
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
            // Both copies now have the same id, so the next operation can pair them.
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
            // Sanity check: it is gone.
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
            // Sanity check: the dock change moved the source copy to the end.
            expect(source.lastElementChild).toBe(
                getInlineImageInEditable(source),
            );

            syncInlineImagesFromEditable(source);

            const sibling = editableFor(group, "fr");
            const stamped = getInlineImageInEditable(sibling)!;
            expect(sibling.lastElementChild).toBe(stamped);
            // A bottom-docked wrapper must not have bloom-keepFirstInField, or BloomField
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
            // Sanity check: the other copy is at the end before we dock it elsewhere.
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
            // Changing the image leaves a temporary id on the img.
            wrapper.querySelector("img")!.setAttribute("id", "tempImageId");

            syncInlineImagesFromEditable(source);

            const stamped = getInlineImageInEditable(editableFor(group, "fr"))!;
            expect(stamped.querySelector(".bloom-ui")).toBeNull();
            expect(stamped.classList.contains(kInlineImageSelectedClass)).toBe(
                false,
            );
            expect(stamped.querySelector("[id]")).toBeNull();
            // The source keeps its bloom-ui element; sync does not clean up the source.
            expect(wrapper.querySelector(".bloom-ui")).not.toBeNull();
        });

        it("does nothing when the editable has no inline image", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            insertInlineImage(group);
            const before = group.innerHTML;

            // The 'fr' editable has a copy, so remove it first to get an editable with none.
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
        // nobody can see. If normalize chose that copy, it would copy it over the language the
        // person was working in.
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
            // Sanity check: the two copies differ before we normalize.
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
            // The text is still there.
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
            // Given the translation group, removeInlineImage could only guess which of several
            // images to remove, so it must throw.
            expect(() => removeInlineImage(group)).toThrow();
            expect(getInlineImages(group).length).toBe(1);
        });
    });

    // A text block may hold any number of inline images. Copies are paired across languages
    // by kInlineImageIdAttr, and the DOM order of the images before the text (or after it) is
    // the order of the images.
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
                // The text is still between them, unchanged.
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
            // The old picture's ratio must be removed when the picture changes.
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
            // Sanity check: the insert happened and can be undone.
            expect(getInlineImages(group).length).toBe(3);
            expect(inlineImageCanUndo()).toBe(true);

            expect(inlineImageUndo()).toBe(true);

            expect(getInlineImages(group).length).toBe(0);
            // Inserting and undoing leaves the text unchanged.
            expect(editableFor(group, "fr").textContent).toBe("b");
        });

        // Deleting the image leaves nothing selected, so inlineImageCanUndo instead checks
        // whether the caret is still in that block. Without that, deleting an inline image
        // could never be undone.
        it("undoes a remove, restoring the image to every editable", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            removeInlineImage(insertInlineImage(group));
            // Sanity check: they are gone, and nothing is selected.
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

        // Once the person has typed in the block, their typing is the most recent thing they
        // did, so ctrl+z should go to ckeditor. If inlineImageCanUndo said yes, the picture would
        // come back BEFORE the typing was undone, out of the order things happened in. The two
        // undo stacks are separate, so this check is what keeps them in order.
        it("declines the removed-image case once the user has typed in that block", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            removeInlineImage(insertInlineImage(group));
            const editable = editableFor(group, "en");
            putCaretIn(editable);
            // Sanity check: with nothing typed since, inlineImageCanUndo says yes.
            expect(inlineImageCanUndo()).toBe(true);

            editable.querySelector("p")!.textContent = "a and some more words";

            expect(inlineImageCanUndo()).toBe(false);
        });

        // Like the test above, but this edit leaves the text the same, because bolding a word
        // changes only the markup. CKEditor has an undo point for it, so ctrl+z should go to
        // CKEditor; otherwise the picture would come back while the bolding stayed.
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
            // Sanity check: with nothing changed since, inlineImageCanUndo says yes.
            expect(inlineImageCanUndo()).toBe(true);

            const paragraph = editable.querySelector("p")!;
            paragraph.innerHTML = "<strong>some</strong> words";
            // Sanity check: the text is unchanged, so only the markup shows the edit.
            expect(paragraph.textContent).toBe("some words");

            expect(inlineImageCanUndo()).toBe(false);
        });

        // Selecting a picture does not stop the person typing, because the caret stays in the
        // block and the picture keeps its handles. So once they have typed, their typing is the
        // most recent thing they did, and ctrl+z must go to it even though the picture is still
        // selected. Right-clicking the text gets into this state without the person meaning
        // to, because the menu leaves the picture selected.
        it("declines with a picture still selected once the person has typed in the block", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            select(insertInlineImage(group));
            const editable = editableFor(group, "en");
            // Sanity check: with nothing typed since, ctrl+z goes to the selected picture.
            expect(inlineImageCanUndo()).toBe(true);

            editable.querySelector("p")!.textContent = "a and some more words";

            expect(inlineImageCanUndo()).toBe(false);
        });

        // Comparing the content can't see an edit that left it unchanged, such as typing a word
        // and deleting it again, but CKEditor has two undo points for that edit, and they are
        // newer than our snapshot. So the page reports the editing with
        // noteInlineImageBlockWasEdited, and that is what sends ctrl+z to CKEditor.
        it("declines after an edit that left the content exactly as it was", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            select(insertInlineImage(group));
            const editable = editableFor(group, "en");
            expect(inlineImageCanUndo()).toBe(true);

            // The page reports any typing in the block, whatever the result, and CKEditor
            // holds the undo points for it.
            noteInlineImageBlockWasEdited(editable);
            setCkeditorUndoable(true);

            expect(inlineImageCanUndo()).toBe(false);
            setCkeditorUndoable(undefined);
        });

        // Reporting an edit must not block the picture undo for good. Once the person has undone
        // their text editing, CKEditor has nothing left, and the picture operation from before
        // it is the next thing to undo. If inlineImageCanUndo still said no, ctrl+z would stop
        // working and the insert could never be undone.
        it("comes back once the text edits have been undone", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            select(insertInlineImage(group));
            noteInlineImageBlockWasEdited(editableFor(group, "en"));
            setCkeditorUndoable(true);
            // Sanity check: their editing is undone first while CKEditor still holds it.
            expect(inlineImageCanUndo()).toBe(false);

            setCkeditorUndoable(false);

            expect(inlineImageCanUndo()).toBe(true);
            setCkeditorUndoable(undefined);
        });

        // Whether CKEditor has anything to undo is a different question from whether any of it
        // is newer than this snapshot. When the person typed both before and after the picture
        // operation, undoing the newer typing leaves CKEditor still holding the older typing.
        // Asking only whether CKEditor has anything would send every ctrl+z to CKEditor, which
        // would undo text from before the picture operation while the picture change stayed.
        it("comes back with older typing still behind it", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            const editable = editableFor(group, "en");
            // Type first, so CKEditor holds one snapshot when ours is taken.
            noteInlineImageBlockWasEdited(editable);
            setCkeditorUndoable(true, 0);

            select(insertInlineImage(group));

            // Then type something that leaves the content exactly as it was. Only the report
            // shows it, and CKEditor takes a snapshot of it.
            noteInlineImageBlockWasEdited(editable);
            setCkeditorUndoable(true, 1);
            // Sanity check: their newer typing is undone first.
            expect(inlineImageCanUndo()).toBe(false);

            // Ctrl+Z undoes that typing; the older typing is still there to undo.
            setCkeditorUndoable(true, 0);

            expect(inlineImageCanUndo()).toBe(true);
            setCkeditorUndoable(undefined);
        });

        // CKEditor keeps at most 20 snapshots, and once it has that many its position stops
        // going up, because save() drops the oldest snapshot before pushing the newest, so the
        // newest is at the same position as before. Looking only at the position would say
        // nothing was typed since, for typing in a long block, and ctrl+z would undo the
        // picture before the typing.
        it("defers to typing after the snapshot once CKEditor's history is full", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            const editable = editableFor(group, "en");
            // A block with plenty of typing behind it, so CKEditor's stack is already full.
            noteInlineImageBlockWasEdited(editable);
            setCkeditorUndoable(true, 19, true);

            select(insertInlineImage(group));
            // Sanity check: with nothing typed since, ctrl+z goes to the picture operation.
            expect(inlineImageCanUndo()).toBe(true);

            // Then type something that leaves the content exactly as it was, which only the
            // report shows. CKEditor saves it, and its position stays where it was.
            noteInlineImageBlockWasEdited(editable);
            setCkeditorUndoable(true, 19, true);

            expect(inlineImageCanUndo()).toBe(false);

            // Once that typing is undone, the position drops below ours and ctrl+z goes to the
            // picture operation again.
            setCkeditorUndoable(true, 18, true);
            expect(inlineImageCanUndo()).toBe(true);
            setCkeditorUndoable(undefined);
        });

        // The report is about one block, and the undo point for that block may not be at the
        // top of the stack, because a picture operation in another block can be on top of it.
        // If only the top were marked, the older one would look as if nothing had been typed
        // since, and after the newer one was undone, ctrl+z would bring back a picture from
        // before an edit the person has made since.
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
            // top. It leaves the content as it was, so only the report shows it.
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

        // SetupElements runs on PART of the page whenever a canvas element is added (and for
        // the image description tool), and it calls setupInlineImages. Clearing the whole stack
        // there would take away the undo for a picture move the user had just made, in a block
        // that setup never touched.
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

            // Set up something elsewhere on the page. It holds no inline images, so the
            // picture's own group is not touched, which is the case under test.
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

        // When the page frame is rebuilt, every recorded element is detached, and restoring
        // into those would put the picture somewhere the user can't see.
        it("drops an undo point whose group has left the document", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            const wrapper = insertInlineImage(group);
            recordInlineImageUndoPoint(group);
            wrapper.style.setProperty("--inline-image-width", "80%");
            select(wrapper);
            // Sanity check: recorded, and inlineImageCanUndo says yes.
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
            // Sanity check: the change reached the other editable too.
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
                // It is the first child again, with the text after it.
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
            // The restored wrapper is a new element, but it is still selected,
            expect(
                getInlineImageInEditable(source)!.classList.contains(
                    kInlineImageSelectedClass,
                ),
            ).toBe(true);
            // so the next undo still comes to the inline image undo stack.
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
            // Listening on the document shows that the event bubbles, which lets the code that
            // handles clicks and drags on inline images use one listener for the whole page.
            document.addEventListener(kInlineImagesRestoredEvent, (e) => {
                eventsSeen++;
                groupFromEvent = e.target;
            });

            inlineImageUndo();

            expect(eventsSeen).toBe(1);
            expect(groupFromEvent).toBe(group);
        });

        // Undo replaces the wrappers, so the copy that was selected is gone, and when the
        // operation it undid was the insert that created that picture, there is no copy of it
        // left to select. If inlineImageCanUndo required a selected picture, the second ctrl+z
        // in a row would go to text undo instead.
        it("allows a second undo after the first one left nothing selected", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            const first = insertInlineImage(group);
            // An earlier operation on the first picture, which the second ctrl+z has to undo.
            // The first picture is never selected, because the user moves on to the new one.
            prepareInlineImageUndo(group);
            first.style.setProperty(kInlineImageWidthVar, "80%");
            commitPendingInlineImageUndo(group);
            select(insertInlineImage(group));
            // Sanity check: two pictures, and undo will undo the newer insert first.
            expect(getInlineImages(group).length).toBe(2);

            expect(inlineImageUndo()).toBe(true);
            expect(getInlineImages(group).length).toBe(1);
            // Nothing is selected now, because undo removed the picture that was selected.
            expect(
                group.querySelector("." + kInlineImageSelectedClass),
            ).toBeNull();
            // Selecting a picture leaves the caret in the block's text, so put it there.
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
            // Sanity check: it says yes, and the recorded insert is the only reason it could.
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

            // The insert is recorded, but nothing is selected, so undo must go on to ckeditor
            // and undo whatever the user did most recently.
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

            // Something has to change between prepare and commit. With prepare and commit, the
            // change has already happened when we commit, so a snapshot that still matches the
            // group means the operation ended where it began, and that is not recorded.
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

            // This is what a drag leaves when the move did not fit and the picture was put back.
            // The drag prepared and committed, but the picture is back where it started. An undo
            // point here would do nothing visible, and the undo after it would take back a
            // change the person had stopped thinking about.
            prepareInlineImageUndo(group);
            commitPendingInlineImageUndo(group);

            expect(inlineImageCanUndo()).toBe(false);
        });

        it("forgets everything when the page changes", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            select(insertInlineImage(group));
            // Sanity check: recorded, and inlineImageCanUndo says yes on this page.
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

            // The return value tells bloomEditing's changeImage whether this file or
            // ImageUndoManager handles undo for the change.
            expect(prepareInlineImageUndoForImageChange(inlineImg)).toBe(true);
            expect(prepareInlineImageUndoForImageChange(ordinaryImg)).toBe(
                false,
            );
        });
    });
});
