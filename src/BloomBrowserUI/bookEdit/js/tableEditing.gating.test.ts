import { beforeEach, describe, it, expect, vi } from "vitest";

// A book may hold a table its owner is not entitled to make: the subscription is
// below Pro, or the Tables experiment has been turned off. Bloom's rule there is the
// canvas rule -- localize, don't create -- so the table stays attached and typable
// while everything that would create or restructure one is withheld. These tests
// exercise the two hooks that do that (see installHostHooks in tableEditing.ts)
// through the DOM the library actually builds: its structural chrome (the pills and
// "+" buttons) and its Cell menu.
//
// A file of their own, because the library's overlays and its document-level
// listeners are installed once per module load and taken down again when the last
// table is detached. The other tests in tableEditing.test.ts detach their tables,
// which would leave nothing here for a right-click to reach.

// SetupTableEditing asks the server for the table feature's status, and a video cell
// asks it for the placeholder graphic. There is no server here; never calling the
// status callback is what these tests want, so that the remembered answer stays
// whatever setTablesMayBeRestructuredForTests set.
vi.mock("../../utils/bloomApi", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    post: () => {},
    get: () => {},
}));

import { findParagraphForTextContextMenu } from "../textContextMenu/noIndent";
import { AttachNewTable, SetupTableEditing } from "./tableEditing";
import { setTablesMayBeRestructuredForTests } from "./tableFeature";

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
    config: { colorButton_colors: "FFFFFF,FF0000" },
    inline: () => ({
        id: "stubEditor",
        config: {},
        on: () => {},
        addCommand: () => {},
        ui: { addButton: () => {} },
    }),
};

// The pills and the popup menu are the library's own DOM, named by its data
// attributes.
const tablePill = () =>
    document.querySelector<HTMLElement>('[data-btable-menu-pill="table"]');
const menuPopup = () =>
    document.querySelector<HTMLElement>("[data-btable-menu]");

// jsdom gives every element a zero rect, and the library places its pills and menus
// from the cells' rects, so a table with no geometry gets no chrome at all and the
// gate would look as if it were working when it was not. Lay the four cells out as
// 50px squares filling [100,100]..[200,200], as bloom-table's own tests do.
const stubCellGeometry = (tableDiv: HTMLElement) => {
    Array.from(tableDiv.querySelectorAll<HTMLElement>(".bloom-cell")).forEach(
        (cell, i) => {
            const left = 100 + (i % 2) * 50;
            const top = 100 + Math.floor(i / 2) * 50;
            cell.getBoundingClientRect = () =>
                ({
                    left,
                    top,
                    right: left + 50,
                    bottom: top + 50,
                    width: 50,
                    height: 50,
                    x: left,
                    y: top,
                }) as DOMRect;
        },
    );
};

/**
 * Put a table on the page the way a page load does, and select a cell, which is
 * what makes the chrome appear. Returns the table and the focused cell's editable.
 */
const attachATableWithOneCellFocused = (tableId: string) => {
    const tableDiv = document.createElement("div");
    tableDiv.classList.add("bloom-table");
    tableDiv.id = tableId;
    document.body.appendChild(tableDiv);
    SetupTableEditing(document.body);
    AttachNewTable(tableDiv);
    // In the app the page-load pass makes the cells' editables contenteditable;
    // here the library's own end-of-operation notification is the shortest way to get
    // Bloom's cell wiring to run, and it must run whether or not the table is gated.
    document.dispatchEvent(
        new CustomEvent("tableHistoryUpdated", { detail: { table: tableDiv } }),
    );
    stubCellGeometry(tableDiv);
    const editable = tableDiv.querySelector<HTMLElement>("[contenteditable]")!;
    editable.dispatchEvent(new FocusEvent("focusin", { bubbles: true }));
    return { tableDiv, editable };
};

const rightClick = (element: HTMLElement) =>
    element.dispatchEvent(
        new MouseEvent("contextmenu", {
            bubbles: true,
            cancelable: true,
            clientX: 120,
            clientY: 120,
        }),
    );

// A menu one test opened is still on the page for the next one, as it would be for
// a user who has not clicked away yet. Take it down, so that "no Cell menu opened"
// cannot be satisfied by an earlier menu. The tables themselves are left in place:
// clearing the body would orphan the library's overlays, which it builds once.
beforeEach(() => {
    document
        .querySelectorAll("[data-btable-menu]")
        .forEach((menu) => menu.remove());
});

describe("a table where tables may be made", () => {
    it("gets the structural chrome and the Cell menu", () => {
        setTablesMayBeRestructuredForTests(true);
        const { editable } = attachATableWithOneCellFocused("allowed");

        expect(tablePill()).not.toBeNull();
        expect(tablePill()!.style.display).not.toBe("none");

        rightClick(editable);
        expect(menuPopup()).not.toBeNull();
        expect(menuPopup()!.getAttribute("data-btable-menu")).toBe("cell");
    });
});

describe("a table where tables may not be made", () => {
    it("gets no structural chrome and no Cell menu, and stays typable", () => {
        setTablesMayBeRestructuredForTests(false);
        const { tableDiv, editable } = attachATableWithOneCellFocused("frozen");

        // Sanity check: the chrome exists (the library builds it either way), so the
        // assertions below are about the gate hiding it, not about a table that never
        // got any.
        expect(tablePill()).not.toBeNull();
        expect(tablePill()!.style.display).toBe("none");

        // Bloom's own text context menu listens on the document, in the bubble phase, so
        // what it needs from the library is that the right-click reach it at all. Count the
        // events a listener like that one sees.
        let contextMenuEventsSeen = 0;
        const countContextMenus = () => contextMenuEventsSeen++;
        document.addEventListener("contextmenu", countContextMenus);
        rightClick(editable);
        document.removeEventListener("contextmenu", countContextMenus);

        // No Cell menu, so no content type, no merge and no split.
        expect(menuPopup()).toBeNull();

        // Instead the right-click is left to Bloom, which puts up its own text context
        // menu: the library must not stop the event on its way to the document, and the
        // paragraph must be one that menu claims.
        expect(contextMenuEventsSeen).toBe(1);
        const paragraph = editable.querySelector("p")!;
        expect(findParagraphForTextContextMenu(paragraph)).toBe(paragraph);

        // The point of gating the chrome rather than skipping attachTable: the cells
        // are still Bloom text boxes the user can work in.
        expect(
            tableDiv.querySelectorAll(".bloom-editable[contenteditable='true']")
                .length,
        ).toBe(4);
    });
});
