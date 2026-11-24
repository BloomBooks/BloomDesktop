// The two choice games of the Games template, played in Bloom's BloomPUB preview, which is Bloom
// Player itself: "Choose Word from Image" (a picture, and buttons with words) and "Choose Image from
// Word" (a word, and buttons with pictures).
//
// The book has two word pages and one picture page. In the preview, each word fits inside its
// button, descenders included; a wrong answer is marked wrong and plays the wrong sound; the right
// answer is marked right and plays the right sound; and Bloom Player shuffles the answers, so the
// right one is not always first as it is in the Edit tab. Then the book is turned to the other
// orientation, and the pages rearrange.
//
// What the marks look like (a dimmed button, a grey border, a rectangle in the theme's color) is
// styling the page declares; this checks the marks Bloom Player sets, not their colors. Which sound
// plays is checked by its file; that it can be heard is not.
//
// choose-word-picture-activities-basic.spec.ts covers a collection without a subscription.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    clickPreviewChoice,
    expectPreviewSoundsPlayed,
    getPreviewChoices,
    goToPreviewPage,
    startRecordingPreviewSounds,
} from "../helpers/bloomPlayer";
import {
    chooseChoicePictures,
    chooseChoicePromptPicture,
    describeChoiceArrangement,
    getChoicePageLayout,
    getChoiceRolesInEditOrder,
    typeChoiceWords,
    type IChoiceArrangement,
} from "../helpers/choiceActivities";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { expectInside } from "../helpers/geometry";
import { getPageSize, setPageSize } from "../helpers/pageSize";
import {
    openPublishDestination,
    refreshBloomPubPreview,
    showBloomPubPreview,
} from "../helpers/publish";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "choice-activities",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const LANG = "en";

const IMAGES = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
);
const BIRD = Path.join(IMAGES, "bird.png");
const SQUARE = Path.join(IMAGES, "blue-square.png");
const HOUSE = Path.join(IMAGES, "house-landscape.png");

// The right answers have descenders (g, j, p, y), so that a button too short for them shows.
const FIRST_WORD_PAGE = { correct: "jumpy", wrong: ["bird", "tree"] };
const SECOND_WORD_PAGE = { correct: "puppy", wrong: ["cat", "hen"] };
const PICTURE_PAGE_WORD = "house";
const PICTURE_PAGE = { correct: HOUSE, wrong: [BIRD, SQUARE] };

const WRONG_SOUND = "wrong_answer.mp3";
const RIGHT_SOUND = "right_answer.mp3";

// Bloom Player shuffles at random, so a page can come out in the Edit tab's order by chance (one
// time in three for three answers). Rebuilding the preview shuffles again; this many rebuilds
// without a single change of order would happen by chance about once in sixty thousand runs.
const MAX_PREVIEW_BUILDS = 10;

// The arrangements the card describes, for each kind of page and orientation.
const WORD_PAGE_PORTRAIT: IChoiceArrangement = {
    prompt: "above",
    choices: "column",
};
const WORD_PAGE_LANDSCAPE: IChoiceArrangement = {
    prompt: "beside",
    choices: "column",
};
const PICTURE_PAGE_PORTRAIT: IChoiceArrangement = {
    prompt: "above",
    choices: "column",
};
const PICTURE_PAGE_LANDSCAPE: IChoiceArrangement = {
    prompt: "above",
    choices: "row",
};

test("Choose Word from Image and Choose Image from Word play, shuffle and rearrange [Test Case ID 534]", async ({
    page,
    step,
}) => {
    test.setTimeout(600000);
    await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Choose Word from Image", 2);
    await addPage(page, "Choose Image from Word");
    const [firstWordPage, secondWordPage, picturePage] =
        await getContentPages(page);

    await goToPage(page, firstWordPage.id);
    const editOrder = await getChoiceRolesInEditOrder(page);
    expect(
        editOrder,
        "Sanity check: in the Edit tab the right answer is the first of three buttons",
    ).toEqual(["correct-answer", "wrong-answer", "wrong-answer"]);
    await chooseChoicePromptPicture(page, BIRD);
    await typeChoiceWords(page, LANG, FIRST_WORD_PAGE);

    await goToPage(page, secondWordPage.id);
    await chooseChoicePromptPicture(page, SQUARE);
    await typeChoiceWords(page, LANG, SECOND_WORD_PAGE);

    await goToPage(page, picturePage.id);
    expect(await getChoiceRolesInEditOrder(page)).toEqual(editOrder);
    await typeInGroup(page, ".word-to-match", LANG, PICTURE_PAGE_WORD);
    await chooseChoicePictures(page, PICTURE_PAGE);
    // Leaving the page saves it.
    await goToPage(page, firstWordPage.id);

    await switchTab(page, "publish");
    await openPublishDestination(page, "BloomPUB");
    let player = await showBloomPubPreview(page);
    await startRecordingPreviewSounds(player);

    await step("Check that each word fits in its button", async () => {
        for (const wordPage of [firstWordPage, secondWordPage]) {
            await goToPreviewPage(page, player, wordPage.id);
            for (const choice of await getPreviewChoices(player)) {
                expect(
                    choice.textRect,
                    `The "${choice.text}" button shows no text`,
                ).toBeDefined();
                expectInside(
                    choice.textRect!,
                    choice.rect,
                    `the word "${choice.text}"`,
                    "its button",
                );
            }
        }
    });

    await step("Answer the first word page wrong, then right", async () => {
        await goToPreviewPage(page, player, firstWordPage.id);
        await clickPreviewChoice(player, "wrong-answer");
        await expectPreviewSoundsPlayed(
            player,
            [WRONG_SOUND],
            "A wrong answer should play the wrong sound",
        );
        await clickPreviewChoice(player, "correct-answer");
        await expectPreviewSoundsPlayed(
            player,
            [WRONG_SOUND, RIGHT_SOUND],
            "The right answer should play the right sound",
        );
        const choices = await getPreviewChoices(player);
        expect(choices.filter((c) => c.chosenWrong).map((c) => c.role)).toEqual(
            ["wrong-answer"],
        );
        expect(
            choices.filter((c) => c.chosenCorrect).map((c) => c.role),
        ).toEqual(["correct-answer"]);
    });

    await step("Answer the second word page right", async () => {
        await goToPreviewPage(page, player, secondWordPage.id);
        await clickPreviewChoice(player, "correct-answer");
        await expectPreviewSoundsPlayed(
            player,
            [WRONG_SOUND, RIGHT_SOUND, RIGHT_SOUND],
            "The right answer on the second page should play the right sound",
        );
    });

    await step("Answer the picture page wrong, then right", async () => {
        await goToPreviewPage(page, player, picturePage.id);
        await clickPreviewChoice(player, "wrong-answer");
        await expectPreviewSoundsPlayed(
            player,
            [WRONG_SOUND, RIGHT_SOUND, RIGHT_SOUND, WRONG_SOUND],
            "A wrong picture should play the wrong sound",
        );
        await clickPreviewChoice(player, "correct-answer");
        await expectPreviewSoundsPlayed(
            player,
            [WRONG_SOUND, RIGHT_SOUND, RIGHT_SOUND, WRONG_SOUND, RIGHT_SOUND],
            "The right picture should play the right sound",
        );
        const choices = await getPreviewChoices(player);
        expect(choices.filter((c) => c.chosenWrong).map((c) => c.role)).toEqual(
            ["wrong-answer"],
        );
        expect(
            choices.filter((c) => c.chosenCorrect).map((c) => c.role),
        ).toEqual(["correct-answer"]);
    });

    await step(
        "Check that the answers are shuffled on a page of each kind",
        async () => {
            const shuffled = { word: false, picture: false };
            for (
                let build = 1;
                build <= MAX_PREVIEW_BUILDS &&
                !(shuffled.word && shuffled.picture);
                build++
            ) {
                if (build > 1)
                    player = await refreshBloomPubPreview(page, player);
                for (const wordPage of [firstWordPage, secondWordPage]) {
                    await goToPreviewPage(page, player, wordPage.id);
                    const order = (await getPreviewChoices(player)).map(
                        (c) => c.role,
                    );
                    if (JSON.stringify(order) !== JSON.stringify(editOrder))
                        shuffled.word = true;
                }
                await goToPreviewPage(page, player, picturePage.id);
                const order = (await getPreviewChoices(player)).map(
                    (c) => c.role,
                );
                if (JSON.stringify(order) !== JSON.stringify(editOrder))
                    shuffled.picture = true;
            }
            expect(
                shuffled,
                `In ${MAX_PREVIEW_BUILDS} builds of the preview, Bloom Player should have put the answers of a page of each kind in an order other than the Edit tab's`,
            ).toEqual({ word: true, picture: true });
        },
    );

    await step(
        "Turn the book to the other orientation and check the pages rearrange",
        async () => {
            await switchTab(page, "edit");
            await goToPage(page, firstWordPage.id);
            const portrait = await getPageSize(page);
            expect(
                portrait,
                "Sanity check: a Basic Book starts in a portrait size",
            ).toMatch(/Portrait$/);
            expect(
                describeChoiceArrangement(await getChoicePageLayout(page)),
            ).toEqual(WORD_PAGE_PORTRAIT);
            await goToPage(page, picturePage.id);
            expect(
                describeChoiceArrangement(await getChoicePageLayout(page)),
            ).toEqual(PICTURE_PAGE_PORTRAIT);

            await setPageSize(page, portrait.replace(/Portrait$/, "Landscape"));
            expect(
                describeChoiceArrangement(await getChoicePageLayout(page)),
                "In landscape the word should be above a row of pictures",
            ).toEqual(PICTURE_PAGE_LANDSCAPE);
            await goToPage(page, firstWordPage.id);
            expect(
                describeChoiceArrangement(await getChoicePageLayout(page)),
                "In landscape the picture should be beside a column of buttons",
            ).toEqual(WORD_PAGE_LANDSCAPE);
        },
    );
});
