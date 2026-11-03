import { beforeEach, describe, expect, it, vi } from "vitest";

const api = vi.hoisted(() => ({
    getAsync: vi.fn(),
    postJsonAsync: vi.fn(),
}));
vi.mock("./bloomApi", () => ({
    getAsync: api.getAsync,
    postJsonAsync: api.postJsonAsync,
}));

describe("bloomClipboard", () => {
    beforeEach(() => {
        // The module remembers whether Bloom runs under --e2e, so each test loads it afresh.
        vi.resetModules();
        api.getAsync.mockReset();
        api.postJsonAsync.mockReset();
    });

    it("asks again after a failed --e2e check, rather than failing every later call", async () => {
        const readText = vi.fn().mockResolvedValue("from the browser");
        Object.defineProperty(navigator, "clipboard", {
            value: { readText },
            configurable: true,
        });
        api.getAsync
            .mockRejectedValueOnce(new Error("request failed"))
            .mockResolvedValueOnce({ data: { runningE2eTests: false } });
        const { readClipboardText } = await import("./bloomClipboard");

        await expect(readClipboardText()).rejects.toThrow("request failed");
        expect(await readClipboardText()).toBe("from the browser");
        expect(api.getAsync).toHaveBeenCalledTimes(2);
    });

    it("reads Bloom's in-memory clipboard under --e2e", async () => {
        api.getAsync.mockImplementation((url: string) =>
            Promise.resolve(
                url === "common/instanceInfo"
                    ? { data: { runningE2eTests: true } }
                    : {
                          data: {
                              text: "some text",
                              html: "<b>bold</b>",
                              picturePath: null,
                              files: null,
                          },
                      },
            ),
        );
        const { readClipboardText, readClipboardHtml, writeClipboardText } =
            await import("./bloomClipboard");

        expect(await readClipboardText()).toBe("some text");
        expect(await readClipboardHtml()).toEqual(["<b>bold</b>"]);
        await writeClipboardText("new text");
        expect(api.postJsonAsync).toHaveBeenCalledWith("e2e/clipboard", {
            text: "new text",
        });
    });
});
