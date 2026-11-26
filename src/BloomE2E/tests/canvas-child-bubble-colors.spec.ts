// A speech bubble and its child bubbles are one family. Where a child lies over its parent, the
// two are drawn as one shape, with no outline between them. The family shares its colors: a
// background, text, text outline or outer outline color chosen while the parent is selected goes
// to the child too, and one chosen while the child is selected goes to the parent.
//
// Automates "Child Comic Bubbles" (Test Case ID 310).

import { type Locator, type Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    waitForSnapshotStreamIdle,
} from "../helpers/bookMaking";
import {
    addChildBubble,
    canvasElement,
    clearCanvasElementSelection,
    dragPaletteItemOntoCanvas,
    selectCanvasElement,
    typeInCanvasElement,
} from "../helpers/canvasElements";
import {
    chooseOuterOutlineColor,
    getCanvasElementTextColors,
    readSavedCanvasShapes,
    type ICanvasTextColors,
    type OuterOutlineColor,
    typeCanvasElementBackgroundColor,
    typeCanvasElementTextColor,
    typeCanvasElementTextOutlineColor,
} from "../helpers/canvasToolOptions";
import { getElementSize, type IElementSize } from "../helpers/canvasResize";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { hexToRgba } from "../helpers/colorPicker";
import { cssColorToHex } from "../helpers/cssValues";
import { realClickAt } from "../helpers/realClick";
import { watchForScriptErrors } from "../helpers/scriptErrors";
import {
    findOpaqueDrawingPointNear,
    type IDrawingPixel,
    type IScreenPoint,
    readBubbleFamilySpec,
    readDrawingAlong,
} from "../helpers/speechBubbleTail";

test.use({
    collectionSpec: {
        name: "canvas-child-bubble-colors",
        languages: ["en"],
        // Below Pro, the Canvas tool's palette sits behind a subscription notice.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const kParentWords = "Parent";
const kChildWords = "Child";

/** The colors of a bubble family that the Canvas tool lets a person choose. */
interface IFamilyColors {
    background: string;
    text: string;
    textOutline: string;
    outerOutline: Exclude<OuterOutlineColor, "none">;
}

/** The colors chosen while the parent is selected. */
const kParentChoice: IFamilyColors = {
    background: "#87ceeb",
    text: "#8b0000",
    textOutline: "#006400",
    outerOutline: "crimson",
};
/** The colors chosen afterwards while the child is selected; each differs from kParentChoice. */
const kChildChoice: IFamilyColors = {
    background: "#ffe4b5",
    text: "#00008b",
    textOutline: "#800080",
    outerOutline: "yellow",
};
/** The CSS colors the Outer Outline Color choices name. */
const kOuterOutlineHex: Record<IFamilyColors["outerOutline"], string> = {
    crimson: "#dc143c",
    yellow: "#ffff00",
};
/** How far each channel of a drawn pixel may be from a color and still count as that color. */
const kColorTolerance = 3;
/** The same, for an outer outline, whose pixels the browser smooths into what lies beside them. */
const kOutlineTolerance = 24;
/** How far past a bubble's box the outer outline is looked for, in screen pixels. */
const kOutlineReach = 30;

type Rgba = { r: number; g: number; b: number; a: number };

/** Whether `pixel` is opaque and within `tolerance` of the color `hex` in every channel. */
function isColor(pixel: Rgba, hex: string, tolerance: number): boolean {
    const want = hexToRgba(hex);
    return (
        pixel.a === 255 &&
        Math.abs(pixel.r - want.r) <= tolerance &&
        Math.abs(pixel.g - want.g) <= tolerance &&
        Math.abs(pixel.b - want.b) <= tolerance
    );
}

/** A pixel's color written as "#rrggbb/alpha", for failure messages. */
function describe(pixel: Rgba): string {
    return (
        "#" +
        [pixel.r, pixel.g, pixel.b]
            .map((n) => n.toString(16).padStart(2, "0"))
            .join("") +
        `/${pixel.a}`
    );
}

/** The screen box of `element`, after scrolling it to the middle of the page's window. */
async function screenBox(
    element: Locator,
    what: string,
): Promise<{ x: number; y: number; width: number; height: number }> {
    await element.evaluate((e) =>
        e.scrollIntoView({ block: "center", inline: "center" }),
    );
    const box = await element.boundingBox({ timeout: 30000 });
    if (!box) throw new Error(`The ${what} has no on-screen box.`);
    return box;
}

type ScreenBox = { x: number; y: number; width: number; height: number };

/** Whether two screen boxes share any area (boxes that only touch do not). */
function boxesOverlap(a: ScreenBox, b: ScreenBox): boolean {
    return (
        a.x < b.x + b.width &&
        b.x < a.x + a.width &&
        a.y < b.y + b.height &&
        b.y < a.y + a.height
    );
}

/** The middle of a screen box. */
function middleOf(box: {
    x: number;
    y: number;
    width: number;
    height: number;
}) {
    return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
}

/**
 * Where to look at a bubble's own drawing, away from the other bubble of the family: a point inside
 * the bubble, a quarter of its width in from the side `side`, and a point `kOutlineReach` pixels
 * beyond that side, level with it. Between the two lie the bubble's outline and, when it has one,
 * its outer outline.
 */
async function sideOfBubble(
    bubble: Locator,
    side: "left" | "right",
    what: string,
): Promise<{ inside: IScreenPoint; beyond: IScreenPoint }> {
    const box = await screenBox(bubble, what);
    const y = box.y + box.height / 2;
    return side === "left"
        ? {
              inside: { x: box.x + box.width / 4, y },
              beyond: { x: box.x - kOutlineReach, y },
          }
        : {
              inside: { x: box.x + (box.width * 3) / 4, y },
              beyond: { x: box.x + box.width + kOutlineReach, y },
          };
}

/** The color ComicalJS fills a bubble with, read solidly inside it at `inside`. */
async function readFillColor(page: Page, inside: IScreenPoint): Promise<Rgba> {
    return (await findOpaqueDrawingPointNear(page, inside)).color;
}

/**
 * Check what the drawing and the text of one bubble show: its fill is `colors.background`, its text
 * is drawn in `colors.text` with a 2px outline in `colors.textOutline`, and beyond its side `side`
 * the drawing has a band of the outer outline color. `when` and `what` go in failure messages.
 */
async function expectBubbleColors(
    page: Page,
    bubble: Locator,
    side: "left" | "right",
    colors: IFamilyColors,
    what: string,
    when: string,
): Promise<void> {
    const outerHex = kOuterOutlineHex[colors.outerOutline];
    await expect
        .poll(
            async () => {
                const place = await sideOfBubble(bubble, side, what);
                const fill = await readFillColor(page, place.inside);
                const ray = await readDrawingAlong(
                    page,
                    place.inside,
                    place.beyond,
                );
                return {
                    fill: isColor(fill, colors.background, kColorTolerance)
                        ? colors.background
                        : describe(fill),
                    outerOutline: ray.some((p) =>
                        isColor(p.color, outerHex, kOutlineTolerance),
                    )
                        ? colors.outerOutline
                        : `none of ${describeRay(ray)}`,
                };
            },
            {
                timeout: 15000,
                message:
                    `${when}, the ${what} should be filled with ${colors.background} and have ` +
                    `a ${colors.outerOutline} (${outerHex}) outer outline.`,
            },
        )
        .toEqual({
            fill: colors.background,
            outerOutline: colors.outerOutline,
        });
    await expect
        .poll(() => getCanvasElementTextColors(bubble), {
            timeout: 15000,
            message:
                `${when}, the ${what}'s text should be ${colors.text}, outlined 2px in ` +
                `${colors.textOutline}.`,
        })
        .toEqual({
            color: colors.text,
            outlineColor: colors.textOutline,
            outlineWidth: "2px",
        } satisfies ICanvasTextColors);
}

/** A saved shape's fill or stroke as lower-case "#rrggbb" where it is a color this can read. */
function savedColor(value: string): string {
    if (value.toLowerCase() === "yellow") return "#ffff00";
    if (value.toLowerCase() === "crimson") return "#dc143c";
    try {
        return cssColorToHex(value);
    } catch {
        return value.toLowerCase();
    }
}

/** How far, in the page's own pixels, a saved shape's middle may be from its bubble's middle. */
const kSavedShapeTolerance = 3;

/** A bubble of the family, by name, with its place on the canvas in the page's own pixels. */
interface IPlacedBubble {
    what: string;
    place: IElementSize;
}

/**
 * Leave the page, which saves it, and wait until the drawing Bloom saved into it, which is what a
 * reader of the book sees, paints each bubble of `bubbles` in `colors`: ComicalJS saves each
 * bubble as its own shape filled with the background color, over a larger one filled with the
 * outer outline color, both centred on the bubble. Also that it paints none of `earlierColors`.
 * Then come back to the page and leave nothing selected.
 */
async function expectSavedDrawing(
    page: Page,
    bookFolder: string,
    pageId: string,
    bubbles: IPlacedBubble[],
    colors: IFamilyColors,
    earlierColors: string[],
): Promise<void> {
    const otherPage = (await getPages(page)).find((p) => !p.isContentPage);
    if (!otherPage)
        throw new Error("The book has no page but its content pages.");
    // Leaving a page saves the last snapshot of it that Bloom received, which is sent a moment
    // after the page last changed; let the change reach Bloom first.
    await waitForSnapshotStreamIdle(page);
    await goToPage(page, otherPage.id);
    const outerHex = kOuterOutlineHex[colors.outerOutline];
    const readSaved = async () => {
        const shapes = await readSavedCanvasShapes(page, bookFolder, pageId);
        const filledAround = (bubble: IPlacedBubble, color: string) =>
            shapes.some(
                (s) =>
                    savedColor(s.fill) === color &&
                    Math.abs(
                        s.x +
                            s.width / 2 -
                            (bubble.place.left + bubble.place.width / 2),
                    ) <= kSavedShapeTolerance &&
                    Math.abs(
                        s.y +
                            s.height / 2 -
                            (bubble.place.top + bubble.place.height / 2),
                    ) <= kSavedShapeTolerance,
            );
        const painted = [
            ...new Set(
                shapes.flatMap((s) => [
                    savedColor(s.fill),
                    savedColor(s.stroke),
                ]),
            ),
        ];
        const missing = bubbles.flatMap((b) => [
            ...(filledAround(b, colors.background)
                ? []
                : [`${b.what}: ${colors.background} fill`]),
            ...(filledAround(b, outerHex)
                ? []
                : [`${b.what}: ${colors.outerOutline} outer outline`]),
        ]);
        const leftOver = earlierColors.filter((c) => painted.includes(c));
        return {
            missing,
            leftOver,
            // Every saved shape, but only when something is wrong, to show what the drawing holds.
            shapes: missing.length || leftOver.length ? shapes : [],
        };
    };
    await expect
        .poll(readSaved, {
            timeout: 15000,
            message:
                `The page's saved drawing should paint ${bubbles.map((b) => b.what).join(", ")} ` +
                `filled with ${colors.background} over a ${colors.outerOutline} outer outline, ` +
                `and none of ${earlierColors.join(", ")}.`,
        })
        .toEqual({ missing: [], leftOver: [], shapes: [] });
    await goToPage(page, pageId);
    await clearCanvasElementSelection(page);
}

/** The distinct colors along a ray of the drawing, in order, for a failure message. */
function describeRay(ray: IDrawingPixel[]): string {
    const seen: string[] = [];
    for (const p of ray) {
        const c = describe(p.color);
        if (seen[seen.length - 1] !== c) seen.push(c);
    }
    return seen.join(" ");
}

/**
 * Whether a pixel of the drawing is part of a bubble's dark outline: drawn (not left empty) and
 * darker than any fill this test uses, whose lightest channel is never below 0x87.
 */
function isOutlinePixel(pixel: Rgba): boolean {
    return pixel.a > 0 && Math.max(pixel.r, pixel.g, pixel.b) < 0x60;
}

/** The pixels of the drawing on the straight line from the parent's middle to the child's. */
async function readDrawingBetween(
    page: Page,
    parent: Locator,
    child: Locator,
): Promise<IDrawingPixel[]> {
    const from = middleOf(await screenBox(parent, "parent bubble"));
    const to = middleOf(await screenBox(child, "child bubble"));
    return readDrawingAlong(page, from, to);
}

/**
 * Check that the parent and child are drawn as one shape: the line from the parent's middle to the
 * child's lies wholly inside the drawing's fill, with no outline across it, while a line from
 * inside each bubble out beyond its far side (the parent's left, the child's right) does cross an
 * outline, which shows that the drawing has outlines this check would see. `when` goes in failure
 * messages.
 */
async function expectOutlinesOnlyOutside(
    page: Page,
    parent: Locator,
    child: Locator,
    when: string,
): Promise<void> {
    for (const [bubble, side, what] of [
        [parent, "left", "parent bubble"],
        [child, "right", "child bubble"],
    ] as const) {
        const place = await sideOfBubble(bubble, side, what);
        const ray = await readDrawingAlong(page, place.inside, place.beyond);
        expect(
            ray.some((p) => isOutlinePixel(p.color)),
            `${when}, a line from inside the ${what} out past its ${side} side should cross ` +
                `its outline: ${describeRay(ray)}`,
        ).toBe(true);
    }
    const between = await readDrawingBetween(page, parent, child);
    expect(
        between.every((p) => p.color.a === 255) &&
            !between.some((p) => isOutlinePixel(p.color)),
        `${when}, the child and its parent should be drawn as one filled shape, with no ` +
            `outline between their middles: ${describeRay(between)}`,
    ).toBe(true);
}

/**
 * Drag the bubble `bubble` by pressing on it at the screen point `from` and moving `dx`, `dy`
 * screen pixels, the way a person does. Nothing must be selected when this is called, so that the
 * press moves the bubble rather than putting a caret in its text. Waits until the bubble has moved.
 */
async function dragBubbleBy(
    page: Page,
    bubble: Locator,
    from: IScreenPoint,
    dx: number,
    dy: number,
    what: string,
): Promise<void> {
    const before = await screenBox(bubble, what);
    await page.mouse.move(from.x, from.y);
    await page.mouse.down();
    await page.mouse.move(from.x + dx, from.y + dy, { steps: 12 });
    await page.mouse.up();
    await expect
        .poll(
            async () => {
                const now = await bubble.boundingBox();
                if (!now) return 0;
                return Math.abs(now.x - before.x) + Math.abs(now.y - before.y);
            },
            {
                timeout: 15000,
                message: `Dragging the ${what} by ${dx},${dy} did not move it.`,
            },
        )
        .toBeGreaterThan(Math.max(Math.abs(dx), Math.abs(dy)) / 2);
}

/**
 * Select `bubble` by clicking inside it a quarter of its width in from its side `side`, away from
 * the other bubble of the family: where a child lies over its parent, a click on the overlap
 * selects the parent (BL-17204). Waits until Bloom marks it active.
 */
async function selectBubble(
    page: Page,
    bubble: Locator,
    side: "left" | "right",
    what: string,
): Promise<void> {
    const at = (await sideOfBubble(bubble, side, what)).inside;
    await realClickAt(page, at.x, at.y);
    await expectSelected(bubble, what, "Clicking it");
}

/** Wait until `bubble` is the selected canvas element; `after` says what should have selected it. */
async function expectSelected(
    bubble: Locator,
    what: string,
    after: string,
): Promise<void> {
    await expect(
        bubble,
        `${after} should leave the ${what} selected.`,
    ).toHaveAttribute("data-bloom-active", "true", { timeout: 15000 });
}

/**
 * Choose every color of `colors` from the Canvas tool, with `bubble` selected, the way a person
 * does, checking that it is still the one selected after each choice. The background color goes
 * first, and `afterBackground` runs before the others, so that a test can check it alone.
 */
async function chooseColors(
    page: Page,
    bubble: Locator,
    side: "left" | "right",
    what: string,
    colors: IFamilyColors,
    afterBackground: () => Promise<void>,
): Promise<void> {
    await selectBubble(page, bubble, side, what);
    await typeCanvasElementBackgroundColor(page, colors.background);
    await expectSelected(bubble, what, "Choosing the background color");
    await afterBackground();
    await expectSelected(bubble, what, "Checking the background color");
    await typeCanvasElementTextColor(page, colors.text);
    await expectSelected(bubble, what, "Choosing the text color");
    await typeCanvasElementTextOutlineColor(page, colors.textOutline);
    await expectSelected(bubble, what, "Choosing the text outline color");
    await chooseOuterOutlineColor(page, colors.outerOutline);
    await expectSelected(bubble, what, "Choosing the outer outline color");
}

test("Child bubbles merge with their parent and share its colors [Test Case ID 310]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    const errors = watchForScriptErrors(page);
    try {
        const before = (await getContentPages(page)).map((p) => p.id);
        await addPage(page, "Canvas");
        const canvasPage = (await getContentPages(page)).find(
            (p) => !before.includes(p.id),
        );
        if (!canvasPage)
            throw new Error("Adding a Canvas page added no content page.");
        await goToPage(page, canvasPage.id);

        const parentIndex = await step("Add a bubble", async () => {
            const index = await dragPaletteItemOntoCanvas(page, "speech", {
                xFraction: 0.4,
                yFraction: 0.4,
            });
            await selectCanvasElement(page, index);
            await typeInCanvasElement(page, kParentWords);
            return index;
        });
        if (parentIndex === undefined)
            throw new Error("Adding the bubble gave no index.");
        const parent = canvasElement(page, parentIndex);

        // Bloom appends the child, so it is the element after the parent.
        const childIndex = parentIndex + 1;
        await step("Add a child bubble", async () => {
            await selectCanvasElement(page, parentIndex);
            await addChildBubble(page);
            await selectCanvasElement(page, childIndex);
            await typeInCanvasElement(page, kChildWords);
        });
        const child = canvasElement(page, childIndex);
        expect(
            (await readBubbleFamilySpec(child)).tails.map((t) => t.joiner),
            "The new bubble should be a child, joined to its parent by its one tail.",
        ).toEqual([true]);

        // Where Bloom puts a new child depends on the room around the parent. Lay it so that its
        // middle is on the parent's right edge, level with the parent's middle: the line between
        // the two middles then lies inside one bubble or the other all the way, and crosses the
        // child's left edge inside the parent.
        await step(
            "Drag the child so that it overlaps its parent",
            async () => {
                await clearCanvasElementSelection(page);
                const parentBox = await screenBox(parent, "parent bubble");
                const childBox = await screenBox(child, "child bubble");
                const parentMiddle = middleOf(parentBox);
                const childMiddle = middleOf(childBox);
                // Press on the child's side away from the parent: a press where the child lies over
                // its parent grabs the parent (BL-17204).
                const farSide =
                    childMiddle.x < parentMiddle.x ? "left" : "right";
                await dragBubbleBy(
                    page,
                    child,
                    (await sideOfBubble(child, farSide, "child bubble")).inside,
                    Math.round(parentBox.x + parentBox.width - childMiddle.x),
                    Math.round(parentMiddle.y - childMiddle.y),
                    "child bubble",
                );
            },
        );
        await step("The overlapping bubbles merge into one shape", async () => {
            await clearCanvasElementSelection(page);
            const parentNow = await screenBox(parent, "parent bubble");
            const childNow = await screenBox(child, "child bubble");
            expect(
                boxesOverlap(parentNow, childNow),
                `The child should now overlap its parent: parent ${JSON.stringify(parentNow)}, ` +
                    `child ${JSON.stringify(childNow)}.`,
            ).toBe(true);
            await expectOutlinesOnlyOutside(
                page,
                parent,
                child,
                "Once the child was dragged over its parent",
            );
        });

        // Before any color is chosen, the child has none of the colors this test chooses.
        const childStart = await sideOfBubble(child, "right", "child bubble");
        const startFill = await readFillColor(page, childStart.inside);
        expect(
            isColor(startFill, kParentChoice.background, kColorTolerance),
            `Before any color was chosen, the child should not already be ` +
                `${kParentChoice.background}.`,
        ).toBe(false);
        const startRay = await readDrawingAlong(
            page,
            childStart.inside,
            childStart.beyond,
        );
        expect(
            startRay.some((p) =>
                isColor(
                    p.color,
                    kOuterOutlineHex[kParentChoice.outerOutline],
                    kOutlineTolerance,
                ),
            ),
            `Before any color was chosen, the child should have no outer outline: ` +
                describeRay(startRay),
        ).toBe(false);
        const startText = await getCanvasElementTextColors(child);
        expect(
            startText.color === kParentChoice.text ||
                startText.outlineColor === kParentChoice.textOutline,
            `Before any color was chosen, the child's text should not already have the colors ` +
                `the test chooses: ${JSON.stringify(startText)}`,
        ).toBe(false);

        await step("Set the colors of the parent bubble", async () => {
            await chooseColors(
                page,
                parent,
                "left",
                "parent bubble",
                kParentChoice,
                async () => {
                    const childSide = await sideOfBubble(
                        child,
                        "right",
                        "child bubble",
                    );
                    await expect
                        .poll(
                            async () =>
                                describe(
                                    await readFillColor(page, childSide.inside),
                                ),
                            {
                                timeout: 15000,
                                message:
                                    "Once the parent's background color was set, the child " +
                                    `should be filled with it, ${kParentChoice.background}.`,
                            },
                        )
                        .toBe(`${kParentChoice.background}/255`);
                },
            );
        });
        await step("The child has the parent's colors", async () => {
            await expectBubbleColors(
                page,
                child,
                "right",
                kParentChoice,
                "child bubble",
                "After the parent's colors were set",
            );
            await expectBubbleColors(
                page,
                parent,
                "left",
                kParentChoice,
                "parent bubble",
                "After the parent's colors were set",
            );
        });

        await step("Set the colors of the child bubble", async () => {
            await chooseColors(
                page,
                child,
                "right",
                "child bubble",
                kChildChoice,
                async () => {
                    const parentSide = await sideOfBubble(
                        parent,
                        "left",
                        "parent bubble",
                    );
                    await expect
                        .poll(
                            async () =>
                                describe(
                                    await readFillColor(
                                        page,
                                        parentSide.inside,
                                    ),
                                ),
                            {
                                timeout: 15000,
                                message:
                                    "Once the child's background color was set, the parent " +
                                    `should be filled with it, ${kChildChoice.background}.`,
                            },
                        )
                        .toBe(`${kChildChoice.background}/255`);
                },
            );
        });
        await step("The parent has the child's colors", async () => {
            await expectBubbleColors(
                page,
                parent,
                "left",
                kChildChoice,
                "parent bubble",
                "After the child's colors were set",
            );
            await expectBubbleColors(
                page,
                child,
                "right",
                kChildChoice,
                "child bubble",
                "After the child's colors were set",
            );
        });

        const earlierColors = [
            kParentChoice.background,
            kOuterOutlineHex[kParentChoice.outerOutline],
        ];
        await step("Leave the page and come back", async () => {
            await expectSavedDrawing(
                page,
                bookFolder,
                canvasPage.id,
                [
                    {
                        what: "parent bubble",
                        place: await getElementSize(parent),
                    },
                    {
                        what: "child bubble",
                        place: await getElementSize(child),
                    },
                ],
                kChildChoice,
                earlierColors,
            );
            await expectBubbleColors(
                page,
                parent,
                "left",
                kChildChoice,
                "parent bubble",
                "After the page was saved and shown again",
            );
            await expectBubbleColors(
                page,
                child,
                "right",
                kChildChoice,
                "child bubble",
                "After the page was saved and shown again",
            );
            await expectOutlinesOnlyOutside(
                page,
                parent,
                child,
                "After the page was saved and shown again",
            );
        });

        await step("Add another child to the colored bubble", async () => {
            await selectBubble(page, parent, "left", "parent bubble");
            await addChildBubble(page);
            const newChild = canvasElement(page, childIndex + 1);
            expect(
                (await readBubbleFamilySpec(newChild)).tails.map(
                    (t) => t.joiner,
                ),
                "The added bubble should be a child, joined to the family by its one tail.",
            ).toEqual([true]);
            expect(
                (await getCanvasElementTextColors(newChild)).color,
                "A child added to the family should take its text color.",
            ).toBe(kChildChoice.text);
            // The new child may lie over the others, so its fill is read from the saved drawing,
            // where it is a shape of its own.
            await expectSavedDrawing(
                page,
                bookFolder,
                canvasPage.id,
                [
                    {
                        what: "parent bubble",
                        place: await getElementSize(parent),
                    },
                    {
                        what: "child bubble",
                        place: await getElementSize(child),
                    },
                    {
                        what: "new child bubble",
                        place: await getElementSize(newChild),
                    },
                ],
                kChildChoice,
                earlierColors,
            );
        });

        expect(errors.errors(), "Bloom raised a script error.").toEqual([]);
    } finally {
        errors.stop();
    }
});
