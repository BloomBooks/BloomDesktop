// Read what a branding put on a book's front and back matter pages.
//
// A branding (src/content/branding/<name>/branding.json) fills the slots of the front and back
// matter, each a div whose data-book names it, e.g. "outside-back-cover-branding-bottom-html",
// with pictures and text. Some brandings also carry the "Made with Bloom" badge, which Bloom
// gives a QR code and a link to the book's language on Bloom Library when the collection shows QR
// codes. These helpers read a slot as the Edit tab draws it, so a test can check what a reader of
// the printed book would see there.

import { type Page } from "@playwright/test";
import { editablePageFrame, getPages, goToPage } from "./bookMaking";
import { getPicturesShownIn } from "./images";

/** One picture a branding slot shows. */
export interface IBrandingPicture {
    /** The picture's file name, e.g. "sampleFiveLogos.png". */
    file: string;
    /** The size of the picture file in pixels; 0 by 0 when the browser could not load it. */
    width: number;
    height: number;
}

/** A branding slot as the Edit tab draws it. */
export interface IBrandingSlot {
    /** The text a reader sees in the slot, with its white space collapsed. */
    text: string;
    /** The pictures a reader sees in the slot. */
    pictures: IBrandingPicture[];
    /** Where the slot's "Made with Bloom" badge links to, or null when it has no badge link. */
    badgeLink: string | null;
    /**
     * Each picture or line of text in the slot that is drawn past an edge of its page, so that a
     * printed book would cut it off, described for a failure message. Empty when all of it is on
     * the page.
     */
    offThePage: string[];
}

/**
 * Show the front or back matter page that the pack calls `xmatterPage` (as in its
 * data-xmatter-page, e.g. "frontCover", "titlePage", "credits", "outsideBackCover") in the Edit
 * tab. The Edit tab must be showing.
 */
export async function goToXmatterPage(
    page: Page,
    xmatterPage: string,
): Promise<void> {
    const xmatterPages = (await getPages(page)).filter((p) => !p.isContentPage);
    const seen: string[] = [];
    for (const candidate of xmatterPages) {
        await goToPage(page, candidate.id);
        const kind = await editablePageFrame(page)
            .locator(`.bloom-page[id="${candidate.id}"]`)
            .getAttribute("data-xmatter-page");
        if (kind === xmatterPage) return;
        seen.push(kind ?? "(none)");
    }
    throw new Error(
        `The book has no "${xmatterPage}" page. Its front and back matter pages: ${seen.join(", ")}.`,
    );
}

/**
 * The branding slot `slot` (its data-book, e.g. "cover-branding-top-html") on the page the Edit tab
 * shows, as a reader sees it. Throws when the page has no such slot.
 */
export async function getBrandingSlot(
    page: Page,
    slot: string,
): Promise<IBrandingSlot> {
    const frame = editablePageFrame(page);
    // The book's data div keeps a hidden copy of each slot's content; the one on the page is the
    // one a reader sees.
    const slotElement = frame.locator(`.bloom-page [data-book="${slot}"]`);
    const count = await slotElement.count();
    if (count !== 1)
        throw new Error(
            `The page being edited has ${count} "${slot}" slots; expected exactly one.`,
        );
    const pictures = (await getPicturesShownIn(slotElement)).map((p) => ({
        file: decodeURIComponent(
            new URL(p.url).pathname.split("/").pop() ?? "",
        ),
        width: p.width,
        height: p.height,
    }));
    const drawn = await slotElement.evaluate((root) => {
        const view = root.ownerDocument.defaultView!;
        const pageBox = root.closest(".bloom-page")!.getBoundingClientRect();
        // Allow for the rounding of fractional pixels at an edge.
        const slack = 1;
        const offThePage: string[] = [];
        const check = (box: DOMRect, what: string) => {
            if (box.width === 0 || box.height === 0) return;
            if (
                box.left < pageBox.left - slack ||
                box.top < pageBox.top - slack ||
                box.right > pageBox.right + slack ||
                box.bottom > pageBox.bottom + slack
            )
                offThePage.push(
                    `${what} is drawn at (${Math.round(box.left - pageBox.left)}, ` +
                        `${Math.round(box.top - pageBox.top)})-(${Math.round(box.right - pageBox.left)}, ` +
                        `${Math.round(box.bottom - pageBox.top)}) on a page ` +
                        `${Math.round(pageBox.width)} by ${Math.round(pageBox.height)}`,
                );
        };
        for (const image of root.querySelectorAll("img"))
            check(
                image.getBoundingClientRect(),
                `the picture ${image.getAttribute("src")}`,
            );
        // Each line of text: a range over a text node gives one box per line it is drawn on.
        const walker = root.ownerDocument.createTreeWalker(
            root,
            view.NodeFilter.SHOW_TEXT,
        );
        for (let node = walker.nextNode(); node; node = walker.nextNode()) {
            const words = node.textContent?.trim();
            if (!words) continue;
            const range = root.ownerDocument.createRange();
            range.selectNodeContents(node);
            for (const box of range.getClientRects())
                check(box, `a line of the text "${words.slice(0, 40)}"`);
        }
        const link = root.querySelector(".bloom-branding-wrapper a");
        return {
            text: (root as HTMLElement).innerText.replace(/\s+/g, " ").trim(),
            badgeLink: link?.getAttribute("href") ?? null,
            offThePage,
        };
    });
    return { ...drawn, pictures };
}
