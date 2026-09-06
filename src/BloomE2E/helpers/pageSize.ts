// Change the book's page size and orientation.
//
// This is SETUP: a test uses it to get a page drawn landscape, or at another size, so that it can
// then ask whether what is on the page still looks right. It therefore goes by the same route the
// Edit tab's size menu posts on, rather than opening that menu: the menu's entries carry no id of
// their own, so clicking one would mean matching the localized words Bloom shows, and the words
// are not the subject. A test whose subject IS the size menu should click the menu instead.

import { expect, type Page } from "@playwright/test";
import { apiGetJson, apiPost } from "./api";
import { waitForEditablePage } from "./bookMaking";

/** One of the sizes Bloom offers for the book, as its layout-choice API describes it. */
interface ILayoutChoice {
    /** The layout id, which is also the class Bloom puts on the page, e.g. "A5Landscape". */
    id: string;
    /** The name the menu shows, localized. */
    label: string;
}

interface ILayoutChoiceData {
    choices: ILayoutChoice[];
    currentLayoutChoiceId: string;
}

/** The sizes this book can be drawn at, and which one it is at now. */
export async function getPageSizeChoices(
    page: Page,
): Promise<ILayoutChoiceData> {
    return apiGetJson<ILayoutChoiceData>(
        page,
        "editView/topBar/layoutChoiceData",
    );
}

/**
 * Draw the book at a page size and orientation, by its layout id, e.g. "A5Portrait" or
 * "A5Landscape", and wait until Bloom has rebuilt the page at it.
 *
 * The change is to the whole book, and Bloom rebuilds the page being edited, so anything a test
 * measured before this call has to be measured again after it.
 */
export async function setPageSize(page: Page, layoutId: string): Promise<void> {
    const data = await getPageSizeChoices(page);
    // The endpoint answers with the sizes this book's template allows, and only those, so being
    // in the list is the whole test of whether a size can be asked for.
    if (!data.choices.some((choice) => choice.id === layoutId))
        throw new Error(
            `This book cannot be drawn at "${layoutId}". It offers: ` +
                `${data.choices.map((c) => c.id).join(", ")}.`,
        );
    await apiPost(
        page,
        "editView/topBar/layoutChoiceChange",
        JSON.stringify({ layoutChoiceId: layoutId }),
        "application/json",
    );
    await expect
        .poll(async () => getPageSize(page), {
            timeout: 60000,
            message: `Asking for "${layoutId}" did not change the page's size.`,
        })
        .toBe(layoutId);
    await waitForEditablePage(page);
}

/**
 * The layout the page being edited is drawn at, as the page div's own class records it: Bloom puts
 * the layout id, e.g. "A5Portrait", on the page element.
 */
export async function getPageSize(page: Page): Promise<string> {
    const frame = page.frame({ name: "page" });
    if (!frame) return "";
    return frame
        .locator(".bloom-page")
        .first()
        .evaluate(
            (element) =>
                [...element.classList].find((c) =>
                    /^[A-Za-z0-9]+(Portrait|Landscape|Square)$/.test(c),
                ) ?? "",
        )
        .catch(() => "");
}
