import { afterEach, beforeEach, describe, it, expect, vi } from "vitest";
import { addRow, getTableInfo, setupContentsOfCell } from "bloom-table";
import { createEditTimeMarkupFilter } from "../pageHistory/editTimeMarkup";
import { createPageHistory } from "../pageHistory/pageHistory";
import { kTypingUndoDescriptionL10nKey } from "../pageHistory/pageHistoryInput";
import {
    kGenericUndoDescriptionL10nKey,
    type PageHistory,
} from "../pageHistory/pageHistoryTypes";
// A new video cell asks the server to put the video placeholder in the book
// folder; there is no server here, and the call is also what proves the wiring
// ran.
const postMock = vi.fn();
vi.mock("../../utils/bloomApi", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    post: (...args: unknown[]) => postMock(...args),
    // SetupTableEditing asks the server for the table feature's status. There is no
    // server here, and never calling the callback is what these tests want: the
    // remembered answer stays whatever setTablesMayBeRestructuredForTests set, rather
    // than a real status arriving mid-test.
    get: () => {},
}));
// Every new cell editor gets long-press, which asks the server whether it is on, with axios
// directly, so the mock above does not catch it. With no server here each request fails a
// moment later and logs; when that lands after vitest has torn this file's environment down,
// vitest fails the whole run although every test passed. Nothing here is about long-press.
vi.mock("./bloomEditing", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    activateLongPressFor: () => {},
}));

import {
    AttachNewTable,
    AttachNewTableThatFillsItsSpace,
    SetupTableEditing,
    TeardownTableEditing,
} from "./tableEditing";

// jsdom has no ResizeObserver, which attachSingleTable installs to keep cell
// pictures fitted. Nothing here resizes, so a do-nothing stand-in is enough.
class NoopResizeObserver {
    public observe(): void {}
    public unobserve(): void {}
    public disconnect(): void {}
}
(globalThis as unknown as { ResizeObserver: unknown }).ResizeObserver =
    NoopResizeObserver;

// GetSettings is injected into the page by RuntimeInformationInjector (C#). The
// text cell template needs the language new text boxes use.
(globalThis as unknown as { GetSettings: unknown }).GetSettings = () => ({
    languageForNewTextBoxes: "xyz",
});

// CKEditor is loaded into the real editing page by a script tag, and
// attachToCkEditor is what makes a bloom-editable a live editor. All this
// stand-in has to do is let that call run to the end without a browser.
(globalThis as unknown as { CKEDITOR: unknown }).CKEDITOR = {
    // A palette of its own keeps attachToCkEditor from asking Bloom's server
    // for one.
    config: { colorButton_colors: "FFFFFF,FF0000" },
    // The page history's undo asks every editor to forget a saved selection;
    // these stand-in editors save none.
    instances: {},
    inline: () => ({
        id: "stubEditor",
        config: {},
        on: () => {},
        fire: () => true,
        addCommand: () => {},
        ui: { addButton: () => {} },
    }),
};

describe("AttachNewTableThatFillsItsSpace", () => {
    it("gives every row and column the growing size", () => {
        const tableDiv = document.createElement("div");
        tableDiv.classList.add("bloom-table");
        document.body.appendChild(tableDiv);
        // Sanity check: nothing has sized this table yet.
        expect(tableDiv.getAttribute("data-column-widths")).toBeNull();
        expect(tableDiv.getAttribute("data-row-heights")).toBeNull();

        AttachNewTableThatFillsItsSpace(tableDiv);

        const info = getTableInfo(tableDiv);
        expect(info.columnCount).toBe(2);
        expect(info.rowCount).toBe(2);
        // Two by two, and no more: a table built twice would hold eight cells,
        // and the extra rows would show as thin empty bands.
        expect(tableDiv.children.length).toBe(4);
        // Each cell can be typed in at once, without waiting for a page load.
        expect(
            tableDiv.querySelectorAll(
                ".bloom-translationGroup > .bloom-editable[lang='xyz']",
            ).length,
        ).toBe(4);
        // "fill" is what the table's own Size control calls "Grow".
        expect(info.columnWidths).toEqual(["fill", "fill"]);
        expect(info.rowHeights).toEqual(["fill", "fill"]);
        // The renderer turns a growing line into a fraction of the space.
        expect(tableDiv.style.gridTemplateRows).toContain("fr");
        expect(tableDiv.style.gridTemplateColumns).toContain("fr");
    });
});

describe("AttachNewTable", () => {
    it("leaves the library's own sizing alone: columns grow, rows hug", () => {
        const tableDiv = document.createElement("div");
        tableDiv.classList.add("bloom-table");
        document.body.appendChild(tableDiv);
        expect(tableDiv.getAttribute("data-row-heights")).toBeNull();

        AttachNewTable(tableDiv);

        const info = getTableInfo(tableDiv);
        expect(info.columnWidths).toEqual(["fill", "fill"]);
        expect(info.rowHeights).toEqual(["hug", "hug"]);
    });
});

describe("cells the library builds after the page has loaded", () => {
    it("are given Bloom's editing wiring", () => {
        const tableDiv = document.createElement("div");
        tableDiv.classList.add("bloom-table");
        document.body.appendChild(tableDiv);
        SetupTableEditing(document.body);
        AttachNewTable(tableDiv);
        const cellCountAtLoad = tableDiv.querySelectorAll(".bloom-cell").length;
        // Sanity check: the page-load pass has made these four typable, which is
        // what the new row's cells must end up with too.
        expect(cellCountAtLoad).toBe(4);
        tableDiv
            .querySelectorAll<HTMLElement>(".bloom-editable")
            .forEach((e) => e.setAttribute("contenteditable", "true"));

        // A library operation, which announces itself with tableHistoryUpdated.
        addRow(tableDiv);

        const newCells = tableDiv.querySelectorAll(".bloom-cell").length;
        expect(newCells).toBe(6);
        // Without the wiring these two have no contenteditable at all, so the
        // user cannot type in the row they just added.
        expect(
            tableDiv.querySelectorAll(".bloom-editable[contenteditable='true']")
                .length,
        ).toBe(6);
        TeardownTableEditing(document.body);
    });

    it("include the cells of a nested table, which the library attaches itself", () => {
        document.body.innerHTML = "";
        const tableDiv = document.createElement("div");
        tableDiv.classList.add("bloom-table");
        document.body.appendChild(tableDiv);
        SetupTableEditing(document.body);
        AttachNewTable(tableDiv);
        const hostCell = tableDiv.querySelector<HTMLElement>(".bloom-cell")!;

        // What the cell menu's Table content type does.
        const nested = document.createElement("div");
        nested.classList.add("bloom-table");
        hostCell.innerHTML = "";
        hostCell.appendChild(nested);
        hostCell.dataset.contentType = "table";
        AttachNewTable(nested);
        hostCell.dispatchEvent(
            new CustomEvent("tableCellContentChanged", {
                bubbles: true,
                detail: { cell: hostCell, contentType: "table" },
            }),
        );

        expect(
            nested.querySelectorAll(".bloom-editable[contenteditable='true']")
                .length,
        ).toBe(4);

        // The nested table has none of our data-table-attached marker (the
        // library attaches it in the real app), so teardown has to find it by
        // its class or it keeps its listeners.
        nested.removeAttribute("data-table-attached");
        TeardownTableEditing(document.body);
        expect(nested.getAttribute("data-table-attached")).toBeNull();
    });
});

describe("video cells", () => {
    it("hold Bloom's own video container, wired for editing", () => {
        document.body.innerHTML = "";
        postMock.mockClear();
        const tableDiv = document.createElement("div");
        tableDiv.classList.add("bloom-table");
        document.body.appendChild(tableDiv);
        SetupTableEditing(document.body);
        AttachNewTable(tableDiv);
        const cell = tableDiv.querySelector<HTMLElement>(".bloom-cell")!;
        // Sanity check: it starts as a text cell, and nothing has asked for the
        // placeholder yet.
        expect(cell.dataset.contentType).toBe("text");
        expect(postMock).not.toHaveBeenCalled();

        // What picking Video in the cell menu does.
        setupContentsOfCell(cell, "video");

        expect(cell.dataset.contentType).toBe("video");
        // The library's own template would put a bare <video> here, which none
        // of Bloom's video tooling knows about.
        expect(cell.querySelector("video")).toBeNull();
        const container = cell.querySelector(".bloom-videoContainer");
        expect(container).not.toBeNull();
        // Until a video is recorded or chosen, this is what shows the placeholder.
        expect(container!.classList.contains("bloom-noVideoSelected")).toBe(
            true,
        );
        expect(postMock).toHaveBeenCalledWith(
            "edit/pageControls/requestVideoPlaceHolder",
        );

        // Wiring the same table again (which every later table operation does)
        // must not treat the container as new a second time: that would add the
        // click that opens the Sign Language tool over and over.
        postMock.mockClear();
        addRow(tableDiv);
        expect(postMock).not.toHaveBeenCalled();

        TeardownTableEditing(document.body);
    });
});

// A table's structural change and the typing in its cells are steps of the one page history, in
// the order they were made.
describe("undo of a row and of typing in a table", () => {
    let page: HTMLElement;
    let tableDiv: HTMLElement;
    let history: PageHistory;

    beforeEach(() => {
        document.body.innerHTML = "";
        page = document.createElement("div");
        page.classList.add("bloom-page");
        tableDiv = document.createElement("div");
        tableDiv.classList.add("bloom-table");
        page.appendChild(tableDiv);
        document.body.appendChild(page);
        AttachNewTable(tableDiv);
        history = createPageHistory({
            root: page,
            filter: createEditTimeMarkupFilter(),
        });
        // Sanity check: a new table is two by two.
        expect(getTableInfo(tableDiv).rowCount).toBe(2);
    });

    afterEach(() => {
        history.dispose();
        document.body.innerHTML = "";
    });

    const addARow = () =>
        history.doTransaction(kGenericUndoDescriptionL10nKey, () =>
            addRow(tableDiv),
        );

    function firstCellBox(): HTMLElement {
        return tableDiv.querySelector(".bloom-editable") as HTMLElement;
    }

    const type = (text: string) =>
        history.doTransaction(kTypingUndoDescriptionL10nKey, () => {
            firstCellBox().innerHTML = `<p>${text}</p>`;
        });

    it("takes back a row that was added, and redo puts it back", async () => {
        await addARow();
        expect(getTableInfo(tableDiv).rowCount).toBe(3);

        await history.undo();
        expect(getTableInfo(tableDiv).rowCount).toBe(2);
        expect(tableDiv.querySelectorAll(".bloom-cell").length).toBe(4);

        await history.redo();
        expect(getTableInfo(tableDiv).rowCount).toBe(3);
        expect(tableDiv.querySelectorAll(".bloom-cell").length).toBe(6);
    });

    it("takes back typing that came after a row before the row", async () => {
        await addARow();
        await type("Banana");

        await history.undo();
        expect(firstCellBox().textContent).not.toContain("Banana");
        expect(
            getTableInfo(tableDiv).rowCount,
            "the first undo should take back only the typing",
        ).toBe(3);

        await history.undo();
        expect(getTableInfo(tableDiv).rowCount).toBe(2);
    });

    it("takes back a row that came after typing before the typing, and then the typing", async () => {
        await type("Banana");
        await addARow();

        await history.undo();
        expect(getTableInfo(tableDiv).rowCount).toBe(2);
        expect(
            firstCellBox().textContent,
            "the first undo should take back only the row",
        ).toContain("Banana");

        await history.undo();
        expect(firstCellBox().textContent).not.toContain("Banana");
    });
});
