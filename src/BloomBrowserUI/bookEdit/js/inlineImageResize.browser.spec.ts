import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { userEvent } from "vitest/browser";
import inlineImagesCss from "../../../content/bookLayout/inlineImages.less?inline";
import inlineImageEditingCss from "../css/inlineImageEditing.less?inline";
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import {
    insertInlineImage,
    kInlineImageLeftClass,
    kInlineImageMiddleClass,
    kInlineImageRightClass,
    kInlineImageWidthVar,
    kInlineImageAspectRatioVar,
    syncInlineImagesFromEditable,
} from "./inlineImages";
import {
    cleanupInlineImageInteractions,
    kMaxInlineImageWidthPercent,
    selectInlineImage,
    setupInlineImageInteractions,
} from "./inlineImageInteractions";

// These drag a corner handle with the real mouse (Playwright's), in headless Edge, so that the
// sizes the resize reads come from real layout. jsdom lays nothing out, so there the resize
// has nothing to measure the block's height against.

// The layout rules of a text block that matter here, copied from basePage.less (which is too
// big to load in a test): the group stacks its editables in a column, and each editable grows
// to take a share of the group's empty space.
const kTextBlockCss = `
.bloom-translationGroup { display: flex; flex-direction: column; overflow: hidden; }
.bloom-editable { flex-grow: 1; height: auto; padding: 0; border: 0; margin: 0;
    font: 16px/20px monospace; }
.bloom-editable p { margin: 0; }
`;

let root: HTMLElement;

beforeEach(() => {
    root = document.createElement("div");
    document.body.appendChild(root);
    // Ending a resize asks for an overflow check on a one-second timer, which would fire after
    // the test has ended.
    vi.spyOn(
        OverflowChecker,
        "AdjustSizeOrMarkOverflowSoon",
    ).mockImplementation(() => {});
});

afterEach(() => {
    cleanupInlineImageInteractions();
    root.remove();
    vi.restoreAllMocks();
});

// Builds a page holding one text block of the given size, with one visible editable for each
// entry in `texts`, and returns the block.
function makeTextBlock(
    widthPx: number,
    heightPx: number,
    texts: string[],
): HTMLElement {
    const langs = ["en", "fr", "es"];
    root.innerHTML =
        `<style>${inlineImagesCss}\n${inlineImageEditingCss}\n${kTextBlockCss}</style>` +
        `<div class="bloom-page" data-page-id="resize-test-page" style="position:absolute; left:0; top:0; width:410px; height:880px;">` +
        `<div class="bloom-translationGroup" id="group" style="width:${widthPx}px; height:${heightPx}px;">` +
        texts
            .map(
                (text, i) =>
                    `<div class="bloom-editable bloom-content${i + 1} bloom-visibility-code-on" lang="${langs[i]}"><p>${text}</p></div>`,
            )
            .join("") +
        `</div></div>`;
    setupInlineImageInteractions(root);
    return root.querySelector("#group") as HTMLElement;
}

// A 300x200 picture. The handles are hidden on a placeholder, so the image needs a real one.
function makeThreeByTwoPictureUrl(): string {
    const canvas = document.createElement("canvas");
    canvas.width = 300;
    canvas.height = 200;
    const context = canvas.getContext("2d")!;
    context.fillStyle = "steelblue";
    context.fillRect(0, 0, 300, 200);
    return canvas.toDataURL("image/png");
}

// Puts a 3:2 inline image, 30% of the block's width and docked left, at the top of every
// editable in the block, selects the copy in the first one, and returns that copy once every
// copy's picture has loaded.
async function setUpImage(
    group: HTMLElement,
    dockClass: string = kInlineImageLeftClass,
): Promise<HTMLElement> {
    const wrapper = insertInlineImage(group);
    wrapper.classList.remove(kInlineImageRightClass);
    wrapper.classList.add(dockClass);
    wrapper.style.setProperty(kInlineImageWidthVar, "30%");
    wrapper.style.setProperty(kInlineImageAspectRatioVar, "3 / 2");
    imgOf(wrapper).setAttribute("src", makeThreeByTwoPictureUrl());
    const editable = wrapper.closest(".bloom-editable") as HTMLElement;
    syncInlineImagesFromEditable(editable);
    await Promise.all(
        Array.from(group.querySelectorAll("img")).map((img) => img.decode()),
    );
    selectInlineImage(wrapper);
    return wrapper;
}

function imgOf(wrapper: HTMLElement): HTMLImageElement {
    return wrapper.querySelector("img") as HTMLImageElement;
}

// Drags the image's lower right corner handle to a point near the right edge of the page,
// well below the image.
async function dragLowerRightCornerFarOut(wrapper: HTMLElement): Promise<void> {
    const handle = wrapper.querySelector(
        '[data-inline-image-corner="se"]',
    ) as HTMLElement;
    expect(handle, "expected a lower right resize handle").not.toBeNull();
    const page = root.querySelector(".bloom-page") as HTMLElement;
    // The handles show only while the pointer is over the page.
    await userEvent.hover(page);
    await userEvent.dragAndDrop(handle, page, {
        targetPosition: { x: 400, y: 600 },
    });
}

describe("resizing an inline image with a corner handle", () => {
    // BL-17078: on a landscape page the image could be stretched past the bottom of the page.
    it("stops where the image's bottom reaches the bottom of a wide, short block", async () => {
        const group = makeTextBlock(400, 120, [
            "Once upon a time, there was a sweet little girl.",
        ]);
        // Bloom's format button sits in the block's lower corner and hangs a few pixels below
        // it. It is not text, so it must not count as the block overflowing; if it did, the
        // block would look full before the resize began, and the resize would not be stopped.
        group.style.position = "relative";
        const formatButton = document.createElement("div");
        formatButton.className = "bloom-ui";
        formatButton.style.cssText =
            "position:absolute; left:3px; bottom:-5px; width:25px; height:25px;";
        group.appendChild(formatButton);
        expect(group.scrollHeight).toBeGreaterThan(group.clientHeight);
        const wrapper = await setUpImage(group);
        const blockBottom = group.getBoundingClientRect().bottom;
        // Sanity check: the image starts 120px wide and 80px tall at the top of the block.
        const before = imgOf(wrapper).getBoundingClientRect();
        expect(Math.round(before.width)).toBe(120);
        expect(Math.round(before.height)).toBe(80);
        expect(Math.round(before.top)).toBe(
            Math.round(group.getBoundingClientRect().top),
        );

        await dragLowerRightCornerFarOut(wrapper);

        const after = imgOf(wrapper).getBoundingClientRect();
        expect(after.bottom).toBeLessThanOrEqual(blockBottom);
        const editable = group.querySelector(".bloom-editable") as HTMLElement;
        const editableOverflow = () =>
            editable.getBoundingClientRect().bottom - blockBottom;
        expect(editableOverflow()).toBeLessThanOrEqual(1);
        // It stops close to the largest width that fits: the 120px block holds a 3:2 picture
        // up to 112px tall (with its 8px bottom margin), 168px wide, which is 42%; 1% more would
        // not fit.
        const widthPercent = parseFloat(
            wrapper.style.getPropertyValue(kInlineImageWidthVar),
        );
        expect(widthPercent).toBeGreaterThan(40);
        wrapper.style.setProperty(kInlineImageWidthVar, `${widthPercent + 1}%`);
        expect(editableOverflow()).toBeGreaterThan(1);
    });

    // A middle-docked picture has text above and below it but none beside it, so growing it
    // pushes the text after it down. It must stop before that text leaves the block.
    it("stops before pushing the text after a middle-docked picture out of the block", async () => {
        const group = makeTextBlock(400, 120, ["The end."]);
        const wrapper = await setUpImage(group, kInlineImageMiddleClass);
        const editable = group.querySelector(".bloom-editable") as HTMLElement;
        // A range around the words, because the paragraph's own box starts at the top of the
        // block whatever the picture does; only its lines move down below the picture.
        const text = document.createRange();
        text.selectNodeContents(editable.querySelector("p")!);
        // Sanity checks: the text starts below the 80px picture, and the block fits it.
        expect(text.getBoundingClientRect().top).toBeGreaterThanOrEqual(
            imgOf(wrapper).getBoundingClientRect().bottom,
        );
        // (The editable grows to fit its text, so it is the block, the group, that would
        // overflow.)
        expect(group.scrollHeight).toBeLessThanOrEqual(group.clientHeight);

        await dragLowerRightCornerFarOut(wrapper);

        const widthPercent = parseFloat(
            wrapper.style.getPropertyValue(kInlineImageWidthVar),
        );
        expect(widthPercent).toBeGreaterThan(30);
        expect(text.getBoundingClientRect().bottom).toBeLessThanOrEqual(
            group.getBoundingClientRect().bottom + 0.5,
        );
        expect(group.scrollHeight).toBeLessThanOrEqual(group.clientHeight + 1);
        // It stops close to the largest width that fits: 1% more would not fit.
        wrapper.style.setProperty(kInlineImageWidthVar, `${widthPercent + 1}%`);
        expect(group.scrollHeight).toBeGreaterThan(group.clientHeight + 1);
    });

    // BL-17078: on a portrait page the image stopped a little short of the edge of the text.
    it("reaches the full width of the text in a tall, narrow block", async () => {
        const group = makeTextBlock(400, 800, ["Once upon a time."]);
        const wrapper = await setUpImage(group);

        await dragLowerRightCornerFarOut(wrapper);

        expect(wrapper.style.getPropertyValue(kInlineImageWidthVar)).toBe(
            `${kMaxInlineImageWidthPercent}%`,
        );
        // The picture spans the text from edge to edge.
        const picture = imgOf(wrapper).getBoundingClientRect();
        const block = group.getBoundingClientRect();
        expect(Math.round(picture.left)).toBe(Math.round(block.left));
        expect(Math.round(picture.right)).toBe(Math.round(block.right));
    });
});
