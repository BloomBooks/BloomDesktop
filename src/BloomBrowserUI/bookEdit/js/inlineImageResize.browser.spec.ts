import {
    describe,
    it,
    expect,
    beforeAll,
    afterAll,
    beforeEach,
    afterEach,
    vi,
} from "vitest";
import { cdp, userEvent } from "vitest/browser";

// Tests must not reach the network, and there is no Bloom here to answer.
// setupInlineImageInteractions asks whether AI image editing is on; the mock answers that it is
// off. The toolbar a selected picture shows asks Bloom for more (feature status, the collection's
// language names), and it sits outside the text block, so these layout tests show none. In
// browser mode a mocked module must still provide every name that some module imports from it,
// including ones these tests never call.
vi.mock("../../react_components/featureStatus", () => ({
    getFeatureStatusAsync: () => Promise.resolve(undefined),
    useGetFeatureStatus: () => undefined,
    useGetFeatureAvailabilityMessage: () => "",
    openBloomSubscriptionSettings: () => {},
}));
vi.mock("./canvasElementManager/CanvasElementContextControls", () => ({
    CanvasElementContextControls: () => null,
    renderCanvasElementContextControls: () => {},
}));
import { kInlineImageTestCss } from "./inlineImageTestCss";
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import { getTestRoot, removeTestRoot } from "../../utils/testHelper";
import {
    insertInlineImage,
    kInlineImageBottomClass,
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
// to take a share of the group's empty space. The small min-height matters: it lets an editable
// be shorter than its text, so a block with too much text makes its editable scroll, which is
// what the drag's overflow check measures. The editable hides its overflow, as Bloom's does,
// so its scrollWidth counts a float's margin that reaches past its side.
const kTextBlockCss = `
.bloom-translationGroup { display: flex; flex-direction: column; overflow: hidden; }
.bloom-editable { flex-grow: 1; height: auto; min-height: 24px; padding: 0; border: 0; margin: 0;
    overflow: hidden; font: 16px/20px monospace; }
.bloom-editable p { margin: 0; }
`;

type Corner = "nw" | "ne" | "sw" | "se";
interface IPoint {
    x: number;
    y: number;
}

let root: HTMLElement;
let style: HTMLStyleElement;

beforeAll(() => {
    style = document.createElement("style");
    style.textContent = `${kInlineImageTestCss}\n${kTextBlockCss}`;
    document.head.appendChild(style);
});

afterAll(() => {
    style.remove();
});

beforeEach(() => {
    root = getTestRoot();
    // Ending a resize asks for an overflow check on a one-second timer, which would fire after
    // the test has ended.
    vi.spyOn(
        OverflowChecker,
        "AdjustSizeOrMarkOverflowSoon",
    ).mockImplementation(() => {});
});

afterEach(() => {
    cleanupInlineImageInteractions();
    removeTestRoot();
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

// A tall block of the given width, centered on the 400px the drags reach across, so that a
// narrow one has empty page on both sides of it and a corner on either side can be dragged
// further than the full width of the text.
function makeBlockInMiddleOfPage(
    widthPx: number,
    texts: string[],
): HTMLElement {
    const group = makeTextBlock(widthPx, 800, texts);
    group.style.marginLeft = `${(400 - widthPx) / 2}px`;
    return group;
}

let threeByTwoPictureUrl: string | undefined;

// A 300x200 picture. The handles are hidden on a placeholder, so the image needs a real one.
function makeThreeByTwoPictureUrl(): string {
    if (threeByTwoPictureUrl) return threeByTwoPictureUrl;
    const canvas = document.createElement("canvas");
    canvas.width = 300;
    canvas.height = 200;
    const context = canvas.getContext("2d")!;
    context.fillStyle = "steelblue";
    context.fillRect(0, 0, 300, 200);
    threeByTwoPictureUrl = canvas.toDataURL("image/png");
    return threeByTwoPictureUrl;
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

// The tests of a block that shows one language use overflowPx; the tests of two or three
// languages use totalScrollOverflowPx, because Bloom's check adds up the editables, and each
// editable of such a block can hide less than a pixel while together they hide more.

// How far the block's text runs past its bottom, in pixels; zero or less when it all shows. It
// covers both ways a block overflows: an editable that has to scroll, and an editable that
// grows below the block.
function overflowPx(group: HTMLElement): number {
    const blockBottom = group.getBoundingClientRect().bottom;
    const editables = Array.from(
        group.querySelectorAll(".bloom-editable"),
    ) as HTMLElement[];
    return Math.max(
        ...editables.map((editable) =>
            Math.max(
                editable.scrollHeight - editable.clientHeight,
                editable.getBoundingClientRect().bottom - blockBottom,
            ),
        ),
    );
}

// How much text all the block's editables together hide by scrolling, in pixels.
function totalScrollOverflowPx(group: HTMLElement): number {
    return (
        Array.from(group.querySelectorAll(".bloom-editable")) as HTMLElement[]
    ).reduce(
        (total, editable) =>
            total + editable.scrollHeight - editable.clientHeight,
        0,
    );
}

function imgOf(wrapper: HTMLElement): HTMLImageElement {
    return wrapper.querySelector("img") as HTMLImageElement;
}

function pageOf(): HTMLElement {
    return root.querySelector(".bloom-page") as HTMLElement;
}

function editableOf(group: HTMLElement): HTMLElement {
    return group.querySelector(".bloom-editable") as HTMLElement;
}

function centerOf(element: Element): IPoint {
    const box = element.getBoundingClientRect();
    return { x: box.left + box.width / 2, y: box.top + box.height / 2 };
}

function widthPercentOf(wrapper: HTMLElement): number {
    return parseFloat(wrapper.style.getPropertyValue(kInlineImageWidthVar));
}

// A range around the words of the first editable's paragraph. The paragraph's own box starts at
// the top of the block whatever the picture does; only its lines move down below the picture.
function rangeAroundTextOf(group: HTMLElement): Range {
    const text = document.createRange();
    text.selectNodeContents(editableOf(group).querySelector("p")!);
    return text;
}

// Drags one of the image's corner handles far outward in one movement: to the right edge of the
// page for a right-hand corner, to its left edge for a left-hand one, and well below the image.
async function dragCornerFarOut(
    wrapper: HTMLElement,
    corner: Corner,
): Promise<void> {
    const handle = wrapper.querySelector(
        `[data-inline-image-corner="${corner}"]`,
    ) as HTMLElement;
    expect(handle, `expected a ${corner} resize handle`).not.toBeNull();
    // The handles show only while the pointer is over the page.
    await userEvent.hover(pageOf());
    await userEvent.dragAndDrop(handle, pageOf(), {
        targetPosition: { x: corner.endsWith("e") ? 400 : 0, y: 600 },
    });
}

// Drags the picture in one movement to a point of the page.
async function dragPictureTo(wrapper: HTMLElement, to: IPoint): Promise<void> {
    await userEvent.hover(pageOf());
    await userEvent.dragAndDrop(imgOf(wrapper), pageOf(), {
        targetPosition: to,
    });
}

// Sends one event of the real mouse, at a point in this test page's own coordinates.
// userEvent.dragAndDrop makes a whole drag in one movement, so a drag that has to be watched part
// way through, or made slowly, is built from these. The browser takes mouse coordinates in the
// outer page, which shows the test page in a scaled iframe.
async function sendMouse(
    type: "mouseMoved" | "mousePressed" | "mouseReleased",
    point: IPoint,
    buttonIsDown: boolean,
): Promise<void> {
    const frame = window.frameElement!.getBoundingClientRect();
    const scale = frame.width / window.innerWidth;
    await cdp().send("Input.dispatchMouseEvent", {
        type,
        x: frame.left + point.x * scale,
        y: frame.top + point.y * scale,
        button: type === "mouseMoved" && !buttonIsDown ? "none" : "left",
        buttons: buttonIsDown ? 1 : 0,
        clickCount: type === "mouseMoved" ? 0 : 1,
    });
}

function nextFrame(): Promise<void> {
    return new Promise((resolve) => requestAnimationFrame(() => resolve()));
}

// Drags with the real mouse from one point to another in `steps` equal movements, calling
// `afterEachMove` once the page has been laid out after each one.
async function dragSlowly(
    from: IPoint,
    to: IPoint,
    steps: number,
    afterEachMove: () => void = () => {},
): Promise<void> {
    await sendMouse("mouseMoved", from, false);
    await sendMouse("mousePressed", from, true);
    for (let step = 1; step <= steps; step++) {
        await sendMouse(
            "mouseMoved",
            {
                x: from.x + ((to.x - from.x) * step) / steps,
                y: from.y + ((to.y - from.y) * step) / steps,
            },
            true,
        );
        await nextFrame();
        afterEachMove();
    }
    await sendMouse("mouseReleased", to, false);
    await nextFrame();
}

// Checks that the block's text fits with the picture at its present width, and that it would
// not fit with the picture 1% wider: the resize stopped close to the largest width that fits.
function expectLargestWidthThatFits(
    wrapper: HTMLElement,
    overflow: () => number,
): void {
    const widthPercent = widthPercentOf(wrapper);
    expect(overflow()).toBeLessThanOrEqual(1);
    wrapper.style.setProperty(kInlineImageWidthVar, `${widthPercent + 1}%`);
    expect(overflow()).toBeGreaterThan(1);
}

// Moves a left-docked picture into the full-width band, runs `check`, and moves it back.
function whileInMiddleDock(wrapper: HTMLElement, check: () => void): void {
    expect(wrapper.classList.contains(kInlineImageLeftClass)).toBe(true);
    wrapper.classList.replace(kInlineImageLeftClass, kInlineImageMiddleClass);
    check();
    wrapper.classList.replace(kInlineImageMiddleClass, kInlineImageLeftClass);
}

// Checks that the picture spans the text from edge to edge and that Bloom would not mark the
// block as overflowing, which is what turns it red. `shown` is the element whose box is the part
// of the picture that shows: the img, or the wrapper when the picture has been cropped.
function expectFullWidthWithoutOverflow(
    group: HTMLElement,
    wrapper: HTMLElement,
    shown: Element = imgOf(wrapper),
): void {
    expect(wrapper.style.getPropertyValue(kInlineImageWidthVar)).toBe(
        `${kMaxInlineImageWidthPercent}%`,
    );
    const picture = shown.getBoundingClientRect();
    const block = group.getBoundingClientRect();
    expect(Math.round(picture.left)).toBe(Math.round(block.left));
    expect(Math.round(picture.right)).toBe(Math.round(block.right));
    // Nothing reaches past the side of the text, including the space a side-docked picture
    // keeps between itself and the text beside it. Bloom marks a block whose content is wider
    // than the block as overflowing.
    const editable = editableOf(group);
    expect(editable.scrollWidth).toBeLessThanOrEqual(editable.clientWidth);
    expect(OverflowChecker.IsOverflowingSelf(editable)).toBe(false);
}

// Checks that the picture is at the very bottom of the block: a side-docked picture keeps its
// 8px (0.5em) bottom margin inside the block.
function expectAtBottomOfBlock(group: HTMLElement, wrapper: HTMLElement): void {
    const blockBottom = group.getBoundingClientRect().bottom;
    const picture = imgOf(wrapper).getBoundingClientRect();
    expect(picture.bottom).toBeLessThanOrEqual(blockBottom);
    expect(picture.bottom).toBeGreaterThan(blockBottom - 12);
    expect(overflowPx(group)).toBeLessThanOrEqual(1);
}

// Checks that the picture's top never went up by more than a pixel from one move of a slow drag
// to the next.
function expectNeverMovedUp(tops: number[], label: string): void {
    tops.forEach((top, i) => {
        if (i > 0)
            expect(
                top,
                `${label} moved up on move ${i} of the drag: ${tops.map(Math.round).join(", ")}`,
            ).toBeGreaterThanOrEqual(tops[i - 1] - 1);
    });
}

// Where a drag to the bottom of the block lets go: just above the block's bottom edge, which is
// further down than the picture can go, so Bloom stops it at the lowest place where it fits.
// (A drop below the block means the dock after the text instead.) The page sits at the top left
// of the viewport, so this point serves both dragPictureTo, which takes page coordinates, and
// dragSlowly, which takes viewport ones.
function bottomOfBlock(group: HTMLElement): number {
    const page = pageOf().getBoundingClientRect();
    expect(page.top, "the page should be at the top of the viewport").toBe(0);
    expect(page.left, "the page should be at the left of the viewport").toBe(0);
    return group.getBoundingClientRect().bottom - 3;
}

// The picture on the left takes 88px with its bottom margin, with the English beside it, and
// the other languages take 20 lines between them, 400px: 488px, which fits in 496px. Moved to
// the full-width band, the picture pushes the English below it, 100px, and the block is 4px
// short.
const kSecondAndThirdLanguages = [
    {
        languages: "two",
        texts: [
            "Once upon a time.",
            Array(20).fill("Il était une fois.").join("<br>"),
        ],
    },
    {
        languages: "three",
        texts: [
            "Once upon a time.",
            Array(10).fill("Il était une fois.").join("<br>"),
            Array(10).fill("Érase una vez.").join("<br>"),
        ],
    },
];

describe("resizing and dragging an inline image", () => {
    // A middle-docked picture has text above and below it but none beside it, so growing it
    // pushes the text after it down. It must stop before that text leaves the block.
    it("stops before pushing the text after a middle-docked picture out of the block", async () => {
        const group = makeTextBlock(400, 120, ["The end."]);
        const wrapper = await setUpImage(group, kInlineImageMiddleClass);
        const text = rangeAroundTextOf(group);
        // Sanity checks: the text starts below the 80px picture, and the block fits it.
        expect(text.getBoundingClientRect().top).toBeGreaterThanOrEqual(
            imgOf(wrapper).getBoundingClientRect().bottom,
        );
        expect(overflowPx(group)).toBeLessThanOrEqual(0);

        await dragCornerFarOut(wrapper, "se");

        expect(widthPercentOf(wrapper)).toBeGreaterThan(30);
        expect(text.getBoundingClientRect().bottom).toBeLessThanOrEqual(
            group.getBoundingClientRect().bottom + 0.5,
        );
        expectLargestWidthThatFits(wrapper, () => overflowPx(group));
    });

    // A drag is undone when it pushes text out of the block. In a block that shows more than one
    // language, the editables share the block, and when one grows the others shrink, each in
    // proportion to its text. So with much more of the other languages than English, the English
    // hides only a fraction of a pixel and the other languages hide the rest. The measure that
    // undoes a drag adds up every language's editable, so a third language counts as the second
    // does.
    it.each(kSecondAndThirdLanguages)(
        "undoes a drag that pushes text out of a block that shows $languages languages",
        async ({ texts }) => {
            const group = makeTextBlock(400, 496, texts);
            const wrapper = await setUpImage(group);
            // Sanity checks: the block fits every language with the picture docked left, and
            // would not with the picture in the full-width band, where most of what it hides is
            // in the languages after the English.
            expect(totalScrollOverflowPx(group)).toBeLessThanOrEqual(0);
            whileInMiddleDock(wrapper, () => {
                expect(totalScrollOverflowPx(group)).toBeGreaterThan(2);
                const english = editableOf(group);
                expect(
                    english.scrollHeight - english.clientHeight,
                ).toBeLessThanOrEqual(1);
            });

            await dragPictureTo(wrapper, {
                x: 200,
                y: centerOf(imgOf(wrapper)).y,
            });

            expect(wrapper.classList.contains(kInlineImageLeftClass)).toBe(
                true,
            );
            expect(totalScrollOverflowPx(group)).toBeLessThanOrEqual(1);
        },
    );

    // A resize stops by the same measure, so it also stops before the text of the languages
    // after the English leaves the block.
    it.each(kSecondAndThirdLanguages)(
        "stops a resize before it pushes text out of a block that shows $languages languages",
        async ({ texts }) => {
            const group = makeTextBlock(400, 496, texts);
            const wrapper = await setUpImage(group);
            // Sanity check: the block fits every language.
            expect(totalScrollOverflowPx(group)).toBeLessThanOrEqual(0);

            await dragCornerFarOut(wrapper, "se");

            const widthPercent = widthPercentOf(wrapper);
            expect(widthPercent).toBeGreaterThan(30);
            expect(widthPercent).toBeLessThan(40);
            expectLargestWidthThatFits(wrapper, () =>
                totalScrollOverflowPx(group),
            );
        },
    );
});

describe("resizing an inline image from any corner, in any dock, quickly or slowly", () => {
    // BL-17078: on a landscape page the image could be stretched past the bottom of the page.
    // This is checked from each corner. A left-hand corner can only be dragged outward when the
    // picture is docked right, because a left-docked picture's left side is at the edge of the
    // page.
    it.each([
        { corner: "se", dockClass: kInlineImageLeftClass },
        { corner: "ne", dockClass: kInlineImageLeftClass },
        { corner: "sw", dockClass: kInlineImageRightClass },
        { corner: "nw", dockClass: kInlineImageRightClass },
    ] as { corner: Corner; dockClass: string }[])(
        "stops at the bottom of a wide, short block when the $corner corner is dragged",
        async ({ corner, dockClass }) => {
            const group = makeTextBlock(400, 120, [
                "Once upon a time, there was a sweet little girl.",
            ]);
            // Bloom's format button sits in the block's lower corner and hangs a few pixels
            // below it. It is not text, so it must not count as the block overflowing; if it
            // did, the block would look full before the resize began, and the resize would not
            // be stopped.
            group.style.position = "relative";
            const formatButton = document.createElement("div");
            formatButton.className = "bloom-ui";
            formatButton.style.cssText =
                "position:absolute; left:3px; bottom:-5px; width:25px; height:25px;";
            group.appendChild(formatButton);
            expect(group.scrollHeight).toBeGreaterThan(group.clientHeight);
            const wrapper = await setUpImage(group, dockClass);
            const blockBottom = group.getBoundingClientRect().bottom;
            // Sanity checks: the image starts 120px wide and 80px tall at the top of the block,
            // and the block fits it.
            const before = imgOf(wrapper).getBoundingClientRect();
            expect(Math.round(before.width)).toBe(120);
            expect(Math.round(before.height)).toBe(80);
            expect(Math.round(before.top)).toBe(
                Math.round(group.getBoundingClientRect().top),
            );
            expect(overflowPx(group)).toBeLessThanOrEqual(0);

            await dragCornerFarOut(wrapper, corner);

            // The overflow check allows a pixel for rounding (shouldRevertInlineImageMove), and
            // the block's height is measured in whole pixels, so the picture may end up to two
            // pixels past the bottom.
            expect(
                imgOf(wrapper).getBoundingClientRect().bottom,
            ).toBeLessThanOrEqual(blockBottom + 2);
            // Every corner stops at the same place: the 120px block holds a 3:2 picture up to
            // 112px tall (with its 8px bottom margin), 168px wide, which is 42%.
            const widthPercent = widthPercentOf(wrapper);
            expect(widthPercent).toBeGreaterThan(41);
            expect(widthPercent).toBeLessThan(43);
            expectLargestWidthThatFits(wrapper, () => overflowPx(group));
        },
    );

    // BL-17078: on a portrait page the image stopped a little short of the edge of the text. In
    // the block as wide as the page, the corner is dragged to the edge of the text; in the narrow
    // ones, past it.
    it.each([
        { widthPx: 400, dockClass: kInlineImageLeftClass, corner: "se" },
        { widthPx: 200, dockClass: kInlineImageRightClass, corner: "sw" },
        { widthPx: 200, dockClass: kInlineImageMiddleClass, corner: "se" },
    ] as { widthPx: number; corner: Corner; dockClass: string }[])(
        "reaches the full width of the text of a $widthPx px block, with nothing past its sides, in $dockClass",
        async ({ widthPx, dockClass, corner }) => {
            const group = makeBlockInMiddleOfPage(widthPx, [
                "Once upon a time.",
            ]);
            const wrapper = await setUpImage(group, dockClass);
            // Sanity check: the picture starts at 30% of the block.
            expect(
                Math.round(imgOf(wrapper).getBoundingClientRect().width),
            ).toBe(Math.round(widthPx * 0.3));

            await dragCornerFarOut(wrapper, corner);

            expectFullWidthWithoutOverflow(group, wrapper);
        },
    );

    // The text beside a side-docked picture gets narrower as the picture grows, so it takes more
    // lines. In a block that the text nearly fills, the text reaches the bottom of the block long
    // before the picture does.
    it.each([
        { dockClass: kInlineImageLeftClass, corner: "se" },
        { dockClass: kInlineImageRightClass, corner: "sw" },
    ] as { corner: Corner; dockClass: string }[])(
        "stops before the text beside a picture in $dockClass is pushed out of the block",
        async ({ dockClass, corner }) => {
            const group = makeTextBlock(400, 200, [
                Array(10).fill("The bird waits by the pool.").join(" "),
            ]);
            const wrapper = await setUpImage(group, dockClass);
            const blockBottom = group.getBoundingClientRect().bottom;
            const text = rangeAroundTextOf(group);
            // Sanity checks: the block fits its text with the picture at 30%, with less than
            // two lines to spare, and would not fit it with the picture at 60%.
            expect(overflowPx(group)).toBeLessThanOrEqual(0);
            expect(text.getBoundingClientRect().bottom).toBeGreaterThan(
                blockBottom - 40,
            );
            wrapper.style.setProperty(kInlineImageWidthVar, "60%");
            expect(overflowPx(group)).toBeGreaterThan(1);
            wrapper.style.setProperty(kInlineImageWidthVar, "30%");

            await dragCornerFarOut(wrapper, corner);

            expect(widthPercentOf(wrapper)).toBeGreaterThan(30);
            expect(text.getBoundingClientRect().bottom).toBeLessThanOrEqual(
                blockBottom + 0.5,
            );
            // The text stopped it, not the picture's own bottom, which is well above the
            // bottom of the block.
            expect(imgOf(wrapper).getBoundingClientRect().bottom).toBeLessThan(
                blockBottom - 20,
            );
            expectLargestWidthThatFits(wrapper, () => overflowPx(group));
        },
    );

    // One fast movement of the pointer jumps from a width that fits to one far too big. The
    // resize then looks for the largest width that fits, so that it ends where a slow drag,
    // which meets every width on the way, ends.
    it("ends at the same width whether the corner is dragged in one movement or slowly", async () => {
        const makeBlock = async () => {
            const group = makeTextBlock(400, 120, [
                "Once upon a time, there was a sweet little girl.",
            ]);
            return { group, wrapper: await setUpImage(group) };
        };
        const fast = await makeBlock();
        await dragCornerFarOut(fast.wrapper, "se");
        const fastPercent = widthPercentOf(fast.wrapper);

        const slow = await makeBlock();
        const handle = slow.wrapper.querySelector(
            '[data-inline-image-corner="se"]',
        ) as HTMLElement;
        // The handles show, and so have a place to press, only while the pointer is over the
        // page.
        await userEvent.hover(pageOf());
        expect(
            handle.getBoundingClientRect().width,
            "the lower right handle should be showing",
        ).toBeGreaterThan(0);
        const widthsOnTheWay = new Set<number>();
        await dragSlowly(centerOf(handle), { x: 400, y: 600 }, 40, () =>
            widthsOnTheWay.add(widthPercentOf(slow.wrapper)),
        );
        const slowPercent = widthPercentOf(slow.wrapper);

        // Sanity checks: the slow drag did pass through several widths, and it ended where the
        // block is full.
        expect(widthsOnTheWay.size).toBeGreaterThan(3);
        expect(slowPercent).toBeGreaterThan(41);
        expect(overflowPx(slow.group)).toBeLessThanOrEqual(1);
        expect(Math.abs(fastPercent - slowPercent)).toBeLessThan(0.5);
    });

    // A picture as wide as the text, cropped on one side and then uncropped all the way, is as
    // wide as the text again. Uncropping is limited to the same width as a resize.
    it("is as wide as the text again after its side is cropped and then uncropped all the way", async () => {
        const group = makeBlockInMiddleOfPage(200, ["Once upon a time."]);
        const wrapper = await setUpImage(group);
        await dragCornerFarOut(wrapper, "se");
        expectFullWidthWithoutOverflow(group, wrapper);
        const sideHandle = () =>
            wrapper.querySelector(
                '[data-inline-image-side="e"]',
            ) as HTMLElement;
        const block = group.getBoundingClientRect();

        // Crop the right side by 80px, which is 40% of the picture.
        await userEvent.hover(pageOf());
        await userEvent.dragAndDrop(sideHandle(), pageOf(), {
            targetPosition: {
                x: centerOf(sideHandle()).x - 80,
                y: centerOf(sideHandle()).y,
            },
        });
        // Sanity check: the picture is now cropped and narrower than the text.
        expect(widthPercentOf(wrapper)).toBeGreaterThan(55);
        expect(widthPercentOf(wrapper)).toBeLessThan(65);
        expect(
            Math.round(imgOf(wrapper).parentElement!.scrollWidth),
        ).toBeLessThanOrEqual(Math.round(block.width));

        // Uncrop it: drag the same handle far out to the right.
        await userEvent.dragAndDrop(sideHandle(), pageOf(), {
            targetPosition: { x: 400, y: centerOf(sideHandle()).y },
        });

        expectFullWidthWithoutOverflow(group, wrapper, wrapper);
    });
});

describe("dragging an inline image to the bottom of its block", () => {
    // BL-17078: dragged to the bottom of the block, a picture in each third of its width rests on
    // the bottom in that third's dock. A side dock has a bottom margin, which the drag has to keep
    // inside the block, or the move is undone.
    it.each([
        { x: 60, dockClass: kInlineImageLeftClass },
        { x: 200, dockClass: kInlineImageMiddleClass },
        { x: 340, dockClass: kInlineImageRightClass },
    ])(
        "leaves a picture dragged to the bottom of the block at x=$x at the bottom, in $dockClass",
        async ({ x, dockClass }) => {
            const group = makeTextBlock(400, 300, ["The end."]);
            const wrapper = await setUpImage(group, kInlineImageMiddleClass);
            // Sanity check: the picture starts at the top of the block, in the middle dock.
            expect(Math.round(imgOf(wrapper).getBoundingClientRect().top)).toBe(
                Math.round(group.getBoundingClientRect().top),
            );

            await dragPictureTo(wrapper, { x, y: bottomOfBlock(group) });

            expect(wrapper.classList.contains(dockClass)).toBe(true);
            expectAtBottomOfBlock(group, wrapper);
            // It is against that side of the block.
            const picture = imgOf(wrapper).getBoundingClientRect();
            const block = group.getBoundingClientRect();
            if (dockClass === kInlineImageLeftClass)
                expect(Math.round(picture.left)).toBe(Math.round(block.left));
            else if (dockClass === kInlineImageRightClass)
                expect(Math.round(picture.right)).toBe(Math.round(block.right));
        },
    );

    // From the bottom of the block, a drag sideways changes the dock and leaves the picture at
    // the bottom.
    it("moves a picture at the bottom of the block to the right side when it is dragged right", async () => {
        const group = makeTextBlock(400, 300, ["The end."]);
        const wrapper = await setUpImage(group, kInlineImageMiddleClass);
        await dragPictureTo(wrapper, { x: 200, y: bottomOfBlock(group) });
        // Sanity check: it is at the bottom, in the middle dock.
        expect(wrapper.classList.contains(kInlineImageMiddleClass)).toBe(true);
        expectAtBottomOfBlock(group, wrapper);

        await dragPictureTo(wrapper, {
            x: 340,
            y: centerOf(imgOf(wrapper)).y,
        });

        expect(wrapper.classList.contains(kInlineImageRightClass)).toBe(true);
        expect(Math.round(imgOf(wrapper).getBoundingClientRect().right)).toBe(
            Math.round(group.getBoundingClientRect().right),
        );
        expectAtBottomOfBlock(group, wrapper);
    });

    // BL-17078: the picture used to jump up to just below the text when it came near the bottom
    // of the block. A slow drag shows every position on the way, so it shows a jump wherever it
    // happens.
    it("never moves the picture up while it is dragged slowly down to the bottom", async () => {
        const group = makeTextBlock(400, 300, ["The end."]);
        const wrapper = await setUpImage(group, kInlineImageMiddleClass);
        const start = centerOf(imgOf(wrapper));
        const tops: number[] = [imgOf(wrapper).getBoundingClientRect().top];

        // A few pixels at a time, to the bottom of the block.
        await dragSlowly(
            start,
            { x: start.x, y: bottomOfBlock(group) },
            56,
            () => tops.push(imgOf(wrapper).getBoundingClientRect().top),
        );

        // Sanity check: the picture did move on the way, not only at the end.
        expect(new Set(tops.map(Math.round)).size).toBeGreaterThan(10);
        expectNeverMovedUp(tops, "the picture");
        expect(wrapper.classList.contains(kInlineImageMiddleClass)).toBe(true);
        expectAtBottomOfBlock(group, wrapper);
    });

    // In a block that has more text than it can show, the bottom dock is at the end of the
    // content, so below the block still means the bottom dock: right after the last line of text.
    it("puts a picture dragged below a block that is full of text right after the last line", async () => {
        // 16 lines of 20px beside and below the picture are more than the 300px block holds.
        const group = makeTextBlock(400, 300, [
            Array(16).fill("Line of it.").join("<br>"),
        ]);
        const wrapper = await setUpImage(group);
        const editable = editableOf(group);
        // Sanity check: the block overflows before the drag.
        expect(editable.scrollHeight).toBeGreaterThan(editable.clientHeight);

        await dragPictureTo(wrapper, { x: 200, y: 700 });

        expect(wrapper.classList.contains(kInlineImageBottomClass)).toBe(true);
        // The picture is the last thing in the block, and it starts below the last line.
        expect(editable.lastElementChild).toBe(wrapper);
        const textBottom =
            rangeAroundTextOf(group).getBoundingClientRect().bottom;
        const picture = imgOf(wrapper).getBoundingClientRect();
        expect(picture.top).toBeGreaterThanOrEqual(textBottom);
        // Right after it: the bottom dock's 8px (0.5em) top margin and no more.
        expect(picture.top).toBeLessThan(textBottom + 12);
    });

    // With two pictures in one block, dragging each to the bottom in turn must not make either
    // jump up, and must not push text out.
    it("keeps two pictures low in the block when each is dragged to the bottom in turn", async () => {
        const group = makeTextBlock(400, 400, ["The end."]);
        const first = await setUpImage(group, kInlineImageMiddleClass);
        const second = await setUpImage(group, kInlineImageMiddleClass);
        const topOf = (wrapper: HTMLElement) =>
            imgOf(wrapper).getBoundingClientRect().top;
        const bottomOf = (wrapper: HTMLElement) =>
            imgOf(wrapper).getBoundingClientRect().bottom;
        // Sanity checks: two different pictures, one above the other at the top of the block.
        expect(second).not.toBe(first);
        expect(Math.round(topOf(first))).toBe(
            Math.round(group.getBoundingClientRect().top),
        );
        expect(topOf(second)).toBeGreaterThanOrEqual(bottomOf(first));
        const secondTopAtStart = topOf(second);

        await dragPictureTo(first, { x: 200, y: bottomOfBlock(group) });

        // The first is at the very bottom, and the second has not moved.
        expectAtBottomOfBlock(group, first);
        expect(Math.round(topOf(second))).toBe(Math.round(secondTopAtStart));
        const firstTopAtBottom = topOf(first);

        // This drag is slow, as a person's is. The bottom of the block is taken, and a move
        // that asks for it is undone, so one movement straight there would leave the picture
        // where it started.
        const secondTops: number[] = [secondTopAtStart];
        await dragSlowly(
            centerOf(imgOf(second)),
            { x: 200, y: bottomOfBlock(group) },
            60,
            () => secondTops.push(topOf(second)),
        );
        expectNeverMovedUp(secondTops, "the second picture");

        // The second has gone down as far as the first lets it, the first has stayed at the
        // bottom, and the block still shows all its text.
        expect(Math.round(topOf(first))).toBe(Math.round(firstTopAtBottom));
        expect(topOf(second)).toBeGreaterThan(secondTopAtStart);
        expect(bottomOf(second)).toBeLessThanOrEqual(topOf(first) + 1);
        expect(bottomOf(second)).toBeGreaterThan(topOf(first) - 12);
        expect(overflowPx(group)).toBeLessThanOrEqual(1);
    });
});
