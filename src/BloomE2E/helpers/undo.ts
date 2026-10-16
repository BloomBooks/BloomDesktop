// Undo and redo in the Edit tab: the top bar's Undo button, the keys, and a way to tell whether a
// gesture reached the page history.
//
// Bloom has one undo history for the page open in the Edit tab, the page history
// (bookEdit/pageHistory/ in BloomBrowserUI), which lives in the page frame and starts as soon as the
// page's DOM is there. Until Bloom has finished setting the page up, only the user's own actions
// are recorded; Bloom's setup is part of the page as the user found it. Change Layout mode
// keeps its own undo, which the Undo button and the keys reach while the mode is on. That a text
// box's text changed back says nothing about which of them ran, so watchUndoAndRedo counts the
// undos and redos the page history runs, by wrapping its undo() and redo(). (The counts are kept
// in the page, so they are lost when the page reloads; watch again after leaving a page or Change
// Layout mode.)
//
// Ctrl+Z and Ctrl+Y are ordinary key presses as far as Bloom is concerned: the WinForms shell does
// not claim them, so they reach the frame with the focus, where Bloom takes them for the page
// history. A test presses them with pressKey (keys.ts).

import { expect, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import { pressKey } from "./keys";

/** What the page history has done since watchUndoAndRedo. */
export interface IUndoAndRedoCalls {
    /** Undos the page history ran: from the Undo button, Ctrl+Z, or the undo helper. */
    historyUndo: number;
    /** Redos the page history ran: from Ctrl+Y or Ctrl+Shift+Z. */
    historyRedo: number;
    /**
     * The commands CKEditor itself ran, in order, e.g. "undo" for its own handling of Ctrl+Z,
     * which the page history's keys keep from it.
     */
    ckeditorCommands: string[];
}

/** The page history, as far as this module uses it. */
interface IPageHistoryForUndo {
    undo: () => Promise<void>;
    redo: () => Promise<void>;
    isDisposed: boolean;
}

/** The page bundle, as far as this module uses it. */
interface IPageBundleForUndo {
    pageHistory: () => IPageHistoryForUndo;
}

/** The workspace bundle, as far as this module uses it. */
interface IWorkspaceForUndo {
    canUndo: () => string;
}

/**
 * Wait until the page history of the page showing has started, which it does as soon as the page's
 * DOM is there; a change made before that is not undoable. Call it after going to a page and before
 * the change that the test will undo.
 */
export async function waitForPageHistory(page: Page): Promise<void> {
    await expect
        .poll(
            async () =>
                editablePageFrame(page)
                    .evaluate(() => {
                        const bundle = (
                            window as unknown as {
                                editablePageBundle?: IPageBundleForUndo;
                            }
                        ).editablePageBundle;
                        try {
                            return !!bundle && !bundle.pageHistory().isDisposed;
                        } catch {
                            // pageHistory() throws until the history has started.
                            return false;
                        }
                    })
                    .catch(() => false),
            {
                timeout: 30000,
                message: "The page history never started for the page showing.",
            },
        )
        .toBe(true);
}

/**
 * Start counting the undos and redos that the page history runs, and the commands CKEditor runs,
 * from zero (see getUndoAndRedoCalls). Call it once the page history has started
 * (waitForPageHistory), and again after anything reloads the page. Calling it again only resets the
 * counts.
 */
export async function watchUndoAndRedo(page: Page): Promise<void> {
    await waitForPageHistory(page);
    await editablePageFrame(page).evaluate(() => {
        type Fn = ((...args: unknown[]) => unknown) & { __e2eOriginal?: Fn };
        interface ICkEditor {
            __e2eWatched?: boolean;
            on: (
                event: string,
                handler: (e: { data: { name: string } }) => void,
            ) => void;
        }
        const w = window as unknown as {
            editablePageBundle: IPageBundleForUndo;
            CKEDITOR?: { instances: Record<string, ICkEditor> };
            __e2eUndoCalls: Record<string, number>;
            __e2eCkeditorCommands: string[];
        };
        w.__e2eUndoCalls = { historyUndo: 0, historyRedo: 0 };
        const history = w.editablePageBundle.pageHistory() as unknown as Record<
            string,
            unknown
        >;
        const count = (functionName: string, countAs: string) => {
            const current = history[functionName] as Fn;
            const original = current.__e2eOriginal ?? current;
            const counted: Fn = function (this: unknown, ...args: unknown[]) {
                w.__e2eUndoCalls[countAs]++;
                return original.apply(this, args);
            };
            counted.__e2eOriginal = original;
            history[functionName] = counted;
        };
        count("undo", "historyUndo");
        count("redo", "historyRedo");

        w.__e2eCkeditorCommands = [];
        for (const editor of Object.values(w.CKEDITOR?.instances ?? {})) {
            if (editor.__e2eWatched) continue;
            editor.__e2eWatched = true;
            editor.on("afterCommandExec", (e) =>
                w.__e2eCkeditorCommands.push(e.data.name),
            );
        }
    });
}

/** What the page history and CKEditor have done since watchUndoAndRedo. */
export async function getUndoAndRedoCalls(
    page: Page,
): Promise<IUndoAndRedoCalls> {
    return editablePageFrame(page).evaluate(() => {
        const w = window as unknown as {
            __e2eUndoCalls?: Record<string, number>;
            __e2eCkeditorCommands?: string[];
        };
        if (!w.__e2eUndoCalls)
            throw new Error(
                "Undo and redo are not being watched; call watchUndoAndRedo first.",
            );
        return {
            historyUndo: w.__e2eUndoCalls.historyUndo,
            historyRedo: w.__e2eUndoCalls.historyRedo,
            ckeditorCommands: w.__e2eCkeditorCommands ?? [],
        };
    });
}

/**
 * Wait until the page history and CKEditor have done exactly what `expected` says, and fail,
 * showing what they did do, if they never do. Fields left out of `expected` are not checked.
 * `gesture` names what the test just did, for the message, e.g. "clicking Undo". `timeoutMs` is
 * for a gesture a person makes, which takes longer than one the test makes.
 */
export async function expectUndoAndRedoCalls(
    page: Page,
    expected: Partial<IUndoAndRedoCalls>,
    gesture: string,
    timeoutMs = 30000,
): Promise<void> {
    await expect
        .poll(async () => getUndoAndRedoCalls(page), {
            timeout: timeoutMs,
            message: `After ${gesture}, the page history did not undo and redo as expected.`,
        })
        .toMatchObject(expected);
}

/**
 * True when Bloom has something it could undo. This is the question the C# side asks on a timer to
 * decide whether the top bar's Undo button is enabled.
 */
export async function canUndo(page: Page): Promise<boolean> {
    // canUndo() answers "yes" when the front end has something to undo and "fail" when it has
    // not (see workspaceRoot.ts); the C# side makes the same comparison.
    const answer = await page.evaluate(() =>
        (
            window as unknown as { workspaceBundle: IWorkspaceForUndo }
        ).workspaceBundle.canUndo(),
    );
    return answer === "yes";
}

/**
 * Click the top bar's Undo button, the way a person does, once Bloom has enabled it. Returns as
 * soon as the click is made; the undo itself goes through C# and back into the page, so wait for
 * the state you expect (a text, a count, a class) rather than reading the page straight after this.
 *
 * Bloom enables the button from canUndo on a timer, so waiting for it to be enabled is also a check
 * that Bloom knows there is something to undo.
 */
export async function clickUndoButton(page: Page): Promise<void> {
    const button = page.getByTestId("undo-button");
    await expect(
        button,
        "The Undo button never became enabled, so Bloom says there is nothing to undo.",
    ).toBeEnabled({ timeout: 30000 });
    await button.click();
}

/**
 * Undo the last change, for setup; a test about undo itself uses clickUndoButton or the keys.
 * This clicks the Undo button too: a real press is what ends the typing step that typing in a box
 * holds open, and the page history undoes nothing while a step is open. Returns as soon as the
 * click is made, so wait for the state you expect afterwards.
 */
export async function undo(page: Page): Promise<void> {
    if (!(await canUndo(page)))
        throw new Error(
            "Bloom says there is nothing to undo, so calling undo would do nothing. " +
                "The change you meant to undo may not have registered.",
        );
    await clickUndoButton(page);
}

/**
 * Press Ctrl+Z once, the way a person does, into whatever has the focus, and wait until `read`
 * gives `expected`, the state from before `change`. Fails, naming the change, when the page history
 * did not run exactly one undo for the press, or when the state never came back.
 *
 * `read` is the test's own reading of the state it cares about, such as
 * getCanvasElementMarkupAndRects (canvasElements.ts). `change` says what the test did, for the
 * messages, e.g. "moving a speech bubble".
 */
export async function undoWithCtrlZAndExpect<T>(
    page: Page,
    read: () => Promise<T>,
    expected: T,
    change: string,
): Promise<void> {
    await watchUndoAndRedo(page);
    await pressKey(page, "Control+z");
    await expectUndoAndRedoCalls(
        page,
        { historyUndo: 1, historyRedo: 0 },
        `pressing Ctrl+Z after ${change}`,
    );
    await expect
        .poll(read, {
            timeout: 15000,
            message: `Ctrl+Z after ${change} did not put the page back the way it was before ${change}.`,
        })
        .toEqual(expected);
}

/**
 * Press Ctrl+Y once, the way a person does, into whatever has the focus, and wait until `read`
 * gives `expected`, the state from after `change`. Fails, naming the change, when the page history
 * did not run exactly one redo for the press, or when the state never came back. Call it after
 * undoWithCtrlZAndExpect has undone the change.
 */
export async function redoWithCtrlYAndExpect<T>(
    page: Page,
    read: () => Promise<T>,
    expected: T,
    change: string,
): Promise<void> {
    await watchUndoAndRedo(page);
    await pressKey(page, "Control+y");
    await expectUndoAndRedoCalls(
        page,
        { historyUndo: 0, historyRedo: 1 },
        `pressing Ctrl+Y after undoing ${change}`,
    );
    await expect
        .poll(read, {
            timeout: 15000,
            message: `Ctrl+Y after undoing ${change} did not put back the page as ${change} left it.`,
        })
        .toEqual(expected);
}

/**
 * Wait until the page history has no transaction open, so that the change made last is a finished
 * undo step and the next press of the mouse or a key starts a step of its own rather than joining
 * it. A change such as choosing a picture keeps its transaction open until the page has settled.
 * Do not call it while typing in a text box: the typing's transaction stays open until the typing
 * ends, by design.
 */
export async function waitForOpenTransactionToEnd(page: Page): Promise<void> {
    await expect
        .poll(
            async () =>
                editablePageFrame(page).evaluate(
                    () =>
                        (
                            window as unknown as {
                                editablePageBundle: {
                                    pageHistory: () => {
                                        hasOpenTransaction: boolean;
                                    };
                                };
                            }
                        ).editablePageBundle.pageHistory().hasOpenTransaction,
                ),
            {
                timeout: 30000,
                message:
                    "The page history kept a transaction open, so the last change never became an undo step of its own.",
            },
        )
        .toBe(false);
}
