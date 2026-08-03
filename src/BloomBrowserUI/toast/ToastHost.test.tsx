import { act } from "react";
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";
import { renderRootSync, unmountRoot } from "../utils/reactRender";

// react-toastify is mocked so we can see which of its calls ToastHost makes: showing a new toast,
// or refreshing one that is already on screen. That distinction is the whole behaviour under test
// and it cannot be seen in the DOM, because a refreshed toast looks exactly like the old one.
const { mockToast, mockUpdate, mockDismiss } = vi.hoisted(() => ({
    mockToast: vi.fn(),
    mockUpdate: vi.fn(),
    mockDismiss: vi.fn(),
}));

vi.mock("react-toastify", () => {
    const toast = (...args: unknown[]) => mockToast(...args);
    (toast as unknown as Record<string, unknown>).update = (
        ...args: unknown[]
    ) => mockUpdate(...args);
    (toast as unknown as Record<string, unknown>).dismiss = (
        ...args: unknown[]
    ) => mockDismiss(...args);
    return { toast, ToastContainer: () => <div /> };
});
vi.mock("react-toastify/dist/ReactToastify.css", () => ({}));
vi.mock("../utils/bloomApi", () => ({ postJsonAsync: vi.fn() }));
vi.mock("../utils/WebSocketManager", () => ({
    default: { addListener: vi.fn(), removeListener: vi.fn() },
}));

import { ToastHost } from "./ToastHost";

let container: HTMLDivElement;

// The debug event is the same path the backend's websocket messages take into enqueueToasts.
const send = (toastInfo: Record<string, unknown>) =>
    act(() => {
        window.dispatchEvent(
            new CustomEvent("bloom-toast-debug-show", { detail: toastInfo }),
        );
    });

beforeEach(() => {
    vi.clearAllMocks();
    container = document.createElement("div");
    document.body.appendChild(container);
    act(() => {
        renderRootSync(<ToastHost />, container);
    });
});

afterEach(() => {
    unmountRoot(container);
    container.remove();
});

describe("ToastHost duplicate handling", () => {
    test("shows a message that isn't already on screen", () => {
        send({ text: "Bloom was not able to copy that.", durationSeconds: 15 });

        expect(mockToast).toHaveBeenCalledTimes(1);
        expect(mockUpdate).not.toHaveBeenCalled();
    });

    // The default, which keeps repeated notices from piling up.
    test("suppresses an identical repeat by default", () => {
        const message = { text: "Something happened.", durationSeconds: 15 };

        send(message);
        send(message);

        expect(mockToast).toHaveBeenCalledTimes(1);
        expect(mockUpdate).not.toHaveBeenCalled();
    });

    // Clipboard failures opt out of that: a second failed copy is a new attempt by the user, and
    // showing nothing reads as "that one worked" (BL-16459).
    test("refreshes an identical repeat when the sender asks for it", () => {
        const message = {
            text: "Bloom was not able to copy that.",
            durationSeconds: 15,
            showEvenIfDuplicate: true,
        };

        send(message);
        send(message);

        expect(mockToast).toHaveBeenCalledTimes(1);
        expect(mockUpdate).toHaveBeenCalledTimes(1);
        // Same identity, and its time on screen restarted.
        const [identity, options] = mockUpdate.mock.calls[0] as [
            string,
            { autoClose?: number },
        ];
        expect(identity).toBe("Bloom was not able to copy that.");
        expect(options.autoClose).toBe(15000);
    });

    test("a third identical failure is shown again too", () => {
        const message = {
            text: "Bloom was not able to paste.",
            durationSeconds: 15,
            showEvenIfDuplicate: true,
        };

        send(message);
        send(message);
        send(message);

        expect(mockUpdate).toHaveBeenCalledTimes(2);
    });
});
