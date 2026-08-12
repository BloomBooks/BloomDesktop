// Tests for runUndoable (BL-6681). See docs/retire-ckeditor/PLAN.md 4.13.
//
// The case that motivates all of this: deleting a canvas element whose content is a background
// image already records an image undo of its own, so one gesture must not leave two entries.

import { describe, it, expect, beforeEach } from "vitest";
import { runUndoable } from "./runUndoable";
import { UndoStack } from "./UndoStack";
import { IUndoEntry } from "./undoTypes";

function makeEntry(label: string, log: string[]): IUndoEntry {
    return {
        label,
        pageId: "page1",
        kind: "custom",
        undo: () => {
            log.push(`undo ${label}`);
        },
    };
}

describe("runUndoable", () => {
    let stack: UndoStack;
    let log: string[];

    beforeEach(() => {
        stack = new UndoStack();
        log = [];
    });

    it("produces one entry for a gesture whose inner code also records an undo", () => {
        runUndoable(
            "Delete canvas element",
            () => {
                // What deleteCanvasElement's background-image branch does today.
                stack.push(makeEntry("image operation", log));
                stack.push(makeEntry("element removal", log));
            },
            stack,
        );

        expect(stack.getEntryCount()).toBe(1);
        expect(stack.peekUndoLabel()).toBe("Delete canvas element");
        stack.undo();
        // The kept entry is the *first* one pushed, relabelled — not a new synthetic entry.
        expect(log).toEqual(["undo image operation"]);
    });

    it("returns the operation's value", () => {
        const result = runUndoable("compute", () => 42, stack);

        expect(result).toBe(42);
    });

    it("closes the scope so later work records normally", () => {
        runUndoable("gesture", () => stack.push(makeEntry("a", log)), stack);
        expect(stack.isInUndoableScope()).toBe(false);

        stack.push(makeEntry("later", log));

        expect(stack.getEntryCount()).toBe(2);
        expect(stack.peekUndoLabel()).toBe("later");
    });

    it("closes the scope even when the operation throws", () => {
        expect(() =>
            runUndoable(
                "gesture that fails",
                () => {
                    stack.push(makeEntry("recorded before the failure", log));
                    throw new Error("boom");
                },
                stack,
            ),
        ).toThrow("boom");

        // A leaked scope would silently swallow every later undo entry, which is far worse than
        // the original failure and much harder to diagnose.
        expect(stack.isInUndoableScope()).toBe(false);
        expect(stack.getEntryCount()).toBe(1);
    });

    it("treats a nested runUndoable as part of the outer gesture", () => {
        runUndoable(
            "outer gesture",
            () => {
                runUndoable(
                    "inner gesture",
                    () => stack.push(makeEntry("inner", log)),
                    stack,
                );
                stack.push(makeEntry("outer", log));
            },
            stack,
        );

        expect(stack.getEntryCount()).toBe(1);
        expect(stack.peekUndoLabel()).toBe("outer gesture");
    });

    it("holds the scope open across an await, and passes the promise through", async () => {
        let resolveInner: () => void = () => {
            throw new Error("test bug: resolve called before it was set");
        };
        const gate = new Promise<void>((resolve) => {
            resolveInner = resolve;
        });

        const promise = runUndoable(
            "async gesture",
            async () => {
                stack.push(makeEntry("first", log));
                await gate;
                stack.push(makeEntry("after the await", log));
                return "done";
            },
            stack,
        );

        // Sanity check: we really are mid-operation, with the scope still open.
        expect(stack.isInUndoableScope()).toBe(true);
        resolveInner();
        const result = await promise;

        expect(result).toBe("done");
        expect(stack.isInUndoableScope()).toBe(false);
        expect(stack.getEntryCount()).toBe(1);
        expect(stack.peekUndoLabel()).toBe("async gesture");
    });

    it("closes the scope when an async operation rejects", async () => {
        const promise = runUndoable(
            "async gesture that fails",
            async () => {
                throw new Error("async boom");
            },
            stack,
        );

        await expect(promise).rejects.toThrow("async boom");
        expect(stack.isInUndoableScope()).toBe(false);
    });
});
