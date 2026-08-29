import { describe, it, expect, vi } from "vitest";
import { addRow, getTableInfo, setupContentsOfCell } from "bloom-table";
// A new video cell asks the server to put the video placeholder in the book
// folder; there is no server here, and the call is also what proves the wiring
// ran.
const postMock = vi.fn();
vi.mock("../../utils/bloomApi", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    post: (...args: unknown[]) => postMock(...args),
}));

import {
    AttachNewTable,
    AttachNewTableThatFillsItsSpace,
    SetupTableEditing,
    TeardownTableEditing,
} from "./tableEditing";
import { attachToCkEditor } from "./bloomEditing";

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
// attachToCkEditor is what makes a bloom-editable a live editor. This stand-in
// has to let that call run to the end without a browser, and it keeps the one
// rule of the real CKEDITOR.inline() that Bloom code has to respect: an element
// may hold only one editor, and asking for a second one throws.
const attachedEditors = new Map<unknown, { name: string }>();
(globalThis as unknown as { CKEDITOR: unknown }).CKEDITOR = {
    // A palette of its own keeps attachToCkEditor from asking Bloom's server
    // for one.
    config: { colorButton_colors: "FFFFFF,FF0000" },
    dom: {
        element: {
            get: (element: unknown) => ({
                getEditor: () => attachedEditors.get(element),
            }),
        },
    },
    inline: (element: unknown) => {
        if (attachedEditors.has(element))
            throw `The editor instance "${
                attachedEditors.get(element)!.name
            }" is already attached to the provided element.`;
        const editor = {
            name: "stubEditor" + attachedEditors.size,
            id: "stubEditor",
            config: {},
            on: () => {},
            addCommand: () => {},
            ui: { addButton: () => {} },
        };
        attachedEditors.set(element, editor);
        return editor;
    },
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

describe("attachToCkEditor", () => {
    it("leaves an element that already has an editor alone", () => {
        document.body.innerHTML = "";
        const editable = document.createElement("div");
        editable.classList.add("bloom-editable");
        document.body.appendChild(editable);

        attachToCkEditor(editable);
        // Sanity check: the first call is the one that makes the editor.
        expect(attachedEditors.has(editable)).toBe(true);
        const firstEditor = attachedEditors.get(editable);

        // Two paths wire up a new calendar grid's cells: the code that builds
        // the grid, and the pass over the visible editables of the canvas
        // element it lands on. The second call must not throw.
        attachToCkEditor(editable);

        expect(attachedEditors.get(editable)).toBe(firstEditor);
    });
});
