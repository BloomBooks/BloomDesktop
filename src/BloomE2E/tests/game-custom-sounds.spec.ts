// The correct and wrong sounds that the Game tool lets a person choose for a comprehension quiz page
// and for a simple choice page ("Choose Word from Image"), played in Bloom's BloomPUB preview, which
// is Bloom Player itself.
//
// The book gets a quiz page and a choice page, and each is given its own sound files for a correct
// and a wrong answer, through the sound menus' "Choose..." item. In the preview, a wrong answer and
// then the right one on each page play that page's chosen sounds. Then a second quiz page and a
// second choice page are added without touching their sounds: in the rebuilt preview they play
// Bloom Player's default sounds, while the first two pages still play their chosen ones.
//
// Which sound plays is checked by its file; that it can be heard is not.
//
// Automates the manual test "Quiz right/wrong sound controls" (Test Case ID 693).

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Frame, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    clickPreviewChoice,
    clickPreviewQuizAnswer,
    expectPreviewSoundsPlayed,
    goToPreviewPage,
    startRecordingPreviewSounds,
} from "../helpers/bloomPlayer";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForSnapshotStreamIdle,
} from "../helpers/bookMaking";
import {
    chooseChoicePromptPicture,
    typeChoiceWords,
} from "../helpers/choiceActivities";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    chooseGameSoundFile,
    getGameSound,
    type GameSoundType,
} from "../helpers/games";
import {
    openPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import { realClick } from "../helpers/realClick";
import { expandToolByHeader } from "../helpers/toolbox";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "game-custom-sounds",
        languages: ["en"],
        // Games need a subscription.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const LANG = "en";

const FIXTURES = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
);
const SAMPLE_SOUND = Path.join(FIXTURES, "audio", "sample.mp3");
const BIRD = Path.join(FIXTURES, "images", "bird.png");

/** The Add Page label of the comprehension quiz page. */
const kQuizPageLabel = "Comprehension Quiz";
/** The Add Page label of the simple choice page, in the Games template. */
const kChoicePageLabel = "Choose Word from Image";

const DEFAULT_RIGHT_SOUND = "right_answer.mp3";
const DEFAULT_WRONG_SOUND = "wrong_answer.mp3";

/**
 * The sound files chosen for the first quiz page and the first choice page. Each page has its own
 * pair, so a page that played the other page's sound, or swapped its own two, shows.
 */
const QUIZ_SOUNDS: Record<GameSoundType, string> = {
    correct: "quiz-cheer.mp3",
    wrong: "quiz-buzz.mp3",
};
const CHOICE_SOUNDS: Record<GameSoundType, string> = {
    correct: "choice-cheer.mp3",
    wrong: "choice-buzz.mp3",
};

function groupWithStyle(style: string): string {
    // The template gives the group the style's class, and Bloom moves it onto the group's boxes.
    return `.bloom-translationGroup:is(.${style}, :has(> .${style}))`;
}
const kQuestionGroup = groupWithStyle("QuizQuestion-style");
const kAnswerGroup = groupWithStyle("QuizAnswer-style");

/** On the quiz page being shown, type a question and answers, and tick the correct answer. */
async function fillQuizPage(
    page: Page,
    question: string,
    answers: string[],
    correct: number,
): Promise<void> {
    await typeInGroup(page, kQuestionGroup, LANG, question);
    for (const [index, answer] of answers.entries())
        await typeInGroup(page, kAnswerGroup, LANG, answer, index);
    const choice = editablePageFrame(page)
        .locator(".checkbox-and-textbox-choice")
        .nth(correct);
    await realClick(choice.locator("input.styled-check-box"));
    await expect(
        choice,
        `"${answers[correct]}" should be marked as the correct answer`,
    ).toHaveClass(/\bcorrect-answer\b/);
    await waitForSnapshotStreamIdle(page);
}

/** Add a page with this label after the page shown, and return its id. */
async function addPageAndGetId(page: Page, label: string): Promise<string> {
    const before = new Set((await getContentPages(page)).map((p) => p.id));
    await addPage(page, label);
    const added = (await getContentPages(page)).filter(
        (p) => !before.has(p.id),
    );
    expect(added.length, `Adding a "${label}" page should add one page`).toBe(
        1,
    );
    return added[0].id;
}

/** Make sound files with these names, copies of a sample, and return their paths by kind. */
function makeSoundFiles(
    names: Record<GameSoundType, string>,
): Record<GameSoundType, string> {
    const folder = test.info().outputPath("sounds");
    fs.mkdirSync(folder, { recursive: true });
    const paths = {} as Record<GameSoundType, string>;
    for (const kind of ["correct", "wrong"] as GameSoundType[]) {
        paths[kind] = Path.join(folder, names[kind]);
        fs.copyFileSync(SAMPLE_SOUND, paths[kind]);
    }
    return paths;
}

/** Show the page in the Edit tab with the Game tool open, and give it these sound files. */
async function chooseSoundsForPage(
    page: Page,
    pageId: string,
    files: Record<GameSoundType, string>,
): Promise<void> {
    await goToPage(page, pageId);
    await expandToolByHeader(page, "game");
    expect(
        [
            await getGameSound(page, "correct"),
            await getGameSound(page, "wrong"),
        ],
        "Sanity check: a new page starts with the default sounds",
    ).toEqual(["default", "default"]);
    await chooseGameSoundFile(page, "correct", files.correct);
    await chooseGameSoundFile(page, "wrong", files.wrong);
}

/**
 * In the preview, answer the quiz page wrong and then right, and expect `sounds` (wrong first)
 * to be the two sounds played after `playedBefore`. Returns every sound played so far.
 */
async function answerQuiz(
    page: Page,
    player: Frame,
    pageId: string,
    sounds: Record<GameSoundType, string>,
    playedBefore: string[],
    what: string,
): Promise<string[]> {
    await goToPreviewPage(page, player, pageId);
    await clickPreviewQuizAnswer(player, false);
    const afterWrong = [...playedBefore, sounds.wrong];
    await expectPreviewSoundsPlayed(
        player,
        afterWrong,
        `A wrong answer on ${what} should play ${sounds.wrong}`,
    );
    await clickPreviewQuizAnswer(player, true);
    const afterRight = [...afterWrong, sounds.correct];
    await expectPreviewSoundsPlayed(
        player,
        afterRight,
        `The right answer on ${what} should play ${sounds.correct}`,
    );
    return afterRight;
}

/** As answerQuiz, for a choice page. */
async function answerChoice(
    page: Page,
    player: Frame,
    pageId: string,
    sounds: Record<GameSoundType, string>,
    playedBefore: string[],
    what: string,
): Promise<string[]> {
    await goToPreviewPage(page, player, pageId);
    await clickPreviewChoice(player, "wrong-answer");
    const afterWrong = [...playedBefore, sounds.wrong];
    await expectPreviewSoundsPlayed(
        player,
        afterWrong,
        `A wrong choice on ${what} should play ${sounds.wrong}`,
    );
    await clickPreviewChoice(player, "correct-answer");
    const afterRight = [...afterWrong, sounds.correct];
    await expectPreviewSoundsPlayed(
        player,
        afterRight,
        `The right choice on ${what} should play ${sounds.correct}`,
    );
    return afterRight;
}

const DEFAULT_SOUNDS: Record<GameSoundType, string> = {
    correct: DEFAULT_RIGHT_SOUND,
    wrong: DEFAULT_WRONG_SOUND,
};

test("Quiz and choice pages play the correct and wrong sounds chosen for them [Test Case ID 693]", async ({
    page,
    step,
}) => {
    test.setTimeout(600000);
    const quizFiles = makeSoundFiles(QUIZ_SOUNDS);
    const choiceFiles = makeSoundFiles(CHOICE_SOUNDS);

    await makeBookFromTemplate(page, "Basic Book");
    const firstQuiz = await addPageAndGetId(page, kQuizPageLabel);
    await goToPage(page, firstQuiz);
    await fillQuizPage(
        page,
        "Where did the cat sleep?",
        ["On the roof", "Under the bed", "In the basket"],
        2,
    );
    const firstChoice = await addPageAndGetId(page, kChoicePageLabel);
    await goToPage(page, firstChoice);
    await chooseChoicePromptPicture(page, BIRD);
    await typeChoiceWords(page, LANG, {
        correct: "bird",
        wrong: ["tree", "cat"],
    });

    await step("Choose sounds for the quiz and choice pages", async () => {
        await chooseSoundsForPage(page, firstQuiz, quizFiles);
        await chooseSoundsForPage(page, firstChoice, choiceFiles);
        // Leaving the page saves it.
        await goToPage(page, firstQuiz);
        expect(
            [
                await getGameSound(page, "correct"),
                await getGameSound(page, "wrong"),
            ],
            "The quiz page should still have its chosen sounds after leaving it",
        ).toEqual([QUIZ_SOUNDS.correct, QUIZ_SOUNDS.wrong]);
    });

    await step(
        "In the BloomPUB preview, each page plays its chosen sounds",
        async () => {
            await switchTab(page, "publish");
            await openPublishDestination(page, "BloomPUB");
            const player = await showBloomPubPreview(page);
            await startRecordingPreviewSounds(player);
            const played = await answerQuiz(
                page,
                player,
                firstQuiz,
                QUIZ_SOUNDS,
                [],
                "the quiz page",
            );
            await answerChoice(
                page,
                player,
                firstChoice,
                CHOICE_SOUNDS,
                played,
                "the choice page",
            );
        },
    );

    let secondQuiz = "";
    let secondChoice = "";
    await step(
        "Add a second quiz page and choice page, leaving their sounds alone",
        async () => {
            await switchTab(page, "edit");
            await goToPage(page, firstChoice);
            secondQuiz = await addPageAndGetId(page, kQuizPageLabel);
            await goToPage(page, secondQuiz);
            await fillQuizPage(
                page,
                "What did the dog eat?",
                ["A bone", "An apple", "Some rice"],
                0,
            );
            secondChoice = await addPageAndGetId(page, kChoicePageLabel);
            await goToPage(page, secondChoice);
            await chooseChoicePromptPicture(page, BIRD);
            await typeChoiceWords(page, LANG, {
                correct: "wing",
                wrong: ["leaf", "dog"],
            });
            // Leaving the page saves it.
            await goToPage(page, secondQuiz);
        },
    );

    await step(
        "In the rebuilt preview, the new pages play the default sounds and the old ones their chosen sounds",
        async () => {
            await switchTab(page, "publish");
            await openPublishDestination(page, "BloomPUB");
            const player = await showBloomPubPreview(page);
            await startRecordingPreviewSounds(player);
            // The pages are answered in the book's order, so that the preview shows each of
            // them once: a quiz page shown a second time plays its sound once more for every
            // time it was shown before (BL-17217).
            let played = await answerQuiz(
                page,
                player,
                firstQuiz,
                QUIZ_SOUNDS,
                [],
                "the first quiz page",
            );
            played = await answerChoice(
                page,
                player,
                firstChoice,
                CHOICE_SOUNDS,
                played,
                "the first choice page",
            );
            played = await answerQuiz(
                page,
                player,
                secondQuiz,
                DEFAULT_SOUNDS,
                played,
                "the new quiz page",
            );
            await answerChoice(
                page,
                player,
                secondChoice,
                DEFAULT_SOUNDS,
                played,
                "the new choice page",
            );
        },
    );
});
