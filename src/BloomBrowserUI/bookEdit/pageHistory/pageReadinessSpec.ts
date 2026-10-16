// Tests of when the page is ready for its undo history (pageReadiness.ts): docs/undo/MINIMUM.md
// item 6. A fake window stands for the page frame, with fake CKEditor instances.
import { describe, expect, it } from "vitest";
import { PageReadinessWaits, whenPageIsReady } from "./pageReadiness";

/** A CKEditor 4 instance, as far as pageReadiness looks at one. */
class FakeEditor {
    public status = "loaded";
    private listeners: Record<string, (() => void)[]> = {};
    public once(event: string, callback: () => void): void {
        (this.listeners[event] ??= []).push(callback);
    }
    public fire(event: string): void {
        if (event === "instanceReady") this.status = "ready";
        if (event === "destroy") this.status = "destroyed";
        const listeners = this.listeners[event] ?? [];
        this.listeners[event] = [];
        listeners.forEach((listener) => listener());
    }
}

function fakeWindow(editors: Record<string, FakeEditor>): Window {
    return {
        document: { readyState: "complete" },
        CKEDITOR: { instances: editors },
    } as unknown as Window;
}

/** Waits that resolve only when the test says, and record the order they were asked in. */
function controlledWaits(asked: string[]) {
    const pending: Record<string, (() => void)[]> = {
        delays: [],
        toolbox: [],
    };
    const waits: PageReadinessWaits = {
        noPageContentDelays: () => {
            asked.push("delays");
            return new Promise((resolve) => pending.delays.push(resolve));
        },
        toolboxPassesHaveRun: () => {
            asked.push("toolbox");
            return new Promise((resolve) => pending.toolbox.push(resolve));
        },
    };
    const release = async (which: "delays" | "toolbox") => {
        expect(
            pending[which].length,
            `test setup: something should be waiting for ${which}`,
        ).toBeGreaterThan(0);
        pending[which].shift()!();
        await settle();
    };
    return { waits, release };
}

async function settle(): Promise<void> {
    for (let i = 0; i < 10; i++) await Promise.resolve();
}

describe("whenPageIsReady", () => {
    it("waits for every CKEditor, the page content delays and the toolbox, in that order", async () => {
        const first = new FakeEditor();
        const second = new FakeEditor();
        const asked: string[] = [];
        const { waits, release } = controlledWaits(asked);
        let ready = false;
        void whenPageIsReady(fakeWindow({ first, second }), waits).then(
            () => (ready = true),
        );

        await settle();
        expect(asked, "nothing else is asked before CKEditor is ready").toEqual(
            [],
        );
        first.fire("instanceReady");
        await settle();
        expect(asked).toEqual([]);
        second.fire("instanceReady");
        await settle();
        expect(asked).toEqual(["delays"]);

        await release("delays");
        expect(asked).toEqual(["delays", "toolbox"]);
        await release("toolbox");
        // The toolbox's passes can start page content delays of their own.
        expect(asked).toEqual(["delays", "toolbox", "delays"]);
        expect(ready).toBe(false);
        await release("delays");
        expect(ready).toBe(true);
    });

    it("does not wait for an editor that is destroyed before it is ready", async () => {
        const editor = new FakeEditor();
        const asked: string[] = [];
        const { waits } = controlledWaits(asked);
        void whenPageIsReady(fakeWindow({ editor }), waits);
        await settle();
        expect(asked).toEqual([]);
        editor.fire("destroy");
        await settle();
        expect(asked).toEqual(["delays"]);
    });

    it("waits for nothing from CKEditor on a page without it", async () => {
        const asked: string[] = [];
        const { waits } = controlledWaits(asked);
        const pageWindow = {
            document: { readyState: "complete" },
        } as unknown as Window;
        void whenPageIsReady(pageWindow, waits);
        await settle();
        expect(asked).toEqual(["delays"]);
    });
});
