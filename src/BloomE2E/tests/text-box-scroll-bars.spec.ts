// The scroll bar of a text box whose text does not fit it (BL-17110). Bloom gives such a box a
// scroll bar, and it rebuilds the page's scroll bars only when a box's text starts or stops
// fitting, not after every check of every box. So three things have to keep working without a
// rebuild on every keystroke: the scroll bar comes when the text stops fitting, it follows the
// text as more is typed (a shorter handle, and the whole text still in reach of the mouse wheel),
// and it goes when the text fits again, leaving the box's text where the author placed it.
//
// The first steps run on two pages of one book: one whose text starts at the top of its box, and
// one whose text is centred in it, which Bloom has to stop centring while the scroll bar is there.
// The tests are serial and share the book; the mouse wheel test is last, on the second page.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    getTextBoxScrollBar,
    getTextBoxScrollPosition,
    getVerticalAlignment,
    retypeGroupWithKeys,
    typeAtEndOfGroup,
    waitForShorterScrollBarHandle,
    waitForTextBoxScrollBar,
    waitForVerticalAlignment,
    wheelTextBoxTo,
    type VerticalAlignment,
} from "../helpers/textBoxScrollBars";

test.use({
    collectionSpec: { name: "text-box-scroll-bars", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The page's one text box.
const GROUP = ".bloom-translationGroup";
// More than any text box of an A5 page holds.
const TOO_MUCH_TEXT = "The quick brown fox jumps over the lazy dog. ".repeat(
    40,
);

// The template pages the steps run on, in the order they are added to the book, and how each
// places its text.
const CASES: { templatePage: string; alignment: VerticalAlignment }[] = [
    { templatePage: "Basic Text & Image", alignment: "top" },
    { templatePage: "Image on Left", alignment: "center" },
];

test.describe("the scroll bar of a text box that is too full", () => {
    CASES.forEach(({ templatePage, alignment }, index) => {
        test(`comes, follows the text, and goes: text placed at the ${alignment} ("${templatePage}")`, async ({
            page,
            step,
        }) => {
            // The first of these also pays for launching Bloom and making the book.
            test.setTimeout(300000);
            await step(`Go to a new "${templatePage}" page`, async () => {
                if (index === 0) await makeBookFromTemplate(page, "Basic Book");
                await addPage(page, templatePage);
                const contentPages = await getContentPages(page);
                await goToPage(page, contentPages[contentPages.length - 1].id);
                expect(
                    await getVerticalAlignment(page, GROUP),
                    `The text box of "${templatePage}" should place its text at the ${alignment}, or this is not the case it claims to be.`,
                ).toBe(alignment);
                expect(
                    (await getTextBoxScrollBar(page, GROUP, "en")).showing,
                    "An empty text box should have no scroll bar.",
                ).toBe(false);
            });

            const first = await step(
                "Type until the text does not fit: a scroll bar comes",
                async () => {
                    await typeAtEndOfGroup(
                        page,
                        GROUP,
                        "en",
                        TOO_MUCH_TEXT,
                        "one",
                    );
                    return waitForTextBoxScrollBar(page, GROUP, "en", true);
                },
            );
            if (!first) throw new Error("The scroll bar was never read.");

            await step("Keep typing: the handle gets shorter", async () => {
                await typeAtEndOfGroup(page, GROUP, "en", TOO_MUCH_TEXT, "two");
                await waitForShorterScrollBarHandle(page, GROUP, "en", first);
            });

            await step(
                "Cut the text down until it fits: the scroll bar goes",
                async () => {
                    await retypeGroupWithKeys(page, GROUP, "en", "short");
                    await waitForTextBoxScrollBar(page, GROUP, "en", false);
                    await waitForVerticalAlignment(
                        page,
                        GROUP,
                        alignment,
                        "With the scroll bar gone, the text should be placed where it was before.",
                    );
                },
            );
        });
    });

    // The scroll bar that came with the first lot of text is still the one beside the box after
    // more is typed, so it has to have learned how long the text now is. The typing starts with
    // Ctrl+End, and that scroll bar ignores the wheel from a Ctrl keydown until a keyup without
    // Ctrl reaches the document, so the keyups of the typing have to get there.
    test("the mouse wheel reaches both ends of text typed after the scroll bar came", async ({
        page,
        step,
    }) => {
        await step(
            "Type until a scroll bar comes, then as much again",
            async () => {
                await typeAtEndOfGroup(page, GROUP, "en", TOO_MUCH_TEXT, "one");
                await waitForTextBoxScrollBar(page, GROUP, "en", true);
                await typeAtEndOfGroup(page, GROUP, "en", TOO_MUCH_TEXT, "two");
            },
        );
        await step("Turn the wheel back to the start of the text", async () => {
            await wheelTextBoxTo(page, GROUP, "en", "start");
            expect(
                (await getTextBoxScrollPosition(page, GROUP, "en")).hiddenAbove,
                "The wheel should bring the start of the text into view.",
            ).toBe(0);
        });
        await step("Turn the wheel on to the very end", async () => {
            await wheelTextBoxTo(page, GROUP, "en", "end");
            expect(
                (await getTextBoxScrollPosition(page, GROUP, "en")).hiddenBelow,
                "The wheel should reach the very end of the text typed after the scroll bar came.",
            ).toBeLessThanOrEqual(1);
        });
    });
});
