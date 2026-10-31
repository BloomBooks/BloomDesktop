import {
    describe,
    it,
    expect,
    beforeEach,
    afterEach,
    afterAll,
    vi,
} from "vitest";
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import {
    getTestRoot,
    cleanTestRoot,
    removeTestRoot,
} from "../../utils/testHelper";
import {
    getEditables,
    getInlineImage,
    getInlineImageById,
    getInlineImageId,
    getInlineImageInEditable,
    getInlineImages,
    getInlineImagesInEditable,
    handleInlineImageChanged,
    kInlineImageAspectRatioVar,
    kInlineImageLeftClass,
    kInlineImageMiddleClass,
    insertInlineImage,
    kInlineImageBottomClass,
    kInlineImageClass,
    kInlineImageRightClass,
    kInlineImageWidthVar,
    kInlineImageSelectedClass,
    kKeepFirstInFieldClass,
    normalizeInlineImages,
    removeInlineImage,
    setInlineImageDock,
    syncInlineImagesFromEditable,
} from "./inlineImages";
import { createEditTimeMarkupFilter } from "../pageHistory/editTimeMarkup";
import { createPageHistory } from "../pageHistory/pageHistory";
import { kTypingUndoDescriptionL10nKey } from "../pageHistory/pageHistoryInput";
import {
    kGenericUndoDescriptionL10nKey,
    PageHistory,
} from "../pageHistory/pageHistoryTypes";

// Each group is built inside its own .bloom-page with a new data-page-id, which is how it looks
// in Bloom.
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

// The code that handles clicking on inline images marks the one the user has selected with
// kInlineImageSelectedClass.
const select = (wrapper: HTMLElement) =>
    wrapper.classList.add(kInlineImageSelectedClass);

const editableFor = (group: HTMLElement, lang: string) =>
    group.querySelector(`[lang="${lang}"]`) as HTMLElement;

describe("inlineImages", () => {
    beforeEach(() => {
        cleanTestRoot();
        // Some of the code under test asks for an overflow check, which OverflowChecker runs on a
        // one-second timer. That timer would fire after the test has ended, when the canvas
        // element manager it calls is gone, and vitest reports the error as unhandled. The
        // overflow check needs real layout, which jsdom does not have, so it is not run here.
        vi.spyOn(
            OverflowChecker,
            "AdjustSizeOrMarkOverflowSoon",
        ).mockImplementation(() => {});
    });
    afterEach(() => {
        vi.restoreAllMocks();
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

        // inlineImages.less shows the image in a visible bloom-content2 rather than a visible
        // bloom-content3, so the content2 copy is the one the user has been working with.
        it("prefers bloom-content2 over bloom-content3 when there is no bloom-content1", () => {
            const group = makeTranslationGroup([
                {
                    lang: "es",
                    classes: "bloom-content3 bloom-visibility-code-on",
                    content: "<p>c</p>",
                },
                {
                    lang: "fr",
                    classes: "bloom-content2 bloom-visibility-code-on",
                    content: "<p>b</p>",
                },
            ]);
            const returned = insertInlineImage(group);
            expect(returned).toBe(
                getInlineImageInEditable(editableFor(group, "fr")),
            );
            returned.style.setProperty("--inline-image-width", "15%");

            normalizeInlineImages(group);

            expect(
                getInlineImageInEditable(
                    editableFor(group, "es"),
                )!.style.getPropertyValue("--inline-image-width"),
            ).toBe("15%");
        });

        // A language added by an older Bloom has no copy. If normalize chose that editable
        // because it is the one the reader sees, syncing from it would do nothing and the
        // picture would stay only where the CSS hides it.
        it("copies from a visible editable with a copy when the top-ranked visible one has none", () => {
            const group = makeTranslationGroup([
                {
                    lang: "en",
                    classes: "bloom-content1 bloom-visibility-code-on",
                    content: "<p>a</p>",
                },
                {
                    lang: "fr",
                    classes: "bloom-content2 bloom-visibility-code-on",
                    content: "<p>b</p>",
                },
            ]);
            insertInlineImage(group);
            getInlineImageInEditable(editableFor(group, "en"))!.remove();
            // Sanity check: only the French editable has a copy now.
            expect(getInlineImages(group).length).toBe(1);

            normalizeInlineImages(group);

            expect(
                getInlineImageInEditable(editableFor(group, "en")),
            ).not.toBeNull();
            expect(getInlineImage(group)).toBe(
                getInlineImageInEditable(editableFor(group, "en")),
            );
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
    });

    describe("handleInlineImageChanged", () => {
        it("syncs the new picture to the siblings", () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            const wrapper = insertInlineImage(group);
            const img = wrapper.querySelector("img")!;
            // The old picture's ratio must be removed when the picture changes.
            wrapper.style.setProperty("--inline-image-aspect-ratio", "3 / 2");

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
        });
    });

    // Inline images have no undo of their own: the page history records what each operation
    // changes in the page frame, as it records any other change there. These tests make each
    // operation a step the way the page does, inside a transaction that the user's press opens
    // (pageHistoryInput.ts), and check what an undo and a redo of that step leave.
    describe("undo", () => {
        let history: PageHistory;

        /** A page history on the page that holds group, as the page frame makes one. */
        function startHistory(group: HTMLElement): PageHistory {
            history = createPageHistory({
                root: group.closest(".bloom-page") as HTMLElement,
                filter: createEditTimeMarkupFilter(),
            });
            return history;
        }

        /** One user operation, as the transaction that the user's press opens makes it a step. */
        function operation(fn: () => void): Promise<void> {
            return history.doTransaction(kGenericUndoDescriptionL10nKey, fn);
        }

        /** Typing in the block's first paragraph, as a step of its own. */
        function typing(editable: HTMLElement, text: string): Promise<void> {
            return history.doTransaction(kTypingUndoDescriptionL10nKey, () => {
                editable.querySelector("p")!.textContent += text;
            });
        }

        /** Something other than an inline image, such as a table operation, as a step. */
        function otherOperation(
            group: HTMLElement,
            value: string,
        ): Promise<void> {
            return operation(() => group.setAttribute("data-other", value));
        }

        const widthIn = (editable: HTMLElement) =>
            getInlineImageInEditable(editable)!.style.getPropertyValue(
                kInlineImageWidthVar,
            );

        /** Resize the group's one image the way the page does: change it, then sync. */
        function resize(group: HTMLElement, width: string): Promise<void> {
            return operation(() => {
                const source = editableFor(group, "en");
                getInlineImageInEditable(source)!.style.setProperty(
                    kInlineImageWidthVar,
                    width,
                );
                syncInlineImagesFromEditable(source);
            });
        }

        afterEach(() => {
            history?.dispose();
        });

        it("makes an insert a step that takes the image back out of every editable", async () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
                { lang: "z", content: "<p></p>" },
            ]);
            startHistory(group);

            await operation(() => insertInlineImage(group));
            // Sanity check: the insert happened, and is the one step.
            expect(getInlineImages(group).length).toBe(3);
            expect(history.undoDescriptionL10nKey).toBe(
                kGenericUndoDescriptionL10nKey,
            );

            await history.undo();

            expect(getInlineImages(group).length).toBe(0);
            // Inserting and undoing leaves the text unchanged.
            expect(editableFor(group, "fr").textContent).toBe("b");
            expect(history.undoDescriptionL10nKey).toBeUndefined();
        });

        it("undoes a remove, restoring the image to every editable", async () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            startHistory(group);
            const wrapper = insertInlineImage(group);
            await operation(() => removeInlineImage(wrapper));
            // Sanity check: they are gone.
            expect(getInlineImages(group).length).toBe(0);

            await history.undo();

            expect(getInlineImages(group).length).toBe(2);
            expect(
                getInlineImageInEditable(editableFor(group, "fr")),
            ).not.toBeNull();
            expect(editableFor(group, "fr").textContent).toBe("b");
        });

        // The delete command moves the images left in the block back to where they were, and
        // one Undo has to take back both that and the delete.
        it("makes what afterRemoving changes part of the remove's step", async () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            startHistory(group);
            const first = insertInlineImage(group);
            const second = insertInlineImage(group);

            await operation(() =>
                removeInlineImage(first, () => {
                    second.style.setProperty(kInlineImageWidthVar, "80%");
                    syncInlineImagesFromEditable(editableFor(group, "en"));
                }),
            );
            // Sanity check: the change reached the other language.
            expect(widthIn(editableFor(group, "fr"))).toBe("80%");

            await history.undo();

            expect(getInlineImages(group).length).toBe(4);
            getEditables(group).forEach((editable) => {
                const survivor = getInlineImagesInEditable(editable).find(
                    (w) => getInlineImageId(w) === getInlineImageId(second),
                )!;
                expect(
                    survivor.style.getPropertyValue(kInlineImageWidthVar),
                ).toBe("40%");
            });
            // One undo took back both, so there is nothing left to undo.
            expect(history.undoDescriptionL10nKey).toBeUndefined();
        });

        it("undoes a dock and size change in every editable at once", async () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            startHistory(group);
            insertInlineImage(group);
            const source = editableFor(group, "en");
            const sibling = editableFor(group, "fr");

            await operation(() => {
                const wrapper = getInlineImageInEditable(source)!;
                wrapper.style.setProperty(kInlineImageWidthVar, "80%");
                setInlineImageDock(wrapper, kInlineImageBottomClass);
                syncInlineImagesFromEditable(source);
            });
            // Sanity check: the change reached the other editable too.
            expect(sibling.lastElementChild).toBe(
                getInlineImageInEditable(sibling),
            );
            expect(widthIn(sibling)).toBe("80%");

            await history.undo();

            [source, sibling].forEach((editable) => {
                const restored = getInlineImageInEditable(editable)!;
                expect(widthIn(editable)).toBe("40%");
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

        it("restores the whole set of images, in order", async () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            startHistory(group);
            const first = insertInlineImage(group);
            const second = insertInlineImage(group);
            const idsBefore = [
                getInlineImageId(first)!,
                getInlineImageId(second)!,
            ];

            await operation(() => removeInlineImage(second));
            // Sanity check: one image left in each of the two languages.
            expect(getInlineImages(group).length).toBe(2);

            await history.undo();

            getEditables(group).forEach((editable) => {
                expect(
                    getInlineImagesInEditable(editable).map(getInlineImageId),
                ).toEqual(idsBefore);
            });
            expect(editableFor(group, "fr").textContent).toBe("b");
        });

        it("undoes typing that came after a picture operation before the operation", async () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            startHistory(group);
            const editable = editableFor(group, "en");
            await operation(() => insertInlineImage(group));
            await typing(editable, "bc");

            await history.undo();

            expect(editable.textContent).toBe("a");
            expect(getInlineImages(group).length).toBe(1);
        });

        it("undoes a picture operation that came after typing before the typing", async () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            startHistory(group);
            const editable = editableFor(group, "en");
            await typing(editable, "bc");
            await operation(() => insertInlineImage(group));

            await history.undo();

            expect(editable.textContent).toBe("abc");
            expect(getInlineImages(group).length).toBe(0);

            // Then the typing, which came before it, is next.
            await history.undo();
            expect(editable.textContent).toBe("a");
        });

        it("orders picture operations and other operations as they were made", async () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            startHistory(group);
            await otherOperation(group, "Add Row");
            await operation(() => insertInlineImage(group));
            await otherOperation(group, "Add Column");

            await history.undo();
            // Sanity check: the newest, the other operation, went first and left the picture.
            expect(group.getAttribute("data-other")).toBe("Add Row");
            expect(getInlineImages(group).length).toBe(1);

            await history.undo();
            expect(getInlineImages(group).length).toBe(0);
            expect(group.getAttribute("data-other")).toBe("Add Row");

            await history.undo();
            expect(group.hasAttribute("data-other")).toBe(false);
        });

        it("redoes an operation it undid", async () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
                { lang: "fr", content: "<p>b</p>" },
            ]);
            startHistory(group);
            insertInlineImage(group);
            await resize(group, "80%");
            await history.undo();
            // Sanity check: undone in both languages, and there is something to redo.
            expect(widthIn(editableFor(group, "fr"))).toBe("40%");
            expect(history.redoDescriptionL10nKey).toBe(
                kGenericUndoDescriptionL10nKey,
            );

            await history.redo();

            expect(widthIn(editableFor(group, "en"))).toBe("80%");
            expect(widthIn(editableFor(group, "fr"))).toBe("80%");
            // And it can be undone again.
            await history.undo();
            expect(widthIn(editableFor(group, "en"))).toBe("40%");
        });

        // A picture writes its real shape onto the wrapper when it loads, which can happen at
        // any time after the operation. That is not a change anyone made, so it must not stop
        // the undo, and redo must keep the shape.
        it("does not let a picture recording its shape stop the undo or lose it on redo", async () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            startHistory(group);
            insertInlineImage(group);
            await resize(group, "80%");
            const wrapper = getInlineImageInEditable(editableFor(group, "en"))!;
            // As the picture's load handler (wireUpImage) writes it.
            history.addToLatestTransaction(() =>
                wrapper.style.setProperty(kInlineImageAspectRatioVar, "3 / 2"),
            );

            await history.undo();
            expect(widthIn(editableFor(group, "en"))).toBe("40%");
            await history.redo();

            const redone = getInlineImageInEditable(editableFor(group, "en"))!;
            expect(redone.style.getPropertyValue(kInlineImageWidthVar)).toBe(
                "80%",
            );
            expect(
                redone.style.getPropertyValue(kInlineImageAspectRatioVar),
            ).toBe("3 / 2");
        });

        it("keeps the selection on the image after an undo", async () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            startHistory(group);
            const source = editableFor(group, "en");
            select(insertInlineImage(group));
            await resize(group, "60%");
            await resize(group, "80%");

            await history.undo();
            expect(widthIn(source)).toBe("60%");
            expect(
                getInlineImageInEditable(source)!.classList.contains(
                    kInlineImageSelectedClass,
                ),
            ).toBe(true);
        });

        // This is what a drag leaves when the move did not fit and the picture was put back.
        // A step here would do nothing visible, and the undo after it would take back a change
        // the person had stopped thinking about.
        it("makes no step for an operation that ended where it began", async () => {
            const group = makeTranslationGroup([
                { lang: "en", classes: "bloom-content1", content: "<p>a</p>" },
            ]);
            startHistory(group);
            await operation(() => insertInlineImage(group));

            await operation(() => {
                const wrapper = getInlineImage(group)!;
                wrapper.style.setProperty(kInlineImageWidthVar, "70%");
                wrapper.style.setProperty(kInlineImageWidthVar, "40%");
            });

            // The one step is still the insert.
            await history.undo();
            expect(getInlineImages(group).length).toBe(0);
            expect(history.undoDescriptionL10nKey).toBeUndefined();
        });
    });
});
