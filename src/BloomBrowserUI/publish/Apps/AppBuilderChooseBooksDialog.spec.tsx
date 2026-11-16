import * as React from "react";
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../../utils/reactRender";

// vi.mock factories are hoisted above the module body, so anything they use must be hoisted too.
const { mockPostJsonAsync } = vi.hoisted(() => ({
    mockPostJsonAsync: vi.fn(),
}));

vi.mock("../../utils/bloomApi", async (importOriginal) => {
    const actual =
        await importOriginal<typeof import("../../utils/bloomApi")>();
    return {
        ...actual,
        postJsonAsync: mockPostJsonAsync,
        useWatchApiData: (_url: string, defaultValue: unknown) => defaultValue,
    };
});

// The real BloomDialog renders through a MUI Dialog, which portals its content out of our
// container. This stand-in keeps the content where the test can find it, and gives the real
// title bar and Cancel button the dialog's onCancel, as BloomDialog does. (BloomDialog calls
// the same onCancel for Escape and for a click outside the dialog.)
vi.mock(
    "../../react_components/BloomDialog/BloomDialog",
    async (importOriginal) => {
        const actual =
            await importOriginal<
                typeof import("../../react_components/BloomDialog/BloomDialog")
            >();
        return {
            ...actual,
            BloomDialog: (
                props: React.PropsWithChildren<{ onCancel: () => void }>,
            ) => (
                <actual.BloomDialogContext.Provider
                    value={{ onCancel: props.onCancel, disableDragging: true }}
                >
                    <div>{props.children}</div>
                </actual.BloomDialogContext.Provider>
            ),
        };
    },
);

// Choosing books isn't what these tests are about; the book grid would only need more mocking.
vi.mock("../../react_components/BookGridSetup/BookGridSetup", () => ({
    BookGridSetup: () => <div />,
}));

import { AppBuilderChooseBooksDialog } from "./AppBuilderChooseBooksDialog";

describe("AppBuilderChooseBooksDialog saving", () => {
    let container: HTMLDivElement;
    let onSaved: ReturnType<typeof vi.fn>;
    let onClose: ReturnType<typeof vi.fn>;
    // Settles the save request the dialog is waiting for; set once OK has started a save.
    let finishSave: (succeeded: boolean) => Promise<void>;

    beforeEach(() => {
        container = document.createElement("div");
        document.body.appendChild(container);
        onSaved = vi.fn();
        onClose = vi.fn();
        mockPostJsonAsync.mockReset().mockImplementation(
            () =>
                new Promise((resolve, reject) => {
                    finishSave = async (succeeded) => {
                        await act(async () => {
                            if (succeeded) resolve({ data: {} });
                            else reject(new Error("save failed"));
                        });
                    };
                }),
        );
    });

    afterEach(() => {
        unmountRoot(container);
        container.remove();
    });

    async function renderDialog(): Promise<void> {
        await act(async () => {
            renderRoot(
                <AppBuilderChooseBooksDialog
                    currentBooks={[
                        {
                            bookId: "book-1",
                            folderPath: "C:/Collection/Book One",
                            title: "Book One",
                        },
                    ]}
                    sizeEstimates={{
                        books: [],
                        estimatedAppOverheadBytes: 0,
                        maxAppSizeBytes: 100,
                    }}
                    onSaved={onSaved}
                    onClose={onClose}
                />,
                container,
            );
        });
    }

    // Under test, localized strings show their l10n keys, so the buttons are found by key.
    function button(l10nKey: string): HTMLButtonElement {
        const buttons = Array.from(container.querySelectorAll("button"));
        const found = buttons.find((b) => b.textContent === l10nKey);
        if (!found)
            fail(
                `Test cannot proceed: no "${l10nKey}" button in the dialog; its buttons say ${JSON.stringify(buttons.map((b) => b.textContent))}`,
            );
        return found;
    }

    // The X in the dialog's title bar.
    function closeButton(): HTMLButtonElement {
        const found = container.querySelector<HTMLButtonElement>(
            'button[title="Common.Close"]',
        );
        if (!found)
            fail("Test cannot proceed: the dialog's title bar has no X button");
        return found;
    }

    function spinnerShown(): boolean {
        return container.querySelector('[role="progressbar"]') !== null;
    }

    async function click(target: HTMLButtonElement): Promise<void> {
        await act(async () => {
            target.click();
        });
    }

    it("locks the dialog while saving, then saves and closes", async () => {
        await renderDialog();
        expect(spinnerShown()).toBe(false);
        expect(button("Common.OK").disabled).toBe(false);
        expect(button("Common.Cancel").disabled).toBe(false);

        await click(button("Common.OK"));

        expect(mockPostJsonAsync).toHaveBeenCalledTimes(1);
        expect(spinnerShown()).toBe(true);
        expect(button("Common.OK").disabled).toBe(true);
        expect(button("Common.Cancel").disabled).toBe(true);
        expect(onClose).not.toHaveBeenCalled();

        await finishSave(true);

        expect(onSaved).toHaveBeenCalledTimes(1);
        expect(onClose).toHaveBeenCalledTimes(1);
    });

    it("ignores the X (and so Escape and clicks outside) while saving", async () => {
        await renderDialog();
        await click(button("Common.OK"));
        expect(spinnerShown()).toBe(true);

        await click(closeButton());

        expect(onClose).not.toHaveBeenCalled();
        await finishSave(true);
        expect(onClose).toHaveBeenCalledTimes(1);
    });

    it("lets the user cancel before OK is clicked", async () => {
        await renderDialog();

        await click(button("Common.Cancel"));

        expect(onClose).toHaveBeenCalledTimes(1);
        expect(mockPostJsonAsync).not.toHaveBeenCalled();
    });

    it("unlocks the dialog and stays open if saving fails", async () => {
        await renderDialog();
        // The dialog lets the failure propagate so it is still reported; catch it here so it
        // doesn't fail the test run as an unhandled rejection.
        const unhandled = vi.fn();
        process.on("unhandledRejection", unhandled);
        try {
            await click(button("Common.OK"));
            await finishSave(false);

            expect(spinnerShown()).toBe(false);
            expect(button("Common.OK").disabled).toBe(false);
            expect(button("Common.Cancel").disabled).toBe(false);
            expect(onSaved).not.toHaveBeenCalled();
            expect(onClose).not.toHaveBeenCalled();
            // The failure must still reach the user's error report, not be swallowed.
            await new Promise((resolve) => setImmediate(resolve));
            expect(unhandled).toHaveBeenCalledWith(
                expect.objectContaining({ message: "save failed" }),
                expect.anything(),
            );
        } finally {
            process.off("unhandledRejection", unhandled);
        }
    });
});
