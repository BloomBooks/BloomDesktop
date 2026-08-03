import { beforeEach, describe, expect, test, vi } from "vitest";

import {
    copyTextToClipboard,
    listenForBrowserClipboardOperations,
    readTextFromClipboard,
    verifyBrowserCopyAfterDefault,
    verifyBrowserCopyReachedClipboard,
    verifyBrowserPasteAfterDefault,
    verifyBrowserPasteGotClipboard,
} from "./hostClipboard";
import { getAsync, postJson, postJsonAsync } from "../../utils/bloomApi";

vi.mock("../../utils/bloomApi", () => ({
    getAsync: vi.fn(),
    postJson: vi.fn(),
    postJsonAsync: vi.fn(),
}));

const mockGetAsync = vi.mocked(getAsync);
const mockPostJson = vi.mocked(postJson);
const mockPostJsonAsync = vi.mocked(postJsonAsync);

beforeEach(() => {
    vi.clearAllMocks();
    vi.useRealTimers();
});

describe("copyTextToClipboard", () => {
    test("sends the text to C# rather than to navigator.clipboard", async () => {
        mockPostJsonAsync.mockResolvedValue({ data: true } as never);

        await copyTextToClipboard("some text");

        expect(mockPostJsonAsync).toHaveBeenCalledWith("common/clipboardText", {
            text: "some text",
        });
    });

    test("reports success when C# says the text reached the clipboard", async () => {
        mockPostJsonAsync.mockResolvedValue({ data: true } as never);

        expect(await copyTextToClipboard("some text")).toBe(true);
    });

    // This is the whole point of returning a value: cut must not delete text it failed to
    // copy. C# replies false when the clipboard could not be written (BL-16459).
    test("reports failure when C# says the copy did not happen", async () => {
        mockPostJsonAsync.mockResolvedValue({ data: false } as never);

        expect(await copyTextToClipboard("some text")).toBe(false);
    });

    test("reports failure rather than a bogus success if the reply has no data", async () => {
        mockPostJsonAsync.mockResolvedValue(undefined as never);

        expect(await copyTextToClipboard("some text")).toBe(false);
    });
});

describe("readTextFromClipboard", () => {
    test("asks C# for the clipboard text", async () => {
        mockGetAsync.mockResolvedValue({ data: "clipboard contents" } as never);

        expect(await readTextFromClipboard()).toBe("clipboard contents");
        expect(mockGetAsync).toHaveBeenCalledWith("common/clipboardText");
    });

    // A failed read gets us "" from C# (it has already toasted), and so does an empty
    // clipboard; callers treat both as "nothing to paste".
    test("yields an empty string when there is no text", async () => {
        mockGetAsync.mockResolvedValue({ data: "" } as never);

        expect(await readTextFromClipboard()).toBe("");
    });

    test("yields an empty string rather than a non-string when the reply is not text", async () => {
        mockGetAsync.mockResolvedValue({ data: undefined } as never);

        expect(await readTextFromClipboard()).toBe("");
    });
});

describe("verify helpers", () => {
    test("the copy check asks C# about the clipboard", () => {
        verifyBrowserCopyReachedClipboard();

        expect(mockPostJson).toHaveBeenCalledWith(
            "common/verifyClipboardAfterBrowserCopy",
            {},
        );
    });

    // A separate endpoint, so the user gets "not able to paste" rather than a message about
    // copying, which would be baffling when they were pasting.
    test("the paste check uses the paste endpoint, not the copy one", () => {
        verifyBrowserPasteGotClipboard();

        expect(mockPostJson).toHaveBeenCalledWith(
            "common/verifyClipboardAfterBrowserPaste",
            {},
        );
    });

    // The browser touches the clipboard as part of the default action, i.e. after our handler
    // returns, so checking synchronously would look before the operation had been attempted.
    test("the after-default variants wait for the current event to finish", () => {
        vi.useFakeTimers();

        verifyBrowserCopyAfterDefault();
        verifyBrowserPasteAfterDefault();
        expect(mockPostJson).not.toHaveBeenCalled();

        vi.runAllTimers();
        expect(mockPostJson).toHaveBeenCalledWith(
            "common/verifyClipboardAfterBrowserCopy",
            {},
        );
        expect(mockPostJson).toHaveBeenCalledWith(
            "common/verifyClipboardAfterBrowserPaste",
            {},
        );
    });
});

describe("listenForBrowserClipboardOperations", () => {
    const dispatch = (type: string, cancelled = false) => {
        const event = new Event(type, { cancelable: true });
        if (cancelled) event.preventDefault();
        document.dispatchEvent(event);
    };

    // Chromium touches the clipboard as part of the default action, after our handler returns,
    // so the check has to be deferred; verifying immediately would look at the clipboard before
    // the operation had even been attempted.
    test("verifies a native copy, but only after the event has been handled", () => {
        vi.useFakeTimers();
        listenForBrowserClipboardOperations(document);

        dispatch("copy");
        expect(mockPostJson).not.toHaveBeenCalled();

        vi.runAllTimers();
        expect(mockPostJson).toHaveBeenCalledWith(
            "common/verifyClipboardAfterBrowserCopy",
            {},
        );
    });

    test("verifies a native cut too", () => {
        vi.useFakeTimers();
        listenForBrowserClipboardOperations(document);

        dispatch("cut");
        vi.runAllTimers();

        expect(mockPostJson).toHaveBeenCalledWith(
            "common/verifyClipboardAfterBrowserCopy",
            {},
        );
    });

    // Ctrl+V in a text box is left to Chromium, so this check is the only way a failed paste
    // can reach the user.
    test("verifies a native paste, with the paste endpoint", () => {
        vi.useFakeTimers();
        listenForBrowserClipboardOperations(document);

        dispatch("paste");
        vi.runAllTimers();

        expect(mockPostJson).toHaveBeenCalledWith(
            "common/verifyClipboardAfterBrowserPaste",
            {},
        );
    });

    // Regression guard. Skipping defaultPrevented events looks like the right way to avoid
    // double-reporting, and it silently disables the whole feature: CKEditor preventDefaults copy
    // and cut inside a .bloom-editable, which is exactly where the user is when a clipboard
    // failure matters. Measured in the running app: copy and cut both arrive with
    // defaultPrevented=true, and with such a check in place nothing was ever reported.
    test("still verifies when the event has been defaultPrevented", () => {
        vi.useFakeTimers();
        listenForBrowserClipboardOperations(document);

        dispatch("copy", true);
        vi.runAllTimers();

        expect(mockPostJson).toHaveBeenCalledWith(
            "common/verifyClipboardAfterBrowserCopy",
            {},
        );
    });

    test("does not verify anything until a clipboard event actually happens", () => {
        vi.useFakeTimers();
        listenForBrowserClipboardOperations(document);

        dispatch("keydown");
        vi.runAllTimers();

        expect(mockPostJson).not.toHaveBeenCalled();
    });
});
