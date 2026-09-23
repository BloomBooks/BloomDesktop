// The credits page, and in particular the sentence Bloom writes on it about the original book when
// the book is a derivative ("This book is an adaptation of the original, ...").
//
// That sentence has two shapes. Normally Bloom writes it into a plain div marked
// data-derived="originalCopyrightAndLicense", which the user cannot type in. Its hint bubble,
// "Original copyright & license", carries a closed padlock; clicking the padlock asks Bloom to hand
// the sentence over, and Bloom reloads the page with the same spot turned into a translation group
// holding one editable box (data-book="originalCopyrightAndLicense", lang="*"). That shape lasts for
// one showing of the page: leaving the page, or clicking the open padlock that replaces the closed
// one, brings back the plain div, now holding whatever the user typed. See
// BookCopyrightAndLicense.MakeOriginalCopyrightNoticeEditable and LockOriginalCopyrightNotice.
//
// The hint bubbles are qTip2 tooltips that Bloom's editing code puts inside the page frame. The
// padlock is an <a class="hintBubbleIcon"> in the bubble (BloomHintBubbles.getPossibleLinkIcon),
// and the copyright block's own bubble is a link that opens the Copyright and License dialog.

import { expect, type Locator, type Page } from "@playwright/test";
import {
    editablePageFrame,
    getPages,
    goToPage,
    typeInGroup,
    waitForEditablePage,
} from "./bookMaking";
import { waitForCopyrightDialog } from "./copyrightAndLicense";
import { realClick } from "./realClick";

const ORIGINAL_NOTICE_KEY = "originalCopyrightAndLicense";

// The translation group the sentence becomes while the user may edit it. Passed to typeInGroup,
// which finds the box inside it.
const UNLOCKED_SENTENCE_GROUP = `.bloom-page .bloom-translationGroup:has(> [data-book="${ORIGINAL_NOTICE_KEY}"])`;

/**
 * Show the book's credits page in the Edit tab. Throws, listing the pages there are, when the book
 * has none. Like goToPage, leaving the page Bloom was showing saves it.
 */
export async function goToCreditsPage(page: Page): Promise<void> {
    const pages = await getPages(page);
    const credits = pages.find(
        (p) => !p.isContentPage && /credits/i.test(p.caption),
    );
    if (!credits)
        throw new Error(
            `The book has no credits page. Its pages: ${pages.map((p) => p.caption).join(", ")}.`,
        );
    await goToPage(page, credits.id);
}

/**
 * The spot on the credits page where the sentence about the original book goes, in whichever of its
 * two shapes it is in. Exported for pictures (saveScreenshotIfAsked); tests read its state through
 * getOriginalCopyrightSentence.
 */
export function originalCopyrightSentence(page: Page): Locator {
    return editablePageFrame(page)
        .locator(
            `.bloom-page [data-derived="${ORIGINAL_NOTICE_KEY}"], ${UNLOCKED_SENTENCE_GROUP}`,
        )
        .first();
}

/** What the credits page shows of the sentence about the original book. */
export interface IOriginalCopyrightSentence {
    /** The sentence as the reader sees it, trimmed. Empty when there is no sentence. */
    text: string;
    /** True when the user can type in it: the unlocked shape, with its editable box. */
    editable: boolean;
    /** True when the keyboard focus is in it, which is where the caret goes right after unlocking. */
    hasFocus: boolean;
    /** True when Bloom gave it a hint bubble. Bloom does that exactly when there is a sentence. */
    hasHintBubble: boolean;
}

/**
 * Read the sentence about the original book on the credits page the Edit tab is showing. Call
 * goToCreditsPage first. Throws when the page has no place for the sentence at all, which would
 * mean the xmatter pack has no such field rather than that the book has no original.
 */
export async function getOriginalCopyrightSentence(
    page: Page,
): Promise<IOriginalCopyrightSentence> {
    await waitForEditablePage(page);
    const spot = originalCopyrightSentence(page);
    if ((await spot.count()) === 0)
        throw new Error(
            "The page Bloom is showing has no place for the sentence about the original book. " +
                "Is it the credits page?",
        );
    return spot.evaluate((element) => {
        const box = element.querySelector(
            ".bloom-editable",
        ) as HTMLElement | null;
        const active = element.ownerDocument.activeElement;
        return {
            text: (element as HTMLElement).innerText.trim(),
            editable: !!box && box.isContentEditable,
            hasFocus: !!active && element.contains(active),
            hasHintBubble: element.hasAttribute("data-hint"),
        };
    });
}

/**
 * Wait until the sentence about the original book is in the state `expected` describes (only the
 * fields given are compared), and fail with `message` and the last state seen if it never gets
 * there. Use this rather than a bare expect on getOriginalCopyrightSentence after anything that
 * makes Bloom rebuild the page, such as saving the Copyright and License dialog, because for a
 * moment the old page is still the one showing.
 */
export async function expectOriginalCopyrightSentence(
    page: Page,
    expected: Partial<IOriginalCopyrightSentence>,
    message: string,
): Promise<IOriginalCopyrightSentence> {
    let last: IOriginalCopyrightSentence | undefined;
    const matches = () =>
        !!last &&
        (Object.keys(expected) as (keyof IOriginalCopyrightSentence)[]).every(
            (key) => last![key] === expected[key],
        );
    try {
        await expect
            .poll(
                async () => {
                    last = await getOriginalCopyrightSentence(page).catch(
                        () => undefined,
                    );
                    return matches();
                },
                { timeout: 30000 },
            )
            .toBe(true);
    } catch {
        throw new Error(
            `${message}\nExpected: ${JSON.stringify(expected)}\nLast seen: ${JSON.stringify(last)}`,
        );
    }
    return last!;
}

/** What the sentence's hint bubble shows. */
export interface IOriginalCopyrightHintBubble {
    /** The bubble's words, e.g. "Original copyright & license". */
    label: string;
    /** Which padlock the bubble offers: closed ("locked"), open ("unlocked"), or none at all. */
    padlock: "locked" | "unlocked" | "none";
    /** The padlock's tooltip, or null when it has none (the open padlock has none). */
    padlockTooltip: string | null;
}

/**
 * The hint bubble of the sentence about the original book. Only that bubble has a padlock in it,
 * so the padlock is how it is told from the credits page's other bubbles. Exported for pictures.
 */
export function originalCopyrightHintBubble(page: Page): Locator {
    const frame = editablePageFrame(page);
    return frame
        .locator(".qtip")
        .filter({ has: frame.locator(".hintBubbleIcon") })
        .first();
}

/**
 * Point at the sentence about the original book, as a person does to see its hint bubble, and
 * report what the bubble shows. The pointer stays on the sentence, so the bubble stays open for a
 * picture.
 */
export async function hoverOriginalCopyrightSentence(
    page: Page,
): Promise<IOriginalCopyrightHintBubble> {
    await waitForEditablePage(page);
    await originalCopyrightSentence(page).hover();
    const bubble = originalCopyrightHintBubble(page);
    try {
        await bubble.waitFor({ state: "visible", timeout: 15000 });
    } catch {
        throw new Error(
            "Pointing at the sentence about the original book showed no hint bubble with a padlock.",
        );
    }
    return bubble.evaluate((element) => {
        const icon = element.querySelector(".hintBubbleIcon");
        const src = icon?.querySelector("img")?.getAttribute("src") ?? "";
        let padlock: "locked" | "unlocked" | "none" = "none";
        if (/\/unlock\.svg$/.test(src)) padlock = "unlocked";
        else if (/\/lock\.svg$/.test(src)) padlock = "locked";
        const content = element.querySelector(
            ".qtip-content",
        ) as HTMLElement | null;
        return {
            label: (content ?? (element as HTMLElement)).innerText.trim(),
            padlock,
            padlockTooltip: icon?.getAttribute("title") ?? null,
        };
    });
}

/**
 * Click the padlock in the sentence's hint bubble, pointing at the sentence first so the bubble is
 * open, then wait for Bloom to reload the page in the other shape. `expected` is which padlock the
 * bubble must be showing; a bubble showing the other one fails here rather than silently doing the
 * opposite.
 */
async function clickOriginalCopyrightPadlock(
    page: Page,
    expected: "locked" | "unlocked",
): Promise<void> {
    const bubble = await hoverOriginalCopyrightSentence(page);
    if (bubble.padlock !== expected)
        throw new Error(
            `Expected the sentence's hint bubble to show the ${expected} padlock, but it showed ` +
                `${bubble.padlock === "none" ? "no padlock" : `the ${bubble.padlock} one`}.`,
        );
    await realClick(
        originalCopyrightHintBubble(page).locator("a.hintBubbleIcon"),
    );
    const becomesEditable = expected === "locked";
    // Bloom saves the page and reloads it, so wait for the new page to be in the other shape, and
    // for Bloom to be editing it again.
    await expect
        .poll(
            async () => {
                const spot = originalCopyrightSentence(page);
                if ((await spot.count().catch(() => 0)) === 0) return undefined;
                return spot
                    .evaluate((element) =>
                        element.classList.contains("bloom-translationGroup"),
                    )
                    .catch(() => undefined);
            },
            {
                timeout: 30000,
                message: `Clicking the ${expected} padlock did not ${becomesEditable ? "unlock" : "lock"} the sentence about the original book.`,
            },
        )
        .toBe(becomesEditable);
    await waitForEditablePage(page);
}

/**
 * Click the closed padlock in the hint bubble of the sentence about the original book, which hands
 * the sentence over to the user, and return once Bloom is showing it as an editable box.
 */
export async function unlockOriginalCopyrightSentence(
    page: Page,
): Promise<void> {
    await clickOriginalCopyrightPadlock(page, "locked");
}

/**
 * Click the open padlock in the hint bubble of the sentence about the original book, which locks it
 * again, and return once Bloom is showing it as text that cannot be typed in.
 */
export async function relockOriginalCopyrightSentence(
    page: Page,
): Promise<void> {
    await clickOriginalCopyrightPadlock(page, "unlocked");
}

/**
 * Replace the words of the unlocked sentence about the original book with `text`, typing as a
 * person does. Call unlockOriginalCopyrightSentence first. Nothing is saved until the page is left
 * or locked again.
 */
export async function typeInOriginalCopyrightSentence(
    page: Page,
    text: string,
): Promise<void> {
    await typeInGroup(page, UNLOCKED_SENTENCE_GROUP, "*", text);
}

/**
 * The copyright and license block on the credits page, whose hint bubble "Click to Edit Copyright &
 * License" opens the Copyright and License dialog. Exported for pictures.
 */
export function copyrightAndLicenseBlock(page: Page): Locator {
    return editablePageFrame(page)
        .locator(".bloom-page .licenseAndCopyrightBlock")
        .first();
}

/**
 * Open the Copyright and License dialog the way a person does from the credits page: point at the
 * copyright block and click the link in its hint bubble. Returns once the dialog is open (see
 * helpers/copyrightAndLicense.ts for driving it).
 */
export async function openCopyrightDialogFromCreditsPage(
    page: Page,
): Promise<void> {
    await waitForEditablePage(page);
    await copyrightAndLicenseBlock(page).hover();
    const link = editablePageFrame(page)
        .locator(".qtip a[href*='showCopyrightAndLicenseDialog']")
        .first();
    try {
        await link.waitFor({ state: "visible", timeout: 15000 });
    } catch {
        throw new Error(
            'Pointing at the credits page\'s copyright block showed no "Click to Edit Copyright & License" bubble.',
        );
    }
    await realClick(link);
    await waitForCopyrightDialog(page);
}
