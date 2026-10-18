// An end-of-test check that whatever a test did to the page undoes and redoes cleanly. A spec turns
// it on with one line:
//
//     checkUndoRoundTripAfterEachTest(test);
//
// After each test that passed and left a page showing in the Edit tab, it walks that page's
// history both ways and comes back to where the test left it: it redoes every step left to redo
// and undoes them again, then undoes every step and redoes them all. It fails unless:
// - every step taken back put the page back exactly as it was, as the page history compares pages
//   (pageHistory().keptMarkupOfPage(): the kept attributes and text, without what the edit-time
//   filter leaves out);
// - no undo or redo was refused, nothing cleared the history, and nothing dropped the redo steps
//   (the Show Page History record, workspaceBundle.pageHistoryRecorder).
// Those failures are what an undo or redo that does not put back what its step changed, or a change
// that Bloom makes after an undo outside any transaction, look like.
//
// A test that should not be checked says so with
// test.info().annotations.push({ type: kSkipUndoRoundTrip }).

import { expect, type Page } from "@playwright/test";
import type { test as bloomTest } from "../fixtures/bloomTest";
import { editablePageFrame } from "./bookMaking";
import { waitForOpenTransactionToEnd } from "./undo";

/** The annotation type that keeps checkUndoRoundTripAfterEachTest from checking a test. */
export const kSkipUndoRoundTrip = "skip-undo-round-trip";

// More steps than the page history keeps (50), to stop a loop that never ends.
const kMostSteps = 60;

// The kinds of Show Page History event that the round trip must not cause.
const kBadEventKinds = ["refused", "cleared", "redoDropped"];

/** The page history, as far as this module uses it. */
interface IPageHistoryForRoundTrip {
    undo: () => Promise<void>;
    redo: () => Promise<void>;
    undoDescriptionL10nKey: string | undefined;
    redoDescriptionL10nKey: string | undefined;
    keptMarkupOfPage: () => string;
    isDisposed: boolean;
}

/** The page bundle, as far as this module uses it. */
interface IPageBundleForRoundTrip {
    pageHistory: () => IPageHistoryForRoundTrip;
}

/** One way through the history: the description of each step taken, and the page around each. */
interface IWalk {
    descriptions: string[];
    // The page before the first step, then after each.
    states: string[];
}

type Direction = "undo" | "redo";

/** Turn the check on for every test in the spec that calls this, after each test that passed. */
export function checkUndoRoundTripAfterEachTest(test: typeof bloomTest): void {
    // bloomApp.page, not the page fixture, which is gone after a test restarts Bloom.
    test.afterEach(async ({ bloomApp }, testInfo) => {
        if (testInfo.status !== testInfo.expectedStatus) return;
        if (testInfo.annotations.some((a) => a.type === kSkipUndoRoundTrip))
            return;
        await expectUndoRoundTrip(
            bloomApp.page,
            `the end of "${testInfo.title}"`,
        );
    });
}

/**
 * Walk the page history of the page showing both ways, checking what
 * checkUndoRoundTripAfterEachTest says, and leave it where it was. Does nothing when no page is
 * open in the Edit tab. `when` names the moment, for the messages.
 */
export async function expectUndoRoundTrip(
    page: Page,
    when: string,
): Promise<void> {
    if (!(await hasPageHistory(page))) return;
    // A typing step stays open until something ends it; End ends it and changes nothing.
    await page.keyboard.press("End");
    await waitForOpenTransactionToEnd(page);
    const eventCountBefore = (await getPageHistoryEvents(page)).length;

    const redone = await walk(page, "redo", when);
    await walkBack(page, "undo", redone, when);
    const undone = await walk(page, "undo", when);
    await walkBack(page, "redo", undone, when);

    const badEvents = (await getPageHistoryEvents(page))
        .slice(eventCountBefore)
        .filter((event) => kBadEventKinds.includes(event.kind));
    expect(
        badEvents.map((event) => event.text),
        `At ${when}, walking the page history (undo: ${undone.descriptions.join(", ")}; redo: ${redone.descriptions.join(", ")}) refused a step, cleared the history or dropped the redo steps.`,
    ).toEqual([]);
}

/** Undo, or redo, until there is nothing left to, recording each step and the page after it. */
async function walk(
    page: Page,
    direction: Direction,
    when: string,
): Promise<IWalk> {
    const result: IWalk = {
        descriptions: [],
        states: [await readSettledMarkup(page)],
    };
    while (true) {
        const description = await readHistory(page, direction);
        if (description === undefined) return result;
        expect(
            result.descriptions.length,
            `At ${when}, the page history had more steps to ${direction} than it keeps.`,
        ).toBeLessThan(kMostSteps);
        await runHistory(page, direction);
        result.descriptions.push(description);
        result.states.push(await readSettledMarkup(page));
    }
}

/**
 * Take back every step of walked with `direction`, the opposite of the walk's, checking that each
 * is the next one to take back and that taking it back puts the page back as it was before it.
 */
async function walkBack(
    page: Page,
    direction: Direction,
    walked: IWalk,
    when: string,
): Promise<void> {
    const opposite = direction === "undo" ? "redo" : "undo";
    for (let i = walked.descriptions.length - 1; i >= 0; i--) {
        const description = walked.descriptions[i];
        expect(
            await readHistory(page, direction),
            `At ${when}, the next step to ${direction} should be "${description}".`,
        ).toBe(description);
        await runHistory(page, direction);
        expect(
            await readSettledMarkup(page),
            `At ${when}, the ${direction} of "${description}" did not put the page back as it was before its ${opposite}.`,
        ).toBe(walked.states[i]);
    }
}

/** True when the Edit tab shows a page with a page history. */
async function hasPageHistory(page: Page): Promise<boolean> {
    // The frame that editablePageFrame finds; a test may end in another tab, with none.
    const frame = page.frame({ name: "page" });
    if (!frame) return false;
    return frame
        .evaluate(() => {
            const bundle = (
                window as unknown as {
                    editablePageBundle?: IPageBundleForRoundTrip;
                }
            ).editablePageBundle;
            try {
                return !!bundle && !bundle.pageHistory().isDisposed;
            } catch {
                // pageHistory() throws until the history has started.
                return false;
            }
        })
        .catch(() => false);
}

/** The description of the next step to undo, or to redo, if any. */
async function readHistory(
    page: Page,
    direction: Direction,
): Promise<string | undefined> {
    return editablePageFrame(page).evaluate((which) => {
        const history = (
            window as unknown as { editablePageBundle: IPageBundleForRoundTrip }
        ).editablePageBundle.pageHistory();
        return which === "undo"
            ? history.undoDescriptionL10nKey
            : history.redoDescriptionL10nKey;
    }, direction);
}

/** Run the page history's undo or redo, as the Undo and Redo buttons do. */
async function runHistory(page: Page, direction: Direction): Promise<void> {
    await editablePageFrame(page).evaluate((which) => {
        const history = (
            window as unknown as { editablePageBundle: IPageBundleForRoundTrip }
        ).editablePageBundle.pageHistory();
        return which === "undo" ? history.undo() : history.redo();
    }, direction);
}

/**
 * The page as the page history compares it, once whatever Bloom does after an undo or redo has
 * finished: no transaction open, and the same markup on two reads 200 ms apart.
 */
async function readSettledMarkup(page: Page): Promise<string> {
    await waitForOpenTransactionToEnd(page);
    const read = () =>
        editablePageFrame(page).evaluate(() =>
            (
                window as unknown as {
                    editablePageBundle: IPageBundleForRoundTrip;
                }
            ).editablePageBundle
                .pageHistory()
                .keptMarkupOfPage(),
        );
    let previous = await read();
    for (let i = 0; i < 25; i++) {
        await page.waitForTimeout(200);
        const now = await read();
        if (now === previous) return now;
        previous = now;
    }
    throw new Error(
        "The page kept changing for 5 seconds after an undo or redo.",
    );
}

/** The Show Page History record: every event's kind and text, oldest first. */
async function getPageHistoryEvents(
    page: Page,
): Promise<{ kind: string; text: string }[]> {
    return page.evaluate(() =>
        (
            window as unknown as {
                workspaceBundle: {
                    pageHistoryRecorder: {
                        getEvents: () => readonly {
                            kind: string;
                            text: string;
                        }[];
                    };
                };
            }
        ).workspaceBundle.pageHistoryRecorder
            .getEvents()
            .map((event) => ({ kind: event.kind, text: event.text })),
    );
}
