import { describe, it, expect, vi } from "vitest";

// A new text cell shows the collection's first language. When the toolbar puts another language
// first, that box must not be marked bloom-content1, which the reader tools take to be the
// toolbar's first language.
//
// A file of its own, because tableEditing.ts builds its text cell template once per module load,
// from the settings this file gives it.

vi.mock("../../utils/bloomApi", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    post: () => {},
    get: () => {},
}));
// Every new cell editor gets long-press, which asks the server whether it is on.
vi.mock("./bloomEditing", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    activateLongPressFor: () => {},
}));
// Setting up a box needs the real editing page (CKEditor, the toolbox).
vi.mock("./translationGroupLanguages", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    setUpBoxesBuiltAfterLoad: vi.fn(),
}));
vi.mock("bloom-table", async (importOriginal) => ({
    ...(await importOriginal<typeof import("bloom-table")>()),
    setGetLocalizations: vi.fn(() => Promise.resolve()),
}));

import { AttachNewTable, SetupTableEditing } from "./tableEditing";

// jsdom has no ResizeObserver, which attachSingleTable installs to keep cell pictures fitted.
class NoopResizeObserver {
    public observe(): void {}
    public unobserve(): void {}
    public disconnect(): void {}
}
(globalThis as unknown as { ResizeObserver: unknown }).ResizeObserver =
    NoopResizeObserver;
// GetSettings is injected into the page by RuntimeInformationInjector (C#). The toolbar's first
// language is English, the collection's first Tok Pisin.
(globalThis as unknown as { GetSettings: unknown }).GetSettings = () => ({
    languageForNewTextBoxes: "en",
    collectionLanguage1: "tpi",
});

describe("a new text cell when the toolbar's first language is not the collection's", () => {
    it("shows the collection's first language, not marked as the toolbar's first", () => {
        const tableDiv = document.createElement("div");
        tableDiv.classList.add("bloom-table");
        document.body.appendChild(tableDiv);
        SetupTableEditing(document.body);
        AttachNewTable(tableDiv);
        const boxes = Array.from(
            tableDiv.querySelectorAll<HTMLElement>(
                ".bloom-cell > .bloom-translationGroup > .bloom-editable",
            ),
        );
        // sanity check: the table has text cells
        expect(boxes.length, "the new table has no text cells").toBeGreaterThan(
            0,
        );

        expect(boxes.map((box) => box.getAttribute("lang"))).toEqual(
            boxes.map(() => "tpi"),
        );
        expect(
            boxes.filter((box) => box.classList.contains("bloom-content1")),
        ).toEqual([]);
    });
});
