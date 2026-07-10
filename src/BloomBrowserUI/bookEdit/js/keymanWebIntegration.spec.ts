import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../utils/bloomApi", () => ({
    postJsonAsync: vi.fn(),
    postBoolean: vi.fn(),
    // The keyboarding/oskVisible endpoint doesn't exist yet; simulate that by
    // invoking the error callback, which leaves the desired-visible default
    // (true) in place.
    get: vi.fn((_url, _success, error?: () => void) => error?.()),
}));

vi.mock("../longPressShared", () => ({
    setKmwAttached: vi.fn(),
}));

import { postJsonAsync } from "../../utils/bloomApi";
import { setKmwAttached } from "../longPressShared";
import type { attachKeymanWebIfNeeded as AttachFn } from "./keymanWebIntegration";

const postJsonAsyncMock = vi.mocked(postJsonAsync);
const setKmwAttachedMock = vi.mocked(setKmwAttached);

// Fake KeymanWeb engine object. The real keymanweb.js (vendored, not loaded in
// tests) is what defines window.keyman when it finishes loading; we simulate
// that by installing this fake on window before letting the mocked <script>
// "load".
function makeFakeKeyman() {
    return {
        init: vi.fn().mockResolvedValue(undefined),
        // The real engine resolves with one {keyboard, error?} entry per
        // registered keyboard; an entry with `error` means that stub was
        // rejected (it does NOT reject the promise).
        addKeyboards: vi
            .fn()
            .mockResolvedValue([
                { keyboard: { id: "Keyboard_thai_kedmanee" } },
            ]),
        getKeyboards: vi.fn(() => [
            { InternalName: "Keyboard_thai_kedmanee", HasLoaded: true },
        ]),
        getActiveKeyboard: vi.fn(() => "Keyboard_thai_kedmanee"),
        setActiveKeyboard: vi.fn().mockResolvedValue(undefined),
        attachToControl: vi.fn(),
        setKeyboardForControl: vi.fn(),
        osk: makeFakeOsk(),
    };
}

// The OSK exposes show/hide plus an addEventListener the integration uses to
// learn when the USER closes the keyboard. fireHide() lets tests simulate that
// close (HiddenByUser: true) or a programmatic hide (false).
function makeFakeOsk() {
    const hideHandlers: ((obj: { HiddenByUser: boolean }) => void)[] = [];
    const fireHide = (hiddenByUser: boolean) =>
        hideHandlers.forEach((h) => h({ HiddenByUser: hiddenByUser }));
    return {
        show: vi.fn(),
        // Mirror the real engine: on desktop the public hide() is a "user-level"
        // hide, so it fires the "hide" event with HiddenByUser:true even though
        // WE called it. This is exactly the ambiguity the integration must handle.
        hide: vi.fn(() => fireHide(true)),
        addEventListener: vi.fn(
            (event: string, handler: (obj: any) => void) => {
                if (event === "hide") hideHandlers.push(handler);
            },
        ),
        // Simulate the user clicking the OSK's own close button.
        fireHide,
    };
}

function makeEditable(lang: string): HTMLElement {
    const el = document.createElement("div");
    el.setAttribute("lang", lang);
    // Focusable so tests can make it document.activeElement — the OSK is only
    // shown while focus is still in the field being attached.
    el.tabIndex = 0;
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
    let attachKeymanWebIfNeeded: typeof AttachFn;

    beforeEach(async () => {
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

        // keymanWebIntegration.ts memoizes its engine-load promise (and the
        // registered-keyboard/attached-editable caches) in module-level
        // state, exactly once per real page load. Reset the module registry
        // and re-import fresh for every test so that state doesn't leak
        // between tests — otherwise a later test's assertions against a
        // NEW fakeKeyman would silently check the wrong object, since
        // ensureEngineLoaded() would still be resolving to whichever
        // fakeKeyman was current the first time any test reached useKmw:true.
        vi.resetModules();
        ({ attachKeymanWebIfNeeded } = await import("./keymanWebIntegration"));
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

    it("passes font/oskFont into the addKeyboards language spec when the server supplies them", async () => {
        postJsonAsyncMock.mockResolvedValue({
            data: {
                ...kmwInfo,
                fontFamily: "Padauk",
                fontUrls: ["/fonts/padauk.woff"],
                oskFontFamily: "Mmrtext",
                oskFontUrls: ["/fonts/mmrtext.ttf"],
            },
        } as any);

        const editable = makeEditable("th");
        await attachKeymanWebIfNeeded(editable);

        expect(fakeKeyman.addKeyboards).toHaveBeenCalledWith(
            expect.objectContaining({
                id: "thai_kedmanee",
                // The engine's custom-keyboard validation requires a keyboard
                // name and a language region; without them registration is
                // rejected (see attachKmwKeyboard).
                name: "thai_kedmanee",
                languages: [
                    expect.objectContaining({
                        id: "th",
                        region: expect.any(String),
                        font: {
                            family: "Padauk",
                            source: ["/fonts/padauk.woff"],
                        },
                        oskFont: {
                            family: "Mmrtext",
                            source: ["/fonts/mmrtext.ttf"],
                        },
                    }),
                ],
            }),
        );
    });

    it("throws when the engine rejects the keyboard stub", async () => {
        postJsonAsyncMock.mockResolvedValue({ data: kmwInfo } as any);
        // The real engine reports a bad stub via an `error` entry in the
        // RESOLVED array (it does not reject), e.g. when required fields are
        // missing. That must surface as a failure, not vanish silently.
        fakeKeyman.addKeyboards.mockResolvedValue([
            {
                keyboard: { id: "Keyboard_thai_kedmanee" },
                error: new Error("To use a custom keyboard, ..."),
            },
        ]);

        const editable = makeEditable("th");
        await expect(attachKeymanWebIfNeeded(editable)).rejects.toThrow(
            /rejected keyboard thai_kedmanee/,
        );
        expect(fakeKeyman.attachToControl).not.toHaveBeenCalled();
    });

    it("omits font/oskFont from the language spec when the server doesn't supply them", async () => {
        postJsonAsyncMock.mockResolvedValue({ data: kmwInfo } as any);

        const editable = makeEditable("th");
        await attachKeymanWebIfNeeded(editable);

        const call = fakeKeyman.addKeyboards.mock.calls[0][0];
        expect(call.languages[0]).not.toHaveProperty("font");
        expect(call.languages[0]).not.toHaveProperty("oskFont");
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

    it("stops auto-showing the OSK once the user has closed it, but still shows it the first time", async () => {
        postJsonAsyncMock.mockResolvedValue({ data: kmwInfo } as any);

        // First KMW focus: the OSK is shown automatically (default desired state).
        const field1 = makeEditable("th");
        field1.focus();
        await attachKeymanWebIfNeeded(field1);
        expect(fakeKeyman.osk.show).toHaveBeenCalledTimes(1);

        // The user closes the OSK themselves (HiddenByUser).
        fakeKeyman.osk.fireHide(true);

        // Focusing another KMW field must NOT pop the OSK back up.
        const field2 = makeEditable("th");
        field2.focus();
        await attachKeymanWebIfNeeded(field2);
        expect(fakeKeyman.osk.show).toHaveBeenCalledTimes(1);
    });

    it("keeps auto-showing the OSK when Bloom hides it for a non-KMW field (Thai → English → Thai)", async () => {
        // Reproduces the reported bug: after clicking English (which makes Bloom
        // hide the OSK) the Thai OSK must still come back.
        postJsonAsyncMock.mockImplementation((_url: string, body: any) =>
            Promise.resolve({
                data: body.lang === "th" ? kmwInfo : { useKmw: false },
            } as any),
        );

        const thai1 = makeEditable("th");
        thai1.focus();
        await attachKeymanWebIfNeeded(thai1);
        expect(fakeKeyman.osk.show).toHaveBeenCalledTimes(1);

        // Click into English: Bloom hides the OSK. On desktop this goes through
        // the engine's user-level hide() (HiddenByUser:true) — but it's OUR hide,
        // not a user close, so it must not disable auto-showing.
        const english = makeEditable("en");
        english.focus();
        await attachKeymanWebIfNeeded(english);
        expect(fakeKeyman.osk.hide).toHaveBeenCalled();

        // Back into Thai: the OSK should reappear.
        const thai2 = makeEditable("th");
        thai2.focus();
        await attachKeymanWebIfNeeded(thai2);
        expect(fakeKeyman.osk.show).toHaveBeenCalledTimes(2);
    });
});
