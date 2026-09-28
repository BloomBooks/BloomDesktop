// Change the book's settings, the ones the Book Settings dialog edits (theme, margins, cover color
// and so on, which Bloom keeps in the book's appearance.json).
//
// This is SETUP: a test uses it to get a book into a state, then measures something else. It posts
// the same settings object the dialog posts, read back from Bloom and changed in one field, rather
// than driving the dialog, whose controls carry no ids of their own. A test whose subject IS the
// dialog should drive the dialog instead.

import { expect, type Page } from "@playwright/test";
import { apiGetJson, apiPost } from "./api";
import { waitForEditablePage } from "./bookMaking";

/** What book/settings replies with. Only the part used here is typed. */
interface IBookSettings {
    appearance: Record<string, unknown> & { cssThemeName: string };
    [key: string]: unknown;
}

/** The theme the Book Settings dialog shows as chosen for the book, e.g. "default". */
export async function getBookTheme(page: Page): Promise<string> {
    return (await apiGetJson<IBookSettings>(page, "book/settings")).appearance
        .cssThemeName;
}

/**
 * Choose a theme for the book being edited, by its name, e.g. "rounded-border-ebook", as the
 * Book Settings dialog would, and wait until Bloom reports it and has redrawn the page. The Edit
 * tab must be showing the book.
 */
export async function setBookTheme(page: Page, theme: string): Promise<void> {
    const settings = await apiGetJson<IBookSettings>(page, "book/settings");
    if (settings.appearance.cssThemeName === theme)
        throw new Error(
            `The book already has the theme "${theme}", so choosing it changes nothing.`,
        );
    settings.appearance.cssThemeName = theme;
    await apiPost(
        page,
        "book/settings",
        JSON.stringify(settings),
        "application/json",
    );
    await expect
        .poll(() => getBookTheme(page), {
            timeout: 30000,
            message: `Bloom did not take the theme "${theme}".`,
        })
        .toBe(theme);
    await waitForEditablePage(page);
}
