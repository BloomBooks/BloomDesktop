import { beforeEach, describe, expect, it, vi } from "vitest";

// The calls the client makes, in the order it made them, and what each was answered with.
const calls: { endpoint: string; body?: unknown }[] = [];
// Endpoint -> what to answer with. A function answers with a promise the test controls.
let answers: Record<string, () => Promise<{ data?: unknown }>> = {};
const delays: string[] = [];

vi.mock("../js/bloomEditing", () => ({
    addRequestPageContentDelay: (id: string) => delays.push(id),
    removeRequestPageContentDelay: (id: string) => {
        const at = delays.indexOf(id);
        if (at >= 0) delays.splice(at, 1);
    },
}));

vi.mock("../../utils/bloomApi", () => ({
    getAsync: (endpoint: string) => {
        calls.push({ endpoint });
        return (answers[endpoint.split("?")[0]] ?? emptyAnswer)();
    },
    postJsonAsync: (endpoint: string, body: unknown) => {
        calls.push({ endpoint, body });
        return (answers[endpoint] ?? emptyAnswer)();
    },
}));

function emptyAnswer(): Promise<{ data?: unknown }> {
    return Promise.resolve({ data: undefined });
}

/** Let the queue's own promise links settle, without letting any answer resolve. */
async function flushMicrotasks(): Promise<void> {
    for (let tick = 0; tick < 5; tick++) await Promise.resolve();
}

import {
    continueInto,
    getPendingCaret,
    getPendingOverflow,
    isBoundaryBusy,
    peekNext,
    setNextContent,
    unlinkFrom,
    waitForBoundaryQueue,
} from "./flowBoundaryClient";

describe("flowBoundaryClient", () => {
    beforeEach(async () => {
        await waitForBoundaryQueue();
        calls.length = 0;
        delays.length = 0;
        answers = {};
    });

    it("reports no source when C# says no page has one", async () => {
        answers["flowText/pendingOverflow"] = () =>
            Promise.resolve({ data: { pageId: null } });

        expect(await getPendingOverflow("page2", "en")).toBeUndefined();
        expect(calls[0].endpoint).toContain("beforePageId=page2");
        expect(calls[0].endpoint).toContain("lang=en");
    });

    it("hands back the source C# names", async () => {
        answers["flowText/pendingOverflow"] = () =>
            Promise.resolve({
                data: {
                    pageId: "page1",
                    pageNumber: "2",
                    indexInPage: 0,
                    previewText: "and the rest",
                },
            });

        const found = await getPendingOverflow("page2", "en");

        expect(found?.pageNumber).toBe("2");
        expect(found?.previewText).toBe("and the rest");
    });

    it("hands back the new content of the target box", async () => {
        answers["flowText/continueInto"] = () =>
            Promise.resolve({
                data: {
                    chainId: "chain-1",
                    targetHtmlByLang: { en: "<p>tail</p>" },
                },
            });

        const result = await continueInto({
            sourcePageId: "page1",
            sourceIndexInPage: 0,
            targetPageId: "page2",
            targetIndexInPage: 0,
            lang: "en",
        });

        expect(result?.chainId).toBe("chain-1");
        expect(result?.targetHtmlByLang.en).toBe("<p>tail</p>");
        expect(calls[0].body).toEqual({
            sourcePageId: "page1",
            sourceIndexInPage: 0,
            targetPageId: "page2",
            targetIndexInPage: 0,
            lang: "en",
        });
    });

    it("reports no next box when the chain ends on this page", async () => {
        answers["flowText/peekNext"] = () =>
            Promise.resolve({ data: { pageId: null } });

        expect(await peekNext("chain-1", "page2", "en")).toBeUndefined();
    });

    it("hands back what the reader calls the page the next box is on", async () => {
        // The label saying where the text goes needs that, and only C# knows it.
        answers["flowText/peekNext"] = () =>
            Promise.resolve({
                data: {
                    pageId: "page3",
                    pageNumber: "6",
                    indexInPage: 1,
                    html: "<p>tail</p>",
                },
            });

        const next = await peekNext("chain-1", "page2", "en");

        expect(next?.pageNumber).toBe("6");
        expect(next?.pageId).toBe("page3");
        expect(next?.indexInPage).toBe(1);
        expect(next?.html).toBe("<p>tail</p>");
    });

    it("runs one round trip at a time, in the order it was asked", async () => {
        // Two round trips in flight at once would each work from the state before the other, and
        // the second answer would undo the first.
        const finished: string[] = [];
        let releaseFirst = () => undefined as void;
        answers["flowText/peekNext"] = () =>
            new Promise((resolve) => {
                releaseFirst = () => {
                    finished.push("peekNext");
                    resolve({ data: { pageId: null } });
                };
            });
        answers["flowText/setNextContent"] = () => {
            finished.push("setNextContent");
            return Promise.resolve({ data: undefined });
        };

        const first = peekNext("chain-1", "page2", "en");
        const second = setNextContent(
            "chain-1",
            "page2",
            "en",
            "<p>x</p>",
            "<p>old</p>",
        );
        // Each call starts on a microtask of its own, so let the queue get going.
        await flushMicrotasks();

        // The second call has not even reached the server while the first is outstanding.
        expect(calls.map((call) => call.endpoint.split("?")[0])).toEqual([
            "flowText/peekNext",
        ]);
        expect(isBoundaryBusy()).toBe(true);

        releaseFirst();
        await Promise.all([first, second]);

        expect(finished).toEqual(["peekNext", "setNextContent"]);
        expect(isBoundaryBusy()).toBe(false);
    });

    it("holds a save delay while a round trip runs, and gives it back afterwards", async () => {
        let release = () => undefined as void;
        answers["flowText/unlinkFrom"] = () =>
            new Promise((resolve) => {
                release = () => resolve({ data: undefined });
            });

        const call = unlinkFrom("chain-1", "page2", 0);
        await flushMicrotasks();
        expect(delays).toEqual(["flowText"]);

        release();
        await call;
        expect(delays).toEqual([]);
    });

    it("keeps serving later calls after one fails", async () => {
        answers["flowText/peekNext"] = () =>
            Promise.reject(new Error("simulated 500 from the server"));
        answers["flowText/pendingCaret"] = () =>
            Promise.resolve({
                data: {
                    pageId: "page3",
                    chainId: "c",
                    lang: "en",
                    charOffset: 4,
                },
            });

        await expect(peekNext("chain-1", "page2", "en")).rejects.toThrow();
        const caret = await getPendingCaret("page3");

        expect(caret?.charOffset).toBe(4);
        expect(delays).toEqual([]);
    });

    it("reports a refusal of setNextContent rather than throwing", async () => {
        // C# refuses when the next box is on the page being edited. The text stays where it is,
        // and the pass carries on; nothing about that is worth an exception.
        answers["flowText/setNextContent"] = () =>
            Promise.reject(
                new Error("the next box is on the page being edited"),
            );

        expect(
            await setNextContent(
                "chain-1",
                "page2",
                "en",
                "<p>x</p>",
                "<p>old</p>",
            ),
        ).toBe(false);
    });
});
