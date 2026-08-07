import { beforeEach, describe, expect, it, vi } from "vitest";
import {
    describeBadUrl,
    installUndefinedUrlDetector,
    isJavascriptValueUrl,
    resetReportingForTests,
} from "./undefinedUrlDetector";

describe("isJavascriptValueUrl", () => {
    it("catches a url that is nothing but the value", () => {
        expect(isJavascriptValueUrl("undefined")).toBe(true);
        expect(isJavascriptValueUrl("null")).toBe(true);
        expect(isJavascriptValueUrl("NaN")).toBe(true);
    });

    it("catches the value as a path segment, which is the shape we see in Sentry", () => {
        // The real reported paths look like this, because a bare "undefined" src resolves
        // against a document whose base is the temp folder.
        expect(
            isJavascriptValueUrl(
                "C:/Users/someone/AppData/Local/Temp/undefined",
            ),
        ).toBe(true);
        expect(isJavascriptValueUrl("audio/undefined")).toBe(true);
        expect(isJavascriptValueUrl("undefined/audio/abc.mp3")).toBe(true);
    });

    it("ignores the query string and fragment", () => {
        expect(isJavascriptValueUrl("audio/undefined?assetv=1")).toBe(true);
        expect(isJavascriptValueUrl("audio/real.mp3?name=undefined")).toBe(
            false,
        );
        expect(isJavascriptValueUrl("page.htm#undefined")).toBe(false);
    });

    it("catches the values themselves, which is what an assignment actually passes", () => {
        // `img.src = x` hands us the real value; the DOM stringifies it afterwards. Catching it
        // here is the only moment at which we still know who set it.
        expect(isJavascriptValueUrl(undefined)).toBe(true);
        expect(isJavascriptValueUrl(null)).toBe(true);
        expect(isJavascriptValueUrl(Number.NaN)).toBe(true);
    });

    it("leaves real files and folders alone", () => {
        expect(isJavascriptValueUrl("audio/abc123.mp3")).toBe(false);
        expect(isJavascriptValueUrl("undefined.png")).toBe(false);
        expect(isJavascriptValueUrl("undefinedThings/x.png")).toBe(false);
        expect(isJavascriptValueUrl("images/Undefined")).toBe(false);
        expect(isJavascriptValueUrl("")).toBe(false);
        // A real number or a URL object stringifies to something legitimate.
        expect(isJavascriptValueUrl(17)).toBe(false);
        expect(isJavascriptValueUrl(new URL("http://x/y.png"))).toBe(false);
    });
});

describe("describeBadUrl", () => {
    it("names both the value and where it was set", () => {
        const message = describeBadUrl(undefined, "an image's src");
        expect(message).toContain("an image's src");
        expect(message).toContain('"undefined"');
        expect(message).toContain("BL-16666");
    });
});

describe("installUndefinedUrlDetector", () => {
    // Installed once for the whole file, as it is in the real app: the installer deliberately
    // ignores repeat calls so a bundle can't double-report.
    const report = vi.fn();
    installUndefinedUrlDetector(report);

    beforeEach(() => {
        report.mockClear();
        // Each test would otherwise be silenced by the previous test's identical message.
        resetReportingForTests();
    });

    it("reports an image src assigned an undefined variable, with a stack", () => {
        report.mockClear();
        const image = document.createElement("img");
        let notReadyYet: string | undefined;

        image.src = notReadyYet as unknown as string;

        expect(report).toHaveBeenCalledTimes(1);
        const [message, stack] = report.mock.calls[0];
        expect(message).toContain("an image's src");
        // The stack is the entire point of this layer: it is what names the offending line.
        expect(stack).toBeTruthy();
        expect(stack).toContain("undefinedUrlDetectorSpec");
    });

    it("still actually sets the src, so nothing behaves differently", () => {
        report.mockClear();
        const image = document.createElement("img");

        image.src = "audio/real.mp3";

        expect(report).not.toHaveBeenCalled();
        expect(image.getAttribute("src")).toBe("audio/real.mp3");
    });

    it("reports setAttribute('src', undefined) too", () => {
        report.mockClear();
        const image = document.createElement("img");

        image.setAttribute("src", undefined as unknown as string);

        expect(report).toHaveBeenCalledTimes(1);
        expect(report.mock.calls[0][0]).toContain('setAttribute("src")');
    });

    it("leaves other attributes alone", () => {
        report.mockClear();
        const div = document.createElement("div");

        div.setAttribute("title", "undefined");

        expect(report).not.toHaveBeenCalled();
        expect(div.getAttribute("title")).toBe("undefined");
    });

    it("reports a repeating bug only once, so a re-rendering component can't flood the server", () => {
        const image = document.createElement("img");

        image.src = undefined as unknown as string;
        image.src = undefined as unknown as string;
        image.src = undefined as unknown as string;

        expect(report).toHaveBeenCalledTimes(1);
    });

    it("reports a fetch of an undefined url, which is how BL-16447 escaped", () => {
        // Called bare, exactly as application code calls it - which means the wrapper receives
        // `this === undefined` and must not pass that on to the real fetch.
        void fetch("audio/undefined").catch(() => {
            // jsdom has no real network; the rejection is expected and irrelevant here.
        });

        expect(report).toHaveBeenCalledTimes(1);
        expect(report.mock.calls[0][0]).toContain("a fetch()");
    });

    it("leaves an ordinary fetch alone", () => {
        void fetch("audio/real.mp3").catch(() => {
            // Again, no network in jsdom.
        });

        expect(report).not.toHaveBeenCalled();
    });
});
