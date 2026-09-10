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
    getPendingWalks,
    getRefitResult,
    getReflowOnPageChange,
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
                },
            });

        const walks = await getPendingWalks();

        expect(walks?.pending).toBe(true);
        expect(walks?.chainIds).toEqual(["chain-1"]);
        expect(walks?.reflowOnPageChange).toBe(true);
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

    it("asks Bloom to run the refits, and to run them on a page change", async () => {
        await postReflowNow();
        await postReflowOnPageChange(true);

        expect(calls[0].endpoint).toBe("flowText/reflowNow");
        expect(calls[1].endpoint).toBe("flowText/reflowOnPageChange");
        expect(calls[1].body).toEqual({ value: true });
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
