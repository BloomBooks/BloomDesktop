import { describe, it, expect, vi } from "vitest";

// A text cell's languages travel with its other settings: into a new row or column, and by Paint
// format and Copy / Paste properties (setHostCellSettings in tableEditing.ts). The menus' language
// names are asked for once per page load; a copied setting must reach its cell at once, without
// waiting for them, since showing it needs only the page's settings.
//
// A file of its own, because tableEditing.ts asks for the languages once per module load, and this
// test needs to be the one that sees that request and keeps it unanswered.

// The answer to each GET, which the test never gives: it runs as if the languages had not arrived.
const pendingGets = new Map<string, (result: { data: unknown }) => void>();
vi.mock("../../utils/bloomApi", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    post: () => {},
    get: (urlSuffix: string, callback: (result: { data: unknown }) => void) =>
        pendingGets.set(urlSuffix, callback),
}));
// Every new cell editor gets long-press, which asks the server whether it is on, with axios
// directly, so the mock above does not catch it. Nothing here is about long-press.
vi.mock("./bloomEditing", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    activateLongPressFor: () => {},
}));
// Setting up a box needs the real editing page (CKEditor, the toolbox); what matters here is which
// boxes are shown, and that the ones added are handed on to be set up.
vi.mock("./translationGroupLanguages", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    setUpBoxesBuiltAfterLoad: vi.fn(),
}));
// The library's cell-settings hooks, as Bloom installs them.
type CellSettingsHooks = {
    snapshot: (cell: HTMLElement) => string | undefined;
    apply: (cell: HTMLElement, value: string | undefined) => void;
};
let cellSettingsHooks: CellSettingsHooks | undefined;
vi.mock("bloom-table", async (importOriginal) => ({
    ...(await importOriginal<typeof import("bloom-table")>()),
    setGetLocalizations: vi.fn(() => Promise.resolve()),
    setHostCellSettings: (hooks: CellSettingsHooks) => {
        cellSettingsHooks = hooks;
    },
}));

import { AttachNewTable, SetupTableEditing } from "./tableEditing";
import { setUpBoxesBuiltAfterLoad } from "./translationGroupLanguages";

// jsdom has no ResizeObserver, which attachSingleTable installs to keep cell pictures fitted.
class NoopResizeObserver {
    public observe(): void {}
    public unobserve(): void {}
    public disconnect(): void {}
}
(globalThis as unknown as { ResizeObserver: unknown }).ResizeObserver =
    NoopResizeObserver;
// GetSettings is injected into the page by RuntimeInformationInjector (C#): a collection of Tok
// Pisin, then English.
(globalThis as unknown as { GetSettings: unknown }).GetSettings = () => ({
    languageForNewTextBoxes: "tpi",
    collectionLanguage1: "tpi",
    metadataLanguage1: "en",
});

const shownLangsOf = (group: HTMLElement) =>
    Array.from(
        group.querySelectorAll<HTMLElement>(
            ":scope > .bloom-editable.bloom-visibility-code-on",
        ),
    ).map((box) => box.getAttribute("lang"));

describe("a language setting copied into a cell", () => {
    it("reaches the cell at once, before the menus' languages have arrived", () => {
        const tableDiv = document.createElement("div");
        tableDiv.classList.add("bloom-table");
        document.body.appendChild(tableDiv);
        SetupTableEditing(document.body);
        AttachNewTable(tableDiv);
        const cell = tableDiv.querySelector<HTMLElement>(".bloom-cell")!;
        const group = cell.querySelector<HTMLElement>(
            ":scope > .bloom-translationGroup",
        )!;
        // sanity checks: the hooks are installed, the languages were asked for and have not
        // arrived, and the new cell shows only its first language
        expect(
            cellSettingsHooks,
            "setHostCellSettings was not called",
        ).toBeDefined();
        const answerLanguages = pendingGets.get("settings/languageNames");
        expect(
            answerLanguages,
            "the languages were not asked for",
        ).toBeDefined();
        expect(group.getAttribute("data-default-languages")).toBe("CL1");
        expect(shownLangsOf(group)).toEqual(["tpi"]);
        // The collection's first language is the toolbar's first here, so its box is marked so.
        expect(
            group
                .querySelector("[lang='tpi']")!
                .classList.contains("bloom-content1"),
        ).toBe(true);

        cellSettingsHooks!.apply(cell, "CL1,N1");

        expect(group.getAttribute("data-default-languages")).toBe("CL1,N1");
        expect(shownLangsOf(group)).toEqual(["tpi", "en"]);
        // The English box was added, with the classes page load would give it; the wiring pass
        // that follows every copy sets it up (see "cells the library announces" below).
        const english = group.querySelector<HTMLElement>("[lang='en']")!;
        expect(english.classList.contains("bloom-contentNational1")).toBe(true);
    });
});

describe("cells the library announces during one operation", () => {
    // An Add Row announces each new cell on its own. Setting up a box measures the page, so the
    // cells are set up together, in one pass, rather than one by one.
    it("are set up together, in one pass", async () => {
        const tableDiv = document.createElement("div");
        tableDiv.classList.add("bloom-table");
        document.body.appendChild(tableDiv);
        SetupTableEditing(document.body);
        AttachNewTable(tableDiv);
        const cells = Array.from(
            tableDiv.querySelectorAll<HTMLElement>(".bloom-cell"),
        ).slice(0, 2);
        // sanity check: there are two cells to announce
        expect(cells.length).toBe(2);
        const setUp = vi.mocked(setUpBoxesBuiltAfterLoad);
        setUp.mockClear();

        cells.forEach((cell) =>
            cell.dispatchEvent(
                new CustomEvent("tableCellContentChanged", {
                    bubbles: true,
                    detail: { cell, contentType: "text" },
                }),
            ),
        );
        expect(setUp).not.toHaveBeenCalled();
        await Promise.resolve();

        expect(setUp).toHaveBeenCalledTimes(1);
    });
});
