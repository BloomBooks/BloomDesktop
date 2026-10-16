// Page content delays (addRequestPageContentDelay and the functions built on it in
// bloomEditing.ts) must hold the page frame's save wherever they are called from. Each frame loads
// its own copy of bloomEditing.ts, so a call from another frame, such as the toolbox, goes to the
// page frame's exports. Here the test's window plays the other frame, and an iframe with id "page"
// plays the page frame, whose exports are stand-ins that note what they were given.
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
    addRequestPageContentDelay,
    removeRequestPageContentDelay,
    whenNoRequestPageContentDelays,
    wrapWithRequestPageContentDelay,
} from "./bloomEditing";

let added: string[];
let removed: string[];
// The page frame's waits for no delay to be pending, which the stand-in settles on a remove.
let pageFrameWaiters: (() => void)[];
let pageFrame: HTMLIFrameElement | undefined;

beforeEach(() => {
    added = [];
    removed = [];
    pageFrameWaiters = [];
});

afterEach(() => {
    pageFrame?.remove();
    pageFrame = undefined;
});

/** Make an iframe with id "page" whose window holds stand-in page frame exports. */
function addPageFrameInAnotherWindow(): void {
    pageFrame = document.createElement("iframe");
    pageFrame.id = "page";
    document.body.appendChild(pageFrame);
    (
        pageFrame.contentWindow as unknown as { editablePageBundle: unknown }
    ).editablePageBundle = {
        addRequestPageContentDelay: (id: string) => added.push(id),
        removeRequestPageContentDelay: (id: string) => {
            removed.push(id);
            if (added.length === removed.length) {
                pageFrameWaiters.forEach((resolve) => resolve());
                pageFrameWaiters = [];
            }
        },
        whenNoRequestPageContentDelays: () =>
            added.length === removed.length
                ? Promise.resolve()
                : new Promise<void>((resolve) =>
                      pageFrameWaiters.push(resolve),
                  ),
    };
    expect(pageFrame.contentWindow, "test setup: another window").not.toBe(
        window,
    );
}

/**
 * True if whenNoRequestPageContentDelays, called in this window, finds a delay pending: in this
 * window's copy of bloomEditing.ts, or in the page frame's when there is one.
 */
async function hasPendingDelay(): Promise<boolean> {
    let settled = false;
    void whenNoRequestPageContentDelays().then(() => (settled = true));
    await Promise.resolve();
    return !settled;
}

describe("page content delays", () => {
    it("are added and removed in the page frame when called from another frame", async () => {
        addPageFrameInAnotherWindow();

        addRequestPageContentDelay("fromToolbox");

        expect(added).toEqual(["fromToolbox"]);
        // Waiting from this frame waits for the page frame's delays.
        expect(await hasPendingDelay()).toBe(true);
        removeRequestPageContentDelay("fromToolbox");
        expect(removed).toEqual(["fromToolbox"]);
        expect(await hasPendingDelay()).toBe(false);
    });

    it("are held in the page frame around a function wrapped in another frame", async () => {
        addPageFrameInAnotherWindow();
        let addedWhileRunning: string[] = [];
        let removedWhileRunning: string[] = [];

        await wrapWithRequestPageContentDelay(async () => {
            addedWhileRunning = [...added];
            removedWhileRunning = [...removed];
        }, "wrapped");

        expect(addedWhileRunning).toEqual(["wrapped"]);
        expect(removedWhileRunning).toEqual([]);
        expect(removed).toEqual(["wrapped"]);
    });

    it("are held here when there is no page frame, or this is it", async () => {
        addRequestPageContentDelay("noPageFrame");

        expect(added).toEqual([]);
        expect(await hasPendingDelay()).toBe(true);
        removeRequestPageContentDelay("noPageFrame");
        expect(await hasPendingDelay()).toBe(false);
    });
});
