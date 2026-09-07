// Tests for the one undo stack (BL-6681). See docs/retire-ckeditor/PLAN.md 4.1.
//
// These are specification tests, not characterization tests: the stack is new code, so each case
// pins a decision the plan made rather than recording what some existing code happens to do.

import { describe, it, expect, beforeEach } from "vitest";
import { UndoStack } from "./UndoStack";
import { ILegacyUndoProvider, IUndoEntry, kMaxUndoEntries } from "./undoTypes";

/** A minimal entry that appends its label to `log` when undone or redone. */
function makeEntry(
    label: string,
    log: string[],
    options?: { pageId?: string; canRedo?: boolean },
): IUndoEntry {
    const entry: IUndoEntry = {
        label,
        pageId: options && "pageId" in options ? options.pageId : "page1",
        kind: "custom",
        undo: () => {
            log.push(`undo ${label}`);
        },
    };
    // Redo is optional by design (an entry without it is a redo floor), so tests must be able to
    // create entries both ways.
    if (options?.canRedo !== false) {
        entry.redo = () => {
            log.push(`redo ${label}`);
        };
    }
    return entry;
}

/** A legacy-mechanism adapter whose availability the test controls. */
function makeProvider(
    name: string,
    log: string[],
    available: () => boolean,
): ILegacyUndoProvider {
    return {
        name,
        canUndo: available,
        undo: () => {
            log.push(`legacy ${name}`);
        },
    };
}

describe("UndoStack", () => {
    let stack: UndoStack;
    let log: string[];

    beforeEach(() => {
        stack = new UndoStack();
        log = [];
    });

    describe("basic undo and redo", () => {
        it("has nothing to undo or redo when empty", () => {
            expect(stack.canUndo()).toBe(false);
            expect(stack.canRedo()).toBe(false);
        });

        it("undoes the most recent entry first", () => {
            stack.push(makeEntry("first", log));
            stack.push(makeEntry("second", log));
            expect(stack.canUndo()).toBe(true);

            stack.undo();
            stack.undo();

            expect(log).toEqual(["undo second", "undo first"]);
            expect(stack.canUndo()).toBe(false);
        });

        it("redoes in the reverse order of undoing", () => {
            stack.push(makeEntry("first", log));
            stack.push(makeEntry("second", log));
            stack.undo();
            stack.undo();
            expect(stack.canRedo()).toBe(true);

            stack.redo();
            stack.redo();

            expect(log).toEqual([
                "undo second",
                "undo first",
                "redo first",
                "redo second",
            ]);
            expect(stack.canRedo()).toBe(false);
            expect(stack.canUndo()).toBe(true);
        });

        it("does nothing when asked to undo or redo past the end", () => {
            stack.push(makeEntry("only", log));
            stack.undo();
            expect(log).toEqual(["undo only"]);

            stack.undo(); // nothing left
            stack.redo();
            stack.redo(); // nothing left to redo either

            expect(log).toEqual(["undo only", "redo only"]);
        });
    });

    describe("the redo branch", () => {
        it("is discarded by a new push, so the new entry is what gets undone", () => {
            stack.push(makeEntry("first", log));
            stack.push(makeEntry("second", log));
            stack.undo();
            // Sanity check: "second" is undone and would otherwise be redoable.
            expect(log).toEqual(["undo second"]);
            expect(stack.canRedo()).toBe(true);

            stack.push(makeEntry("third", log));

            expect(stack.canRedo()).toBe(false);
            expect(stack.getEntryCount()).toBe(2); // first, third — "second" is gone
            stack.undo();
            expect(log).toEqual(["undo second", "undo third"]);
        });

        it("stops at an entry that cannot redo, rather than skipping it", () => {
            stack.push(makeEntry("noRedo", log, { canRedo: false }));
            stack.undo();

            expect(stack.canRedo()).toBe(false);
            stack.redo();

            expect(log).toEqual(["undo noRedo"]);
        });
    });

    describe("lazy redo capture", () => {
        it("calls prepareRedo immediately before undo, not at push time", () => {
            const entry = makeEntry("captured", log);
            entry.prepareRedo = () => {
                log.push("prepareRedo");
            };

            stack.push(entry);
            // The whole point: pushing costs nothing extra. This is what keeps Redo cheap on the
            // common path (every typing transaction).
            expect(log).toEqual([]);

            stack.undo();

            expect(log).toEqual(["prepareRedo", "undo captured"]);
        });
    });

    describe("page scoping", () => {
        it("discards entries for other pages when the page changes", () => {
            stack.setCurrentPageId("page1");
            stack.push(makeEntry("onPage1", log));
            stack.push(makeEntry("alsoPage1", log));
            expect(stack.getEntryCount()).toBe(2);

            stack.setCurrentPageId("page2");

            expect(stack.getEntryCount()).toBe(0);
            expect(stack.canUndo()).toBe(false);
        });

        it("keeps entries with no page id, so deleting a page stays undoable", () => {
            stack.setCurrentPageId("page1");
            stack.push(makeEntry("pageScoped", log));
            stack.push(makeEntry("deletePage", log, { pageId: undefined }));

            stack.setCurrentPageId("page2");

            expect(stack.getEntryCount()).toBe(1);
            expect(stack.peekUndoLabel()).toBe("deletePage");
            stack.undo();
            expect(log).toEqual(["undo deletePage"]);
        });

        it("keeps the undo position pointing at the same entry after filtering", () => {
            stack.setCurrentPageId("page1");
            stack.push(makeEntry("survives", log, { pageId: undefined }));
            stack.push(makeEntry("dropped", log));
            stack.push(makeEntry("alsoSurvives", log, { pageId: undefined }));
            stack.undo(); // undoes alsoSurvives; it is now the redo entry
            expect(stack.peekUndoLabel()).toBe("dropped");

            stack.setCurrentPageId("page2");

            // "dropped" is gone, so the next undo is "survives" and the redo branch is intact.
            expect(stack.peekUndoLabel()).toBe("survives");
            expect(stack.peekRedoLabel()).toBe("alsoSurvives");
        });

        it("discards page-scoped entries on a page-frame reload that keeps the same page", () => {
            stack.setCurrentPageId("page1");
            stack.push(makeEntry("pageScoped", log));
            stack.push(makeEntry("deletePage", log, { pageId: undefined }));

            // Ctrl+wheel zoom and leaving Change Layout mode both rebuild the page frame without
            // changing page, so setCurrentPageId would not notice, but the captured DOM is stale.
            stack.clearPageScopedEntries();

            expect(stack.getEntryCount()).toBe(1);
            expect(stack.peekUndoLabel()).toBe("deletePage");
        });

        it("does not discard anything when told the page id it already has", () => {
            stack.setCurrentPageId("page1");
            stack.push(makeEntry("onPage1", log));

            stack.setCurrentPageId("page1");

            expect(stack.getEntryCount()).toBe(1);
        });
    });

    describe("bounding", () => {
        it("drops the oldest entry rather than growing without limit", () => {
            for (let i = 0; i < kMaxUndoEntries + 5; i++) {
                stack.push(makeEntry(`entry${i}`, log));
            }

            expect(stack.getEntryCount()).toBe(kMaxUndoEntries);
            expect(stack.peekUndoLabel()).toBe(
                `entry${kMaxUndoEntries + 5 - 1}`,
            );
            // Undoing all the way down must stop cleanly at the truncated end.
            for (let i = 0; i < kMaxUndoEntries; i++) {
                stack.undo();
            }
            expect(stack.canUndo()).toBe(false);
            expect(log.length).toBe(kMaxUndoEntries);
            expect(log[log.length - 1]).toBe("undo entry5");
        });
    });

    describe("legacy providers", () => {
        it("consults them in registration order, before our own entries", () => {
            stack.registerLegacyProvider(
                makeProvider("origami", log, () => true),
            );
            stack.registerLegacyProvider(
                makeProvider("toolbox", log, () => true),
            );
            stack.push(makeEntry("ours", log));

            stack.undo();

            expect(log).toEqual(["legacy origami"]);
        });

        it("falls through to the next provider, and then to our entries", () => {
            let origamiHasSomething = true;
            stack.registerLegacyProvider(
                makeProvider("origami", log, () => origamiHasSomething),
            );
            stack.push(makeEntry("ours", log));

            stack.undo();
            expect(log).toEqual(["legacy origami"]);

            origamiHasSomething = false;
            stack.undo();

            expect(log).toEqual(["legacy origami", "undo ours"]);
        });

        it("reports canUndo when only a legacy provider has something", () => {
            expect(stack.canUndo()).toBe(false); // sanity check: nothing yet
            stack.registerLegacyProvider(
                makeProvider("image", log, () => true),
            );

            expect(stack.canUndo()).toBe(true);
        });

        it("takes no part in redo", () => {
            stack.registerLegacyProvider(
                makeProvider("origami", log, () => true),
            );

            // Origami keeps its own Ctrl+Y handler until it is converted, so the shared stack must
            // not claim to be able to redo on its behalf.
            expect(stack.canRedo()).toBe(false);
            stack.redo();
            expect(log).toEqual([]);
        });
    });

    describe("undoable scopes (runUndoable's mechanism)", () => {
        it("keeps only the first entry pushed in a scope, relabelled with the scope label", () => {
            stack.beginUndoableScope("Delete canvas element");
            stack.push(makeEntry("inner image undo", log));
            stack.push(makeEntry("another inner push", log));
            stack.endUndoableScope();

            expect(stack.getEntryCount()).toBe(1);
            expect(stack.peekUndoLabel()).toBe("Delete canvas element");
        });

        it("treats a nested scope as part of the outer one", () => {
            stack.beginUndoableScope("outer");
            stack.beginUndoableScope("inner");
            stack.push(makeEntry("pushed by inner", log));
            stack.endUndoableScope();
            stack.push(makeEntry("pushed by outer", log));
            stack.endUndoableScope();

            expect(stack.getEntryCount()).toBe(1);
            expect(stack.peekUndoLabel()).toBe("outer");
        });

        it("keeps the outer operation's own entry even when an inner scope pushed first", () => {
            // The inner layer usually runs, and records, before the outer operation gets to
            // record its own entry. The outer entry describes the whole gesture, so it must win.
            stack.beginUndoableScope("outer");
            stack.beginUndoableScope("inner");
            stack.push(makeEntry("inner", log));
            stack.endUndoableScope();
            stack.push(makeEntry("outer", log));
            stack.endUndoableScope();

            stack.undo();
            expect(log).toEqual(["undo outer"]);
        });

        it("falls back to the first inner entry when the outer scope records nothing itself", () => {
            stack.beginUndoableScope("outer wrapper");
            stack.beginUndoableScope("inner a");
            stack.push(makeEntry("a", log));
            stack.endUndoableScope();
            stack.beginUndoableScope("inner b");
            stack.push(makeEntry("b", log));
            stack.endUndoableScope();
            stack.endUndoableScope();

            expect(stack.getEntryCount()).toBe(1);
            expect(stack.peekUndoLabel()).toBe("outer wrapper");
            stack.undo();
            expect(log).toEqual(["undo a"]);
        });

        it("records nothing while the scope is still open", () => {
            stack.beginUndoableScope("gesture");
            stack.push(makeEntry("a", log));
            expect(stack.getEntryCount()).toBe(0);
            expect(stack.canUndo()).toBe(false);
            stack.endUndoableScope();
            expect(stack.getEntryCount()).toBe(1);
        });

        it("starts a fresh claim for each new outermost scope", () => {
            stack.beginUndoableScope("first gesture");
            stack.push(makeEntry("a", log));
            stack.endUndoableScope();
            stack.beginUndoableScope("second gesture");
            stack.push(makeEntry("b", log));
            stack.endUndoableScope();

            expect(stack.getEntryCount()).toBe(2);
            expect(stack.peekUndoLabel()).toBe("second gesture");
        });

        it("records normally again once the scope is closed", () => {
            stack.beginUndoableScope("gesture");
            stack.push(makeEntry("a", log));
            stack.push(makeEntry("b", log));
            stack.endUndoableScope();
            expect(stack.isInUndoableScope()).toBe(false);

            stack.push(makeEntry("afterwards", log));

            expect(stack.getEntryCount()).toBe(2);
            expect(stack.peekUndoLabel()).toBe("afterwards");
        });
    });

    describe("a failing undo or redo", () => {
        function failingEntry(
            label: string,
            options: {
                failUndo?: boolean;
                failRedo?: boolean;
                failPrepare?: boolean;
            },
        ): IUndoEntry {
            return {
                label,
                pageId: "page1",
                kind: "custom",
                prepareRedo: () => {
                    if (options.failPrepare) throw new Error("prepare failed");
                },
                undo: () => {
                    if (options.failUndo) throw new Error("undo failed");
                    log.push(`undo ${label}`);
                },
                redo: () => {
                    if (options.failRedo) throw new Error("redo failed");
                    log.push(`redo ${label}`);
                },
            };
        }

        it("leaves a synchronously failing entry as the next thing to undo", () => {
            stack.push(makeEntry("a", log));
            stack.push(failingEntry("b", { failUndo: true }));
            expect(stack.peekUndoLabel()).toBe("b"); // sanity

            expect(() => stack.undo()).toThrow("undo failed");

            expect(stack.peekUndoLabel()).toBe("b");
            expect(stack.canRedo()).toBe(false);
            expect(log).toEqual([]);
        });

        it("does the same when prepareRedo is what fails", () => {
            stack.push(failingEntry("b", { failPrepare: true }));
            expect(() => stack.undo()).toThrow("prepare failed");
            expect(stack.peekUndoLabel()).toBe("b");
            expect(stack.canRedo()).toBe(false);
        });

        it("leaves an asynchronously failing entry as the next thing to undo", async () => {
            const entry: IUndoEntry = {
                label: "async b",
                pageId: "page1",
                kind: "custom",
                undo: () => Promise.reject(new Error("async undo failed")),
                redo: () => {},
            };
            stack.push(makeEntry("a", log));
            stack.push(entry);

            await expect(stack.undo()).rejects.toThrow("async undo failed");

            expect(stack.peekUndoLabel()).toBe("async b");
            expect(stack.canRedo()).toBe(false);
            // The guard is released, so the user can retry; it fails again, harmlessly, and the
            // entry is still where it was.
            await expect(stack.undo()).rejects.toThrow("async undo failed");
            expect(stack.peekUndoLabel()).toBe("async b");
        });

        it("leaves a failing redo as the next thing to redo", async () => {
            stack.push(failingEntry("b", { failRedo: true }));
            stack.undo();
            expect(stack.peekRedoLabel()).toBe("b"); // sanity

            expect(() => stack.redo()).toThrow("redo failed");

            expect(stack.peekRedoLabel()).toBe("b");
            expect(stack.canUndo()).toBe(false);

            const asyncEntry: IUndoEntry = {
                label: "async c",
                pageId: "page1",
                kind: "custom",
                undo: () => {},
                redo: () => Promise.reject(new Error("async redo failed")),
            };
            stack.push(asyncEntry);
            stack.undo();
            await expect(stack.redo()).rejects.toThrow("async redo failed");
            expect(stack.peekRedoLabel()).toBe("async c");
        });
    });

    describe("a page change during an in-flight or held operation", () => {
        it("does not leave a dangling index when the failing entry was cleared meanwhile", async () => {
            let reject: (e: Error) => void = () => {
                throw new Error("test bug: reject called before it was set");
            };
            const slowFailing: IUndoEntry = {
                label: "slow failing",
                pageId: "page1",
                kind: "custom",
                undo: () =>
                    new Promise<void>((_, rej) => {
                        reject = rej;
                    }),
                redo: () => {},
            };
            stack.setCurrentPageId("page1");
            stack.push(makeEntry("a", log));
            stack.push(slowFailing);

            const pending = stack.undo() as Promise<void>;
            // The user changes page while the undo is still in flight.
            stack.clearPageScopedEntries();
            expect(stack.getEntryCount()).toBe(0); // sanity
            reject(new Error("too late"));
            await expect(pending).rejects.toThrow("too late");

            // A numeric rollback would have pointed past the end here.
            expect(stack.canUndo()).toBe(false);
            expect(stack.peekUndoLabel()).toBeUndefined();
            expect(() => stack.undo()).not.toThrow();
        });

        it("drops a held page-scoped push when the page is left before the scope closes", () => {
            stack.setCurrentPageId("page1");
            stack.beginUndoableScope("async gesture");
            stack.push(makeEntry("old page work", log));
            stack.push(makeEntry("delete page", log, { pageId: undefined }));
            // Navigation happens while the gesture is still awaiting.
            stack.clearPageScopedEntries();
            stack.endUndoableScope();

            // The page-scoped entry is gone; the one that survives page changes was kept.
            expect(stack.getEntryCount()).toBe(1);
            stack.undo();
            expect(log).toEqual(["undo delete page"]);
        });
    });

    describe("asynchronous entries", () => {
        it("waits for an async undo before allowing another", async () => {
            let release: () => void = () => {
                throw new Error("test bug: release called before it was set");
            };
            const slow: IUndoEntry = {
                label: "slow",
                pageId: "page1",
                kind: "custom",
                undo: () =>
                    new Promise<void>((resolve) => {
                        release = () => {
                            log.push("undo slow finished");
                            resolve();
                        };
                    }),
            };
            stack.push(slow);
            stack.push(makeEntry("fast", log));

            // "fast" is on top and is synchronous, so this completes before returning.
            const inFlight = stack.undo();
            expect(log).toEqual(["undo fast"]);
            expect(inFlight).toBeUndefined();

            const slowPromise = stack.undo();
            // While that is in flight a second undo must be ignored rather than interleaved.
            stack.undo();
            expect(log).toEqual(["undo fast"]);

            release();
            await slowPromise;

            expect(log).toEqual(["undo fast", "undo slow finished"]);
        });

        it("releases the guard when an undo throws, so undo is not wedged", () => {
            const bad: IUndoEntry = {
                label: "bad",
                pageId: "page1",
                kind: "custom",
                undo: () => {
                    throw new Error("boom");
                },
            };
            stack.push(makeEntry("good", log));
            stack.push(bad);

            expect(() => stack.undo()).toThrow("boom");

            // The guard is released, so the stack still works: new work can be recorded and
            // undone. (The broken entry itself stays put as the next thing to undo — see
            // "a failing undo or redo" — rather than being skipped.)
            stack.push(makeEntry("later", log));
            stack.undo();
            expect(log).toEqual(["undo later"]);
            expect(stack.peekUndoLabel()).toBe("bad");
        });
    });

    describe("clear", () => {
        it("discards everything, including entries that survive page changes", () => {
            stack.push(makeEntry("pageScoped", log));
            stack.push(makeEntry("deletePage", log, { pageId: undefined }));
            expect(stack.canUndo()).toBe(true); // sanity check

            stack.clear();

            expect(stack.getEntryCount()).toBe(0);
            expect(stack.canUndo()).toBe(false);
            expect(stack.canRedo()).toBe(false);
        });
    });
});
