import { describe, it, expect } from "vitest";
import { addRow, getTableInfo } from "bloom-table";
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
    inline: () => ({
        id: "stubEditor",
        config: {},
        on: () => {},
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
