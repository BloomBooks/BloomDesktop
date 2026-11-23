import { describe, it, expect, vi } from "vitest";

// A text cell's languages travel with its other settings: into a new row or column, and by Paint
// format and Copy / Paste properties (setHostCellSettings in tableEditing.ts). The collection's
// languages, which showing a setting needs, are asked for once per page load; a setting copied
// before they arrive must still reach its cell once they do.
//
// A file of its own, because tableEditing.ts asks for the languages once per module load, and this
// test needs to be the one that sees that request and decides when it is answered.

// The answer to each GET, held until the test gives it.
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

import {
    AttachNewTable,
    kCopiedLanguagesDelayId,
    SetupTableEditing,
} from "./tableEditing";
import { setUpBoxesBuiltAfterLoad } from "./translationGroupLanguages";
import { getActiveDelayIds, whenNoActiveDelays } from "./pageContentDelays";

// jsdom has no ResizeObserver, which attachSingleTable installs to keep cell pictures fitted.
class NoopResizeObserver {
    public observe(): void {}
    public unobserve(): void {}
    public disconnect(): void {}
}
(globalThis as unknown as { ResizeObserver: unknown }).ResizeObserver =
    NoopResizeObserver;
// GetSettings is injected into the page by RuntimeInformationInjector (C#).
(globalThis as unknown as { GetSettings: unknown }).GetSettings = () => ({
    languageForNewTextBoxes: "tpi",
    collectionLanguage1: "tpi",
});

const shownLangsOf = (group: HTMLElement) =>
    Array.from(
        group.querySelectorAll<HTMLElement>(
            ":scope > .bloom-editable.bloom-visibility-code-on",
        ),
    ).map((box) => box.getAttribute("lang"));

describe("a language setting copied into a cell", () => {
    it("reaches the cell when the collection's languages arrive after the copy, and saving waits for it", async () => {
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
        expect(getActiveDelayIds()).not.toContain(kCopiedLanguagesDelayId);

        cellSettingsHooks!.apply(cell, "CL1,N1");
        // Until the cell has its setting, saving the page must wait, or it would save the old one.
        expect(getActiveDelayIds()).toContain(kCopiedLanguagesDelayId);

        answerLanguages!({
            data: {
                language1Tag: "tpi",
                language1Name: "Tok Pisin",
                language2Tag: "en",
                language2Name: "English",
            },
        });

        expect(group.getAttribute("data-default-languages")).toBe("CL1,N1");
        expect(shownLangsOf(group)).toEqual(["tpi", "en"]);
        const added = vi
            .mocked(setUpBoxesBuiltAfterLoad)
            .mock.calls.flatMap((call) => call[0]);
        expect(added.map((box) => box.getAttribute("lang"))).toContain("en");
        await whenNoActiveDelays();
        expect(getActiveDelayIds()).not.toContain(kCopiedLanguagesDelayId);
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
