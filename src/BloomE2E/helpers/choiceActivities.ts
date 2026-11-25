// Fill in and read the two choice activities of the Games template on the page being edited:
// "Choose Word from Image" (a picture, and buttons that hold words) and "Choose Image from Word"
// (a word, and buttons that hold pictures). Both are made by simple-dom-choice-activities.pug in
// src/content/templates/template books/Activity.
//
// Each answer button is a div.player-button marked with data-activityRole, "correct-answer" for
// the one right answer and "wrong-answer" for the others. In the Edit tab the right answer is
// always the first button; Bloom Player turns the divs into real buttons and shuffles them (see
// helpers/bloomPlayer.ts for the preview's side).

import { expect, type Locator, type Page } from "@playwright/test";
import { editablePageFrame, typeInGroup } from "./bookMaking";
import { bottom, kEdgeTolerance, right, type IRect } from "./geometry";
import { chooseImageFile } from "./images";
import type { ChoiceRole } from "./bloomPlayer";

/** The words or pictures for a choice activity: the right answer, and the wrong ones. */
export interface IChoiceAnswers {
    correct: string;
    wrong: string[];
}

/** The answer buttons on the page being edited, in document order. */
function answerButtons(page: Page): Locator {
    return editablePageFrame(page).locator(".player-button[data-activityrole]");
}

/** The kinds of the answer buttons on the page being edited, in the order the page holds them. */
export async function getChoiceRolesInEditOrder(
    page: Page,
): Promise<ChoiceRole[]> {
    const buttons = answerButtons(page);
    await expect(
        buttons.first(),
        "The page being edited has no answer buttons; is it a choice activity page?",
    ).toBeAttached({ timeout: 30000 });
    return buttons.evaluateAll((elements) =>
        elements.map(
            (e) =>
                e.getAttribute("data-activityrole") as
                    | "correct-answer"
                    | "wrong-answer",
        ),
    );
}

/**
 * The pictures in the answer buttons of the "Choose Image from Word" page being edited, in the
 * order the page holds them: each one's file name in the book, without its folder or extension.
 * Bloom names the book's copy itself (a second picture from a file called bird.png becomes
 * bird1.png), so a test asks for the names rather than assuming them.
 */
export async function getChoicePicturesInEditOrder(
    page: Page,
): Promise<string[]> {
    const buttons = answerButtons(page);
    await expect(
        buttons.first(),
        "The page being edited has no answer buttons; is it a choice activity page?",
    ).toBeAttached({ timeout: 30000 });
    return buttons.evaluateAll((elements) =>
        elements.map((button) =>
            decodeURIComponent(
                new URL(
                    button.querySelector("img")!.getAttribute("src")!,
                    button.ownerDocument.baseURI,
                ).pathname
                    .split("/")
                    .pop()!,
            ).replace(/\.[^.]*$/, ""),
        ),
    );
}

/**
 * The indexes, in document order, of the answer buttons for these answers: the right answer's
 * button first, then the wrong answers' buttons in order. Fails when the page has a different
 * number of wrong-answer buttons than there are wrong answers.
 */
async function buttonIndexesFor(
    page: Page,
    answers: IChoiceAnswers,
): Promise<number[]> {
    const roles = await getChoiceRolesInEditOrder(page);
    const correct = roles.indexOf("correct-answer");
    const wrong = roles
        .map((role, index) => ({ role, index }))
        .filter((r) => r.role === "wrong-answer")
        .map((r) => r.index);
    if (correct < 0 || wrong.length !== answers.wrong.length)
        throw new Error(
            `The page's answer buttons are ${roles.join(", ")}; they do not fit one right answer and ${answers.wrong.length} wrong ones.`,
        );
    return [correct, ...wrong];
}

/**
 * Type the words into the answer buttons of a "Choose Word from Image" page, in `languageTag`: the
 * right answer into the right-answer button, the wrong ones into the others. Returns once Bloom has
 * the page with every word in it.
 */
export async function typeChoiceWords(
    page: Page,
    languageTag: string,
    answers: IChoiceAnswers,
): Promise<void> {
    const indexes = await buttonIndexesFor(page, answers);
    const words = [answers.correct, ...answers.wrong];
    for (let i = 0; i < words.length; i++)
        await typeInGroup(
            page,
            ".player-button .bloom-translationGroup",
            languageTag,
            words[i],
            indexes[i],
        );
}

/**
 * Put pictures into the answer buttons of a "Choose Image from Word" page, by file path: the right
 * answer's picture into the right-answer button, the wrong ones into the others. Uses the image
 * chooser's route (chooseImageFile), and returns once every button shows its picture.
 */
export async function chooseChoicePictures(
    page: Page,
    answers: IChoiceAnswers,
): Promise<void> {
    const indexes = await buttonIndexesFor(page, answers);
    const files = [answers.correct, ...answers.wrong];
    for (let i = 0; i < files.length; i++)
        await chooseImageFile(
            page,
            files[i],
            answerButtons(page).nth(indexes[i]),
        );
}

/**
 * Put a picture into the prompt of a "Choose Word from Image" page: the picture the reader matches
 * a word to. Uses the image chooser's route (chooseImageFile).
 */
export async function chooseChoicePromptPicture(
    page: Page,
    filePath: string,
): Promise<void> {
    await chooseImageFile(
        page,
        filePath,
        editablePageFrame(page).locator(".imageThenChoices > .bloom-canvas"),
    );
}

/** How a choice activity page is laid out in the Edit tab. */
export interface IChoicePageLayout {
    /** What the reader matches against: the picture, or the word. */
    prompt: IRect;
    /** The answer buttons, in document order. */
    choices: IRect[];
}

/**
 * Where the prompt (the picture of a "Choose Word from Image" page, or the word of a "Choose Image
 * from Word" page) and the answer buttons are drawn on the page being edited.
 */
export async function getChoicePageLayout(
    page: Page,
): Promise<IChoicePageLayout> {
    const frame = editablePageFrame(page);
    const prompt = frame.locator(
        ".imageThenChoices > .bloom-canvas, .wordThenChoices > .word-to-match",
    );
    await expect(
        prompt,
        "The page being edited has no choice activity prompt (picture or word to match).",
    ).toHaveCount(1, { timeout: 30000 });
    const toRect = (
        box: { x: number; y: number; width: number; height: number } | null,
        what: string,
    ) => {
        if (!box) throw new Error(`${what} is not drawn on the page.`);
        return { x: box.x, y: box.y, width: box.width, height: box.height };
    };
    const promptRect = toRect(await prompt.boundingBox(), "The prompt");
    const choices: IRect[] = [];
    const buttons = answerButtons(page);
    for (let i = 0; i < (await buttons.count()); i++)
        choices.push(
            toRect(await buttons.nth(i).boundingBox(), `Answer button ${i}`),
        );
    return { prompt: promptRect, choices };
}

/** How a choice page arranges its prompt and answer buttons, in words a tester would use. */
export interface IChoiceArrangement {
    /** Where the prompt sits: above every answer button, or beside them (to their left). */
    prompt: "above" | "beside" | "elsewhere";
    /** How the answer buttons run: one under another, or side by side. */
    choices: "column" | "row" | "neither";
}

/**
 * Describe the arrangement of a choice page's layout (getChoicePageLayout) from where its parts
 * are, with no pixel values, so a test can say "a picture beside a column of buttons".
 */
export function describeChoiceArrangement(
    layout: IChoicePageLayout,
): IChoiceArrangement {
    const { prompt, choices } = layout;
    let promptPlace: IChoiceArrangement["prompt"] = "elsewhere";
    if (choices.every((c) => bottom(prompt) <= c.y + kEdgeTolerance))
        promptPlace = "above";
    else if (choices.every((c) => right(prompt) <= c.x + kEdgeTolerance))
        promptPlace = "beside";
    const pairs = choices.slice(1).map((c, i) => ({ before: choices[i], c }));
    let run: IChoiceArrangement["choices"] = "neither";
    if (pairs.every((p) => bottom(p.before) <= p.c.y + kEdgeTolerance))
        run = "column";
    else if (pairs.every((p) => right(p.before) <= p.c.x + kEdgeTolerance))
        run = "row";
    return { prompt: promptPlace, choices: run };
}
