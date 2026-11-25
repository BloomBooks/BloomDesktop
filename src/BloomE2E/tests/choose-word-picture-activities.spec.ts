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
    getChoicePicturesInEditOrder,
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

// The words of each word page in the order the Edit tab holds them: the right answer first.
const WORDS_IN_EDIT_ORDER = {
    firstWordPage: [FIRST_WORD_PAGE.correct, ...FIRST_WORD_PAGE.wrong],
    secondWordPage: [SECOND_WORD_PAGE.correct, ...SECOND_WORD_PAGE.wrong],
};

// Bloom Player shuffles at random, so a page's right answer can come out first, where the Edit tab
// has it, by chance: one time in three for three answers. Rebuilding the preview shuffles again. A
// correct Bloom Player leaves the picture page's right answer first through this many builds once
// in 3 to the power of 30 runs, which is 2 followed by 14 zeroes, so a failure here is not chance.
const MAX_PREVIEW_BUILDS = 30;

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
    const picturesInEditOrder = await getChoicePicturesInEditOrder(page);
    expect(
        new Set(picturesInEditOrder).size,
        `Sanity check: the picture page's three answers should be three different pictures, not ${picturesInEditOrder.join(", ")}`,
    ).toBe(3);
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

    // The template's pages can carry a mark on an answer from the Edit tab; Bloom Player must
    // show a page with no answer marked until the reader chooses one.
    const expectNoAnswerMarked = async () =>
        expect(
            (await getPreviewChoices(player))
                .filter((c) => c.chosenCorrect || c.chosenWrong)
                .map((c) => c.role),
            "No answer should be marked before the reader chooses one",
        ).toEqual([]);

    await step("Answer the first word page wrong, then right", async () => {
        await goToPreviewPage(page, player, firstWordPage.id);
        await expectNoAnswerMarked();
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
        await expectNoAnswerMarked();
        await clickPreviewChoice(player, "correct-answer");
        await expectPreviewSoundsPlayed(
            player,
            [WRONG_SOUND, RIGHT_SOUND, RIGHT_SOUND],
            "The right answer on the second page should play the right sound",
        );
    });

    await step("Answer the picture page wrong, then right", async () => {
        await goToPreviewPage(page, player, picturePage.id);
        await expectNoAnswerMarked();
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
            // Whether Bloom Player shows this page's right and wrong answers in an order other
            // than the Edit tab's, so that the right one is no longer first. Whatever the
            // order, they must be the answers the page has in the Edit tab, each known by its
            // word or its picture's name: a page that lost one would otherwise count as
            // shuffled. Bloom Player loads a page's pictures as it comes to the page, so
            // this waits for them.
            const isShuffled = async (pageId: string, answers: string[]) => {
                await goToPreviewPage(page, player, pageId);
                let roles: string[] = [];
                await expect
                    .poll(
                        async () => {
                            const shown = await getPreviewChoices(player);
                            roles = shown.map((c) => c.role);
                            return shown.map((c) => c.text || c.picture).sort();
                        },
                        {
                            message:
                                "Bloom Player should offer the answers the page has in the Edit tab",
                            timeout: 15000,
                        },
                    )
                    .toEqual([...answers].sort());
                return JSON.stringify(roles) !== JSON.stringify(editOrder);
            };
            const shuffled = { word: false, picture: false };
            for (
                let build = 1;
                build <= MAX_PREVIEW_BUILDS &&
                !(shuffled.word && shuffled.picture);
                build++
            ) {
                if (build > 1)
                    player = await refreshBloomPubPreview(page, player);
                if (
                    await isShuffled(
                        firstWordPage.id,
                        WORDS_IN_EDIT_ORDER.firstWordPage,
                    )
                )
                    shuffled.word = true;
                if (
                    await isShuffled(
                        secondWordPage.id,
                        WORDS_IN_EDIT_ORDER.secondWordPage,
                    )
                )
                    shuffled.word = true;
                if (await isShuffled(picturePage.id, picturesInEditOrder))
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
