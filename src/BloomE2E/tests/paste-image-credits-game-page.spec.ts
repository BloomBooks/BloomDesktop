// "Paste Image Credits" in a book whose only pictures are a game page's stock pictures (BL-14829).
// Automates the manual test "No Spam Game Image Credits" (Test Case ID 791).
//
// A "Drag Images to Targets" page from the Games template brings along its pictures: the
// smiling-flowers.gif and sad-face.gif that show when an answer is right or wrong, which carry
// credits of their own (Ilona Spaeder, Pixabay license), and the pictures on its Check, Try Again,
// Show Answer and page-turn buttons, which have none. Bloom leaves the button pictures out of the
// credits (ImageApi.IsImgInsideGameButton), so they must not be listed as missing credits, and it
// shows the Pixabay license without its URL. So the pasted credits must be one line, crediting
// Ilona Spaeder under the Pixabay license, with no link and nothing missing.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import { readSavedPagePictures } from "../helpers/bookHtml";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { makeBookWithGamePage } from "../helpers/games";
import {
    getOriginalContributions,
    goToTitlePage,
    originalContributionsBox,
    pasteImageCredits,
} from "../helpers/titlePage";

test.use({
    collectionSpec: {
        name: "paste-image-credits-game-page",
        languages: ["en"],
        // Games need a subscription that includes them.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

// The holder named in the credits of the Games template's GIFs.
const GAME_GIF_HOLDER = "Ilona Spaeder";

// Pictures the game page brings into the book: the two credited GIFs, and pictures on its buttons,
// which have no credits.
const GAME_GIFS = ["smiling-flowers.gif", "sad-face.gif"];
const GAME_BUTTON_PICTURES = [
    "Check Answer Symbol.svg",
    "Try Again Symbol.svg",
    "Show Answer Symbol.svg",
    "Page Nav.svg",
];

test("Paste Image Credits credits a game page's pictures once, without the Pixabay link or missing credits [Test Case ID 791]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);

    let bookFolder = "";
    let gamePageId = "";
    await step(
        'Create a book and add a "Drag Images to Targets" game page',
        async () => {
            ({ bookFolder, gamePageId } = await makeBookWithGamePage(
                page,
                "Drag Images to Targets",
            ));
        },
    );

    let credits: string[] = [];
    let creditsMarkup = "";
    await step(
        'Go to the title page and click "Paste Image Credits"',
        async () => {
            await goToTitlePage(page);
            expect(
                await getOriginalContributions(page),
                "The title page's contributions box should start empty.",
            ).toEqual([]);
            await pasteImageCredits(page);
            credits = await getOriginalContributions(page);
            creditsMarkup = await originalContributionsBox(page).innerHTML();
        },
    );

    // Bloom credits the pictures that the book's pages show, as the book holds them
    // (ImageApi.GetWhichImagesAreUsedOnWhichPages reads the book's markup), and skips a picture
    // whose file is not in the book folder. So the saved game page must show the GIFs, and the
    // button pictures on its buttons, with their files in the folder, for the check below to test
    // how Bloom treats them. Leaving the game page for the title page saved it.
    const savedGamePage = await readSavedPagePictures(
        page,
        bookFolder,
        gamePageId,
    );
    expect(
        GAME_BUTTON_PICTURES.filter(
            (name) => !savedGamePage.onButtons.includes(name),
        ),
        "Sanity check: the saved game page should show each button picture on a game or page-turn button.",
    ).toEqual([]);
    expect(
        GAME_GIFS.filter((name) => !savedGamePage.elsewhere.includes(name)),
        "Sanity check: the saved game page should show both GIFs.",
    ).toEqual([]);
    expect(
        [...GAME_GIFS, ...GAME_BUTTON_PICTURES].filter(
            (name) => !fs.existsSync(Path.join(bookFolder, name)),
        ),
        "Sanity check: the game page should have brought its pictures into the book folder.",
    ).toEqual([]);

    await step(
        "Check that the only credit is the game pictures', without the Pixabay link",
        async () => {
            expect(
                credits,
                "Expected exactly one line of credits: the game's GIFs, which share one credit. " +
                    "Button pictures have no credits and must not be listed as missing them.",
            ).toHaveLength(1);
            const [credit] = credits;
            // Bloom writes "Images by {0}." when every credited picture has the same credit.
            expect(credit).toMatch(
                new RegExp(`^Images by ${GAME_GIF_HOLDER}\\b`),
            );
            expect(credit).toContain(`© 2025 ${GAME_GIF_HOLDER}`);
            expect(credit).toMatch(/Pixabay license\.$/);
            expect(
                credit,
                "The Pixabay license should be shown without its URL.",
            ).not.toMatch(/pixabay\.com|https?:/i);
            // Nor hidden behind a link: the box's markup must not hold it either.
            expect(
                creditsMarkup,
                "The pasted credits should hold no link to Pixabay.",
            ).not.toMatch(/<a\b|href=|pixabay\.com|https?:/i);
        },
    );
});
