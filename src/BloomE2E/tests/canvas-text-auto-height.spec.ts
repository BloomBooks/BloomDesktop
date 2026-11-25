// The height of a text box on a canvas page. With Auto Height on, which a new Text Block has, the
// box grows to fit what is typed. With it turned off on the box's "..." menu, the box gets a handle
// in the middle of its bottom edge, and dragged smaller than its text it scrolls; typing more then
// leaves its height alone. Turned back on,
// the box grows again to show all of its text.
//
// Automates "Canvas Text Box Height" (Test Case ID 305).

import { expect, test } from "../fixtures/bloomTest";
import { addPage, makeBookFromTemplate } from "../helpers/bookMaking";
import {
    dragCanvasElementSide,
    dragPaletteItemOntoCanvas,
    getCanvasElementTextFit,
    isCanvasElementAutoHeight,
    isCanvasElementSideHandleShown,
    selectCanvasElement,
    setCanvasElementAutoHeight,
    typeInCanvasElement,
    waitForCanvasElementScrollBar,
    type ICanvasTextFit,
} from "../helpers/canvasElements";
import { watchForScriptErrors } from "../helpers/scriptErrors";

test.use({
    collectionSpec: { name: "canvas-text-auto-height", languages: ["en"] },
});

const kSentences =
    "The little goat climbed the hill. The wind blew across the grass. " +
    "At the top she found her friends waiting for her.";

/** The height the case asks the box to be dragged down to. */
const kSmallHeight = 20;

/** Assert that a box is tall enough for all of its text. */
function expectAllTextShows(fit: ICanvasTextFit, when: string): void {
    expect(
        fit.textHeight,
        `${when}, the text box's text is taller than the part that shows.`,
    ).toBeLessThanOrEqual(fit.shownHeight);
}

test("a canvas text box fits its height to its text only while Auto Height is on [Test Case ID 305]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Canvas");
    const textBlock = await dragPaletteItemOntoCanvas(page, "none");
    await selectCanvasElement(page, textBlock);

    // Sanity check what the rest stands on: a new Text Block has Auto Height on, so no bottom handle.
    expect(await isCanvasElementAutoHeight(page)).toBe(true);
    expect(await isCanvasElementSideHandleShown(page, "s")).toBe(false);
    const empty = await getCanvasElementTextFit(page);

    const errors = watchForScriptErrors(page);
    try {
        // With Auto Height on, the box grows to fit the sentences typed into it.
        await typeInCanvasElement(page, kSentences, { withKeys: true });
        await expect
            .poll(
                async () => (await getCanvasElementTextFit(page)).elementHeight,
                {
                    timeout: 15000,
                    message:
                        "The text box did not grow as sentences were typed into it.",
                },
            )
            .toBeGreaterThan(empty.elementHeight);
        expectAllTextShows(
            await getCanvasElementTextFit(page),
            "With Auto Height on, after typing",
        );
        await waitForCanvasElementScrollBar(page, false);

        // Turning Auto Height off gives the box a handle in the middle of its bottom edge.
        await setCanvasElementAutoHeight(page, false);
        await expect
            .poll(() => isCanvasElementSideHandleShown(page, "s"), {
                timeout: 15000,
                message:
                    "Turning Auto Height off did not show the bottom handle.",
            })
            .toBe(true);

        // Dragging that handle up until the box is about 20px high makes the box scroll.
        const grown = await getCanvasElementTextFit(page);
        const { after } = await dragCanvasElementSide(
            page,
            "s",
            kSmallHeight - grown.elementHeight,
        );
        expect(
            after.height,
            "Dragging the bottom handle up did not make the box shorter.",
        ).toBeLessThan(grown.elementHeight);
        const squashed = await getCanvasElementTextFit(page);
        expect(
            squashed.textHeight,
            "The squashed box's text is not taller than the box, so it has no reason to scroll.",
        ).toBeGreaterThan(squashed.shownHeight);
        await waitForCanvasElementScrollBar(page, true);

        // With Auto Height off, typing more does not grow the box. Bloom fits a box's height in a
        // zero-delay timeout after each key comes up, and typeInCanvasElement returns only once
        // Bloom has received the typed text, so by then any growing would have happened.
        await typeInCanvasElement(page, " The sun went down.", {
            withKeys: true,
        });
        const typedMore = await getCanvasElementTextFit(page);
        expect(
            typedMore.textHeight,
            "Typing more into the squashed box did not add to its text.",
        ).toBeGreaterThan(squashed.textHeight);
        expect(
            typedMore.elementHeight,
            "With Auto Height off, typing more changed the box's height.",
        ).toBe(squashed.elementHeight);

        // Turning Auto Height back on grows the box until all its text shows, with no scrollbar.
        await setCanvasElementAutoHeight(page, true);
        await expect
            .poll(
                async () => (await getCanvasElementTextFit(page)).elementHeight,
                {
                    timeout: 15000,
                    message:
                        "Turning Auto Height back on did not grow the squashed box.",
                },
            )
            .toBeGreaterThan(squashed.elementHeight);
        expectAllTextShows(
            await getCanvasElementTextFit(page),
            "After Auto Height was turned back on",
        );
        await waitForCanvasElementScrollBar(page, false);
        expect(await isCanvasElementSideHandleShown(page, "s")).toBe(false);

        expect(
            errors.errors(),
            "Changing the text box's height raised a script error.",
        ).toEqual([]);
    } finally {
        errors.stop();
    }
});
