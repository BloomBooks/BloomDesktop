import { beforeEach, describe, expect, it, vi } from "vitest";

// The calls the client made, in order, and what each was answered with.
const calls: { endpoint: string; body?: unknown }[] = [];
// Endpoint, without its query, -> what to answer with.
let answers: Record<string, () => Promise<{ data?: unknown }>> = {};

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

import {
    getAutoPages,
    getPendingWalks,
    getRefitResult,
    getReflowOnPageChange,
    postAutoPages,
    postReflowNow,
    postReflowOnPageChange,
} from "./flowReflowClient";

describe("flowReflowClient", () => {
    beforeEach(() => {
        calls.length = 0;
        answers = {};
    });

    it("hands back the refits Bloom is holding", async () => {
        answers["flowText/pendingWalks"] = () =>
            Promise.resolve({
                data: {
                    pending: true,
                    chainIds: ["chain-1"],
                    reflowOnPageChange: true,
                    autoPages: false,
                },
            });

        const walks = await getPendingWalks();

        expect(walks?.pending).toBe(true);
        expect(walks?.chainIds).toEqual(["chain-1"]);
        expect(walks?.reflowOnPageChange).toBe(true);
        expect(walks?.autoPages).toBe(false);
    });

    it("says nothing about the settings the reply leaves out", async () => {
        answers["flowText/pendingWalks"] = () =>
            Promise.resolve({ data: { pending: false, chainIds: [] } });

        const walks = await getPendingWalks();

        // Sanity check: the reply was read at all.
        expect(walks?.pending).toBe(false);
        expect(walks?.reflowOnPageChange).toBeUndefined();
        expect(walks?.autoPages).toBeUndefined();
    });

    it("says nothing about the refits when Bloom could not be asked", async () => {
        // What the page is already showing is left alone, rather than claiming nothing waits.
        answers["flowText/pendingWalks"] = () =>
            Promise.reject(new Error("simulated 500 from the server"));

        expect(await getPendingWalks()).toBeUndefined();
    });

    it("reads a boolean answered on its own and one wrapped in an object", async () => {
        answers["flowText/reflowOnPageChange"] = () =>
            Promise.resolve({ data: true });
        expect(await getReflowOnPageChange()).toBe(true);

        answers["flowText/reflowOnPageChange"] = () =>
            Promise.resolve({ data: { value: true } });
        expect(await getReflowOnPageChange()).toBe(true);
    });

    it("reads whether a refit may add and remove pages, answered either way", async () => {
        answers["flowText/autoPages"] = () => Promise.resolve({ data: true });
        expect(await getAutoPages()).toBe(true);
        expect(calls[0].endpoint).toBe("flowText/autoPages");

        answers["flowText/autoPages"] = () =>
            Promise.resolve({ data: { value: false } });
        expect(await getAutoPages()).toBe(false);
    });

    it("asks Bloom to run the refits, to run them on a page change, and to add and remove pages", async () => {
        await postReflowNow();
        await postReflowOnPageChange(true);
        await postAutoPages(false);

        expect(calls[0].endpoint).toBe("flowText/reflowNow");
        expect(calls[1].endpoint).toBe("flowText/reflowOnPageChange");
        expect(calls[1].body).toEqual({ value: true });
        expect(calls[2].endpoint).toBe("flowText/autoPages");
        expect(calls[2].body).toEqual({ value: false });
    });

    it("hands back the boxes of this page that the refit rewrote", async () => {
        answers["flowText/refitResult"] = () =>
            Promise.resolve({
                data: {
                    boxes: [
                        {
                            chainId: "chain-1",
                            lang: "en",
                            indexInPage: 1,
                            html: "<p>refitted</p>",
                        },
                    ],
                },
            });

        const boxes = await getRefitResult("page-2");

        expect(calls[0].endpoint).toContain("pageId=page-2");
        expect(boxes).toHaveLength(1);
        expect(boxes[0].chainId).toBe("chain-1");
        expect(boxes[0].lang).toBe("en");
        expect(boxes[0].indexInPage).toBe(1);
        expect(boxes[0].html).toBe("<p>refitted</p>");
    });

    it("hands back no boxes when the refit changed nothing on this page", async () => {
        answers["flowText/refitResult"] = () =>
            Promise.resolve({ data: { boxes: [] } });

        expect(await getRefitResult("page-2")).toEqual([]);
    });

    it("hands back no boxes when Bloom is holding no result", async () => {
        expect(await getRefitResult("page-2")).toEqual([]);
    });
});
