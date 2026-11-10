import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { userEvent } from "vitest/browser";
import inlineImagesCss from "../../../content/bookLayout/inlineImages.less?inline";
import inlineImageEditingCss from "../css/inlineImageEditing.less?inline";
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import {
    getInlineImageInEditable,
    insertInlineImage,
    kInlineImageLeftClass,
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
async function setUpImage(group: HTMLElement): Promise<HTMLElement> {
    const wrapper = insertInlineImage(group);
    wrapper.classList.remove(kInlineImageRightClass);
    wrapper.classList.add(kInlineImageLeftClass);
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
    // BL-17078: on a landscape page the image could be stretched past the bottom of the page,
    // while on a portrait page it stopped at the limit on its width.
    it("stops where the image's bottom reaches the bottom of a wide, short block", async () => {
        const group = makeTextBlock(400, 120, [
            "Once upon a time, there was a sweet little girl.",
        ]);
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
        // The picture's 8px (0.5em) bottom margin has to fit in the block too, so a 3:2
        // image can be 112px tall and 168px wide: 42%.
        expect(wrapper.style.getPropertyValue(kInlineImageWidthVar)).toBe(
            "42%",
        );
        expect(group.scrollHeight).toBeLessThanOrEqual(group.clientHeight + 1);
    });

    it("still stops at the width limit in a tall, narrow block", async () => {
        const group = makeTextBlock(400, 800, ["Once upon a time."]);
        const wrapper = await setUpImage(group);

        await dragLowerRightCornerFarOut(wrapper);

        expect(wrapper.style.getPropertyValue(kInlineImageWidthVar)).toBe(
            `${kMaxInlineImageWidthPercent}%`,
        );
    });

    // In a block that shows two languages, the editables share the block's empty space, and
    // only the first language shows the image. The image may take all of the empty space, the
    // second language's share too, but must leave the second language room for its text.
    it("grows into the whole block, less the other language's text, in a block that shows two languages", async () => {
        const group = makeTextBlock(400, 240, ["One.", "Un."]);
        const wrapper = await setUpImage(group);
        const [english, french] = Array.from(
            group.querySelectorAll(".bloom-editable"),
        ) as HTMLElement[];
        const frenchText = french.querySelector("p") as HTMLElement;
        const frenchLineHeight = frenchText.getBoundingClientRect().height;
        const englishBottomBefore = english.getBoundingClientRect().bottom;
        // Sanity checks: only the English copy of the image shows, and each editable is taller
        // than its content, because each has a share of the block's empty space.
        expect(
            getInlineImageInEditable(french)!.getBoundingClientRect().height,
        ).toBe(0);
        expect(Math.round(imgOf(wrapper).getBoundingClientRect().height)).toBe(
            80,
        );
        expect(englishBottomBefore).toBeGreaterThan(
            imgOf(wrapper).getBoundingClientRect().bottom + 20,
        );
        expect(Math.round(frenchLineHeight)).toBe(20);

        await dragLowerRightCornerFarOut(wrapper);

        // The image now goes below where the English editable ended, taking the French
        // editable's empty space too.
        const imageAfter = imgOf(wrapper).getBoundingClientRect();
        expect(imageAfter.bottom).toBeGreaterThan(englishBottomBefore + 20);
        // 240px less the 20px French line and the picture's 8px bottom margin leaves 212px
        // for the image, which makes a 3:2 image 318px wide: 79.5%.
        expect(wrapper.style.getPropertyValue(kInlineImageWidthVar)).toBe(
            "79.5%",
        );
        // The French text is still inside the block.
        expect(frenchText.getBoundingClientRect().bottom).toBeLessThanOrEqual(
            group.getBoundingClientRect().bottom + 0.5,
        );
        expect(group.scrollHeight).toBeLessThanOrEqual(group.clientHeight + 1);
    });
});
