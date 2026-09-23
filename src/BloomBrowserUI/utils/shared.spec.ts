import { afterEach, describe, expect, it, vi } from "vitest";

import {
    getBloomPageElement,
    getPageIframeBody,
    whenBloomPageIsReady,
} from "./shared";

function setUpPageIframe(): HTMLBodyElement {
    const iframe = document.createElement("iframe");
    iframe.id = "page";
    document.body.appendChild(iframe);
    return iframe.contentDocument!.body;
}

afterEach(() => {
    document.body.innerHTML = "";
});

describe("shared editable page helpers", () => {
    it("finds the bloom page inside the page iframe body", () => {
        const body = setUpPageIframe();
        const page = document.createElement("div");
        page.className = "bloom-page";
        body.appendChild(page);

        expect(getPageIframeBody()).toBe(body);
        expect(getBloomPageElement()).toBe(page);
    });

    it("waits for the bloom page to appear after the iframe body exists", async () => {
        const body = setUpPageIframe();
        body.appendChild(document.createElement("div"));
        const onReady = vi.fn();

        const dispose = whenBloomPageIsReady(onReady);

        expect(onReady).not.toHaveBeenCalled();

        const page = document.createElement("div");
        page.className = "bloom-page";
        body.appendChild(page);

        await vi.waitFor(() => {
            expect(onReady).toHaveBeenCalledTimes(1);
        });
        expect(onReady).toHaveBeenCalledWith(page);

        dispose();
    });
});
