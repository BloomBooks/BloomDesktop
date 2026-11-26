// Where the insertion point lands when Bloom shows a page, and which source bubbles show as a
// person clicks around the page: on a picture, outside the page, and in each text box. Bloom puts
// the insertion point in an empty text-overlay canvas element first, then in any empty text box,
// then in the first plain text box (BL-14265), and shows the bubble of the box that has it.
//
// Automates the manual test "Source Bubble Cursor Focus" (Test Case ID 760).
//
// The case uses a shell book on the Bloom Library sandbox, "Source Bubble Test Book 1 (from
// 6.2.1233 Alpha)" (https://dev.bloomlibrary.org/my-books/book/2GN0oyirik), whose eleven content
// pages put English and French text in every arrangement a source bubble meets: beside a picture,
// over it, several boxes on a page, and text in caption, speech bubble and text-overlay canvas
// elements. The test uses it too, fetched from the sandbox when the test runs, so the test needs
// dev.bloomlibrary.org to be reachable. A person downloads it with the website's "Download into
// Bloom Editor" button, a trip through the system browser that a test cannot drive; the test copies
// the book's files the way Bloom's download does (helpers/bloomLibraryDownload.ts) and makes its
// book from that copy, in a Spanish collection, as the case does.
//
// Which bubble shows is read from Bloom's own markup: the qTip each group's aria-describedby names,
// and whether Bloom has cut it down ("passive-bubble") because another group has the focus.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { downloadBookFromBloomLibrary } from "../helpers/bloomLibraryDownload";
import {
    getContentPages,
    goToPage,
    makeBookFromBookInCollection,
    reloadPageBeingEdited,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    clickIntoGroup,
    clickOnPicture,
    clickOutsidePage,
    expectSourceBubbleStates,
    getFocusedGroupIndex,
    getGroupTexts,
    getSourceBubble,
    getSourceBubbleStates,
    moveMouseAroundWithoutClicking,
    type SourceBubbleState,
    sourceBubbleOverlapsPicture,
} from "../helpers/sourceBubbles";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "source-bubble-cursor-focus", languages: ["es"] },
});

/** "Source Bubble Test Book 1 (from 6.2.1233 Alpha)" on the Bloom Library sandbox. */
const kSandboxBookId = "2GN0oyirik";

/** The language the collection translates into; the book's boxes in it start empty. */
const kVernacular = "es";

// Shorter names for a bubble's states in the tables below.
const NONE: SourceBubbleState = "none";
const FULL: SourceBubbleState = "full";
const CUT: SourceBubbleState = "truncated";

/** Something the case has the tester do on a page, and what it then has them check. */
type Action =
    | { do: "picture" }
    | { do: "outside" }
    | { do: "click"; group: number }
    | { do: "mouse" }
    | {
          /** Type in this group's box, then leave the page and come back to it. */
          do: "type and revisit";
          group: number;
      };

interface ICheck {
    action: Action;
    /** The state each group's bubble is in after the action, in document order. */
    bubbles: SourceBubbleState[];
    /** For "type and revisit": the group Bloom then puts the insertion point in. */
    focus?: number;
    /** The case's words for what the tester does and checks. */
    says: string;
}

interface IPageCase {
    /** The beginning of the English text of each translation group, in document order. */
    groups: string[];
    /** The group Bloom puts the insertion point in when it shows the page. */
    focus: number;
    /** The state each group's bubble is in when Bloom has shown the page. */
    bubbles: SourceBubbleState[];
    says: string;
    checks: ICheck[];
}

// The case's pages, in order. "Mouse" is the case's "moving the mouse around the window without
// clicking does not affect the display of Source bubbles", checked against the states just before.
const PAGES: IPageCase[] = [
    {
        // Picture on Left
        groups: ["Once upon a time"],
        focus: 0,
        bubbles: [FULL],
        says: "Page 1: the Source bubble displays.",
        checks: [
            {
                action: { do: "picture" },
                bubbles: [NONE],
                says: "Page 1, click on the picture: no Source bubble displays.",
            },
            {
                action: { do: "outside" },
                bubbles: [NONE],
                says: "Page 1, click outside the page: no Source bubble displays.",
            },
            {
                action: { do: "click", group: 0 },
                bubbles: [FULL],
                says: "Page 1, click inside the textbox: the Source bubble displays.",
            },
            {
                action: { do: "mouse" },
                bubbles: [FULL],
                says: "Page 1: moving the mouse does not affect the Source bubbles.",
            },
        ],
    },
    {
        // Picture on Right
        groups: ["One day her mother"],
        focus: 0,
        bubbles: [FULL],
        says: "Page 2: the Source bubble displays over the picture.",
        checks: [
            {
                action: { do: "picture" },
                bubbles: [NONE],
                says: "Page 2, click on the picture: the Source bubble disappears.",
            },
            {
                action: { do: "outside" },
                bubbles: [NONE],
                says: "Page 2, click outside the page: no Source bubble displays.",
            },
            {
                action: { do: "click", group: 0 },
                bubbles: [FULL],
                says: "Page 2, click in the textbox: the Source bubble displays over the picture.",
            },
            {
                action: { do: "mouse" },
                bubbles: [FULL],
                says: "Page 2: moving the mouse does not affect the Source bubbles.",
            },
        ],
    },
    {
        // Basic Text & Picture
        groups: ["Little Red Cap promised"],
        focus: 0,
        bubbles: [FULL],
        says: "Page 3: the source bubble appears.",
        checks: [
            {
                action: { do: "picture" },
                bubbles: [FULL],
                says: "Page 3, click on the picture to select it: the source bubble still displays.",
            },
            {
                action: { do: "outside" },
                bubbles: [FULL],
                says: "Page 3, click outside the page: the source bubble still displays.",
            },
        ],
    },
    {
        // Picture on Bottom
        groups: ['"Grandmother is sick'],
        focus: 0,
        bubbles: [FULL],
        says: "Page 4: the Source bubble displays.",
        checks: [
            {
                action: { do: "picture" },
                bubbles: [FULL],
                says: "Page 4, click on the picture to select it: the source bubble still displays.",
            },
            {
                action: { do: "outside" },
                bubbles: [FULL],
                says: "Page 4, click outside the page: the source bubble still displays.",
            },
            {
                action: { do: "mouse" },
                bubbles: [FULL],
                says: "Page 4: moving the mouse does not affect the Source bubbles.",
            },
        ],
    },
    {
        // Picture in Middle, a text box above and below
        groups: ["The wolf thought", "Little Red Cap opened"],
        focus: 0,
        bubbles: [FULL, CUT],
        says: "Page 5: two source bubbles appear, the lower one truncated.",
        checks: [
            {
                action: { do: "outside" },
                bubbles: [CUT, CUT],
                says: "Page 5, click outside of the page: both Source bubbles are visible in truncated form.",
            },
            {
                action: { do: "picture" },
                bubbles: [CUT, CUT],
                says: "Page 5, click on the picture: both Source bubbles are visible in truncated form.",
            },
            {
                action: { do: "click", group: 0 },
                bubbles: [FULL, CUT],
                says: "Page 5, click inside the upper textbox: its Source bubble displays full size, the lower one truncated.",
            },
            {
                action: { do: "click", group: 1 },
                bubbles: [CUT, FULL],
                says: "Page 5, click inside the lower textbox: its Source bubble displays full size, the upper one truncated.",
            },
            {
                action: { do: "mouse" },
                bubbles: [CUT, FULL],
                says: "Page 5: moving the mouse does not affect the Source bubbles.",
            },
        ],
    },
    {
        // Just Text
        groups: ["\"Who's there?"],
        focus: 0,
        bubbles: [FULL],
        says: "Page 6: the Source bubble displays.",
        checks: [
            {
                action: { do: "outside" },
                bubbles: [FULL],
                says: "Page 6, click outside the page: the source bubble displays.",
            },
            {
                action: { do: "mouse" },
                bubbles: [FULL],
                says: "Page 6: moving the mouse does not affect the Source bubbles.",
            },
        ],
    },
    {
        // A picture and three text boxes
        groups: [
            "Then she went",
            '"Oh, grandmother, what big ears',
            '"Oh, grandmother, what big eyes',
        ],
        focus: 0,
        bubbles: [FULL, NONE, NONE],
        says: "Page 7: the Source bubble displays.",
        checks: [
            {
                action: { do: "picture" },
                bubbles: [NONE, NONE, NONE],
                says: "Page 7, click on the picture: the Source bubble disappears.",
            },
            {
                action: { do: "click", group: 0 },
                bubbles: [FULL, NONE, NONE],
                says: "Page 7, click in the first text box: the Source bubble displays for it only.",
            },
            {
                action: { do: "click", group: 1 },
                bubbles: [NONE, FULL, NONE],
                says: "Page 7, click in the second text box: the Source bubble displays for it only.",
            },
            {
                action: { do: "click", group: 2 },
                bubbles: [NONE, NONE, FULL],
                says: "Page 7, click in the third text box: the Source bubble displays for it only.",
            },
            {
                action: { do: "outside" },
                bubbles: [NONE, NONE, NONE],
                says: "Page 7, click outside of the page: no Source bubble displays.",
            },
            {
                action: { do: "mouse" },
                bubbles: [NONE, NONE, NONE],
                says: "Page 7: moving the mouse does not affect the Source bubbles.",
            },
            {
                action: { do: "type and revisit", group: 0 },
                focus: 1,
                bubbles: [NONE, FULL, NONE],
                says: "Page 7, add text to the text box on the left and come back to the page: Bloom puts the insertion point in the next empty text box and shows its bubble.",
            },
            {
                action: { do: "type and revisit", group: 1 },
                focus: 2,
                bubbles: [NONE, NONE, FULL],
                says: "Page 7, add text in the top right text box and refresh the page: Bloom puts the insertion point in the last empty text box and shows its bubble.",
            },
        ],
    },
    {
        // Picture in Middle, three text boxes
        groups: [
            '"Oh, grandmother, what big hands',
            '"Oh, grandmother, what a horribly',
            "And with that",
        ],
        focus: 0,
        bubbles: [FULL, NONE, CUT],
        says: "Page 8: both Source bubbles display.",
        checks: [
            {
                action: { do: "click", group: 0 },
                bubbles: [FULL, NONE, CUT],
                says: "Page 8, click in the top left text box: both source bubbles display.",
            },
            {
                action: { do: "click", group: 2 },
                bubbles: [NONE, NONE, FULL],
                says: "Page 8, click inside the bottom textbox: only its source bubble displays.",
            },
            {
                action: { do: "picture" },
                bubbles: [NONE, NONE, CUT],
                says: "Page 8, click on the picture: only the bottom source bubble (in truncated form) is displayed.",
            },
            {
                action: { do: "picture" },
                bubbles: [NONE, NONE, CUT],
                says: "Page 8, click on the picture again: only the bottom source bubble (in truncated form) is displayed.",
            },
            {
                action: { do: "mouse" },
                bubbles: [NONE, NONE, CUT],
                says: "Page 8: moving the mouse does not affect the Source bubbles.",
            },
        ],
    },
    {
        // Just a Picture, with a caption, a text overlay and a text box
        groups: [
            "He stepped inside",
            "He had cut only",
            "And then the grandmother",
        ],
        focus: 1,
        bubbles: [NONE, FULL, NONE],
        says: "Page 9: one source bubble displays, for a text canvas element.",
        checks: [
            {
                action: { do: "outside" },
                bubbles: [NONE, NONE, NONE],
                says: "Page 9, click outside the page: no Source bubble displays.",
            },
            {
                action: { do: "click", group: 0 },
                bubbles: [FULL, NONE, NONE],
                says: "Page 9, click into the caption box: its Source bubble displays, but no other.",
            },
            {
                action: { do: "click", group: 2 },
                bubbles: [NONE, NONE, FULL],
                says: "Page 9, click into the normal textbox: its Source bubble displays, but no other.",
            },
            {
                action: { do: "picture" },
                bubbles: [NONE, NONE, NONE],
                says: "Page 9, click into the picture away from canvas elements: no Source bubble displays.",
            },
            {
                action: { do: "mouse" },
                bubbles: [NONE, NONE, NONE],
                says: "Page 9: moving the mouse does not affect the Source bubbles.",
            },
        ],
    },
    {
        // Picture on Left, with a speech bubble on the picture
        groups: ['"As long as I live', "The three of them"],
        focus: 0,
        bubbles: [FULL, NONE],
        says: "Page 10: the Source bubble for the Speech Bubble canvas element is displayed.",
        checks: [
            {
                action: { do: "outside" },
                bubbles: [NONE, NONE],
                says: "Page 10, click outside of the page: no Source bubble displays.",
            },
            {
                action: { do: "click", group: 1 },
                bubbles: [NONE, FULL],
                says: "Page 10, click into the text box: its Source bubble displays, but no other.",
            },
            {
                action: { do: "click", group: 0 },
                bubbles: [FULL, NONE],
                says: "Page 10, click into the Canvas text element: its Source bubble displays, but no other.",
            },
            {
                action: { do: "picture" },
                bubbles: [NONE, NONE],
                says: "Page 10, click on the picture outside the canvas element: no Source bubble displays.",
            },
            {
                action: { do: "mouse" },
                bubbles: [NONE, NONE],
                says: "Page 10: moving the mouse does not affect the Source bubbles.",
            },
        ],
    },
    {
        // Picture on Right, with a speech bubble on the picture
        groups: ["The three of them", '"As long as I live'],
        focus: 0,
        bubbles: [FULL, NONE],
        says: "Page 11: the Source bubble for the main text box is shown.",
        checks: [
            {
                action: { do: "outside" },
                bubbles: [NONE, NONE],
                says: "Page 11, click outside of the page: no Source bubble displays.",
            },
            {
                action: { do: "click", group: 1 },
                bubbles: [NONE, FULL],
                says: "Page 11, click into the Speech Bubble canvas element: its Source bubble displays, but no other.",
            },
            {
                action: { do: "click", group: 0 },
                bubbles: [FULL, NONE],
                says: "Page 11, click into the text box: its Source bubble displays, but no other.",
            },
            {
                action: { do: "picture" },
                bubbles: [NONE, NONE],
                says: "Page 11, click on the picture outside the Speech Bubble canvas element: no Source bubble displays.",
            },
            {
                action: { do: "mouse" },
                bubbles: [NONE, NONE],
                says: "Page 11: moving the mouse does not affect the Source bubbles.",
            },
            {
                action: { do: "type and revisit", group: 0 },
                focus: 1,
                bubbles: [NONE, FULL],
                says: "Page 11, add text in the main text box and come back to the page: Bloom puts the insertion point in the empty speech bubble and shows its bubble.",
            },
        ],
    },
];

/** Wait until Bloom has put the insertion point in this group of the page it just showed. */
async function expectFocusInGroup(
    page: Page,
    group: number,
    says: string,
): Promise<void> {
    await expect
        .poll(() => getFocusedGroupIndex(page), {
            timeout: 30000,
            message: `${says} Bloom did not put the insertion point in text box ${group + 1} of the page.`,
        })
        .toBe(group);
}

async function doAction(page: Page, action: Action): Promise<void> {
    switch (action.do) {
        case "picture":
            await clickOnPicture(page);
            break;
        case "outside":
            await clickOutsidePage(page);
            break;
        case "click":
            await clickIntoGroup(page, action.group, kVernacular);
            break;
        case "mouse":
            await moveMouseAroundWithoutClicking(page);
            break;
        case "type and revisit":
            await typeInGroup(
                page,
                ".bloom-translationGroup",
                kVernacular,
                "Hola.",
                action.group,
            );
            await reloadPageBeingEdited(page);
            break;
    }
}

test("the insertion point and the source bubbles follow the clicks on each kind of page [Test Case ID 760]", async ({
    bloomApp,
    step,
}) => {
    test.setTimeout(900000);

    const downloaded = await step(
        "Download the case's book from the Bloom Library sandbox into the collection",
        async () => {
            let folder = "";
            await bloomApp.restart(async () => {
                folder = await downloadBookFromBloomLibrary(
                    kSandboxBookId,
                    bloomApp.collectionDir,
                    "sandbox",
                );
            });
            return folder;
        },
    );
    if (!downloaded) throw new Error("The book was not downloaded.");
    const page = bloomApp.page;

    const contentPages = await step("Make a book from it", async () => {
        await switchTab(page, "collection");
        await makeBookFromBookInCollection(page, downloaded);
        return getContentPages(page);
    });
    if (!contentPages) throw new Error("No book was made from the download.");
    expect(
        contentPages.length,
        "The book does not have the case's eleven content pages.",
    ).toBe(PAGES.length);

    for (const [index, pageCase] of PAGES.entries()) {
        await step(`Page ${index + 1}`, async () => {
            await goToPage(page, contentPages[index].id);
            const english = await getGroupTexts(page, "en");
            expect(
                english.map((text, group) =>
                    text.startsWith(pageCase.groups[group]),
                ),
                `Page ${index + 1} does not have the text boxes the test expects; their English is ${JSON.stringify(english)}.`,
            ).toEqual(pageCase.groups.map(() => true));
            await expectFocusInGroup(page, pageCase.focus, pageCase.says);
            await expectSourceBubbleStates(
                page,
                pageCase.bubbles,
                pageCase.says,
            );
            if (index === 1)
                expect(
                    await sourceBubbleOverlapsPicture(page, 0),
                    `${pageCase.says} The bubble is not over the picture.`,
                ).toBe(true);
            if (index === 2) {
                const bubble = await getSourceBubble(
                    page,
                    ".bloom-translationGroup",
                    kVernacular,
                );
                expect(
                    bubble.texts.map((text) => text.lang),
                    "Page 3: the source bubble does not offer both English and French.",
                ).toEqual(["en", "fr"]);
            }
            for (const check of pageCase.checks) {
                const before = await getSourceBubbleStates(page);
                await doAction(page, check.action);
                if (check.action.do === "mouse") {
                    expect(
                        await getSourceBubbleStates(page),
                        check.says,
                    ).toEqual(before);
                } else if (check.focus !== undefined) {
                    await expectFocusInGroup(page, check.focus, check.says);
                }
                await expectSourceBubbleStates(page, check.bubbles, check.says);
                if (
                    index === 1 &&
                    check.action.do === "click" &&
                    check.bubbles[0] === FULL
                )
                    expect(
                        await sourceBubbleOverlapsPicture(page, 0),
                        `${check.says} The bubble is not over the picture.`,
                    ).toBe(true);
            }
        });
    }
});
