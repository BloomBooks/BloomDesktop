import { describe, it, expect, beforeEach, afterAll } from "vitest";
import {
    getTestRoot,
    cleanTestRoot,
    removeTestRoot,
} from "../../utils/testHelper";
import {
    getInlineImage,
    getInlineImageInEditable,
    handleInlineImageChanged,
    inlineImageUndo,
    insertInlineImage,
    kInlineImageBottomClass,
    kInlineImageClass,
    kInlineImageIdAttr,
    kInlineImageLeftClass,
    kInlineImageMiddleClass,
    kInlineImageRightClass,
    kInlineImageSelectedClass,
    recordInlineImageUndoPoint,
    setInlineImageDock,
    syncInlineImagesFromEditable,
} from "./inlineImages";
import {
    buildInlineImageMenuItems,
    cleanupInlineImageInteractions,
    clampInlineImageOffset,
    clampInlineImageWidthPercent,
    computeInlineImageDock,
    computeInlineImageWidthPercent,
    deselectAllInlineImages,
    getInlineImageActionTarget,
    getInlineImageDock,
    getInlineImageHandleHorizontalSign,
    getInlineImageMenuItemsForClick,
    kInlineImageHandleClass,
    kInlineImageHandleFrameClass,
    kMaxInlineImageWidthPercent,
    kMinInlineImageWidthPercent,
    selectInlineImage,
    setupInlineImageInteractions,
} from "./inlineImageInteractions";

// jsdom reports every element's box as empty, so a real drag cannot be checked for where it
// put the image; the arithmetic that decides that lives in the pure functions these tests
// aim at. The DOM tests cover selection and which commands a right-click offers where.

// jsdom has no PointerEvent, but a listener registered for "pointerdown" fires for any event
// of that type, and MouseEvent carries the button and the coordinates this module reads.
function pointerEvent(
    type: string,
    clientX: number,
    clientY: number,
): MouseEvent {
    return new MouseEvent(type, {
        bubbles: true,
        cancelable: true,
        button: 0,
        clientX,
        clientY,
    });
}

// A block 300px wide and 200px tall at the origin, so that thirds land on round numbers
// (100 and 200) and the bottom fifth starts at y=160.
const kEditableBox = { left: 0, top: 0, width: 300, height: 200 };

let pageCounter = 0;

// Builds a page with one translation group, one editable per entry. Same shape as
// inlineImages.test.ts, since the undo layer keys on the page id.
function makeTranslationGroup(
    editables: { lang: string; classes?: string; content?: string }[],
    options?: { insideCanvasElement?: boolean },
): HTMLElement {
    const root = getTestRoot();
    const groupHtml =
        `<div class="bloom-translationGroup" id="group">` +
        editables
            .map(
                (e) =>
                    `<div class="bloom-editable ${e.classes ?? ""}" lang="${e.lang}">${
                        e.content ?? ""
                    }</div>`,
            )
            .join("") +
        `</div>`;
    root.innerHTML =
        `<div class="bloom-page" data-page-id="test-page-${++pageCounter}">` +
        (options?.insideCanvasElement
            ? `<div class="bloom-canvas-element">${groupHtml}</div>`
            : groupHtml) +
        `</div>`;
    return root.querySelector("#group") as HTMLElement;
}

const makeSimpleGroup = (options?: { insideCanvasElement?: boolean }) =>
    makeTranslationGroup(
        [
            {
                lang: "en",
                classes: "bloom-content1 bloom-visibility-code-on",
                content: "<p>English</p>",
            },
            { lang: "fr", content: "<p>French</p>" },
        ],
        options,
    );

const editableFor = (group: HTMLElement, lang: string) =>
    group.querySelector(`[lang="${lang}"]`) as HTMLElement;

describe("inlineImageInteractions", () => {
    beforeEach(() => {
        cleanTestRoot();
        cleanupInlineImageInteractions();
    });
    afterAll(removeTestRoot);

    describe("computeInlineImageDock", () => {
        it("switches dock at the thirds of the block's width", () => {
            const dockAt = (x: number) =>
                computeInlineImageDock({ x, y: 10 }, kEditableBox);
            expect(dockAt(1)).toBe(kInlineImageLeftClass);
            expect(dockAt(99)).toBe(kInlineImageLeftClass);
            // Exactly on a boundary belongs to the band, not to the side it came from.
            expect(dockAt(100)).toBe(kInlineImageMiddleClass);
            expect(dockAt(150)).toBe(kInlineImageMiddleClass);
            expect(dockAt(200)).toBe(kInlineImageMiddleClass);
            expect(dockAt(201)).toBe(kInlineImageRightClass);
            expect(dockAt(299)).toBe(kInlineImageRightClass);
        });

        it("keeps the dock of a position beyond the sides of the block", () => {
            expect(
                computeInlineImageDock({ x: -500, y: 10 }, kEditableBox),
            ).toBe(kInlineImageLeftClass);
            expect(
                computeInlineImageDock({ x: 900, y: 10 }, kEditableBox),
            ).toBe(kInlineImageRightClass);
        });

        it("docks at the bottom in the last fifth of the block, whatever the horizontal position", () => {
            // Sanity check: the same x above the bottom zone is not the bottom dock.
            expect(
                computeInlineImageDock({ x: 20, y: 159 }, kEditableBox),
            ).toBe(kInlineImageLeftClass);
            [20, 150, 280].forEach((x) => {
                expect(
                    computeInlineImageDock({ x, y: 160 }, kEditableBox),
                    `x=${x} at the top of the bottom zone`,
                ).toBe(kInlineImageBottomClass);
            });
        });

        it("docks at the bottom for a position below the block altogether", () => {
            expect(
                computeInlineImageDock({ x: 150, y: 5000 }, kEditableBox),
            ).toBe(kInlineImageBottomClass);
        });

        it("answers with the band for a block that has no width", () => {
            expect(
                computeInlineImageDock(
                    { x: 0, y: 0 },
                    { left: 0, top: 0, width: 0, height: 0 },
                ),
            ).toBe(kInlineImageBottomClass);
            expect(
                computeInlineImageDock(
                    { x: 0, y: -10 },
                    { left: 0, top: 0, width: 0, height: 0 },
                ),
            ).toBe(kInlineImageMiddleClass);
        });
    });

    describe("clampInlineImageOffset", () => {
        it("never goes above the top of the block", () => {
            expect(clampInlineImageOffset(-1)).toBe(0);
            expect(clampInlineImageOffset(-9999)).toBe(0);
            expect(clampInlineImageOffset(0)).toBe(0);
        });

        it("rounds to whole pixels", () => {
            expect(clampInlineImageOffset(12.4)).toBe(12);
            expect(clampInlineImageOffset(12.6)).toBe(13);
        });

        it("stops at the maximum when there is one", () => {
            expect(clampInlineImageOffset(500, 200)).toBe(200);
            expect(clampInlineImageOffset(150, 200)).toBe(150);
        });

        it("treats a maximum of zero or less as no maximum", () => {
            expect(clampInlineImageOffset(150, 0)).toBe(150);
            expect(clampInlineImageOffset(150, -5)).toBe(150);
        });
    });

    describe("width clamping", () => {
        it("keeps a width inside the usable range", () => {
            expect(clampInlineImageWidthPercent(0)).toBe(
                kMinInlineImageWidthPercent,
            );
            expect(clampInlineImageWidthPercent(-40)).toBe(
                kMinInlineImageWidthPercent,
            );
            expect(clampInlineImageWidthPercent(1000)).toBe(
                kMaxInlineImageWidthPercent,
            );
            expect(clampInlineImageWidthPercent(42.5)).toBe(42.5);
        });

        it("turns a corner drag into a percentage of the block's width", () => {
            // 120px of a 300px block is 40%; dragging an east corner 30px right adds 10%.
            expect(computeInlineImageWidthPercent(120, 30, 1, 300)).toBe(50);
            // The same movement on a west corner is inward, so it shrinks.
            expect(computeInlineImageWidthPercent(120, 30, -1, 300)).toBe(30);
            // Dragging a west corner outward (leftward) grows it.
            expect(computeInlineImageWidthPercent(120, -30, -1, 300)).toBe(50);
        });

        it("rounds a width to one decimal place", () => {
            // 121px of 300 is 40.333...%
            expect(computeInlineImageWidthPercent(121, 0, 1, 300)).toBe(40.3);
        });

        it("clamps a drag that would make the image unusably wide or narrow", () => {
            expect(computeInlineImageWidthPercent(120, 9999, 1, 300)).toBe(
                kMaxInlineImageWidthPercent,
            );
            expect(computeInlineImageWidthPercent(120, 9999, -1, 300)).toBe(
                kMinInlineImageWidthPercent,
            );
        });

        it("grows on an outward drag for every corner", () => {
            expect(getInlineImageHandleHorizontalSign("ne")).toBe(1);
            expect(getInlineImageHandleHorizontalSign("se")).toBe(1);
            expect(getInlineImageHandleHorizontalSign("nw")).toBe(-1);
            expect(getInlineImageHandleHorizontalSign("sw")).toBe(-1);
        });
    });

    describe("getInlineImageActionTarget", () => {
        it("offers adding an image in an eligible empty block", () => {
            const group = makeSimpleGroup();
            const editable = editableFor(group, "en");
            const target = getInlineImageActionTarget(
                editable.querySelector("p") as HTMLElement,
            );
            expect(target.kind).toBe("add");
            if (target.kind !== "add") return;
            expect(target.translationGroup).toBe(group);
            expect(target.editable).toBe(editable);
        });

        it("offers nothing for a block inside a canvas element", () => {
            const group = makeSimpleGroup({ insideCanvasElement: true });
            // Sanity check: the same block outside a canvas element would be eligible.
            expect(
                getInlineImageActionTarget(
                    editableFor(group, "en").querySelector("p") as HTMLElement,
                ).kind,
            ).toBe("none");
        });

        it("offers nothing for a bloom-editable that is not a child of a translation group", () => {
            const root = getTestRoot();
            root.innerHTML = `<div class="bloom-page"><div class="bloom-editable"><p>loose</p></div></div>`;
            expect(
                getInlineImageActionTarget(
                    root.querySelector("p") as HTMLElement,
                ).kind,
            ).toBe("none");
        });

        it("offers nothing outside any block", () => {
            const group = makeSimpleGroup();
            expect(getInlineImageActionTarget(group).kind).toBe("none");
            expect(getInlineImageActionTarget(undefined).kind).toBe("none");
        });

        it("offers the image's own commands when the wrapper is pointed at", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const target = getInlineImageActionTarget(
                wrapper.querySelector("img") as HTMLElement,
            );
            expect(target.kind).toBe("existing");
            if (target.kind !== "existing") return;
            expect(target.wrapper).toBe(wrapper);
            expect(target.editable).toBe(editableFor(group, "en"));
        });

        it("still offers adding in the text of a block whose group already has an image", () => {
            const group = makeSimpleGroup();
            insertInlineImage(group);
            // There is no limit on inline images per group; each add appends a new one.
            expect(
                getInlineImageActionTarget(
                    editableFor(group, "en").querySelector("p") as HTMLElement,
                ).kind,
            ).toBe("add");
            // ...from a sibling language's block too.
            expect(
                getInlineImageActionTarget(
                    editableFor(group, "fr").querySelector("p") as HTMLElement,
                ).kind,
            ).toBe("add");
        });
    });

    describe("buildInlineImageMenuItems", () => {
        it("offers only Add Image where there is no image", () => {
            const group = makeSimpleGroup();
            const items = buildInlineImageMenuItems(
                getInlineImageActionTarget(
                    editableFor(group, "en").querySelector("p") as HTMLElement,
                ),
            );
            expect(items.map((i) => i.l10nId)).toEqual([
                "EditTab.InlineImage.AddImage",
            ]);
        });

        it("offers change, credits and remove for an existing image", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const items = buildInlineImageMenuItems(
                getInlineImageActionTarget(wrapper),
            );
            expect(items.map((i) => i.l10nId)).toEqual([
                "EditTab.Image.ChangeImage",
                "EditTab.Image.EditMetadataOverlay",
                "EditTab.InlineImage.RemoveImage",
            ]);
            // A new inline image holds a placeholder, so there are no credits to edit yet.
            expect(items[1].disabled).toBe(true);
        });

        it("enables the credits command once a real picture is in place", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            wrapper.querySelector("img")!.setAttribute("src", "flower.jpg");
            const items = buildInlineImageMenuItems(
                getInlineImageActionTarget(wrapper),
            );
            expect(items[1].l10nId).toBe("EditTab.Image.EditMetadataOverlay");
            expect(items[1].disabled).toBe(false);
        });

        it("offers nothing where there is nothing to act on", () => {
            expect(buildInlineImageMenuItems({ kind: "none" })).toEqual([]);
        });

        it("selects the image a right-click landed on, so its commands have a subject", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            // Sanity check.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                false,
            );

            const items = getInlineImageMenuItemsForClick(
                wrapper.querySelector("img") as HTMLElement,
            );

            expect(items.length).toBe(3);
            // The undo layer's gate is the selection, so this is not merely cosmetic.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
        });

        it("selects nothing for a click that offers no commands", () => {
            const group = makeSimpleGroup();
            insertInlineImage(group);
            // A click outside any editable (here, the group itself) offers nothing.
            const items = getInlineImageMenuItemsForClick(group);
            expect(items).toEqual([]);
            expect(
                document.querySelector("." + kInlineImageSelectedClass),
            ).toBeNull();
        });

        it("Remove Image removes just the clicked image, in every language", () => {
            const group = makeSimpleGroup();
            const first = insertInlineImage(group);
            const second = insertInlineImage(group);
            const firstId = first.getAttribute(kInlineImageIdAttr);
            const secondId = second.getAttribute(kInlineImageIdAttr);
            // Sanity: two distinct images, each present in both languages.
            expect(firstId).toBeTruthy();
            expect(secondId).toBeTruthy();
            expect(firstId).not.toBe(secondId);
            expect(
                document.querySelectorAll(
                    `[${kInlineImageIdAttr}="${firstId}"]`,
                ).length,
            ).toBe(2);

            const items = buildInlineImageMenuItems(
                getInlineImageActionTarget(first),
            );
            const remove = items.find(
                (i) => i.l10nId === "EditTab.InlineImage.RemoveImage",
            );
            expect(remove, "expected a Remove Image item").toBeTruthy();
            // Actually invoke the command (a gap a real runtime break slipped through once).
            (remove!.onClick as () => void)();

            expect(
                document.querySelectorAll(
                    `[${kInlineImageIdAttr}="${firstId}"]`,
                ).length,
            ).toBe(0);
            // The other image survives in both languages.
            expect(
                document.querySelectorAll(
                    `[${kInlineImageIdAttr}="${secondId}"]`,
                ).length,
            ).toBe(2);
        });
    });

    // inlineImages.ts replaces wrappers behind our back in two cases, and tells us about each
    // with an event, because in both the selection (which is the inline-image undo layer's
    // gate) needs re-asserting and the handles live on an element that may be gone.
    describe("reacting to inlineImages.ts", () => {
        it("puts the handles back on the wrapper an undo restored", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            selectInlineImage(wrapper);
            // A dock change, recorded so that undo has something to put back.
            recordInlineImageUndoPoint(group);
            setInlineImageDock(wrapper, kInlineImageBottomClass);
            syncInlineImagesFromEditable(editableFor(group, "en"));

            expect(inlineImageUndo()).toBe(true);

            // Undo rebuilds the wrapper from serialized markup, so this is a new element...
            const restored = getInlineImageInEditable(editableFor(group, "en"));
            expect(restored, "expected a restored wrapper").not.toBeNull();
            expect(restored).not.toBe(wrapper);
            expect(getInlineImageDock(restored!)).toBe(kInlineImageRightClass);
            // ...which inlineImages.ts marks as selected, and which therefore needs handles:
            // serialized markup cannot carry them, since they are bloom-ui.
            expect(
                restored!.classList.contains(kInlineImageSelectedClass),
            ).toBe(true);
            expect(
                restored!.querySelector("." + kInlineImageHandleFrameClass),
                "expected the handles to be rebuilt",
            ).not.toBeNull();
        });

        it("re-asserts the selection when a new picture arrives", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const img = wrapper.querySelector("img") as HTMLElement;
            img.setAttribute("src", "flower.jpg");
            // The trip out to the image chooser can leave the focus back in the text, which
            // is what dropping the selection looks like.
            deselectAllInlineImages(document);

            handleInlineImageChanged(img);

            // Without this the change (and the insert that led to it) would not be undoable.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
            expect(
                wrapper.querySelector("." + kInlineImageHandleFrameClass),
            ).not.toBeNull();
        });
    });

    describe("selection", () => {
        it("marks the wrapper and gives it four resize handles", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            // Sanity check: nothing is selected until we say so.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                false,
            );

            selectInlineImage(wrapper);

            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
            const frame = wrapper.querySelector(
                "." + kInlineImageHandleFrameClass,
            ) as HTMLElement;
            expect(frame, "expected a handle frame").not.toBeNull();
            // Everything the interaction layer adds inside the wrapper has to be bloom-ui, or
            // it would be saved and replicated to the other languages.
            expect(frame.classList.contains("bloom-ui")).toBe(true);
            expect(
                frame.querySelectorAll("." + kInlineImageHandleClass).length,
            ).toBe(4);
        });

        it("does not add a second set of handles when selected again", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            selectInlineImage(wrapper);
            selectInlineImage(wrapper);
            expect(
                wrapper.querySelectorAll("." + kInlineImageHandleFrameClass)
                    .length,
            ).toBe(1);
        });

        it("selects only one image at a time", () => {
            const group = makeSimpleGroup();
            insertInlineImage(group);
            const english = getInlineImageInEditable(
                editableFor(group, "en"),
            ) as HTMLElement;
            const french = getInlineImageInEditable(
                editableFor(group, "fr"),
            ) as HTMLElement;
            selectInlineImage(english);
            expect(english.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );

            selectInlineImage(french);

            expect(english.classList.contains(kInlineImageSelectedClass)).toBe(
                false,
            );
            expect(
                english.querySelector("." + kInlineImageHandleFrameClass),
            ).toBeNull();
            expect(french.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
        });

        it("takes the marker and the handles off again on deselect", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            selectInlineImage(wrapper);

            deselectAllInlineImages(document);

            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                false,
            );
            expect(
                wrapper.querySelector("." + kInlineImageHandleFrameClass),
            ).toBeNull();
        });

        it("clears the selection for the page-save path", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            selectInlineImage(wrapper);
            document.body.classList.add("bloom-inlineImage-dragging");

            cleanupInlineImageInteractions();

            // The class is on the wrapper, which IS saved, so this is the one that matters.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                false,
            );
            expect(
                wrapper.querySelector("." + kInlineImageHandleFrameClass),
            ).toBeNull();
            expect(
                document.body.classList.contains("bloom-inlineImage-dragging"),
            ).toBe(false);
        });
    });

    describe("getInlineImageDock", () => {
        it("reads the dock a wrapper is in", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            // A new inline image is docked right.
            expect(getInlineImageDock(wrapper)).toBe(kInlineImageRightClass);
            wrapper.classList.remove(kInlineImageRightClass);
            wrapper.classList.add(kInlineImageBottomClass);
            expect(getInlineImageDock(wrapper)).toBe(kInlineImageBottomClass);
        });

        it("falls back to the dock a new image gets when the markup has none", () => {
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            wrapper.classList.remove(kInlineImageRightClass);
            expect(getInlineImageDock(wrapper)).toBe(kInlineImageRightClass);
        });
    });

    // A drag touches only the local wrapper while it is in progress and stamps the result onto
    // the other languages once, at the end -- otherwise every mouse move would rewrite every
    // language's copy. These drive the module's own listeners through a whole gesture. Where
    // the drag ends up is not the point (with every box empty, jsdom's answer to "which third
    // is this?" is always the band); how many times the other language's copy got rewritten
    // is, and each sync replaces that copy wholesale, so the replacements can be counted.
    describe("a whole drag gesture", () => {
        // Counts how many times the given editable's inline image has been replaced since the
        // last call. takeRecords is synchronous, so this needs no waiting.
        function makeWrapperReplacementCounter(editable: HTMLElement): {
            count: () => number;
            stop: () => void;
        } {
            const observer = new MutationObserver(() => {
                // Nothing to do on delivery; the records are collected by count() below.
            });
            observer.observe(editable, { childList: true });
            return {
                count: () =>
                    observer
                        .takeRecords()
                        .filter((record) =>
                            Array.from(record.removedNodes).some((node) =>
                                (node as HTMLElement).classList?.contains(
                                    kInlineImageClass,
                                ),
                            ),
                        ).length,
                stop: () => observer.disconnect(),
            };
        }

        it("rewrites the other languages' copies once, at the end", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const img = wrapper.querySelector("img") as HTMLElement;
            const frenchEditable = editableFor(group, "fr");
            const french = makeWrapperReplacementCounter(frenchEditable);

            img.dispatchEvent(pointerEvent("pointerdown", 200, 100));
            // Pressing on the image selects it, and changes nothing else.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
            expect(french.count()).toBe(0);

            // Well past the click threshold, and repeatedly.
            document.dispatchEvent(pointerEvent("pointermove", 160, 80));
            document.dispatchEvent(pointerEvent("pointermove", 120, 60));
            document.dispatchEvent(pointerEvent("pointermove", 100, 50));
            // Sanity check: the drag really did move the image...
            expect(getInlineImageDock(wrapper)).toBe(kInlineImageMiddleClass);
            // ...but only in the block being dragged in. A live preview is local.
            expect(french.count()).toBe(0);

            document.dispatchEvent(pointerEvent("pointerup", 100, 50));

            // At least one: since the sync matches copies up by id and keeps the cluster in
            // order, it may legitimately rewrite a sibling's wrapper more than once in a single
            // pass, so the exact count is not a proxy for "how many syncs". What matters is
            // that it happened here and not above.
            expect(french.count()).toBeGreaterThan(0);
            const frenchWrapper = getInlineImageInEditable(frenchEditable);
            expect(
                frenchWrapper,
                "expected French to still have a copy",
            ).not.toBeNull();
            expect(getInlineImageDock(frenchWrapper!)).toBe(
                kInlineImageMiddleClass,
            );
            // The copy the user dragged is still the canonical one.
            expect(getInlineImage(group)).toBe(wrapper);
            french.stop();
        });

        it("changes nothing for a press that never became a drag", () => {
            setupInlineImageInteractions(getTestRoot());
            const group = makeSimpleGroup();
            const wrapper = insertInlineImage(group);
            const img = wrapper.querySelector("img") as HTMLElement;
            const french = makeWrapperReplacementCounter(
                editableFor(group, "fr"),
            );

            img.dispatchEvent(pointerEvent("pointerdown", 200, 100));
            // A click wobbles by a pixel or two; that is not a drag.
            document.dispatchEvent(pointerEvent("pointermove", 201, 101));
            document.dispatchEvent(pointerEvent("pointerup", 201, 101));

            expect(french.count()).toBe(0);
            expect(getInlineImageDock(wrapper)).toBe(kInlineImageRightClass);
            // It is still a click, so the image ends up selected.
            expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
                true,
            );
            french.stop();
        });
    });
});
