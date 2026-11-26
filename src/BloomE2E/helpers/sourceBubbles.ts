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
 * How a translation group's source bubble shows. Bloom shows a bubble that would overlap its
 * neighbours only while its group has the focus. When a page has several groups, it marks each
 * bubble whose group does not have the focus "passive-bubble" (BloomSourceBubbles.tsx) and limits
 * its height to the group's, so that the bubbles do not cover each other:
 * - "none": not shown;
 * - "full": shown, and not passive;
 * - "truncated": passive, with text cut off by the height limit;
 * - "passive": passive, but all its text fits.
 */
export type SourceBubbleState = "none" | "full" | "truncated" | "passive";

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
                if (!bubble.classList.contains("passive-bubble")) return "full";
                // A passive bubble hides whatever of its text runs past its height limit
                // (qtipOverrides.less); it is cut only when some text does run past.
                const content = bubble.querySelector(".qtip-content");
                return content &&
                    content.scrollHeight > content.clientHeight + 1
                    ? "truncated"
                    : "passive";
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

/**
 * Wait until the page's picture is selected (its background-image canvas element is the active
 * one, as a click on the picture makes it) and no text box has the insertion point, failing with
 * `message` otherwise.
 */
export async function expectPictureSelected(
    page: Page,
    message: string,
): Promise<void> {
    const frame = editablePageFrame(page);
    await expect(
        frame.locator(
            '.bloom-canvas .bloom-backgroundImage[data-bloom-active="true"]',
        ),
        `${message} The click did not select the picture.`,
    ).toHaveCount(1, { timeout: 15000 });
    await expect
        .poll(() => getFocusedGroupIndex(page), {
            timeout: 15000,
            message: `${message} A text box kept the insertion point after the click on the picture.`,
        })
        .toBe(-1);
}

/** Click in the Edit tab outside the page, on the background around it. */
export async function clickOutsidePage(page: Page): Promise<void> {
    const point = await findPointInPageFrame(page, "outsidePage");
    await clickInPageFrameAt(page, point.x, point.y);
}

/**
 * Move the mouse over the Edit tab without pressing a button, and check that the source bubbles
 * stay as they were all the while. The pointer stops on each translation group, picture and
 * source bubble of the page, at a spot of it that nothing else covers, and checks that the
 * element is then under the pointer (`:hover`); an element covered all over cannot be hovered and
 * is passed by. Then it goes to the frame's corners and out to the left and right sides of the
 * window, beyond the page frame. At every stop, once the page has drawn whatever the move caused,
 * the bubbles must be in the states they were in before the first move; otherwise this fails with
 * `message`, the stop, and the states there.
 */
export async function expectMouseMovesLeaveSourceBubbles(
    page: Page,
    message: string,
): Promise<void> {
    const kTargets = ".bloom-translationGroup, .bloom-canvas, .qtip";
    const origin = await pageFrameOrigin(page);
    const frame = editablePageFrame(page);
    const before = await getSourceBubbleStates(page);
    const { targets, unreachable } = await frame.evaluate((selector) => {
        const width = document.documentElement.clientWidth;
        const height = document.documentElement.clientHeight;
        const reached: {
            x: number;
            y: number;
            index: number;
            over: string;
        }[] = [];
        const missed: string[] = [];
        Array.from(document.querySelectorAll(selector)).forEach(
            (element, index) => {
                const rect = element.getBoundingClientRect();
                if (rect.width === 0 || rect.height === 0) return;
                const over = `${element.className.toString().split(" ")[0]} ${index + 1}`;
                // The picture's own spots are the ones where no text, canvas element holding
                // text, or source bubble lies over it, which the picture contains as well.
                const isPicture = element.matches(".bloom-canvas");
                const hits = (hit: Element | null) =>
                    !!hit &&
                    element.contains(hit) &&
                    (!isPicture ||
                        (!hit.closest(".bloom-translationGroup") &&
                            !hit.closest(".qtip") &&
                            !hit.closest(
                                ".bloom-canvas-element:not(.bloom-backgroundImage)",
                            )));
                const middle = {
                    x: rect.left + rect.width / 2,
                    y: rect.top + rect.height / 2,
                };
                // A fine grid reaching close to the edges, nearest the middle first, so that an
                // element of which only an edge shows is still found.
                const points: { x: number; y: number }[] = [];
                for (let i = 1; i < 40; i++)
                    for (let j = 1; j < 40; j++)
                        points.push({
                            x: rect.left + (rect.width * i) / 40,
                            y: rect.top + (rect.height * j) / 40,
                        });
                points.sort(
                    (a, b) =>
                        Math.hypot(a.x - middle.x, a.y - middle.y) -
                        Math.hypot(b.x - middle.x, b.y - middle.y),
                );
                const spot = points.find(
                    (point) =>
                        point.x >= 0 &&
                        point.y >= 0 &&
                        point.x < width &&
                        point.y < height &&
                        hits(document.elementFromPoint(point.x, point.y)),
                );
                if (spot) reached.push({ ...spot, index, over });
                else missed.push(over);
            },
        );
        return { targets: reached, unreachable: missed };
    }, kTargets);
    if (unreachable.length)
        throw new Error(
            `${message} The pointer cannot reach ${unreachable.join(", ")} of the page, so the ` +
                `test cannot show that moving over them changes nothing.`,
        );
    const corners = await frame.evaluate(() => [
        { x: 5, y: 5 },
        {
            x: document.documentElement.clientWidth - 5,
            y: document.documentElement.clientHeight - 5,
        },
    ]);
    const frameBox = await (await frame.frameElement()).boundingBox();
    if (!frameBox) throw new Error("The page frame has no on-screen box.");
    const viewport = await page.evaluate(() => ({
        width: window.innerWidth,
        height: window.innerHeight,
    }));
    const outside = [
        { x: frameBox.x / 2, over: "the left side of the window" },
        {
            x: (frameBox.x + frameBox.width + viewport.width) / 2,
            over: "the right side of the window",
        },
    ];
    if (frameBox.x < 10 || frameBox.x + frameBox.width > viewport.width - 10)
        throw new Error(
            "The page frame reaches the side of the window, so the pointer cannot go beyond it there.",
        );

    const check = async (over: string) => {
        await waitForClickToSettle(frame);
        const now = await getSourceBubbleStates(page);
        expect(
            now,
            `${message} With the pointer over ${over}, the groups' bubbles went from ` +
                `[${before.join(", ")}] to [${now.join(", ")}].`,
        ).toEqual(before);
    };
    for (const target of targets) {
        await page.mouse.move(origin.x + target.x, origin.y + target.y, {
            steps: 5,
        });
        expect(
            await frame.evaluate(
                ({ selector, index }) =>
                    document
                        .querySelectorAll(selector)
                        [index].matches(":hover"),
                { selector: kTargets, index: target.index },
            ),
            `The pointer did not reach ${target.over}.`,
        ).toBe(true);
        await check(target.over);
    }
    for (const corner of corners) {
        await page.mouse.move(origin.x + corner.x, origin.y + corner.y, {
            steps: 5,
        });
        await check("a corner of the page frame");
    }
    for (const stop of outside) {
        await page.mouse.move(stop.x, viewport.height / 2, { steps: 10 });
        await check(stop.over);
    }
}
