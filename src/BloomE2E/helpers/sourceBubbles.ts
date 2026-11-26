// What a text box shows, and the source bubble beside it.
//
// A translation group holds one box per language. Which of them show depends on the book's
// content languages and on the group's own language setting (Text Box Properties). The text of the
// other languages goes into the group's source bubble, a qTip tooltip that Bloom puts inside the
// page's own document and ties to the group through aria-describedby (BloomSourceBubbles.tsx).
// Each language in the bubble is a copy of its box with the class "source-text".

import { expect, type Page } from "@playwright/test";
import { clickInGroup, editablePageFrame } from "./bookMaking";
import { clickInPageFrameAt, pageFrameOrigin } from "./formatDialog";
import { waitForClickToSettle } from "./realClick";

/** One language's text, as a box or a source bubble shows it. */
export interface ILanguageText {
    lang: string;
    text: string;
}

/**
 * The languages of the boxes of one translation group that are showing on the page, with the text
 * in each, in the order they appear. `groupSelector` and `groupIndex` pick the group as for
 * typeInGroup.
 */
export async function getShownBoxes(
    page: Page,
    groupSelector: string,
    groupIndex = 0,
): Promise<ILanguageText[]> {
    return editablePageFrame(page)
        .locator(groupSelector)
        .nth(groupIndex)
        .locator(".bloom-editable")
        .evaluateAll((boxes) =>
            boxes
                .filter(
                    (box) => (box as HTMLElement).getClientRects().length > 0,
                )
                .map((box) => ({
                    lang: box.getAttribute("lang") ?? "",
                    text: (box as HTMLElement).innerText.trim(),
                })),
        );
}

/** A translation group's source bubble, as a person looking at the group sees it. */
export interface ISourceBubble {
    /** Whether the group has a source bubble and it is showing on the page. */
    shown: boolean;
    /** The languages the bubble offers, with the text of each, sorted by language. */
    texts: ILanguageText[];
}

/**
 * One translation group's source bubble: whether it shows, and the languages it offers with the
 * text of each. `texts` is empty when the group has no source bubble.
 *
 * The bubble is shown when its group has the focus, so this first clicks in the group's box in
 * `shownLanguage`, the way a person looks at the bubble. Then it waits until the bubble holds
 * still, because Bloom rebuilds a bubble after a change of focus.
 */
export async function getSourceBubble(
    page: Page,
    groupSelector: string,
    shownLanguage: string,
    groupIndex = 0,
): Promise<ISourceBubble> {
    await clickInGroup(page, groupSelector, shownLanguage, groupIndex);
    const group = editablePageFrame(page)
        .locator(groupSelector)
        .nth(groupIndex);
    const read = async (): Promise<ISourceBubble> => {
        const bubbleId = await group.getAttribute("aria-describedby");
        if (!bubbleId) return { shown: false, texts: [] };
        const bubble = editablePageFrame(page).locator(`[id="${bubbleId}"]`);
        const shown = await bubble.isVisible();
        const texts = await bubble
            .locator(".source-text")
            .evaluateAll((sources) =>
                sources.map((source) => ({
                    lang: source.getAttribute("lang") ?? "",
                    // The bubble adds a copy button to each language, whose label ("Copied") is in
                    // the same element; leave it out of the text.
                    text: (() => {
                        const copy = source.cloneNode(true) as HTMLElement;
                        copy.querySelectorAll(".source-copy-button").forEach(
                            (button) => button.remove(),
                        );
                        return (copy.textContent ?? "").trim();
                    })(),
                })),
            );
        return {
            shown,
            texts: texts.sort((a, b) => a.lang.localeCompare(b.lang)),
        };
    };
    let last = "";
    await expect
        .poll(
            async () => {
                const now = JSON.stringify(await read());
                const steady = now === last;
                last = now;
                return steady;
            },
            {
                timeout: 15000,
                intervals: [250],
                message: "The source bubble never held still.",
            },
        )
        .toBe(true);
    return JSON.parse(last) as ISourceBubble;
}

/**
 * Wait until Bloom has finished setting up the source bubbles of a page it has just loaded.
 *
 * Bloom makes a page's source bubbles in a timer after the page loads (bloomEditing.ts, where it
 * calls makeSourceBubblesIntoQtips), and the last thing that timer does is put the insertion point
 * in the page's first text box. So once a box of the group has the focus without the test having
 * clicked, the bubbles are there, and a group without one has none still to come. This holds for a
 * page Bloom rebuilt or opened from the Pages list, whose first text box is a plain text box (not a
 * canvas element), as on a "Just Text" page; Bloom gives a page opened by a click in the Pages grid
 * no focus.
 */
export async function waitForSourceBubbleSetup(
    page: Page,
    groupSelector: string,
    groupIndex = 0,
): Promise<void> {
    await expect(
        editablePageFrame(page)
            .locator(groupSelector)
            .nth(groupIndex)
            .locator(".bloom-editable:focus"),
        "Bloom never put the insertion point in the text box, which it does once the page's source bubbles are set up.",
    ).toHaveCount(1, { timeout: 30000 });
}

/**
 * Bring one language of the shown source bubble of a group forward by its tab, the way a person
 * reads it, and check that the bubble then shows that language's text and hides the others'.
 * The bubble must be showing (getSourceBubble reports that).
 */
export async function expectSourceBubbleLanguageReadable(
    page: Page,
    groupSelector: string,
    language: string,
    text: string,
    groupIndex = 0,
): Promise<void> {
    const bubbleId = await editablePageFrame(page)
        .locator(groupSelector)
        .nth(groupIndex)
        .getAttribute("aria-describedby");
    if (!bubbleId) throw new Error("The group has no source bubble.");
    const bubble = editablePageFrame(page).locator(`[id="${bubbleId}"]`);
    const shown = bubble.locator(`.source-text[lang="${language}"]`);
    if (!(await shown.isVisible()))
        await bubble.locator(`li[id="${language}"] a.sourceTextTab`).click();
    await expect(
        shown,
        `The source bubble's ${language} tab does not show the ${language} text.`,
    ).toBeVisible({ timeout: 15000 });
    await expect(shown).toContainText(text);
    await expect(
        bubble.locator(`.source-text:not([lang="${language}"]):visible`),
        `While the source bubble shows ${language}, it also shows another language.`,
    ).toHaveCount(0);
}

/**
 * How a translation group's source bubble shows: not at all, in full, or truncated. Bloom shows a
 * bubble that would overlap its neighbours only while its group has the focus, and when a page has
 * several groups it cuts each bubble whose group does not have the focus down to the height of the
 * group ("passive-bubble", BloomSourceBubbles.tsx), so that the bubbles do not cover each other.
 */
export type SourceBubbleState = "none" | "full" | "truncated";

/**
 * The state of the source bubble of each translation group on the page being shown, in document
 * order (the order of `.bloom-translationGroup`, the groupIndex that typeInGroup takes). A group
 * whose bubble Bloom has not made yet counts as showing none.
 */
export async function getSourceBubbleStates(
    page: Page,
): Promise<SourceBubbleState[]> {
    return editablePageFrame(page).evaluate(() =>
        Array.from(document.querySelectorAll(".bloom-translationGroup")).map(
            (group): SourceBubbleState => {
                const bubbleId = group.getAttribute("aria-describedby");
                const bubble = bubbleId
                    ? document.getElementById(bubbleId)
                    : null;
                if (
                    !bubble ||
                    !bubble.checkVisibility({
                        opacityProperty: true,
                        visibilityProperty: true,
                    })
                )
                    return "none";
                return bubble.classList.contains("passive-bubble")
                    ? "truncated"
                    : "full";
            },
        ),
    );
}

/**
 * Wait until the source bubbles of the page being shown are in these states (one per translation
 * group, as getSourceBubbleStates gives them), failing with `message` and the states they are in.
 */
export async function expectSourceBubbleStates(
    page: Page,
    expected: SourceBubbleState[],
    message: string,
): Promise<void> {
    let shown: SourceBubbleState[] = [];
    try {
        await expect
            .poll(
                async () => {
                    shown = await getSourceBubbleStates(page);
                    return shown;
                },
                { timeout: 15000 },
            )
            .toEqual(expected);
    } catch {
        throw new Error(
            `${message} Expected the groups' bubbles to be [${expected.join(", ")}], ` +
                `but they are [${shown.join(", ")}].`,
        );
    }
}

/**
 * Whether the source bubble of one translation group (groupIndex as for getSourceBubbleStates)
 * lies over part of the page's picture, as a bubble of a text box beside a picture does when the
 * bubble's side of the box is the picture's.
 */
export async function sourceBubbleOverlapsPicture(
    page: Page,
    groupIndex: number,
): Promise<boolean> {
    return editablePageFrame(page).evaluate((groupIndex) => {
        const group = document.querySelectorAll(".bloom-translationGroup")[
            groupIndex
        ];
        const bubbleId = group?.getAttribute("aria-describedby");
        const bubble = bubbleId ? document.getElementById(bubbleId) : null;
        const picture = document.querySelector(".bloom-canvas");
        if (!bubble || !picture) return false;
        const a = bubble.getBoundingClientRect();
        const b = picture.getBoundingClientRect();
        return (
            a.left < b.right &&
            b.left < a.right &&
            a.top < b.bottom &&
            b.top < a.bottom
        );
    }, groupIndex);
}

/**
 * The index (as for getSourceBubbleStates) of the translation group that holds the focus on the
 * page being shown, or -1 when none does.
 */
export async function getFocusedGroupIndex(page: Page): Promise<number> {
    return editablePageFrame(page).evaluate(() =>
        Array.from(
            document.querySelectorAll(".bloom-translationGroup"),
        ).findIndex((group) => group.contains(document.activeElement)),
    );
}

/**
 * The text in `lang` of each translation group on the page being shown, in document order, with
 * each run of white space made one space. A test reads it to be sure it has the page it means.
 */
export async function getGroupTexts(
    page: Page,
    lang: string,
): Promise<string[]> {
    return editablePageFrame(page).evaluate(
        (lang) =>
            Array.from(
                document.querySelectorAll(".bloom-translationGroup"),
            ).map((group) =>
                (
                    group.querySelector(`.bloom-editable[lang="${lang}"]`)
                        ?.textContent ?? ""
                )
                    .replace(/\s+/g, " ")
                    .trim(),
            ),
        lang,
    );
}

/**
 * A point of the page frame, in its viewport's coordinates, where a click lands on what `kind`
 * names: the page's picture, away from every text box, canvas element and source bubble; or the
 * background around the page.
 */
async function findPointInPageFrame(
    page: Page,
    kind: "picture" | "outsidePage",
): Promise<{ x: number; y: number }> {
    const point = await editablePageFrame(page).evaluate((kind) => {
        const width = document.documentElement.clientWidth;
        const height = document.documentElement.clientHeight;
        const pageRect = document
            .querySelector(".bloom-page")!
            .getBoundingClientRect();
        let area: DOMRect;
        let start: { x: number; y: number };
        let fits: (element: Element, x: number, y: number) => boolean;
        if (kind === "picture") {
            const picture = document.querySelector(".bloom-canvas");
            if (!picture) return undefined;
            area = picture.getBoundingClientRect();
            start = {
                x: area.left + area.width / 2,
                y: area.top + area.height / 2,
            };
            // ComicalJS draws the outlines and tails of speech bubbles on a canvas over the
            // picture, reaching past the boxes of their canvas elements, and a click on the drawing
            // selects its bubble. So the spot must be clear of the drawing for a few pixels round.
            const drawing = picture.querySelector(
                "canvas.comical-editing",
            ) as HTMLCanvasElement | null;
            const drawingClearAround = (x: number, y: number) => {
                if (!drawing) return true;
                const box = drawing.getBoundingClientRect();
                const scale = drawing.width / box.width;
                const margin = 10;
                const left = Math.floor((x - margin - box.left) * scale);
                const top = Math.floor((y - margin - box.top) * scale);
                const size = Math.ceil(2 * margin * scale);
                const pixels = drawing
                    .getContext("2d")!
                    .getImageData(left, top, size, size).data;
                for (let i = 3; i < pixels.length; i += 4)
                    if (pixels[i] !== 0) return false;
                return true;
            };
            fits = (element, x, y) =>
                picture.contains(element) &&
                // A canvas element holding text, such as a speech bubble, has room of its own
                // around the text; only the picture behind them all is the picture.
                !element.closest(
                    ".bloom-canvas-element:not(.bloom-backgroundImage)",
                ) &&
                drawingClearAround(x, y);
        } else {
            area = new DOMRect(0, 0, width, height);
            start = { x: pageRect.left, y: pageRect.bottom };
            fits = (element) => !element.closest(".bloom-page");
        }
        // Try points on a grid over the area, nearest the start first, and take the first one the
        // pointer would land on as meant.
        const candidates: { x: number; y: number }[] = [];
        for (let i = 1; i < 20; i++)
            for (let j = 1; j < 20; j++)
                candidates.push({
                    x: area.left + (area.width * i) / 20,
                    y: area.top + (area.height * j) / 20,
                });
        candidates.sort(
            (a, b) =>
                Math.hypot(a.x - start.x, a.y - start.y) -
                Math.hypot(b.x - start.x, b.y - start.y),
        );
        return candidates.find((candidate) => {
            if (
                candidate.x < 0 ||
                candidate.y < 0 ||
                candidate.x >= width ||
                candidate.y >= height
            )
                return false;
            const element = document.elementFromPoint(candidate.x, candidate.y);
            return (
                !!element &&
                !element.closest(".qtip") &&
                !element.closest(".bloom-translationGroup") &&
                fits(element, candidate.x, candidate.y)
            );
        });
    }, kind);
    if (!point)
        throw new Error(
            kind === "picture"
                ? "The page shows no part of a picture that no text box, canvas element or source bubble covers."
                : "The page frame shows nothing outside the page to click on.",
        );
    return point;
}

/**
 * Click on the page's picture, at a spot that no text box, canvas element or source bubble covers,
 * the way a person clicks on the picture to select it.
 */
export async function clickOnPicture(page: Page): Promise<void> {
    const point = await findPointInPageFrame(page, "picture");
    await clickInPageFrameAt(page, point.x, point.y);
}

/**
 * Click into one translation group's box in `lang` (groupIndex as for getSourceBubbleStates) until
 * it has the insertion point, the way a person clicks into a text box. A box in a canvas element
 * that is not selected takes two clicks, as for a person: the first selects the canvas element and
 * the second puts the insertion point in its text.
 */
export async function clickIntoGroup(
    page: Page,
    groupIndex: number,
    lang: string,
): Promise<void> {
    const group = editablePageFrame(page)
        .locator(".bloom-translationGroup")
        .nth(groupIndex);
    const box = group.locator(`.bloom-editable[lang="${lang}"]`).first();
    await box.click();
    const inCanvasElement =
        (await group
            .locator(
                "xpath=ancestor::*[contains(@class, 'bloom-canvas-element')]",
            )
            .count()) > 0;
    if (
        inCanvasElement &&
        !(await box.evaluate((e) => e === document.activeElement))
    ) {
        // Wait for the first click to select the canvas element, or the second is a first click.
        await expect(
            group.locator(
                "xpath=ancestor::*[contains(@class, 'bloom-canvas-element')][@data-bloom-active='true']",
            ),
            `Clicking the canvas element of text box ${groupIndex + 1} did not select it.`,
        ).toHaveCount(1, { timeout: 15000 });
        await box.click();
    }
    await expect(
        box,
        `Clicking into the "${lang}" box of text box ${groupIndex + 1} did not give it the insertion point.`,
    ).toBeFocused({ timeout: 15000 });
}

/** Click in the Edit tab outside the page, on the background around it. */
export async function clickOutsidePage(page: Page): Promise<void> {
    const point = await findPointInPageFrame(page, "outsidePage");
    await clickInPageFrameAt(page, point.x, point.y);
}

/**
 * Move the mouse over the Edit tab without pressing a button: across the page frame from corner to
 * corner through the middle of each translation group, picture and source bubble, then out over
 * the page list and the toolbox. Returns once the page has drawn whatever the moves caused.
 */
export async function moveMouseAroundWithoutClicking(
    page: Page,
): Promise<void> {
    const origin = await pageFrameOrigin(page);
    const frame = editablePageFrame(page);
    const points = await frame.evaluate(() => {
        const middles = Array.from(
            document.querySelectorAll(
                ".bloom-translationGroup, .bloom-canvas, .qtip",
            ),
        )
            .map((element) => element.getBoundingClientRect())
            .filter((rect) => rect.width > 0 && rect.height > 0)
            .map((rect) => ({
                x: rect.left + rect.width / 2,
                y: rect.top + rect.height / 2,
            }));
        const width = document.documentElement.clientWidth;
        const height = document.documentElement.clientHeight;
        return [{ x: 5, y: 5 }, ...middles, { x: width - 5, y: height - 5 }];
    });
    for (const point of points)
        await page.mouse.move(origin.x + point.x, origin.y + point.y, {
            steps: 5,
        });
    const viewport = await page.evaluate(() => ({
        width: window.innerWidth,
        height: window.innerHeight,
    }));
    // Out over the page list on the left, then across to the toolbox on the right.
    await page.mouse.move(20, viewport.height / 2, { steps: 5 });
    await page.mouse.move(viewport.width - 20, viewport.height / 2, {
        steps: 10,
    });
    await waitForClickToSettle(frame);
}
