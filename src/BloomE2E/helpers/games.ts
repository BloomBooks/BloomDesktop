// The game tabs (Start, Correct, Wrong, Play) that a game page shows in the bar of controls above
// the page, in the Edit tab's page frame (DragActivityTabControl.tsx). Choosing a tab shows the page
// as it looks in that state of the game. Play puts the page into the state Bloom Player gives it
// when the book is read, and disables editing.
//
// Also: playing a drag game, and reading where its draggable items are. Dragging in Play is Bloom
// Player's own pointer handling, so it needs real mouse events: a press on the item, moves, and a
// release over the target, which it snaps to.

import { expect, type Locator, type Page } from "@playwright/test";
import * as fs from "fs";
import * as path from "path";
import { bookHtmlPath } from "./bookHtml";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
} from "./bookMaking";
import {
    canvasElements,
    dispatchPaletteDrag,
    selectCanvasElementAt,
    type PaletteItem,
} from "./canvasElements";
import type { IViewRect } from "./pageView";
import { realClick, waitForClickToSettle } from "./realClick";
import { armFileChooser } from "./talkingBook";
import { expandToolByHeader, toolboxFrame } from "./toolbox";
export type GameTab = "Start" | "Correct" | "Wrong" | "Play";
export const kGameTabs: GameTab[] = ["Start", "Correct", "Wrong", "Play"];

function gameTabControl(page: Page): Locator {
    return editablePageFrame(page).locator("#drag-activity-tab-control");
}

function gameTab(page: Page, tab: GameTab): Locator {
    return gameTabControl(page).getByRole("button", { name: tab, exact: true });
}

/**
 * Where each game tab is, and the bar of controls across the top of the page frame that holds
 * them, in the page frame's view. Waits for the tabs to show.
 */
export async function getGameTabPlacements(page: Page): Promise<{
    bar: IViewRect;
    tabs: Record<GameTab, IViewRect>;
}> {
    await expect(
        gameTab(page, "Play"),
        "The page being edited shows no game tabs.",
    ).toBeVisible({ timeout: 30000 });
    const rectOf = (l: Locator) =>
        l.evaluate((e) => {
            const r = e.getBoundingClientRect();
            return {
                left: r.left,
                top: r.top,
                right: r.right,
                bottom: r.bottom,
            };
        });
    const tabs = {} as Record<GameTab, IViewRect>;
    for (const tab of kGameTabs) tabs[tab] = await rectOf(gameTab(page, tab));
    const bar = await rectOf(
        editablePageFrame(page).locator("body > .bloom-controls-bar"),
    );
    return { bar, tabs };
}

/**
 * Which game tab the page being edited is showing: the tab the Game tool last set, which it keeps
 * on the Edit tab's own window (setActiveDragActivityTab in GameTool.tsx).
 */
export async function getGameTabShowing(
    page: Page,
): Promise<GameTab | undefined> {
    const index = await page.evaluate(
        () =>
            (window as unknown as Record<string, unknown>).dragActivityPage as
                | number
                | undefined,
    );
    return index === undefined ? undefined : kGameTabs[index];
}

/**
 * Click a game tab with the mouse, and wait until the page shows that state of the game. Clicking
 * the tab already showing is allowed: it is how a test asks for Play a second time.
 */
export async function clickGameTab(page: Page, tab: GameTab): Promise<void> {
    await gameTab(page, tab).click();
    await expect
        .poll(() => getGameTabShowing(page), {
            timeout: 15000,
            message: `Clicking the ${tab} game tab did not show the page in that state.`,
        })
        .toBe(tab);
}

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
 *
 * A game page can open in Play: Bloom keeps the last game tab chosen, and with the Game tool shut
 * nothing takes a newly opened page back to Start. In Play the whole toolbox is disabled, so the
 * tool can't be opened; as a person would, this chooses Start first.
 */
export async function goToGamePage(page: Page, pageId: string): Promise<void> {
    await goToPage(page, pageId);
    await expect(
        gameTab(page, "Play"),
        "The page being edited never showed its game tabs (Start, Correct, Wrong, Play).",
    ).toBeVisible({ timeout: 30000 });
    if ((await getGameTabShowing(page)) === "Play") {
        await clickGameTab(page, "Start");
    }
    await expandToolByHeader(page, "game");
    await expect(
        editablePageFrame(page).locator('[data-target-of=""]'),
        "The Game tool never assigned the page's targets to its items.",
    ).toHaveCount(0, { timeout: 30000 });
}

/**
 * Outside Play, select the first draggable item on the page by clicking it, and wait until Bloom
 * marks it as the selected canvas element. Returns its draggable id.
 */
export async function selectFirstDraggableItem(page: Page): Promise<string> {
    const item = await selectCanvasElementAt(
        editablePageFrame(page).locator("[data-draggable-id]").first(),
        "the first draggable item",
    );
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
    return draggablePositionsIn(page);
}

/** Where each draggable item on one page of the book is, as the book's file records it. */
export async function readSavedDraggablePositions(
    page: Page,
    bookFolder: string,
    pageId: string,
): Promise<DraggablePositions> {
    return draggablePositionsIn(page, {
        html: fs.readFileSync(bookHtmlPath(bookFolder), "utf8"),
        pageId,
    });
}

/**
 * The positions of the draggable items on the page being edited or, given `saved`, on the page
 * with that id in that book HTML.
 */
async function draggablePositionsIn(
    page: Page,
    saved?: { html: string; pageId: string },
): Promise<DraggablePositions> {
    return editablePageFrame(page).evaluate((saved) => {
        let root: ParentNode = document;
        if (saved) {
            const bookPage = new DOMParser()
                .parseFromString(saved.html, "text/html")
                .getElementById(saved.pageId);
            if (!bookPage)
                throw new Error(
                    `The saved book has no page with id ${saved.pageId}.`,
                );
            root = bookPage;
        }
        return Object.fromEntries(
            Array.from(
                root.querySelectorAll<HTMLElement>("[data-draggable-id]"),
            ).map((item) => [
                item.getAttribute("data-draggable-id")!,
                { left: item.style.left, top: item.style.top },
            ]),
        );
    }, saved);
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

/** The Game tool's panel in the toolbox, which holds its palette for the tab showing. */
const GAME_TOOL_PANEL = '.MuiAccordion-root:has([data-toolid="game"])';
/** The canvas of a game page, which takes the items dragged from the Game tool's palette. */
const GAME_PAGE_CANVAS = '.bloom-page[data-tool-id="game"] .bloom-canvas';

/**
 * Drag one item from the Game tool's palette onto the game page being edited, as a person does,
 * and wait until the page has one more canvas element. Returns the position of the new element
 * among the page's canvas elements (canvasElements), which holds until another is added or removed.
 * In the Correct or Wrong tab the palette is that tab's, so the new item shows only in that tab.
 *
 * Bloom does not always add the new element last on a game page, so it is found as the one that
 * was not there before the drag.
 *
 * `at` places the drop within the canvas, as a fraction of its width and height. Like the Canvas
 * tool's palette drag (dragPaletteItemOntoCanvas), the drag is dispatched rather than made with the
 * real mouse; that function's helper says why.
 */
export async function dragGamePaletteItemOntoPage(
    page: Page,
    item: PaletteItem,
    at: { xFraction: number; yFraction: number },
): Promise<number> {
    const existing = await canvasElements(page).elementHandles();
    await dispatchPaletteDrag(
        page,
        item,
        at,
        GAME_TOOL_PANEL,
        GAME_PAGE_CANVAS,
    );
    await expect
        .poll(async () => canvasElements(page).count(), {
            timeout: 30000,
            message: `Dragging the Game tool's ${item} item onto the page did not add a canvas element.`,
        })
        .toBe(existing.length + 1);
    const index = await canvasElements(page).evaluateAll(
        (elements, before) => elements.findIndex((e) => !before.includes(e)),
        existing,
    );
    await Promise.all(existing.map((handle) => handle.dispose()));
    return index;
}

/**
 * Outside Play, select a text box that lies inside another canvas element, such as the word under
 * a target on a Drag Images to Words page, which sits in a rectangle. A click on such a text box
 * selects the element around it, and a second click selects the text box itself, so this clicks
 * once or twice, and waits until Bloom marks the text box as the selected canvas element. `what`
 * names it in the failure message.
 */
export async function selectTextBoxInsideAnotherItem(
    page: Page,
    textBox: Locator,
    what: string,
): Promise<void> {
    await realClick(textBox);
    await waitForClickToSettle(editablePageFrame(page));
    if ((await textBox.getAttribute("data-bloom-active")) !== "true") {
        await realClick(textBox);
    }
    await expect(
        textBox,
        `Clicking ${what} twice did not select it.`,
    ).toHaveAttribute("data-bloom-active", "true", { timeout: 15000 });
}

/** How a target shows the picture that belongs on it, as getPictureOnTarget reports it. */
export interface ITargetPicture {
    /** The picture's file, e.g. "bird.png"; "placeHolder.png" when its item has no picture yet. */
    fileName: string;
    /** True when the browser has the picture: it loaded rather than failed. */
    loaded: boolean;
    /** True when the picture takes up room on the page and is not hidden. */
    laidOut: boolean;
    /**
     * How opaque the picture is drawn, from 0 (not seen at all) to 1, counting everything between
     * it and the target. The game tabs differ in this: Start shows a shadow of the picture, Correct
     * the whole picture, and Wrong nothing.
     */
    opacity: number;
}

/**
 * The copy of its item's picture that a target holds, and how the tab showing draws it. `target`
 * locates one target (an element with data-target-of).
 */
export async function getPictureOnTarget(
    target: Locator,
): Promise<ITargetPicture> {
    return target.evaluate((targetElement) => {
        const image = targetElement.querySelector("img")!;
        let opacity = 1;
        for (
            let e: Element = image;
            e !== targetElement.parentElement!;
            e = e.parentElement!
        ) {
            opacity *= Number(getComputedStyle(e).opacity);
        }
        const src = (image.getAttribute("src") ?? "").split("?")[0];
        return {
            fileName: decodeURIComponent(src.split("/").pop() ?? src),
            loaded: image.complete && image.naturalWidth > 0,
            laidOut:
                image.getClientRects().length > 0 &&
                getComputedStyle(image).visibility === "visible",
            opacity,
        };
    });
}

/**
 * The items that the game tab showing adds to the page and that cover the point (x, y) of the
 * window: in Correct the ones only Correct shows, in Wrong the ones only Wrong shows. A press at a
 * point they cover goes to them, not to an item made in Start that lies under them. Returns their
 * class names, so a failure can say what was in the way.
 */
export async function getTabItemsCovering(
    page: Page,
    x: number,
    y: number,
): Promise<string[]> {
    const covering: string[] = [];
    const tabItems = await editablePageFrame(page)
        .locator(".drag-item-correct, .drag-item-wrong")
        .all();
    for (const item of tabItems) {
        // An item of the tab that is not showing has no box.
        const box = await item.boundingBox();
        if (
            box &&
            x >= box.x &&
            x <= box.x + box.width &&
            y >= box.y &&
            y <= box.y + box.height
        ) {
            covering.push((await item.getAttribute("class"))!);
        }
    }
    return covering;
}

/** The two sounds a game page sets: the one for a correct answer and the one for a wrong one. */
export type GameSoundType = "correct" | "wrong";

/**
 * The sound the page being edited plays for a correct or wrong answer, as the page records it:
 * the file name in the book's audio folder, "none", or "default" when the page names none
 * (GameTool.tsx reads it the same way).
 */
export async function getGameSound(
    page: Page,
    soundType: GameSoundType,
): Promise<string> {
    const attribute = await editablePageFrame(page)
        .locator(".bloom-page")
        .first()
        .getAttribute(`data-${soundType}-sound`);
    return attribute ?? "default";
}

/**
 * On a page that the Game tool offers sounds for (a quiz, or a choice game such as "Choose Word
 * from Image"), with the Game tool open in its Start tab, choose the sound file at `filePath` as
 * the page's correct or wrong sound: open that sound's menu, choose "Choose...", and answer the
 * file chooser with `filePath`. Returns once the page names the file, which Bloom has copied into
 * the book's audio folder by then.
 */
export async function chooseGameSoundFile(
    page: Page,
    soundType: GameSoundType,
    filePath: string,
): Promise<void> {
    const toolbox = toolboxFrame(page);
    const control = toolbox.getByTestId(`game-${soundType}-sound`);
    await expect(
        control,
        `The Game tool shows no ${soundType} sound menu for this page.`,
    ).toBeVisible({ timeout: 30000 });
    await armFileChooser(page, filePath);
    await control.locator('[aria-haspopup="listbox"]').click();
    // The menu's "Choose..." item, found by its value, since its label is localized.
    await toolbox.locator('[role="listbox"] li[data-value="choose"]').click();
    const fileName = path.basename(filePath);
    await expect
        .poll(() => getGameSound(page, soundType), {
            timeout: 15000,
            message: `The page never named ${fileName} as its ${soundType} sound.`,
        })
        .toBe(fileName);
}

/**
 * The draggable items of a game page that hold a video, and the video file of each, by
 * data-draggable-id. `gamePage` locates the page: the page being edited (gamePageInEditTab), or
 * the one the BloomPUB preview shows (activePreviewPage).
 */
export async function getDraggableVideoFiles(
    gamePage: Locator,
): Promise<Record<string, string>> {
    return gamePage.evaluate((pageElement) =>
        Object.fromEntries(
            Array.from(
                pageElement.querySelectorAll<HTMLElement>(
                    "[data-draggable-id]",
                ),
            )
                .map((item) => {
                    const source = item.querySelector("video source");
                    const src = (source?.getAttribute("src") ?? "").split(
                        /[?#]/,
                    )[0];
                    return [
                        item.getAttribute("data-draggable-id")!,
                        decodeURIComponent(src.split("/").pop() ?? ""),
                    ];
                })
                .filter(([, file]) => file !== ""),
        ),
    );
}

/** The game page being edited, as a locator the draggable-video helpers take. */
export function gamePageInEditTab(page: Page): Locator {
    return editablePageFrame(page).locator(".bloom-page").first();
}

/** One thing a draggable item's video did, as startRecordingDraggableVideos records it. */
export interface IDraggableVideoEvent {
    /** The data-draggable-id of the item that holds the video. */
    itemId: string;
    /**
     * "playing" when the video began to play with its sound on, "ended" when it played to its
     * end. Bloom Player also plays each draggable video for an instant with its sound off, to
     * show its first frame (showVideoFirstFrameWhenReady); that is not playing it for the
     * reader, so it is left out.
     */
    event: "playing" | "ended";
    /** How long the video's clip is, in seconds to the tenth: it tells the clips apart. */
    seconds: number;
    /** True when a mouse button was held down on the page at the time. */
    pressed: boolean;
}

/**
 * Start recording what the videos of `gamePage`'s draggable items do (see IDraggableVideoEvent),
 * from now on, and which of them are playing at each release of the mouse button (see
 * getDraggableVideosPlayingAtEachRelease). The record lives in the page's document; calling this
 * again clears it. A media element's events do not bubble, so they are caught on their way down.
 */
export async function startRecordingDraggableVideos(
    gamePage: Locator,
): Promise<void> {
    await gamePage.evaluate((pageElement) => {
        type Recorded = {
            itemId: string;
            event: string;
            seconds: number;
            pressed: boolean;
        };
        const w = pageElement.ownerDocument.defaultView as unknown as {
            e2eDraggableVideoEvents?: Recorded[];
            e2eDraggableVideosPlayingAtRelease?: string[][];
        };
        if (w.e2eDraggableVideoEvents && w.e2eDraggableVideosPlayingAtRelease) {
            w.e2eDraggableVideoEvents.length = 0;
            w.e2eDraggableVideosPlayingAtRelease.length = 0;
            return;
        }
        const events: Recorded[] = [];
        const playingAtRelease: string[][] = [];
        w.e2eDraggableVideoEvents = events;
        w.e2eDraggableVideosPlayingAtRelease = playingAtRelease;
        const doc = pageElement.ownerDocument;
        let pressed = false;
        doc.addEventListener("pointerdown", () => (pressed = true), true);
        doc.addEventListener(
            "pointerup",
            () => {
                pressed = false;
                // A video that play() was called on is no longer paused at once, before it
                // fires "playing", so this sees a start the press made even when the drag was
                // too quick for "playing" to come before the release. This listener, on the
                // document's way down, runs before the game's own one on the item.
                playingAtRelease.push(
                    Array.from(
                        doc.querySelectorAll<HTMLVideoElement>(
                            "[data-draggable-id] video",
                        ),
                    )
                        .filter((v) => !v.paused && !v.muted)
                        .map(
                            (v) =>
                                v
                                    .closest("[data-draggable-id]")!
                                    .getAttribute("data-draggable-id")!,
                        ),
                );
            },
            true,
        );
        doc.addEventListener("pointercancel", () => (pressed = false), true);
        for (const type of ["playing", "ended"]) {
            doc.addEventListener(
                type,
                (e) => {
                    const video = e.target as HTMLVideoElement;
                    if (video?.tagName !== "VIDEO") return;
                    if (type === "playing" && video.muted) return;
                    const item = video.closest("[data-draggable-id]");
                    if (!item) return;
                    events.push({
                        itemId: item.getAttribute("data-draggable-id")!,
                        event: type,
                        seconds: Math.round(video.duration * 10) / 10,
                        pressed,
                    });
                },
                true,
            );
        }
    });
}

/** What the draggable items' videos have done since startRecordingDraggableVideos, in order. */
export async function getDraggableVideoEvents(
    gamePage: Locator,
): Promise<IDraggableVideoEvent[]> {
    const events = await gamePage.evaluate(
        (pageElement) =>
            (
                pageElement.ownerDocument.defaultView as unknown as {
                    e2eDraggableVideoEvents?: IDraggableVideoEvent[];
                }
            ).e2eDraggableVideoEvents,
    );
    if (!events)
        throw new Error(
            "Nothing is recording the draggable videos; call startRecordingDraggableVideos first.",
        );
    return events;
}

/**
 * For each release of the mouse button on the page since startRecordingDraggableVideos, in
 * order, the data-draggable-ids of the draggable videos that were playing, with their sound on,
 * at that moment.
 */
export async function getDraggableVideosPlayingAtEachRelease(
    gamePage: Locator,
): Promise<string[][]> {
    const releases = await gamePage.evaluate(
        (pageElement) =>
            (
                pageElement.ownerDocument.defaultView as unknown as {
                    e2eDraggableVideosPlayingAtRelease?: string[][];
                }
            ).e2eDraggableVideosPlayingAtRelease,
    );
    if (!releases)
        throw new Error(
            "Nothing is recording the draggable videos; call startRecordingDraggableVideos first.",
        );
    return releases;
}

/**
 * Wait until the game on `gamePage` is ready to play its draggable videos, and shows them as it
 * does before anyone touches them: Bloom Player has set each one up to play when pressed (it
 * marks it bloom-ui-no-controls as it adds that listener, in prepareActivity), and each has its
 * first frame (readyState 2 or more) and is paused.
 */
export async function waitForDraggableVideosShown(
    gamePage: Locator,
): Promise<void> {
    await expect
        .poll(
            () =>
                gamePage.evaluate((pageElement) =>
                    Array.from(
                        pageElement.querySelectorAll<HTMLVideoElement>(
                            "[data-draggable-id] video",
                        ),
                    ).every(
                        (v) =>
                            v.classList.contains("bloom-ui-no-controls") &&
                            v.readyState >= 2 &&
                            v.paused,
                    ),
                ),
            {
                timeout: 30000,
                message:
                    "The game never set up all its draggable videos, showing their first frame, paused.",
            },
        )
        .toBe(true);
}

/**
 * How far into its clip each draggable item's video on `gamePage` is, in seconds, by
 * data-draggable-id; a video that has played to its end reads "ended".
 */
export async function getDraggableVideoPlaybackPositions(
    gamePage: Locator,
): Promise<Record<string, number | "ended">> {
    return gamePage.evaluate((pageElement) =>
        Object.fromEntries(
            Array.from(
                pageElement.querySelectorAll<HTMLVideoElement>(
                    "[data-draggable-id] video",
                ),
            ).map((v) => [
                v
                    .closest("[data-draggable-id]")!
                    .getAttribute("data-draggable-id")!,
                v.ended ? "ended" : v.currentTime,
            ]),
        ),
    );
}

/**
 * Drag the draggable item `itemId` of `gamePage` onto the target of item `targetOf` (its own
 * target, or another item's), with the real mouse, the way a reader does: press on the middle of
 * the item, call `whilePressed` (if given) while the button is still down, move to the middle of
 * the target and let go. Returns once the game shows the item on that target: Bloom Player snaps
 * an item dropped near a target to the target's middle and marks it bloom-draggedToTarget.
 * `page` is the Playwright page whose mouse is used.
 */
export async function dragDraggableItemOnto(
    page: Page,
    gamePage: Locator,
    itemId: string,
    targetOf: string,
    whilePressed?: () => Promise<void>,
): Promise<void> {
    const item = gamePage.locator(`[data-draggable-id="${itemId}"]`);
    const target = gamePage.locator(`[data-target-of="${targetOf}"]`);
    const from = await item.boundingBox();
    const to = await target.boundingBox();
    if (!from || !to)
        throw new Error(
            `The draggable item ${itemId} or the target of ${targetOf} has no box on screen to drag between.`,
        );
    await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
    await page.mouse.down();
    try {
        if (whilePressed) await whilePressed();
        await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, {
            steps: 10,
        });
    } finally {
        await page.mouse.up();
    }
    await expect(
        item,
        `Dragging item ${itemId} onto the target of ${targetOf} did not leave it on a target.`,
    ).toHaveClass(/bloom-draggedToTarget/, { timeout: 10000 });
    // Both boxes are read at the same moment, in the page's own document: in the BloomPUB preview
    // the drag also nudges Bloom Player's page slider, which then slides the page back, so a box
    // read before the drag is not where the target is now.
    expect(
        await gamePage.evaluate(
            (pageElement, ids) => {
                const middleOf = (selector: string) => {
                    const r = pageElement
                        .querySelector(selector)!
                        .getBoundingClientRect();
                    return { x: r.x + r.width / 2, y: r.y + r.height / 2 };
                };
                const placed = middleOf(`[data-draggable-id="${ids.item}"]`);
                const target = middleOf(`[data-target-of="${ids.target}"]`);
                return Math.max(
                    Math.abs(placed.x - target.x),
                    Math.abs(placed.y - target.y),
                );
            },
            { item: itemId, target: targetOf },
        ),
        `Item ${itemId} should sit in the middle of the target of ${targetOf} (pixels off)`,
    ).toBeLessThanOrEqual(1);
}
