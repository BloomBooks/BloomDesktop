import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../utils/bloomApi", () => ({
    postJsonAsync: vi.fn(),
}));

vi.mock("../longPressShared", () => ({
    setKmwAttached: vi.fn(),
}));

import { postJsonAsync } from "../../utils/bloomApi";
import { setKmwAttached } from "../longPressShared";
import { attachKeymanWebIfNeeded } from "./keymanWebIntegration";

const postJsonAsyncMock = vi.mocked(postJsonAsync);
const setKmwAttachedMock = vi.mocked(setKmwAttached);

// Fake KeymanWeb engine object. The real keymanweb.js (vendored, not loaded in
// tests) is what defines window.keyman when it finishes loading; we simulate
// that by installing this fake on window before letting the mocked <script>
// "load".
function makeFakeKeyman() {
    return {
        init: vi.fn().mockResolvedValue(undefined),
        addKeyboards: vi.fn(),
        getKeyboards: vi.fn(() => [
            { InternalName: "Keyboard_thai_kedmanee", HasLoaded: true },
        ]),
        setActiveKeyboard: vi.fn().mockResolvedValue(undefined),
        attachToControl: vi.fn(),
        setKeyboardForControl: vi.fn(),
        osk: { show: vi.fn(), hide: vi.fn() },
    };
}

function makeEditable(lang: string): HTMLElement {
    const el = document.createElement("div");
    el.setAttribute("lang", lang);
    document.body.appendChild(el);
    return el;
}

const kmwInfo = {
    useKmw: true,
    keyboardId: "thai_kedmanee",
    languageTag: "th",
    keyboardFileUrl: "/some/collection/path/thai_kedmanee.js",
};

// Captured once, outside any test, so the mock below always creates a truly
// native element rather than risking recursive wrapping if a prior test's spy
// implementation were still somehow in the chain.
const nativeCreateElement = document.createElement.bind(document);

describe("keymanWebIntegration", () => {
    let fakeKeyman: ReturnType<typeof makeFakeKeyman>;

    beforeEach(() => {
        vi.clearAllMocks();
        document.body.innerHTML = "";
        delete (window as any).keyman;
        fakeKeyman = makeFakeKeyman();

        // Simulate the browser firing the vendored <script src="keymanweb.js">
        // element's onload as soon as its src is set, at which point
        // window.keyman would already be defined by the real engine — here we
        // stand in for that by installing the fake first.
        vi.spyOn(document, "createElement").mockImplementation(
            (tagName: string) => {
                const el = nativeCreateElement(tagName);
                if (tagName === "script") {
                    Object.defineProperty(el, "src", {
                        configurable: true,
                        set() {
                            (window as any).keyman = fakeKeyman;
                            queueMicrotask(
                                () =>
                                    (el as HTMLScriptElement).onload &&
                                    (el as any).onload(new Event("load")),
                            );
                        },
                    });
                }
                return el;
            },
        );
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it("skips z, *, and empty langs without posting fieldFocused", async () => {
        for (const lang of ["z", "*", ""]) {
            const editable = makeEditable(lang);
            await attachKeymanWebIfNeeded(editable);
        }

        expect(postJsonAsyncMock).not.toHaveBeenCalled();
    });

    it("posts on every focus, but only registers the keyboard with KMW once per lang", async () => {
        postJsonAsyncMock.mockResolvedValue({ data: kmwInfo } as any);

        const editable1 = makeEditable("th");
        await attachKeymanWebIfNeeded(editable1);
        const editable2 = makeEditable("th");
        await attachKeymanWebIfNeeded(editable2);

        expect(postJsonAsyncMock).toHaveBeenCalledTimes(2);
        expect(postJsonAsyncMock).toHaveBeenCalledWith(
            "keyboarding/fieldFocused",
            {
                lang: "th",
            },
        );
        // addKeyboards (the lazy, network-dependent registration step) should
        // only run once, even though both fields were focused and posted.
        expect(fakeKeyman.addKeyboards).toHaveBeenCalledTimes(1);
        // But KMW must still attach + activate for the second field.
        expect(fakeKeyman.attachToControl).toHaveBeenCalledTimes(2);
        expect(fakeKeyman.setActiveKeyboard).toHaveBeenCalled();
    });

    it("never loads the engine when the server says useKmw: false", async () => {
        postJsonAsyncMock.mockResolvedValue({
            data: { useKmw: false },
        } as any);

        const editable = makeEditable("en");
        await attachKeymanWebIfNeeded(editable);

        expect(postJsonAsyncMock).toHaveBeenCalledWith(
            "keyboarding/fieldFocused",
            {
                lang: "en",
            },
        );
        expect((window as any).keyman).toBeUndefined();
    });

    it("disables long-press only on fields KMW actually attaches to", async () => {
        postJsonAsyncMock.mockResolvedValueOnce({ data: kmwInfo } as any);
        const kmwField = makeEditable("th");
        await attachKeymanWebIfNeeded(kmwField);
        expect(setKmwAttachedMock).toHaveBeenCalledWith(kmwField);

        postJsonAsyncMock.mockResolvedValueOnce({
            data: { useKmw: false },
        } as any);
        const osFallbackField = makeEditable("fr");
        await attachKeymanWebIfNeeded(osFallbackField);
        expect(setKmwAttachedMock).not.toHaveBeenCalledWith(osFallbackField);
    });
});
