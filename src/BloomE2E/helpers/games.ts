// Game pages in the Edit tab: their tabs (Start, Correct, Wrong, Play), playing a drag game, and
// reading where its draggable items are.
//
// The tabs sit above the page, in the page's own frame. Play puts the page into the state Bloom
// Player gives it when the book is read, and disables editing. Bloom keeps the current tab on the
// top window (window.top.dragActivityPage, an index into the tab list), which getGameTab reads.
// Dragging in Play is Bloom Player's own pointer handling, so it needs real mouse events: a press
// on the item, moves, and a release over the target, which it snaps to.

import { expect, type Page } from "@playwright/test";
import * as fs from "fs";
import { bookHtmlPath } from "./bookHtml";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
} from "./bookMaking";
import { realClick } from "./realClick";
import { expandToolByHeader } from "./toolbox";

/** A game page's tabs, as the tab control labels them. */
export type GameTab = "Start" | "Correct" | "Wrong" | "Play";
const kGameTabs: GameTab[] = ["Start", "Correct", "Wrong", "Play"];

/** Where a draggable item is, as its style records it, e.g. { left: "120px", top: "45px" }. */
export interface IDraggablePosition {
    left: string;
    top: string;
}

/** Positions of a page's draggable items, by their data-draggable-id. */
export type DraggablePositions = Record<string, IDraggablePosition>;

/** A book made by makeBookWithGamePage, and the pages in it a game test needs. */
export interface IBookWithGamePage {
    bookFolder: string;
    gamePageId: string;
    /** Another page of the book, to go to when the test needs to leave the game page. */
    otherPageId: string;
}

/**
 * Make a book from Basic Book with one game page from the Games template, e.g. "Drag Images to
 * Targets", and show that page. Needs a collection whose subscription includes games.
 */
export async function makeBookWithGamePage(
    page: Page,
    gamePageLabel: string,
): Promise<IBookWithGamePage> {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, gamePageLabel, 1, "Games");
    const contentPages = await getContentPages(page);
    const gamePageId = contentPages[contentPages.length - 1].id;
    const otherPageId = (await getPages(page)).find(
        (p) => p.id !== gamePageId,
    )!.id;
    await goToGamePage(page, gamePageId);
    return { bookFolder, gamePageId, otherPageId };
}

/**
 * Show a game page in the Edit tab with the Game tool open, as a person working on it has it, and
 * wait until the page shows its game tabs and the tool has set it up. A page fresh from the Games
 * template has its targets not yet assigned to items; the Game tool assigns them when it sets the
 * page up, so until then no item has a target to be dragged onto.
 */
export async function goToGamePage(page: Page, pageId: string): Promise<void> {
    await goToPage(page, pageId);
    await expandToolByHeader(page, "game");
    await expect(
        gameTabButtons(page).first(),
        "The page being edited never showed its game tabs (Start, Correct, Wrong, Play).",
    ).toBeVisible({ timeout: 30000 });
    await expect(
        editablePageFrame(page).locator('[data-target-of=""]'),
        "The Game tool never assigned the page's targets to its items.",
    ).toHaveCount(0, { timeout: 30000 });
}

function gameTabButtons(page: Page) {
    return editablePageFrame(page).locator("#drag-activity-tab-control button");
}
/** Which of the game page's tabs Bloom is on. */
export async function getGameTab(page: Page): Promise<GameTab> {
    const index = await page.evaluate(
        () =>
            (window.top as unknown as { dragActivityPage?: number })
                .dragActivityPage ?? 0,
    );
    return kGameTabs[index];
}

/**
 * Click one of the game page's tabs the way a person does, and wait until Bloom is on it. Clicking
 * the tab that is already selected is allowed: it is how a test asks for Play a second time.
 */
export async function clickGameTab(page: Page, tab: GameTab): Promise<void> {
    await realClick(
        gameTabButtons(page).filter({ hasText: new RegExp(`^${tab}$`) }),
    );
    await expect
        .poll(async () => getGameTab(page), {
            timeout: 15000,
            message: `Clicking the ${tab} tab did not put the game on it.`,
        })
        .toBe(tab);
}

/**
 * Outside Play, select the first draggable item on the page by clicking it, and wait until Bloom
 * marks it as the selected canvas element. Returns its draggable id.
 */
export async function selectFirstDraggableItem(page: Page): Promise<string> {
    const item = editablePageFrame(page).locator("[data-draggable-id]").first();
    await realClick(item);
    await expect(
        item,
        "Clicking the first draggable item did not select it.",
    ).toHaveAttribute("data-bloom-active", "true", { timeout: 15000 });
    return (await item.getAttribute("data-draggable-id"))!;
}

/**
 * Press a key with the keyboard focus on a draggable item's picture, e.g. "Delete". Selecting an
 * item by clicking it does not take the focus from the toolbox when that had it, so a press that
 * must reach the page goes through this.
 */
export async function pressKeyOnDraggableItem(
    page: Page,
    draggableId: string,
    key: string,
): Promise<void> {
    await editablePageFrame(page)
        .locator(`[data-draggable-id="${draggableId}"] [tabindex]`)
        .first()
        .press(key);
}

/**
 * Wait until the draggable item with this id is on the page being edited (`present`) or gone from
 * it, and fail with `message` if it never is.
 */
export async function expectDraggableItem(
    page: Page,
    draggableId: string,
    present: boolean,
    message: string,
): Promise<void> {
    await expect(
        editablePageFrame(page).locator(`[data-draggable-id="${draggableId}"]`),
        message,
    ).toHaveCount(present ? 1 : 0, { timeout: 15000 });
}

/** Where each draggable item on the page being edited is now. */
export async function getDraggablePositions(
    page: Page,
): Promise<DraggablePositions> {
    return editablePageFrame(page)
        .locator("[data-draggable-id]")
        .evaluateAll((items) =>
            Object.fromEntries(
                items.map((item) => [
                    item.getAttribute("data-draggable-id")!,
                    {
                        left: (item as HTMLElement).style.left,
                        top: (item as HTMLElement).style.top,
                    },
                ]),
            ),
        );
}

/** Where each draggable item on one page of the book is, as the book's file records it. */
export async function readSavedDraggablePositions(
    page: Page,
    bookFolder: string,
    pageId: string,
): Promise<DraggablePositions> {
    const source = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate(
        ({ html, id }) => {
            const doc = new DOMParser().parseFromString(html, "text/html");
            const bookPage = doc.getElementById(id);
            if (!bookPage)
                throw new Error(`The saved book has no page with id ${id}.`);
            return Object.fromEntries(
                Array.from(
                    bookPage.querySelectorAll<HTMLElement>(
                        "[data-draggable-id]",
                    ),
                ).map((item) => [
                    item.getAttribute("data-draggable-id")!,
                    { left: item.style.left, top: item.style.top },
                ]),
            );
        },
        { html: source, id: pageId },
    );
}

/**
 * In Play, drag the first item that has a target onto that target with the real mouse, the way a
 * tester does, and wait until the game counts it as placed. Returns the item's draggable id.
 */
export async function dragItemOntoItsTarget(page: Page): Promise<string> {
    const frame = editablePageFrame(page);
    const id = await frame
        .locator("[data-draggable-id]")
        .evaluateAll((items) =>
            items
                .map((item) => item.getAttribute("data-draggable-id"))
                .find(
                    (itemId) =>
                        itemId &&
                        document.querySelector(`[data-target-of="${itemId}"]`),
                ),
        );
    if (!id)
        throw new Error(
            "The page has no draggable item with a target to drag it onto.",
        );
    const item = frame.locator(`[data-draggable-id="${id}"]`);
    const from = await item.boundingBox();
    const to = await frame.locator(`[data-target-of="${id}"]`).boundingBox();
    if (!from || !to)
        throw new Error(
            `The draggable item ${id} or its target has no box on screen to drag between.`,
        );
    await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
    await page.mouse.down();
    await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, {
        steps: 10,
    });
    await page.mouse.up();
    await expect(
        item,
        `Dragging item ${id} onto its target did not place it there.`,
    ).toHaveClass(/bloom-draggedToTarget/, { timeout: 10000 });
    return id;
}
