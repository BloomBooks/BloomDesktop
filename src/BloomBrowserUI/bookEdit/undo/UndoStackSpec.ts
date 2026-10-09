// Tests for the one undo stack (BL-6681). See docs/retire-ckeditor/PLAN.md 4.1.
//
// These are specification tests, not characterization tests: the stack is new code, so each case
// pins a decision the plan made rather than recording what some existing code happens to do.

import { describe, it, expect, beforeEach } from "vitest";
import { nextChangeOrder } from "./changeOrder";
import { UndoStack } from "./UndoStack";
import { ILegacyUndoProvider, IUndoEntry, kMaxUndoEntries } from "./undoTypes";

/** A minimal entry that appends its label to `log` when undone or redone. */
function makeEntry(
    label: string,
    log: string[],
    options?: { canRedo?: boolean },
): IUndoEntry {
    const entry: IUndoEntry = {
        label,
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

/**
 * A legacy-mechanism adapter whose availability the test controls. `options.undoes` says whether
 * its undo finds something (default yes); `options.lastChangeOrder` makes it report its changes on
 * the shared sequence, as the CKEditor provider does.
 */
function makeProvider(
    name: string,
    log: string[],
    available: () => boolean,
    options?: { undoes?: () => boolean; lastChangeOrder?: () => number },
): ILegacyUndoProvider {
    return {
        name,
        canUndo: available,
        undo: () => {
            log.push(`legacy ${name}`);
            return options?.undoes ? options.undoes() : true;
        },
        lastChangeOrder: options?.lastChangeOrder,
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

    describe("an entry that is not available just now", () => {
        it("blocks undo and redo rather than being skipped", () => {
            let available = false;
            stack.push(makeEntry("first", log));
            const second = makeEntry("second", log);
            second.isAvailable = () => available;
            stack.push(second);

            expect(stack.canUndo()).toBe(false);
            stack.undo();
            expect(log).toEqual([]);

            available = true;
            expect(stack.canUndo()).toBe(true);
            stack.undo();
            expect(log).toEqual(["undo second"]);

            available = false;
            expect(stack.canRedo()).toBe(false);
            stack.redo();
            expect(log).toEqual(["undo second"]);
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

        it("passes the turn on when a provider's undo finds nothing to undo", () => {
            // CKEditor can claim an undo it cannot perform; the person's Undo must still land.
            stack.registerLegacyProvider(
                makeProvider("ckeditor", log, () => true, {
                    undoes: () => false,
                }),
            );
            stack.push(makeEntry("ours", log));

            stack.undo();

            expect(log).toEqual(["legacy ckeditor", "undo ours"]);
        });

        it("lets a provider that reports its changes answer only if it changed after our newest entry", () => {
            let ckeditorChangedAt = 0;
            stack.registerLegacyProvider(
                makeProvider("ckeditor", log, () => true, {
                    lastChangeOrder: () => ckeditorChangedAt,
                }),
            );
            // Typing, then a table row: the row is newer, so the stack's entry answers.
            ckeditorChangedAt = nextChangeOrder();
            stack.push(makeEntry("add row", log));
            stack.undo();
            expect(log).toEqual(["undo add row"]);

            // A row, then typing: the typing is newer, so CKEditor answers first.
            stack.push(makeEntry("add another row", log));
            ckeditorChangedAt = nextChangeOrder();
            stack.undo();
            expect(log).toEqual(["undo add row", "legacy ckeditor"]);
        });

        it("still lets a provider that does not report its changes answer first", () => {
            stack.registerLegacyProvider(
                makeProvider("toolbox", log, () => true),
            );
            stack.push(makeEntry("ours", log));

            stack.undo();

            expect(log).toEqual(["legacy toolbox"]);
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
        it("makes one entry of everything pushed in a scope, labelled with the scope label", () => {
            stack.beginUndoableScope("Delete canvas element");
            stack.push(makeEntry("inner image undo", log));
            stack.push(makeEntry("another inner push", log));
            stack.endUndoableScope();

            expect(stack.getEntryCount()).toBe(1);
            expect(stack.peekUndoLabel()).toBe("Delete canvas element");
            // One undo reverses every part.
            stack.undo();
            expect(log).toEqual([
                "undo another inner push",
                "undo inner image undo",
            ]);
            expect(stack.canUndo()).toBe(false);
        });

        it("records a single push as it is, with the scope's label", () => {
            const only = makeEntry("the one push", log);
            stack.beginUndoableScope("gesture");
            stack.push(only);
            stack.endUndoableScope();

            expect(stack.getEntryCount()).toBe(1);
            expect(stack.peekUndoLabel()).toBe("gesture");
            stack.undo();
            expect(log).toEqual(["undo the one push"]);
            expect(only.label).toBe("gesture");
        });

        it("treats a nested scope as part of the outer one", () => {
            stack.beginUndoableScope("outer");
            stack.beginUndoableScope("inner");
            stack.push(makeEntry("pushed by inner", log));
            stack.endUndoableScope();
            // Closing the inner scope recorded nothing.
            expect(stack.getEntryCount()).toBe(0);
            stack.push(makeEntry("pushed by outer", log));
            stack.endUndoableScope();

            expect(stack.getEntryCount()).toBe(1);
            expect(stack.peekUndoLabel()).toBe("outer");
        });

        it("undoes the parts last first, and redoes them in the order they happened", () => {
            stack.beginUndoableScope("outer");
            stack.beginUndoableScope("inner");
            stack.push(makeEntry("inner", log));
            stack.endUndoableScope();
            stack.push(makeEntry("outer", log));
            stack.endUndoableScope();

            stack.undo();
            expect(log).toEqual(["undo outer", "undo inner"]);
            expect(stack.canRedo()).toBe(true);
            stack.redo();
            expect(log).toEqual([
                "undo outer",
                "undo inner",
                "redo inner",
                "redo outer",
            ]);
            expect(stack.canUndo()).toBe(true);
            expect(stack.canRedo()).toBe(false);
        });

        it("cannot be redone unless every part can", () => {
            stack.beginUndoableScope("gesture");
            stack.push(makeEntry("can redo", log));
            stack.push(makeEntry("cannot redo", log, { canRedo: false }));
            stack.endUndoableScope();

            stack.undo();
            expect(log.length).toBe(2); // sanity: both parts were undone
            expect(stack.canRedo()).toBe(false);
        });

        it("calls each part's prepareRedo just before that part's own undo", () => {
            const withPrepare = (label: string): IUndoEntry => ({
                ...makeEntry(label, log),
                prepareRedo: () => {
                    log.push(`prepare ${label}`);
                },
            });
            stack.beginUndoableScope("gesture");
            stack.push(withPrepare("a"));
            stack.push(withPrepare("b"));
            stack.endUndoableScope();
            expect(log).toEqual([]); // sanity: nothing is captured at push time

            stack.undo();

            expect(log).toEqual(["prepare b", "undo b", "prepare a", "undo a"]);
        });

        it("stops at a part that fails, and the stack discards everything", () => {
            const failing: IUndoEntry = {
                ...makeEntry("failing", log),
                undo: () => {
                    throw new Error("part failed");
                },
            };
            stack.push(makeEntry("earlier gesture", log));
            stack.beginUndoableScope("gesture");
            stack.push(makeEntry("first", log));
            stack.push(failing);
            stack.push(makeEntry("last", log));
            stack.endUndoableScope();

            expect(() => stack.undo()).toThrow("part failed");

            // "first" never ran: the run stopped at the failure.
            expect(log).toEqual(["undo last"]);
            expect(stack.getEntryCount()).toBe(0);
            expect(stack.canRedo()).toBe(false);
        });
        it("waits for an asynchronous part before undoing the one before it", async () => {
            let finishSlow: () => void = () => {
                throw new Error(
                    "test bug: finishSlow called before it was set",
                );
            };
            const slow: IUndoEntry = {
                ...makeEntry("slow", log),
                undo: () =>
                    new Promise<void>((resolve) => {
                        finishSlow = () => {
                            log.push("undo slow");
                            resolve();
                        };
                    }),
            };
            stack.beginUndoableScope("gesture");
            stack.push(makeEntry("first", log));
            stack.push(slow);
            stack.endUndoableScope();

            const undone = stack.undo();
            expect(log).toEqual([]); // the slow part is still running
            finishSlow();
            await undone;

            expect(log).toEqual(["undo slow", "undo first"]);
        });

        it("records nothing while the scope is still open", () => {
            stack.beginUndoableScope("gesture");
            stack.push(makeEntry("a", log));
            expect(stack.getEntryCount()).toBe(0);
            expect(stack.canUndo()).toBe(false);
            stack.endUndoableScope();
            expect(stack.getEntryCount()).toBe(1);
        });

        it("starts a fresh entry for each new outermost scope", () => {
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

        it("discards the whole stack when an undo throws, and still reports the failure", () => {
            stack.push(makeEntry("a", log));
            stack.push(failingEntry("b", { failUndo: true }));
            expect(stack.getEntryCount()).toBe(2); // sanity

            expect(() => stack.undo()).toThrow("undo failed");

            // "a" was not undone, and is gone: it assumed the state "b" would have left.
            expect(stack.getEntryCount()).toBe(0);
            expect(stack.canUndo()).toBe(false);
            expect(stack.canRedo()).toBe(false);
            expect(log).toEqual([]);
        });

        it("does the same when prepareRedo is what fails", () => {
            stack.push(makeEntry("a", log));
            stack.push(failingEntry("b", { failPrepare: true }));

            expect(() => stack.undo()).toThrow("prepare failed");

            expect(stack.getEntryCount()).toBe(0);
        });

        it("does the same when an asynchronous undo rejects, and is not wedged afterwards", async () => {
            const entry: IUndoEntry = {
                label: "async b",
                kind: "custom",
                undo: () => Promise.reject(new Error("async undo failed")),
                redo: () => {},
            };
            stack.push(makeEntry("a", log));
            stack.push(entry);

            await expect(stack.undo()).rejects.toThrow("async undo failed");

            expect(stack.getEntryCount()).toBe(0);
            // The guard is released: new work records and undoes normally.
            stack.push(makeEntry("later", log));
            stack.undo();
            expect(log).toEqual(["undo later"]);
        });

        it("discards the whole stack when a redo fails, synchronously or not", async () => {
            stack.push(makeEntry("a", log));
            stack.push(failingEntry("b", { failRedo: true }));
            stack.undo();
            expect(stack.peekRedoLabel()).toBe("b"); // sanity

            expect(() => stack.redo()).toThrow("redo failed");

            expect(stack.getEntryCount()).toBe(0);
            expect(stack.canUndo()).toBe(false);
            expect(stack.canRedo()).toBe(false);

            stack.push({
                label: "async c",
                kind: "custom",
                undo: () => {},
                redo: () => Promise.reject(new Error("async redo failed")),
            });
            stack.undo();
            await expect(stack.redo()).rejects.toThrow("async redo failed");
            expect(stack.getEntryCount()).toBe(0);
        });

        it("also drops what a gesture still being recorded was holding", () => {
            stack.push(failingEntry("b", { failUndo: true }));
            stack.beginUndoableScope("gesture in progress");
            stack.push(makeEntry("held", log));

            expect(() => stack.undo()).toThrow("undo failed");
            stack.endUndoableScope();

            // The gesture started from a state the failure has since changed.
            expect(stack.getEntryCount()).toBe(0);
        });
    });
    describe("asynchronous entries", () => {
        it("waits for an async undo before allowing another", async () => {
            let release: () => void = () => {
                throw new Error("test bug: release called before it was set");
            };
            const slow: IUndoEntry = {
                label: "slow",
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
                kind: "custom",
                undo: () => {
                    throw new Error("boom");
                },
            };
            stack.push(makeEntry("good", log));
            stack.push(bad);

            expect(() => stack.undo()).toThrow("boom");

            // The guard is released, so the stack still works: new work can be recorded and
            // undone. (The failure discarded everything before it; see "a failing undo or redo".)
            stack.push(makeEntry("later", log));
            stack.undo();
            expect(log).toEqual(["undo later"]);
            expect(stack.canUndo()).toBe(false);
        });
    });

    describe("clear", () => {
        it("discards everything", () => {
            stack.push(makeEntry("a", log));
            stack.push(makeEntry("b", log));
            stack.undo();
            expect(stack.canUndo()).toBe(true); // sanity check
            expect(stack.canRedo()).toBe(true); // sanity check

            stack.clear();

            expect(stack.getEntryCount()).toBe(0);
            expect(stack.canUndo()).toBe(false);
            expect(stack.canRedo()).toBe(false);
        });

        it("also discards what a scope still open across the clear was holding, and what it pushes afterwards", () => {
            // An undo that fails mid-gesture clears the stack; the gesture's scope closes later
            // and must not resurrect an entry the clear discarded.
            stack.beginUndoableScope("async gesture");
            stack.push(makeEntry("held before clear", log));
            expect(stack.isInUndoableScope()).toBe(true); // sanity

            stack.clear();
            stack.push(makeEntry("pushed after clear", log));
            stack.endUndoableScope();

            expect(stack.getEntryCount()).toBe(0);
            expect(stack.canUndo()).toBe(false);
        });
    });
});
