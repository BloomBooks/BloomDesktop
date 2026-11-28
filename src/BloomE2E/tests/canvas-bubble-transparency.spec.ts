// A bubble's background may be partly transparent, and it stays so when its Element Type changes,
// except to Caption. A caption draws a black shadow rectangle behind itself, so the Canvas tool
// makes its background opaque when an element becomes a Caption (handleStyleChanged in
// CanvasToolControls.tsx), and its Background Color picker then offers no transparency: no alpha
// slider and no transparent swatch. An element whose background was fully transparent becomes a
// Caption with the white-to-calico gradient, since fully transparent made opaque would be black.
// The test checks that gradient in the element's data-bubble only, not in the saved drawing: the
// saved drawing loses the link from the caption's shape to its gradient (BL-17215).
//
// The test reads what Bloom saved after each change: the element's data-bubble, which holds its
// type and background, and the drawing ComicalJS saved for the page (svg.comical-generated), which
// is what every reader of the book sees, down to how opaque its red shape is drawn. It also reads
// the "% Transparent" that the Canvas tool shows over its Background Color bar.
//
// Automates "Bubble Transparency Behavior" (Test Case ID 323).

import { type Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { getUiString } from "../helpers/api";
import {
    getPages,
    goToPage,
    makeBookFromTemplate,
    waitForSnapshotStreamIdle,
} from "../helpers/bookMaking";
import {
    addCanvasPageWithElement,
    selectCanvasElement,
} from "../helpers/canvasElements";
import {
    chooseCanvasElementType,
    getCanvasElementBackgroundColorBarText,
    getCanvasElementTypeShown,
    openCanvasElementBackgroundColorPicker,
    readSavedBubbleSpecs,
    readSavedCanvasShapes,
    type CanvasElementType,
} from "../helpers/canvasToolOptions";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    closeColorPickerWith,
    clickAlphaSliderMiddle,
    getPaletteSwatchOpacities,
    hasAlphaSlider,
    hexBox,
    hexToRgba,
    parseCssRgba,
    readColorPicker,
    typeInHexBox,
} from "../helpers/colorPicker";
import { cssColorToHex } from "../helpers/cssValues";
import { watchForScriptErrors } from "../helpers/scriptErrors";

test.use({
    collectionSpec: {
        name: "canvas-bubble-transparency",
        languages: ["en"],
        // Below Pro, the Canvas tool's options sit behind a subscription notice.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** The red of Bloom's text color palette (bloomPalette.ts), typed in, since the background palette has no red. */
const kRed = "#ff1616";
/**
 * How far an opacity may be from the one expected, for rounding where colors are converted. It is
 * small enough that a nearly opaque background, such as 0.996, does not pass as opaque.
 */
const kOpacityTolerance = 0.002;

/** Opacities to three decimals, enough to tell 0.996 from 1. */
function round3(n: number): number {
    return Math.round(n * 1000) / 1000;
}

/**
 * A saved background color as "#rrggbb at <alpha>", whether Bloom saved it as hex or as rgba(), so
 * that colors compare by value; a color keyword such as "white" or "transparent" as it is.
 */
function describeColor(color: string): string {
    const value = color.trim().toLowerCase();
    let alpha: number;
    if (value.startsWith("rgb")) alpha = parseCssRgba(value).a;
    else if (value.startsWith("#")) alpha = hexToRgba(value).a;
    else return value;
    return `${cssColorToHex(value)} at ${round3(alpha)}`;
}

/** Whether a described color is `hex` at `opacity`, within kOpacityTolerance. */
function isColorAt(described: string, hex: string, opacity: number): boolean {
    const m = /^(#[0-9a-f]{6}) at ([\d.]+)$/.exec(described);
    return (
        !!m &&
        m[1] === hex &&
        Math.abs(Number(m[2]) - opacity) <= kOpacityTolerance
    );
}

/** What the saved book says about one canvas element and the page's drawing. */
interface ISavedElement {
    style: string | undefined;
    /** The element's background colors, as describeColor gives them. */
    backgroundColors: string[];
    /** How opaque each red shape of the page's drawing is drawn, rounded to three decimals. */
    redShapeOpacities: number[];
}

/**
 * Save the page by leaving it for the cover, then wait until the saved book says what `matches`
 * accepts about the element at `index`, failing with `why` and what was saved if it never does.
 * Then come back to the page and select the element again. Returns what was saved.
 */
async function expectSaved(
    page: Page,
    bookFolder: string,
    pageId: string,
    index: number,
    matches: (saved: ISavedElement) => boolean,
    why: string,
): Promise<ISavedElement> {
    // Leaving a page saves the last snapshot of it that Bloom received, which is sent a moment
    // after the page last changed; let the change reach Bloom first.
    await waitForSnapshotStreamIdle(page);
    const cover = (await getPages(page))[0];
    await goToPage(page, cover.id);
    let last: ISavedElement | undefined;
    const readSaved = async (): Promise<ISavedElement> => {
        const spec = (await readSavedBubbleSpecs(page, bookFolder, pageId))[
            index
        ];
        const shapes = await readSavedCanvasShapes(page, bookFolder, pageId);
        const redShapeOpacities = shapes
            .filter((s) => {
                try {
                    return cssColorToHex(s.fill) === kRed;
                } catch {
                    return false;
                }
            })
            .map((s) => round3(s.fillOpacity));
        return {
            style: spec?.style,
            backgroundColors: (spec?.backgroundColors ?? []).map(describeColor),
            redShapeOpacities,
        };
    };
    await expect
        .poll(
            async () => {
                last = await readSaved();
                // What was saved shows in the failure message when it is not what was expected.
                return matches(last) ? "as expected" : JSON.stringify(last);
            },
            { timeout: 15000, message: why },
        )
        .toBe("as expected");
    await goToPage(page, pageId);
    await selectCanvasElement(page, index);
    return last!;
}

/** Whether every red shape is drawn at `opacity`, and there is at least one. */
function allRedShapesAt(saved: ISavedElement, opacity: number): boolean {
    return (
        saved.redShapeOpacities.length > 0 &&
        saved.redShapeOpacities.every(
            (o) => Math.abs(o - opacity) <= kOpacityTolerance,
        )
    );
}

/** The "{0}% Transparent" text the Canvas tool shows over its Background Color bar, in the UI language. */
async function percentTransparentText(
    page: Page,
    percent: number,
): Promise<string> {
    const format = await getUiString(
        page,
        "ColorPicker.PercentTransparent",
        "{0}% Transparent",
    );
    return format.replace("{0}", String(percent));
}

/** Wait for the Background Color bar to show `text` ("" for none), failing with `why` if it does not. */
async function expectBarText(
    page: Page,
    text: string,
    why: string,
): Promise<void> {
    await expect
        .poll(() => getCanvasElementBackgroundColorBarText(page), {
            timeout: 15000,
            message: why,
        })
        .toBe(text);
}

/** Change the selected element's Element Type and check that the Canvas tool shows it. */
async function changeType(page: Page, type: CanvasElementType): Promise<void> {
    await chooseCanvasElementType(page, type);
    expect(await getCanvasElementTypeShown(page)).toBe(type);
}

test("A half-transparent bubble keeps its transparency through type changes until it becomes a Caption [Test Case ID 323]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    const errors = watchForScriptErrors(page);
    try {
        let pageId = "";
        let index = -1;

        await step("Drag a Speech bubble onto a Canvas page", async () => {
            ({ pageId, index } = await addCanvasPageWithElement(
                page,
                "speech",
            ));
            expect(
                await getCanvasElementTypeShown(page),
                "A new speech bubble should show the Element Type Speech.",
            ).toBe("speech");
            const frame = await openCanvasElementBackgroundColorPicker(page);
            expect(
                await hasAlphaSlider(frame),
                "A speech bubble's Background Color picker should offer an alpha slider.",
            ).toBe(true);
            expect(
                (await getPaletteSwatchOpacities(frame)).some((o) => o < 1),
                "A speech bubble's Background Color palette should offer a transparent swatch.",
            ).toBe(true);
            await closeColorPickerWith(frame, "Cancel");
        });

        // How opaque the alpha slider made the background (0 to 1), and the "% Transparent" text
        // the Background Color bar then shows.
        let opacity = 1;
        let transparentText = "";
        await step(
            "Give it a red background, half transparent with the alpha slider",
            async () => {
                const frame =
                    await openCanvasElementBackgroundColorPicker(page);
                await typeInHexBox(frame, kRed);
                await expect(
                    hexBox(frame),
                    `The color picker's hex box did not take ${kRed}.`,
                ).toHaveValue(new RegExp(`^${kRed}(ff)?$`, "i"));
                await clickAlphaSliderMiddle(frame);
                let percent = 100;
                await expect
                    .poll(
                        async () => {
                            percent = (await readColorPicker(frame))
                                .alphaPercentage;
                            return percent > 20 && percent < 80;
                        },
                        {
                            timeout: 15000,
                            message:
                                "A click in the middle of the alpha slider should make the color about half transparent.",
                        },
                    )
                    .toBe(true);
                await closeColorPickerWith(frame, "OK");
                opacity = percent / 100;
                transparentText = await percentTransparentText(
                    page,
                    100 - percent,
                );
                await expectSaved(
                    page,
                    bookFolder,
                    pageId,
                    index,
                    (s) =>
                        s.style === "speech" &&
                        s.backgroundColors.length === 1 &&
                        isColorAt(s.backgroundColors[0], kRed, opacity) &&
                        allRedShapesAt(s, opacity),
                    `The speech bubble was not saved with a red background ${percent}% opaque, drawn so.`,
                );
                await expectBarText(
                    page,
                    transparentText,
                    "The Background Color bar should say how transparent the background is.",
                );
            },
        );

        await step(
            "Change its Element Type to Exclamation: it stays half transparent",
            async () => {
                await changeType(page, "pointedArcs");
                await expectSaved(
                    page,
                    bookFolder,
                    pageId,
                    index,
                    (s) =>
                        s.style === "pointedArcs" &&
                        s.backgroundColors.length === 1 &&
                        isColorAt(s.backgroundColors[0], kRed, opacity) &&
                        allRedShapesAt(s, opacity),
                    "After its Element Type changed to Exclamation, the bubble was not saved with its half-transparent red background.",
                );
                await expectBarText(
                    page,
                    transparentText,
                    "The Background Color bar should still say how transparent the background is.",
                );
            },
        );

        await step(
            "Change its Element Type to Caption: the background becomes opaque red",
            async () => {
                await changeType(page, "caption");
                await expectSaved(
                    page,
                    bookFolder,
                    pageId,
                    index,
                    (s) =>
                        s.style === "caption" &&
                        s.backgroundColors.length === 1 &&
                        isColorAt(s.backgroundColors[0], kRed, 1) &&
                        allRedShapesAt(s, 1),
                    "After its Element Type changed to Caption, the element was not saved with an opaque red background.",
                );
                await expectBarText(
                    page,
                    "",
                    "An opaque background's color bar should say nothing about transparency.",
                );
            },
        );

        await step(
            "The Caption's Background Color picker offers no transparency",
            async () => {
                const frame =
                    await openCanvasElementBackgroundColorPicker(page);
                expect(
                    await hasAlphaSlider(frame),
                    "A caption's Background Color picker should have no alpha slider.",
                ).toBe(false);
                expect(
                    (await getPaletteSwatchOpacities(frame)).filter(
                        (o) => o < 1,
                    ),
                    "A caption's Background Color palette should offer only opaque swatches.",
                ).toEqual([]);
                await expect(
                    hexBox(frame),
                    "A caption's Background Color picker should show its opaque red, with no alpha.",
                ).toHaveValue(new RegExp(`^${kRed}(ff)?$`, "i"));
                await closeColorPickerWith(frame, "Cancel");
            },
        );

        await step("Change it back to Speech: it stays opaque", async () => {
            await changeType(page, "speech");
            await expectSaved(
                page,
                bookFolder,
                pageId,
                index,
                (s) =>
                    s.style === "speech" &&
                    s.backgroundColors.length === 1 &&
                    isColorAt(s.backgroundColors[0], kRed, 1) &&
                    allRedShapesAt(s, 1),
                "After it changed from Caption back to Speech, the bubble was not saved with the opaque red background.",
            );
            const frame = await openCanvasElementBackgroundColorPicker(page);
            expect(
                await hasAlphaSlider(frame),
                "Back as a speech bubble, its Background Color picker should offer an alpha slider again.",
            ).toBe(true);
            await closeColorPickerWith(frame, "Cancel");
        });

        expect(errors.errors(), "Bloom raised a script error.").toEqual([]);
    } finally {
        errors.stop();
    }
});

test("A fully transparent Just Text becomes a Caption with the white-to-calico background [Test Case ID 323]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    const errors = watchForScriptErrors(page);
    try {
        let pageId = "";
        let index = -1;

        await step("Drag a Text Block onto a Canvas page", async () => {
            ({ pageId, index } = await addCanvasPageWithElement(page, "none"));
            await expectSaved(
                page,
                bookFolder,
                pageId,
                index,
                (s) =>
                    s.style === "none" &&
                    s.backgroundColors.length === 1 &&
                    s.backgroundColors[0] === "transparent",
                "A new Text Block was not saved as Just Text with a transparent background.",
            );
        });

        await step(
            "Change its Element Type to Caption: it gets the white-to-calico background",
            async () => {
                await changeType(page, "caption");
                await expectSaved(
                    page,
                    bookFolder,
                    pageId,
                    index,
                    (s) =>
                        s.style === "caption" &&
                        // The white-to-calico gradient of bloomPalette.ts.
                        JSON.stringify(s.backgroundColors) ===
                            JSON.stringify(["white", "#dfb28b at 1"]),
                    "After a transparent Just Text changed to Caption, it was not saved with the white-to-calico background.",
                );
                await expectBarText(
                    page,
                    "",
                    "An opaque background's color bar should say nothing about transparency.",
                );
            },
        );

        expect(errors.errors(), "Bloom raised a script error.").toEqual([]);
    } finally {
        errors.stop();
    }
});
