// Automates "Add And Import Widgets" (Test Case ID 536): in a collection with a subscription
// (Enterprise, which reaches every feature), add both
// kinds of widget page that the Activity template offers ("Widget Page" and "Captive Widget
// Page") and import a widget into each, one from a .wdgt file and one from an .htm file in a
// folder. Bloom copies each widget into the book's activities folder and the page shows it in an
// iframe.
//
// The widget's Choose Widget button opens a native file chooser, which the test pre-answers
// through Bloom's e2e hook (armFileChooser). add-import-widgets-basic.spec.ts checks that a
// collection without a subscription cannot add the pages.

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { addPageFromDialog, openAddPageDialog } from "../helpers/addPageDialog";
import { bookHtmlPath } from "../helpers/bookHtml";
import {
    editablePageFrame,
    getContentPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    getFeatureStatus,
    kEnterpriseSubscriptionCode,
} from "../helpers/collectionSettings";
import { armFileChooser } from "../helpers/talkingBook";

const widgetFixtures = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "../fixtures/widgets",
);
const wdgtWidget = Path.join(widgetFixtures, "counter.wdgt");
const folderWidget = Path.join(widgetFixtures, "greeting", "greeting.htm");

test.use({
    collectionSpec: {
        name: "add-import-widgets",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/**
 * On the widget page being edited, click the widget's Choose Widget button, answering the file
 * chooser it opens with `widgetPath`, and wait until the page shows the widget in an iframe.
 * Returns the iframe's src, which Bloom gives relative to the book's folder.
 */
async function importWidget(page: Page, widgetPath: string): Promise<string> {
    const frame = editablePageFrame(page);
    const container = frame.locator(".bloom-widgetContainer");
    await expect(
        container,
        "test setup: the page being edited should have a widget container",
    ).toHaveCount(1);
    await expect(
        container,
        "A new widget page should start with no widget chosen",
    ).toHaveClass(/bloom-noWidgetSelected/);
    // The button exists only while the mouse is over the container: bloomWidgets.ts adds it on
    // mouseenter, and only once Bloom has said that the collection reaches widgets. A mouse that
    // is already where the hover goes sends no mouseenter, and a hover before that answer adds
    // nothing; so move the mouse off the page and back until the button comes.
    const chooseButton = container.locator(".chooseWidgetButton");
    await expect(async () => {
        await page.mouse.move(0, 0);
        await container.hover();
        await expect(chooseButton).toBeVisible({ timeout: 2000 });
    }, "Hovering over the widget container of a collection with a subscription should show Choose Widget").toPass(
        { timeout: 30000 },
    );
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

/** The text that a fixture widget shows, read from inside the page's widget iframe. */
async function widgetText(page: Page): Promise<string> {
    const widget = editablePageFrame(page).frameLocator(
        ".bloom-widgetContainer iframe",
    );
    const text = widget.locator("#widget-text");
    await text.waitFor({ timeout: 30000 });
    return (await text.innerText()).trim();
}

/** The src of the widget iframe on each page of the saved book, by page id. */
async function savedWidgetSources(
    page: Page,
    bookFolder: string,
): Promise<Record<string, string | null>> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate((source) => {
        const doc = new DOMParser().parseFromString(source, "text/html");
        const result: Record<string, string | null> = {};
        doc.querySelectorAll(".bloom-page").forEach((p) => {
            const container = p.querySelector(".bloom-widgetContainer");
            if (container)
                result[p.id] =
                    container.querySelector("iframe")?.getAttribute("src") ??
                    null;
        });
        return result;
    }, html);
}

test("both kinds of widget page take a widget from a .wdgt file or an .htm in a folder [Test Case ID 536]", async ({
    page,
}) => {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    const widget = await getFeatureStatus(page, "widget");
    expect(
        widget.enabled,
        "Sanity check: an Enterprise collection should reach widgets",
    ).toBe(true);

    await openAddPageDialog(page);
    await addPageFromDialog(page, "Widget Page");
    const widgetPageId = (await getShownPageId(page))!;
    const fromWdgt = await importWidget(page, wdgtWidget);
    expect(fromWdgt).toBe("activities/counter/index.html");
    expect(
        await widgetText(page),
        "The page should show the widget imported from the .wdgt file",
    ).toBe("Counter widget from a wdgt file");

    await openAddPageDialog(page);
    await addPageFromDialog(page, "Captive Widget Page");
    const captivePageId = (await getShownPageId(page))!;
    expect(captivePageId).not.toBe(widgetPageId);
    const fromFolder = await importWidget(page, folderWidget);
    // Bloom zips the folder that holds the chosen .htm, names the widget after the folder, and
    // renames the chosen file index.html (WidgetHelper.CreateWidgetFromHtmlFolder).
    expect(fromFolder).toBe("activities/greeting/index.html");
    expect(
        await widgetText(page),
        "The page should show the widget imported from an .htm in a folder",
    ).toBe("Greeting widget from a folder");

    // Bloom copied every file of each widget into the book.
    for (const file of [
        "activities/counter/index.html",
        "activities/counter/counter.js",
        "activities/greeting/index.html",
        "activities/greeting/greeting.css",
    ])
        expect(
            fs.existsSync(Path.join(bookFolder, file)),
            `The book should have ${file}`,
        ).toBe(true);

    // Leaving a page saves it; come back to the first widget page, which saves the second.
    await goToPage(page, widgetPageId);
    expect(
        await widgetText(page),
        "The first widget page should still show its widget after the book moved off it",
    ).toBe("Counter widget from a wdgt file");
    await expect
        .poll(() => savedWidgetSources(page, bookFolder), {
            message: "The saved book should keep each page's widget",
            timeout: 30000,
        })
        .toEqual({
            [widgetPageId]: "activities/counter/index.html",
            [captivePageId]: "activities/greeting/index.html",
        });
    expect(
        (await getContentPages(page)).map((p) => p.id),
        "Both widget pages should be in the book",
    ).toEqual(expect.arrayContaining([widgetPageId, captivePageId]));
});
