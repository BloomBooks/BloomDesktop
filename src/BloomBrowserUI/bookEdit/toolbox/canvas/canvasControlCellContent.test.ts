import { beforeEach, describe, expect, test, vi } from "vitest";

import {
    bloomBuildsMenuForCell,
    getCellContentCanvasElement,
    getTableCellOfCellContent,
} from "./canvasElementTableCells";
import { getControlConfiguration } from "./canvasControlResolution";
import {
    imageCanvasElementControls,
    calendarCanvasElementControls,
} from "./canvasElementControlRegistry";
import { IControlContext } from "./canvasControlTypes";

// The canvas context controls of a canvas element that is the content of a table
// cell: a cell's picture. Which menu such an element gets depends on the cell. In a
// plain table it gets no menu of Bloom's: the "..." button opens the bloom-table
// library's Cell menu for the cell, the same menu a right-click on the cell opens. In
// a calendar month grid Bloom composes the menu instead, from its own image section
// plus the items the library gives for the cell, so one menu carries both. The choice
// is made by the resolution, from the context, so these tests exercise the pieces of
// that: recognising the element (getTableCellOfCellContent), deciding whose menu it is
// (bloomBuildsMenuForCell) and choosing the control set (getControlConfiguration).
// How the cell's own items look is the library's business: its CellMenuItems component
// draws them, in Bloom's menu and in its own popup alike, and its tests cover them.

vi.mock("bloom-table", () => ({
    setupContentsOfCell: () => {},
    // The real one reads the content type off the cell, which is where the library
    // records it and where these test cells carry it.
    getCurrentContentTypeId: (cell: HTMLElement) => cell.dataset.contentType,
    openCellMenu: () => true,
}));

// A cell holding Bloom's picture-cell content: a bloom-canvas with one
// background-image canvas element, which is what ensureLibraryConfiguredForBloom
// gives an image cell (tableEditing.ts). `wrapContent` puts the content inside a
// wrapper the way a calendar day cell does, and `inCalendarGrid` makes the table a
// calendar month grid.
const makePictureCell = (
    wrapContent: boolean,
    inCalendarGrid = false,
): HTMLElement => {
    const table = document.createElement("div");
    table.className = "bloom-table";
    if (inCalendarGrid) {
        table.setAttribute("data-calendar-month", "0");
    }
    const cell = document.createElement("div");
    cell.className = "bloom-cell";
    cell.setAttribute("data-content-type", "image");
    table.appendChild(cell);
    const holder = wrapContent ? document.createElement("div") : cell;
    if (wrapContent) {
        holder.className = "calendarDayCellContents";
        cell.appendChild(holder);
    }
    holder.innerHTML =
        "<div class='bloom-canvas bloom-has-canvas-element'>" +
        "<div class='bloom-canvas-element bloom-backgroundImage'>" +
        "<div class='bloom-imageContainer'><img src='placeHolder.png'/></div>" +
        "</div></div>";
    document.body.appendChild(table);
    return cell;
};

const getBackgroundImageElement = (cell: HTMLElement): HTMLElement => {
    const element = cell.querySelector<HTMLElement>(".bloom-backgroundImage");
    if (!element) {
        throw new Error("test setup built a cell with no background image");
    }
    return element;
};

const makeContext = (canvasElement: HTMLElement): IControlContext =>
    ({
        canvasElement,
        page: null,
        elementType: "image",
        tableCell: getTableCellOfCellContent(canvasElement),
    }) as unknown as IControlContext;

beforeEach(() => {
    document.body.innerHTML = "";
});

describe("getTableCellOfCellContent", () => {
    test("a cell's picture reports the cell that holds it", () => {
        const cell = makePictureCell(false);
        const picture = getBackgroundImageElement(cell);
        // Sanity: the element we are about to ask about really is inside the cell.
        expect(cell.contains(picture)).toBe(true);
        expect(getTableCellOfCellContent(picture)).toBe(cell);
    });

    test("a calendar day cell's picture reports the cell, though a wrapper is between them", () => {
        const cell = makePictureCell(true);
        const picture = getBackgroundImageElement(cell);
        // Sanity: the wrapper really is in between, so this is the grandchild case.
        expect(picture.closest(".bloom-canvas")?.parentElement?.className).toBe(
            "calendarDayCellContents",
        );
        expect(getTableCellOfCellContent(picture)).toBe(cell);
    });

    test("a canvas element the user added on top of a cell's picture reports no cell", () => {
        const cell = makePictureCell(false);
        const bloomCanvas = cell.querySelector<HTMLElement>(".bloom-canvas")!;
        const addedElement = document.createElement("div");
        addedElement.className = "bloom-canvas-element";
        bloomCanvas.appendChild(addedElement);
        // Sanity: it is inside the cell, so only the rule keeps it out.
        expect(addedElement.closest(".bloom-cell")).toBe(cell);
        expect(getTableCellOfCellContent(addedElement)).toBeUndefined();
    });

    test("a picture on the page, in no table, reports no cell", () => {
        const bloomCanvas = document.createElement("div");
        bloomCanvas.className = "bloom-canvas";
        const picture = document.createElement("div");
        picture.className = "bloom-canvas-element bloom-backgroundImage";
        bloomCanvas.appendChild(picture);
        document.body.appendChild(bloomCanvas);
        expect(getTableCellOfCellContent(picture)).toBeUndefined();
    });
});

describe("getControlConfiguration", () => {
    test("a cell's picture sends its menu button to the library's Cell menu, and keeps the toolbar of its own type", () => {
        const cell = makePictureCell(false);
        const ctx = makeContext(getBackgroundImageElement(cell));
        // Sanity: the context says this is a cell's content, which is what the
        // resolution reads.
        expect(ctx.tableCell).toBe(cell);

        const configuration = getControlConfiguration(ctx);
        expect(configuration.opensTableCellMenu).toBe(true);
        // Bloom composes no menu of its own for such an element.
        expect(configuration.menuSections).toEqual([]);
        expect(configuration.toolbar).toEqual(
            imageCanvasElementControls.toolbar,
        );
    });

    test("a picture that is not a cell's content keeps its own type's menu", () => {
        const bloomCanvas = document.createElement("div");
        bloomCanvas.className = "bloom-canvas";
        const picture = document.createElement("div");
        picture.className = "bloom-canvas-element bloom-backgroundImage";
        bloomCanvas.appendChild(picture);
        document.body.appendChild(bloomCanvas);

        const configuration = getControlConfiguration(makeContext(picture));
        expect(configuration.opensTableCellMenu).toBeFalsy();
        expect(configuration.menuSections).toEqual(
            imageCanvasElementControls.menuSections,
        );
        // The whole-element rows are what a cell's content must not offer, so
        // check they are still here for an ordinary picture.
        expect(configuration.menuSections).toContain("wholeElement");
    });

    test("a calendar grid, which is a table but not a cell's content, keeps its own menu", () => {
        const ctx = {
            canvasElement: document.createElement("div"),
            elementType: "calendar",
            tableCell: undefined,
        } as unknown as IControlContext;
        expect(getControlConfiguration(ctx).menuSections).toEqual(
            calendarCanvasElementControls.menuSections,
        );
    });

    test("a calendar grid's picture gets one menu: Bloom's image section plus the cell's items", () => {
        const cell = makePictureCell(true, true);
        const ctx = makeContext(getBackgroundImageElement(cell));
        // Sanity: the context says this is a cell's content, and the cell is a
        // calendar grid's, which together are what the decision reads.
        expect(ctx.tableCell).toBe(cell);
        expect(bloomBuildsMenuForCell(cell)).toBe(true);

        const configuration = getControlConfiguration(ctx);

        expect(configuration.menuSections).toEqual(["image"]);
        expect(configuration.includesTableCellMenuItems).toBe(true);
        // Bloom composes this menu, so the button must not go to the library's.
        expect(configuration.opensTableCellMenu).toBeFalsy();
        // A cell's picture is already the background image of the cell's own
        // bloom-canvas, so the image section's one unwanted row is dropped.
        expect(configuration.availabilityRules.becomeBackground).toBe(
            "exclude",
        );
        // The whole-element rows stay out, as they do for any cell's content.
        expect(configuration.menuSections).not.toContain("wholeElement");
        expect(configuration.toolbar).toEqual(
            imageCanvasElementControls.toolbar,
        );
    });

    test("a text cell in a calendar grid keeps the library's menu", () => {
        const cell = makePictureCell(true, true);
        cell.setAttribute("data-content-type", "text");

        expect(bloomBuildsMenuForCell(cell)).toBe(false);
    });

    test("a picture in a plain table keeps the library's menu", () => {
        const cell = makePictureCell(true);
        // Sanity: the same cell in a calendar grid would be Bloom's to build.
        expect(cell.dataset.contentType).toBe("image");

        expect(bloomBuildsMenuForCell(cell)).toBe(false);
    });
});

describe("getCellContentCanvasElement", () => {
    test("finds the picture the cell holds, which is what the right-click route selects", () => {
        const cell = makePictureCell(true, true);
        expect(getCellContentCanvasElement(cell)).toBe(
            getBackgroundImageElement(cell),
        );
    });

    test("finds nothing in a cell that holds no such content", () => {
        const cell = makePictureCell(true, true);
        cell.innerHTML = "";
        expect(getCellContentCanvasElement(cell)).toBeUndefined();
    });
});
