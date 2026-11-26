// Widgets: the HTML5 activities that a Widget Page or Captive Widget Page (Activity template), or
// an HTML Widget section of a custom page, shows in an iframe. A person picks one with the Choose
// Widget button that bloomWidgets.ts puts on the widget container while the mouse is over it;
// Bloom copies the widget into the book's activities folder (WidgetHelper.cs) and points the
// iframe at it.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, type Page } from "@playwright/test";
import { bookHtmlPath } from "./bookHtml";
import { editablePageFrame } from "./bookMaking";
import { armFileChooser } from "./talkingBook";

const kWidgetContainer = ".bloom-widgetContainer";

/**
 * On the widget page being edited, click the Choose Widget button the way a person does,
 * answering the file chooser it opens with `widgetPath` (a .wdgt file, or an .htm or .html file
 * whose folder holds the rest of the widget), and wait until the page shows the widget. Returns
 * the iframe's src, which Bloom gives relative to the book's folder.
 *
 * Fails when the page has no widget container, or one that already has a widget.
 */
export async function importWidget(
    page: Page,
    widgetPath: string,
): Promise<string> {
    const container = editablePageFrame(page).locator(kWidgetContainer);
    await expect(
        container,
        "The page being edited should have one widget container",
    ).toHaveCount(1);
    await expect(
        container,
        "The widget container should start with no widget chosen",
    ).toHaveClass(/bloom-noWidgetSelected/);
    // bloomWidgets.ts adds the button on mouseenter, and only once Bloom has said that the
    // collection reaches widgets. A mouse that is already where the hover goes sends no
    // mouseenter, and a hover before that answer adds nothing; so move the mouse off the page and
    // back until the button comes.
    const chooseButton = container.locator(".chooseWidgetButton");
    await expect(async () => {
        await page.mouse.move(0, 0);
        await container.hover();
        await expect(chooseButton).toBeVisible({ timeout: 2000 });
    }, "Hovering over the widget container should show Choose Widget").toPass({
        timeout: 30000,
    });
    await armFileChooser(page, widgetPath);
    await chooseButton.click();
    const iframe = container.locator("iframe");
    await expect(
        iframe,
        `Choosing ${Path.basename(widgetPath)} never put the widget on the page.`,
    ).toHaveAttribute("src", /\S/, { timeout: 30000 });
    await expect(container).not.toHaveClass(/bloom-noWidgetSelected/);
    return (await iframe.getAttribute("src"))!;
}

/**
 * The text that the widget on the page being edited shows, read from inside its iframe once the
 * widget has loaded.
 */
export async function getWidgetText(page: Page): Promise<string> {
    const body = editablePageFrame(page)
        .frameLocator(`${kWidgetContainer} iframe`)
        .locator("body");
    await expect(
        body,
        "The widget on the page never showed anything",
    ).not.toBeEmpty({ timeout: 30000 });
    return (await body.innerText()).trim();
}

/**
 * The widget that each page of the book in `bookFolder` has, as saved on disk: the src of its
 * widget iframe, or null for a widget container with no widget chosen, by page id. Pages with no
 * widget container are left out. `page` is used only as a DOM parser.
 */
export async function readSavedWidgetSources(
    page: Page,
    bookFolder: string,
): Promise<Record<string, string | null>> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate(
        ({ source, containerSelector }) => {
            const doc = new DOMParser().parseFromString(source, "text/html");
            const result: Record<string, string | null> = {};
            doc.querySelectorAll(".bloom-page").forEach((p) => {
                const container = p.querySelector(containerSelector);
                if (container)
                    result[p.id] =
                        container
                            .querySelector("iframe")
                            ?.getAttribute("src") ?? null;
            });
            return result;
        },
        { source: html, containerSelector: kWidgetContainer },
    );
}
