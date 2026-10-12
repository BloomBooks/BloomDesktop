// The Decodable and Leveled Reader tools' panels in the toolbox: turning a book into a decodable or
// leveled reader, and which stage or level the panel is on. (The Decodable Reader's "Set Up Stages"
// dialog is readerSetup.ts.)
//
// Both tools render the same stepper ("Stage 3 of 10" / "Level 2 of 5", with two arrow buttons),
// and it shows only while the tool's switch is on, so everything here opens the tool with its
// switch on first. The stepper's number is read from data attributes it carries for this suite,
// because its visible text is localized.

import { expect, type Frame, type Page } from "@playwright/test";
import * as fs from "node:fs";
import * as Path from "node:path";
import { apiGet } from "./api";
import { makeBookFromTemplate } from "./bookMaking";
import { enableToolForBook, openTool, toolboxFrame } from "./toolbox";

/** The two reader tools, by the `data-toolid` their toolbox headers carry. */
export type ReaderTool = "decodableReader" | "leveledReader";

/** The "This book is decodable" / "This book is leveled" switch; the input inside MUI's Switch. */
const SWITCH: Record<ReaderTool, string> = {
    decodableReader: '[data-testid="decodable-reader-switch"] input',
    leveledReader: '[data-testid="leveled-reader-switch"] input',
};

/** The test id of each tool's stepper; its arrow buttons add "-previous" and "-next". */
const STEPPER_TEST_ID: Record<ReaderTool, string> = {
    decodableReader: "decodable-reader-stage",
    leveledReader: "leveled-reader-level",
};

function stepper(tool: ReaderTool, part = ""): string {
    return `[data-testid="${STEPPER_TEST_ID[tool]}${part}"]`;
}

/** Where Bloom reports the stage or level a new book will start on: the last one chosen. */
const DEFAULT_API: Record<ReaderTool, string> = {
    decodableReader: "readers/io/defaultStage",
    leveledReader: "readers/io/defaultLevel",
};

/** Which reader tool a person would call this, for messages. */
function describeTool(tool: ReaderTool): string {
    return tool === "decodableReader"
        ? "the Decodable Reader tool"
        : "the Leveled Reader tool";
}

/**
 * Open a reader tool in the toolbox, the way a person does, turn on its switch if it is off, and
 * wait until its stepper is showing with the collection's stages or levels loaded. The tool has to
 * be one the toolbox is offering (see toolbox.ts, enableToolForBook).
 *
 * The switch is what makes the book a decodable or leveled reader; the stepper and everything
 * under it show only while it is on.
 *
 * The stepper appears before the tool has finished restoring the book's stage or level, so what it
 * shows at first can be stale; see waitForReaderToolToRestore.
 */
export async function openReaderTool(
    page: Page,
    tool: ReaderTool,
): Promise<Frame> {
    const frame = await openTool(page, tool, SWITCH[tool]);
    const box = frame.locator(SWITCH[tool]);
    if (!(await box.isChecked())) await box.click();
    await expect(
        box,
        `The switch in ${describeTool(tool)} would not go on.`,
    ).toBeChecked({ timeout: 30000 });
    // The stepper reports 0 stages or levels until the collection's reader settings have loaded.
    await expect
        .poll(async () => getPhaseCount(frame, tool), {
            timeout: 30000,
            message: `${describeTool(tool)} never showed its stepper with any ${
                tool === "decodableReader" ? "stages" : "levels"
            } loaded.`,
        })
        .toBeGreaterThan(0);
    return frame;
}

async function getPhaseCount(frame: Frame, tool: ReaderTool): Promise<number> {
    const box = frame.locator(stepper(tool));
    if ((await box.count()) === 0) return 0;
    return Number(await box.first().getAttribute("data-phase-count"));
}

/**
 * The stage (Decodable Reader) or level (Leveled Reader) the tool's panel is showing. The panel
 * has to be showing; see openReaderTool.
 */
export async function getReaderPhase(
    page: Page,
    tool: ReaderTool,
): Promise<number> {
    const box = toolboxFrame(page).locator(stepper(tool));
    if ((await box.count()) === 0)
        throw new Error(
            `${describeTool(tool)} is not showing its stepper; open it with openReaderTool first.`,
        );
    return Number(await box.first().getAttribute("data-phase-number"));
}

/**
 * Move a reader tool to this stage or level with its arrow buttons, the way a person does, one
 * click at a time, waiting after each click until the stepper shows the next number, and then
 * until Bloom has saved the choice. Opens the tool first. Throws, naming how many there are, when
 * the collection has no such stage or level.
 *
 * The stepper shows the new number at once, but Bloom saves it half a second later, so that a
 * quick run of clicks saves only the last (readerToolsModel's setStageNumber). Leaving the book
 * inside that half second loses the change, so this waits for the save: the choice becomes the
 * default for new books in the same step that saves it into the book, and the default is what
 * Bloom will report.
 *
 * The tool must have finished restoring the book's stage or level first (see
 * waitForReaderToolToRestore; makeBasicBookWithReaderTool does it), or the restore can land after
 * these clicks and undo them.
 */
export async function setReaderPhase(
    page: Page,
    tool: ReaderTool,
    target: number,
): Promise<void> {
    const frame = await openReaderTool(page, tool);
    const count = await getPhaseCount(frame, tool);
    if (target < 1 || target > count)
        throw new Error(
            `Cannot move ${describeTool(tool)} to ${target}: the collection has ${count}.`,
        );
    for (
        let current = await getReaderPhase(page, tool);
        current !== target;
        current = await getReaderPhase(page, tool)
    ) {
        const forward = current < target;
        await frame
            .locator(stepper(tool, forward ? "-next" : "-previous"))
            .click();
        const expected = forward ? current + 1 : current - 1;
        await expect
            .poll(async () => getReaderPhase(page, tool), {
                timeout: 30000,
                message: `Clicking ${forward ? "next" : "previous"} in ${describeTool(
                    tool,
                )} did not move it from ${current} to ${expected}.`,
            })
            .toBe(expected);
    }
    await expect
        .poll(async () => (await apiGet(page, DEFAULT_API[tool])).body, {
            timeout: 30000,
            message: `Bloom never saved ${target} as the ${
                tool === "decodableReader" ? "stage" : "level"
            } chosen in ${describeTool(tool)}.`,
        })
        .toBe(String(target));
}

/**
 * Wait until a reader tool has finished restoring the stage or level of the book being edited: the
 * one saved in the book, or, for a book that has none yet, the default Bloom reports (the one last
 * chosen with the arrows). The tool shows the stepper first and restores asynchronously a moment
 * later, so a click on the stepper in between races the restore, and whichever lands second wins.
 */
export async function waitForReaderToolToRestore(
    page: Page,
    tool: ReaderTool,
    bookFolder: string,
): Promise<void> {
    const saved = getSavedReaderPhase(bookFolder, tool);
    const expected =
        saved ?? Number((await apiGet(page, DEFAULT_API[tool])).body);
    await expect
        .poll(async () => getReaderPhase(page, tool), {
            timeout: 30000,
            message: `${describeTool(tool)} never finished restoring this book's ${
                tool === "decodableReader" ? "stage" : "level"
            }: it should show ${expected}, ${
                saved === undefined
                    ? "the default Bloom reports for a book that has none saved"
                    : "which is what the book has saved"
            }.`,
        })
        .toBe(expected);
}

/**
 * The stage or level a book has saved for a reader tool, read from its meta.json, or undefined
 * when it has none yet. Bloom stores the Decodable Reader's as "stage:3;sort:alphabetic" and the
 * Leveled Reader's as "3". Bloom writes meta.json when it saves the book, so this is what the book
 * had when it was last saved.
 */
function getSavedReaderPhase(
    bookFolder: string,
    tool: ReaderTool,
): number | undefined {
    const meta = JSON.parse(
        fs.readFileSync(Path.join(bookFolder, "meta.json"), "utf8"),
    ) as { tools?: { name: string; state?: string | null }[] };
    const state = meta.tools?.find((t) => t.name === tool)?.state;
    if (!state) return undefined;
    const match =
        tool === "decodableReader"
            ? /stage:(\d+)/.exec(state)
            : /^(\d+)$/.exec(state);
    return match ? Number(match[1]) : undefined;
}

/**
 * Make a new Basic Book with one of the reader tools turned on and open, its switch on, and return
 * the book's folder, once the tool has restored the stage or level a new book starts on. Setup for
 * tests about what a reader book remembers; the tool is turned on by the fast route
 * (enableToolForBook), not through "More...".
 */
export async function makeBasicBookWithReaderTool(
    page: Page,
    tool: ReaderTool,
): Promise<string> {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await enableToolForBook(page, bookFolder, tool);
    await openReaderTool(page, tool);
    await waitForReaderToolToRestore(page, tool, bookFolder);
    return bookFolder;
}

/**
 * Wait until a reader tool shows this stage or level, and fail with `message` if it never does.
 * For assertions after a book is opened: the tool restores the book's stage or level a moment
 * after it appears, so reading the stepper once can catch it before the restore.
 */
export async function expectReaderToolToShow(
    page: Page,
    tool: ReaderTool,
    expected: number,
    message: string,
): Promise<void> {
    await expect
        .poll(async () => getReaderPhase(page, tool), {
            timeout: 30000,
            message,
        })
        .toBe(expected);
}
