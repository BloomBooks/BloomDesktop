// Automates "Add And Import Widgets" (Test Case ID 536): in a collection with a subscription
// (Enterprise, which reaches every feature), add both kinds of widget page that the Activity
// template offers ("Widget Page" and "Captive Widget Page") and import a widget into each, one
// from a .wdgt file and one from an .htm file in a folder. Bloom copies each widget into the
// book's activities folder and the page shows it in an iframe.
//
// The widget's Choose Widget button opens a native file chooser, which importWidget pre-answers
// through Bloom's e2e hook. add-import-widgets-basic.spec.ts checks that a collection without a
// subscription cannot add the pages.

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import { addPageFromDialog, openAddPageDialog } from "../helpers/addPageDialog";
import {
    getContentPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    getFeatureStatus,
    kEnterpriseSubscriptionCode,
} from "../helpers/collectionSettings";
import {
    getWidgetText,
    importWidget,
    readSavedWidgetSources,
} from "../helpers/widgets";

const widgetFixtures = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "../fixtures/widgets",
);
const wdgtWidget = Path.join(widgetFixtures, "counter.wdgt");
const folderWidget = Path.join(widgetFixtures, "greeting", "greeting.htm");
const kWdgtWidgetText = "Counter widget from a wdgt file";
const kFolderWidgetText = "Greeting widget from a folder";

test.use({
    collectionSpec: {
        name: "add-import-widgets",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

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
        await getWidgetText(page),
        "The page should show the widget imported from the .wdgt file",
    ).toContain(kWdgtWidgetText);

    await openAddPageDialog(page);
    await addPageFromDialog(page, "Captive Widget Page");
    const captivePageId = (await getShownPageId(page))!;
    expect(captivePageId).not.toBe(widgetPageId);
    const fromFolder = await importWidget(page, folderWidget);
    // Bloom zips the folder that holds the chosen .htm, names the widget after the folder, and
    // renames the chosen file index.html (WidgetHelper.CreateWidgetFromHtmlFolder).
    expect(fromFolder).toBe("activities/greeting/index.html");
    expect(
        await getWidgetText(page),
        "The page should show the widget imported from an .htm in a folder",
    ).toContain(kFolderWidgetText);

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
        await getWidgetText(page),
        "The first widget page should still show its widget after the book moved off it",
    ).toContain(kWdgtWidgetText);
    await expect
        .poll(() => readSavedWidgetSources(page, bookFolder), {
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
