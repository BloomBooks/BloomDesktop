import { describe, it, expect } from "vitest";
import {
    postBookSelection,
    whenBookSelectionSettled,
} from "./bookSelectionIntent";

// A post we can hold open, so a test can observe what happens while a book selection
// is still in flight.
function makeControllablePost() {
    let resolvePost: () => void = () => {
        throw new Error("post was never started");
    };
    let started = false;
    const send = () =>
        new Promise<void>((resolve) => {
            started = true;
            resolvePost = resolve;
        });
    return {
        send,
        finish: () => resolvePost(),
        get started() {
            return started;
        },
    };
}

// Let any already-resolved promise callbacks run.
const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

// Note that the module deliberately keeps one chain for the whole app, so these tests share it.
// Each test leaves the chain settled so the next one starts clean.
describe("bookSelectionIntent", () => {
    it("does not make an action wait when no book selection is in flight", async () => {
        let settled = false;
        whenBookSelectionSettled().then(() => (settled = true));
        await flush();
        expect(settled).toBe(true);
    });

    it("makes an action wait until the book selection it follows has finished", async () => {
        const selection = makeControllablePost();
        postBookSelection(selection.send);
        await flush();
        expect(selection.started).toBe(true); // sanity check: the selection really is in flight

        let actionReady = false;
        whenBookSelectionSettled().then(() => (actionReady = true));
        await flush();
        if (actionReady)
            throw new Error(
                "the action ran while the book selection was still in flight; it would act on the previously selected book",
            );

        selection.finish();
        await flush();
        expect(actionReady).toBe(true);
    });

    it("sends book selections one at a time, in the order they were clicked", async () => {
        const first = makeControllablePost();
        const second = makeControllablePost();
        postBookSelection(first.send);
        postBookSelection(second.send);
        await flush();

        expect(first.started).toBe(true);
        if (second.started)
            throw new Error(
                "the second selection was sent before the first finished; the two could take effect out of order",
            );

        first.finish();
        await flush();
        expect(second.started).toBe(true);

        second.finish();
        await whenBookSelectionSettled();
    });

    it("keeps going after a book selection fails", async () => {
        postBookSelection(() => Promise.reject(new Error("selection failed")));
        const next = makeControllablePost();
        postBookSelection(next.send);
        await flush();
        expect(next.started).toBe(true);

        next.finish();
        await whenBookSelectionSettled();
    });
});
