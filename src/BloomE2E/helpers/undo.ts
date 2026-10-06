// Undo and redo in the Edit tab: the top bar's Undo button, and a way to tell WHICH of Bloom's undo
// mechanisms a gesture reached.
//
// Bloom has several undo mechanisms: Change Layout mode's, the reader tools' per-box text undo, the
// picture undo, and CKEditor's per-box undo. The Undo button picks between them through Bloom's one
// undo stack (bookEdit/undo/ in BloomBrowserUI), which lives in the page frame, in that order. That a text box's text changed back
// says nothing about which of them ran, so watchUndoMechanisms counts the calls each one receives,
// by wrapping the functions the stack reaches it through. (The calls are counted in the page, so
// they are lost when the page reloads; watch again after leaving a page or Change Layout mode.)
//
// Ctrl+Z and Ctrl+Y are ordinary key presses as far as Bloom is concerned: the WinForms shell does
// not claim them, so they reach the page, where CKEditor, the reader tools and Change Layout mode
// each handle them in TypeScript. A test presses them with pressKey (keys.ts).

import { expect, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";

/** One of Bloom's undo mechanisms, in the order the Undo button consults them. */
export type UndoMechanism =
    | "changeLayout"
    | "readerTools"
    | "picture"
    | "ckeditor";

/** How many times each undo mechanism has been reached since watchUndoMechanisms. */
export interface IUndoMechanismCalls {
    /** Change Layout mode's undo, reached through the one undo stack. */
    changeLayout: number;
    /** The reader tools' text undo, reached through the one undo stack. */
    readerTools: number;
    /** The picture undo (a changed picture, its copyright or its crop), through the stack. */
    picture: number;
    /** CKEditor's per-box undo, reached through the stack (not CKEditor's own Ctrl+Z). */
    ckeditor: number;
    /**
     * Repaints of the reader tools' highlighting, which Bloom does after an undo that rewrote a
     * text box, because no keystroke will trigger one (BL-16558).
     */
    readerMarkupRepaint: number;
    /** Redo through the one undo stack: its Ctrl+Y binding, which has no button. */
    stackRedo: number;
    /**
     * The commands CKEditor itself ran, in order, e.g. "undo" for its own handling of Ctrl+Z. The
     * Undo button does not show up here: the stack calls CKEditor's undo manager directly.
     */
    ckeditorCommands: string[];
}

type CountedName =
    | "changeLayout"
    | "readerTools"
    | "picture"
    | "ckeditor"
    | "readerMarkupRepaint"
    | "stackRedo";

/** The workspace bundle, as far as this module uses it. */
interface IWorkspaceForUndo {
    canUndo: () => string;
    handleUndo: () => void;
    getEditablePageBundleExports: () => Record<string, unknown>;
    getToolboxBundleExports: () => Record<string, unknown>;
}

/**
 * Start counting the calls each undo mechanism receives, from zero (see getUndoMechanismCalls).
 * Call it once the page being edited is showing, and again after anything reloads that page.
 * Calling it again only resets the counts.
 */
export async function watchUndoMechanisms(page: Page): Promise<void> {
    await page.evaluate(() => {
        type Fn = ((...args: unknown[]) => unknown) & { __e2eOriginal?: Fn };
        const w = window as unknown as {
            workspaceBundle: IWorkspaceForUndo;
            __e2eUndoCalls: Record<string, number>;
        };
        const calls: Record<string, number> = {
            changeLayout: 0,
            readerTools: 0,
            picture: 0,
            ckeditor: 0,
            readerMarkupRepaint: 0,
            stackRedo: 0,
        };
        w.__e2eUndoCalls = calls;
        const count = (
            owner: Record<string, unknown>,
            functionName: string,
            countAs: string,
        ) => {
            const current = owner[functionName] as Fn;
            const original = current.__e2eOriginal ?? current;
            const counted: Fn = function (this: unknown, ...args: unknown[]) {
                w.__e2eUndoCalls[countAs]++;
                return original.apply(this, args);
            };
            counted.__e2eOriginal = original;
            owner[functionName] = counted;
        };
        const workspace = w.workspaceBundle;
        const pageExports = workspace.getEditablePageBundleExports();
        const toolboxExports = workspace.getToolboxBundleExports();
        count(pageExports, "origamiUndo", "changeLayout");
        count(toolboxExports, "undo", "readerTools");
        count(pageExports, "imageOperationUndo", "picture");
        count(pageExports, "ckeditorUndo", "ckeditor");
        count(
            toolboxExports,
            "updateMarkupAfterUndoOrRedo",
            "readerMarkupRepaint",
        );
        // The one undo stack lives in the page frame, and its Ctrl+Y binding redoes through the
        // page bundle's handleRedo.
        count(pageExports, "handleRedo", "stackRedo");
    });
    await editablePageFrame(page).evaluate(() => {
        interface ICkEditor {
            __e2eWatched?: boolean;
            on: (
                event: string,
                handler: (e: { data: { name: string } }) => void,
            ) => void;
        }
        const w = window as unknown as {
            CKEDITOR?: { instances: Record<string, ICkEditor> };
            __e2eCkeditorCommands: string[];
        };
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

/** How many times each undo mechanism has been reached since watchUndoMechanisms. */
export async function getUndoMechanismCalls(
    page: Page,
): Promise<IUndoMechanismCalls> {
    const counts = await page.evaluate(() => {
        const calls = (
            window as unknown as { __e2eUndoCalls?: Record<string, number> }
        ).__e2eUndoCalls;
        if (!calls)
            throw new Error(
                "Undo mechanisms are not being watched; call watchUndoMechanisms first.",
            );
        return { ...calls };
    });
    const ckeditorCommands = await editablePageFrame(page).evaluate(
        () =>
            (window as unknown as { __e2eCkeditorCommands?: string[] })
                .__e2eCkeditorCommands ?? [],
    );
    return {
        ...(counts as Record<CountedName, number>),
        ckeditorCommands,
    };
}

/**
 * Wait until the undo mechanisms have been reached exactly as `expected` says, and fail, showing
 * what they did receive, if they never are. Fields left out of `expected` are not checked.
 * `gesture` names what the test just did, for the message, e.g. "clicking Undo". `timeoutMs` is
 * for a gesture a person makes, which takes longer than one the test makes.
 */
export async function expectUndoMechanismCalls(
    page: Page,
    expected: Partial<IUndoMechanismCalls>,
    gesture: string,
    timeoutMs = 30000,
): Promise<void> {
    await expect
        .poll(async () => getUndoMechanismCalls(page), {
            timeout: timeoutMs,
            message: `After ${gesture}, the undo mechanisms were not reached as expected.`,
        })
        .toMatchObject(expected);
}

/**
 * Wait until this undo mechanism says it has something to undo. The Undo button reaches the first
 * mechanism that does (see UndoMechanism for the order), so a test that means to undo through a
 * particular one waits for it first. The reader tools, for one, record typing only after their
 * half-second markup delay, and until then the Undo button goes to CKEditor instead.
 */
export async function waitForUndoAvailableFrom(
    page: Page,
    mechanism: UndoMechanism,
): Promise<void> {
    await expect
        .poll(
            async () =>
                page.evaluate((which) => {
                    const workspace = (
                        window as unknown as {
                            workspaceBundle: IWorkspaceForUndo;
                        }
                    ).workspaceBundle;
                    const pageExports =
                        workspace.getEditablePageBundleExports();
                    const toolboxExports = workspace.getToolboxBundleExports();
                    const ask = (
                        owner: Record<string, unknown>,
                        name: string,
                    ) => !!(owner[name] as () => boolean)();
                    switch (which) {
                        case "changeLayout":
                            return ask(pageExports, "origamiCanUndo");
                        case "readerTools":
                            return ask(toolboxExports, "canUndo");
                        case "picture":
                            return ask(pageExports, "imageOperationCanUndo");
                        case "ckeditor":
                            return ask(pageExports, "ckeditorCanUndo");
                    }
                }, mechanism),
            {
                timeout: 30000,
                message: `The ${mechanism} undo never had anything to undo.`,
            },
        )
        .toBe(true);
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
 * Undo the last change by the route the Undo button ends at, without the click: the front end's
 * `workspaceBundle.handleUndo()`. For setup; a test about undo itself uses clickUndoButton. Returns
 * as soon as the front end has been told to undo, so wait for the state you expect afterwards.
 */
export async function undo(page: Page): Promise<void> {
    if (!(await canUndo(page)))
        throw new Error(
            "Bloom says there is nothing to undo, so calling undo would do nothing. " +
                "The change you meant to undo may not have registered.",
        );
    await page.evaluate(() =>
        (
            window as unknown as { workspaceBundle: IWorkspaceForUndo }
        ).workspaceBundle.handleUndo(),
    );
}
