import { afterEach, beforeEach, describe, it, expect, vi } from "vitest";
import { addRow, getTableInfo, setupContentsOfCell } from "bloom-table";
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

// The tests of cell resizing below need to know when a table takes and releases its delay, which
// the register cannot show: the CKEditor stand-in never reports an editor ready, so the register
// never empties here.
const tableDelayChanges = vi.hoisted(() => [] as string[]);
vi.mock("./pageContentDelays", async (importOriginal) => {
    const original =
        (await importOriginal()) as typeof import("./pageContentDelays");
    return {
        ...original,
        addRequestPageContentDelay: (id: string) => {
            if (id === "tableOverflowCheck") tableDelayChanges.push("taken");
            original.addRequestPageContentDelay(id);
        },
        removeRequestPageContentDelay: (id: string) => {
            if (id === "tableOverflowCheck") tableDelayChanges.push("released");
            original.removeRequestPageContentDelay(id);
        },
    };
});

import {
    AttachNewTable,
    AttachNewTableThatFillsItsSpace,
    SetupTableEditing,
    TeardownTableEditing,
    removeTableEditingMarkupFromClone,
} from "./tableEditing";
import OverflowChecker from "../OverflowChecker/OverflowChecker";

// Every table operation checks the table's text for overflow, and that check needs the canvas
// element manager, which no test here sets up. Nothing here is about overflow.
vi.spyOn(OverflowChecker, "AdjustSizeOrMarkOverflowOfAll").mockImplementation(
    () => {},
);

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

// A save cleans a copy of the page with bloom-table's markup-only cleanup (bloom-table's own tests
// check that it leaves Paint Format and Border Brush running), plus the attribute only Bloom adds.
describe("removeTableEditingMarkupFromClone", () => {
    const pageWithEditingMarkup = `
        <div class="bloom-page bloom-paint-format">
            <div class="bloom-table table--selected bloom-pointer-near bloom-current-table"
                 data-table-attached="1" data-column-widths="fill" data-row-heights="fill">
                <div class="bloom-cell cell--selected bloom-pulse-fill bloom-pulse-border"
                     style="--hint-top-color: red; --hint-left-color: blue; color: green">
                    <div data-btable-anchor-name="--a1" style="anchor-name: --a1; color: green">text</div>
                </div>
                <div data-table-overlay="edge">overlay</div>
            </div>
        </div>`;
    const makePage = (): HTMLElement => {
        const holder = document.createElement("div");
        holder.innerHTML = pageWithEditingMarkup;
        return holder.firstElementChild as HTMLElement;
    };

    it("removes table editing's markup, and data-table-attached", () => {
        const page = makePage();
        const editingMarkup = [
            "bloom-paint-format",
            "table--selected",
            "bloom-pointer-near",
            "bloom-current-table",
            "data-table-attached",
            "cell--selected",
            "bloom-pulse-fill",
            "bloom-pulse-border",
            "--hint-top-color",
            "--hint-left-color",
            "data-btable-anchor-name",
            "anchor-name",
            "data-table-overlay",
        ];
        for (const markup of editingMarkup) {
            expect(page.outerHTML, "the test page should have it").toContain(
                markup,
            );
        }

        removeTableEditingMarkupFromClone(page);

        for (const markup of editingMarkup) {
            expect(page.outerHTML).not.toContain(markup);
        }
        // what belongs to the book stays
        expect(page.outerHTML).toContain('data-column-widths="fill"');
        expect(page.outerHTML).toContain("color: green");
        expect(page.outerHTML).toContain("text");
    });
});

// A ResizeObserver that remembers what it watches and lets a test report a resize, so the tests
// below can play the part of a row being dragged.
class RecordingResizeObserver {
    public static instances: RecordingResizeObserver[] = [];
    public readonly observed = new Set<Element>();
    public constructor(private readonly callback: ResizeObserverCallback) {
        RecordingResizeObserver.instances.push(this);
    }
    public observe(target: Element): void {
        this.observed.add(target);
    }
    public unobserve(target: Element): void {
        this.observed.delete(target);
    }
    public disconnect(): void {
        this.observed.clear();
    }
    public reportResize(targets: Element[]): void {
        this.callback(
            targets.map((target) => ({ target }) as ResizeObserverEntry),
            this as unknown as ResizeObserver,
        );
    }
}

describe("watching the size of table cells", () => {
    // Animation frames run only when a test calls runFrame, so it can say what happens between
    // them, as a browser does when it reports resizes after one frame's callbacks.
    let frames: FrameRequestCallback[] = [];
    const runFrame = () => frames.splice(0).forEach((callback) => callback(0));

    let tableDiv: HTMLElement;
    let cellObserver: RecordingResizeObserver;

    beforeEach(() => {
        frames = [];
        vi.stubGlobal(
            "requestAnimationFrame",
            (callback: FrameRequestCallback) => {
                frames.push(callback);
                return frames.length;
            },
        );
        RecordingResizeObserver.instances = [];
        vi.stubGlobal("ResizeObserver", RecordingResizeObserver);
        vi.mocked(OverflowChecker.AdjustSizeOrMarkOverflowOfAll).mockClear();
        document.body.innerHTML = "";
        tableDiv = document.createElement("div");
        tableDiv.classList.add("bloom-table");
        document.body.appendChild(tableDiv);
        SetupTableEditing(document.body);
        AttachNewTable(tableDiv);
        // The observer of cell sizes is the one that watches the cells.
        const cell = tableDiv.querySelector(".bloom-cell")!;
        cellObserver = RecordingResizeObserver.instances.find((observer) =>
            observer.observed.has(cell),
        )!;
        expect(cellObserver, "no observer is watching the cells").toBeDefined();
    });

    afterEach(() => {
        TeardownTableEditing(document.body);
        vi.unstubAllGlobals();
    });

    it("makes saves wait for a whole drag, and releases them once a frame passes with no check", () => {
        // Sanity check: nothing is waiting for a frame before the drag starts.
        expect(frames).toEqual([]);
        tableDelayChanges.length = 0;
        const cells = Array.from(tableDiv.querySelectorAll(".bloom-cell"));

        // Three frames of a row drag: each frame's check runs, then the browser reports that
        // the row's cells changed size again.
        cellObserver.reportResize(cells);
        for (let frame = 0; frame < 3; frame++) {
            runFrame();
            cellObserver.reportResize(cells);
        }
        runFrame();

        expect(
            OverflowChecker.AdjustSizeOrMarkOverflowOfAll,
        ).toHaveBeenCalledTimes(4);
        // Held from the first resize, and never released between frames: each release would
        // make the page send C# a snapshot.
        expect(tableDelayChanges).toEqual(["taken"]);

        // The drag stops: a frame passes with no check waiting.
        runFrame();

        expect(tableDelayChanges).toEqual(["taken", "released"]);
        expect(frames).toEqual([]);
    });

    it("stops watching the cells of a table that is deleted", () => {
        const cells = Array.from(tableDiv.querySelectorAll(".bloom-cell"));
        // Sanity check: every cell is watched.
        expect(cells.length).toBe(4);
        cells.forEach((cell) =>
            expect(cellObserver.observed.has(cell)).toBe(true),
        );

        // What the library does when the user deletes the table: it takes the table out of the
        // page, then reports the operation with the table it removed.
        tableDiv.remove();
        document.dispatchEvent(
            new CustomEvent("tableHistoryUpdated", {
                detail: { table: tableDiv, operation: "deleteTable" },
            }),
        );

        cells.forEach((cell) =>
            expect(cellObserver.observed.has(cell)).toBe(false),
        );
    });
});
