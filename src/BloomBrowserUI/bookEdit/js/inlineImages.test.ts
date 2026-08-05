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
    inlineImageUndo,
    insertInlineImage,
    kInlineImageBottomClass,
    kInlineImageClass,
    kInlineImageRightClass,
    kInlineImageSelectedClass,
    kInlineImagesRestoredEvent,
    kKeepFirstInFieldClass,
    normalizeInlineImages,
    prepareInlineImageUndo,
    prepareInlineImageUndoForImageChange,
    recordInlineImageUndoPoint,
    removeInlineImage,
    setInlineImageDock,
    syncInlineImagesFromEditable,
} from "./inlineImages";

// Each group is built inside its own .bloom-page with a fresh data-page-id, both because
// that is how it looks in Bloom and because the undo layer keys its "did the page change?"
// check on that id, so a new one per test gives each test a clean undo stack.
let pageCounter = 0;

// Builds a translation group, one editable per entry.
function makeTranslationGroup(
    editables: { lang: string; classes?: string; content?: string }[],
): HTMLElement {
    const root = getTestRoot();
    root.innerHTML =
        `<div class="bloom-page" data-page-id="test-page-${++pageCounter}">` +
        `<div class="bloom-translationGroup" id="group">` +
        editables
            .map(
                (e) =>
                    `<div class="bloom-editable ${e.classes ?? ""}" lang="${e.lang}">${
                        e.content ?? ""
                    }</div>`,
            )
            .join("") +
        `</div></div>`;
    return root.querySelector("#group") as HTMLElement;
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

            prepareInlineImageUndo(group);
            discardPendingInlineImageUndo();
            commitPendingInlineImageUndo(group);
            expect(inlineImageCanUndo()).toBe(false);

            prepareInlineImageUndo(group);
            commitPendingInlineImageUndo(group);
            expect(inlineImageCanUndo()).toBe(true);
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
