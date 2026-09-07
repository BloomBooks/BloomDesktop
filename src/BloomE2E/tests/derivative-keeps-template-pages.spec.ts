// A derivative of a book that was made from somebody else's custom template. That template is not
// on this machine, so the Add Page dialog cannot show its pages; what it must still do is show the
// standard Bloom templates, whole and unchanged, and it must not list the custom template's pages.
// Automates the manual test "Derivative Keeps Template Pages".
//
// The "other user" is played out inside one collection: the test makes a custom template, makes a
// book from it, then quits Bloom, deletes the template, and starts again. The book is now in the
// same state as a downloaded book whose template lives only on its author's machine. A derivative
// made from it is the book under test. The manual test opens the dialog, scrolls it, adds a page
// and repeats; this test does that three times, reading the whole dialog each round, then opens the
// dialog once more to show the list survived the adds, and closes it.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import type { Page } from "@playwright/test";
import {
    addPageFromDialog,
    closeAddPageDialog,
    getAddPageDialogGroups,
    openAddPageDialog,
    scrollAddPageDialog,
} from "../helpers/addPageDialog";
import {
    addPage,
    findBookFolder,
    getContentPages,
    getFactoryTemplateFolder,
    getFactoryTemplatePageLabels,
    getPages,
    goToPage,
    makeBookFromBookInCollection,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { fingerprintFolder, isInsideFolder } from "../helpers/files";

test.use({
    collectionSpec: { name: "derivative-template-pages", languages: ["en"] },
});

// The custom template's title, which is also its folder name and the Add Page dialog's heading
// for its pages. Template Starter names every new template "My Template", and a developer's machine
// can hold a downloaded template of that name, so the test gives its template a name of its own.
const CUSTOM_TEMPLATE_TITLE = "TC72 Custom Template";
// The book the "other user" made from the custom template; the derivative is made from this.
const SHELL_TITLE = "Book From Custom Template";
const STANDARD_TEMPLATE = "Basic Book";
// The standard page each round adds through the dialog.
const PAGE_TO_ADD = "Just Text";
// How many times the dialog is opened, scrolled, read and used, as the manual test's "repeat".
const ROUNDS = 3;
// How many times each round scrolls to the bottom and back.
const SCROLLS_PER_ROUND = 3;

/**
 * Read the whole Add Page dialog and check the two things the manual test is about: the standard
 * template's pages are all there, in the template file's own order, each with a thumbnail; and no
 * page of any book in this collection is offered, which is where the missing custom template's
 * pages would show up if Bloom went looking for them. `round` only names the round in a failure.
 */
async function expectOnlyStandardPagesOffered(
    page: Page,
    round: string,
    expectedLabels: string[],
    collectionDir: string,
): Promise<void> {
    const groups = await getAddPageDialogGroups(page);
    const describeGroups = groups
        .map((g) => `${g.title} (${g.templateBookFolder})`)
        .join(", ");

    const standard = groups.find(
        (g) => Path.basename(g.templateBookFolder) === STANDARD_TEMPLATE,
    );
    expect(
        standard,
        `${round}: the dialog showed no "${STANDARD_TEMPLATE}" group. Groups: ${describeGroups}`,
    ).toBeDefined();
    expect(
        standard!.pages.map((p) => p.label),
        `${round}: the "${STANDARD_TEMPLATE}" group did not offer the pages its template file holds`,
    ).toEqual(expectedLabels);
    expect(
        standard!.pages.filter((p) => !p.thumbnailLoaded).map((p) => p.label),
        `${round}: thumbnails that did not load`,
    ).toEqual([]);

    expect(
        groups.map((g) => g.title),
        `${round}: the dialog headed a group with the missing custom template's name`,
    ).not.toContain(CUSTOM_TEMPLATE_TITLE);
    expect(
        groups.map((g) => Path.basename(g.templateBookFolder)),
        `${round}: the dialog offered pages from the missing custom template's folder`,
    ).not.toContain(CUSTOM_TEMPLATE_TITLE);
    expect(
        groups
            .flatMap((g) => g.pages)
            .filter((p) => isInsideFolder(p.templateBookFolder, collectionDir))
            .map((p) => `${p.label} from ${p.templateBookFolder}`),
        `${round}: pages offered from books in this collection`,
    ).toEqual([]);
}

test("a derivative whose custom template is missing still offers the standard template pages, and not the custom ones [Test Case ID 72]", async ({
    page,
    bloomApp,
}) => {
    test.setTimeout(600000);

    // The custom template, with two pages of its own. A fresh template has no pages, so these
    // come from Basic Book; once added they are the template's own pages.
    await makeBookFromTemplate(page, "Template Starter");
    // The template's title is the "Template Title" box of its "About" page. Leaving that page saves
    // it, and Bloom then renames the folder to match; wait for that before adding pages, so the
    // rename does not land in the middle of an add.
    const templatePages = await getPages(page);
    const aboutPage = templatePages.find((p) => p.caption === "About");
    expect(
        aboutPage,
        `Template Starter made a book with no "About" page. Its pages: ` +
            templatePages.map((p) => p.caption).join(", "),
    ).toBeDefined();
    await goToPage(page, aboutPage!.id);
    await typeInGroup(page, ".title", "en", CUSTOM_TEMPLATE_TITLE);
    // Leaving the About page is what writes the title, so go back to the first page.
    await goToPage(page, templatePages[0].id);
    const templateFolder = await findBookFolder(page, CUSTOM_TEMPLATE_TITLE);
    expect(
        Path.basename(templateFolder),
        "Bloom did not rename the template's folder to match its new title",
    ).toBe(CUSTOM_TEMPLATE_TITLE);
    await addPage(page, "Basic Text & Image", 1, STANDARD_TEMPLATE);
    await addPage(page, PAGE_TO_ADD, 1, STANDARD_TEMPLATE);
    expect(
        (await getContentPages(page)).length,
        "the custom template did not end up with the two pages of its own",
    ).toBe(2);

    // The book made from that template, as the other user would have made it: a title, and a
    // page taken from the custom template.
    await makeBookFromBookInCollection(page, templateFolder);
    await typeInGroup(page, ".bookTitle", "en", SHELL_TITLE);
    await addPage(page, "Basic Text & Image", 1, CUSTOM_TEMPLATE_TITLE);
    expect(
        (await getContentPages(page)).length,
        "the book made from the custom template did not get its one page",
    ).toBe(1);
    // Leave the page so it is written before Bloom is stopped.
    const cover = (await getPages(page)).find((p) => !p.isContentPage);
    expect(
        cover,
        "the book has no front or back matter page to move to",
    ).toBeDefined();
    await goToPage(page, cover!.id);

    // Now the template exists only on the other user's machine.
    const afterRestart = await bloomApp.restart(() =>
        fs.rmSync(templateFolder, { recursive: true, force: true }),
    );
    expect(
        fs.existsSync(templateFolder),
        "the custom template's folder is still on disk",
    ).toBe(false);
    // Bloom renamed the shell's folder to match its title, so ask rather than remember.
    const shellFolder = await findBookFolder(afterRestart, SHELL_TITLE);

    // The derivative: the book under test. It carries the shell's page and the shell's record of
    // which template it came from.
    await makeBookFromBookInCollection(afterRestart, shellFolder);
    let expectedContentPages = 1;
    expect(
        (await getContentPages(afterRestart)).length,
        "the derivative did not carry over the page of the book it was made from",
    ).toBe(expectedContentPages);

    // What the standard template's pages are, from the file on disk, and what the files are now,
    // so the end of the test can show nothing touched them.
    const standardTemplateFolder = await getFactoryTemplateFolder(
        afterRestart,
        STANDARD_TEMPLATE,
    );
    const expectedLabels = await getFactoryTemplatePageLabels(
        afterRestart,
        STANDARD_TEMPLATE,
    );
    expect(
        expectedLabels.length,
        `"${STANDARD_TEMPLATE}" offered too few template pages for this test to mean anything`,
    ).toBeGreaterThan(5);
    const filesBefore = fingerprintFolder(standardTemplateFolder);

    for (let round = 1; round <= ROUNDS; round++) {
        await openAddPageDialog(afterRestart);
        const scrolled = await scrollAddPageDialog(
            afterRestart,
            SCROLLS_PER_ROUND,
        );
        expect(
            scrolled,
            `Round ${round}: every group fit in the dialog, so scrolling it tested nothing`,
        ).toBeGreaterThan(0);
        await expectOnlyStandardPagesOffered(
            afterRestart,
            `Round ${round}`,
            expectedLabels,
            bloomApp.collectionDir,
        );

        // Adding a standard page is what the pages are for, and it is the step the manual test
        // repeats. It also closes the dialog.
        await addPageFromDialog(afterRestart, PAGE_TO_ADD, STANDARD_TEMPLATE);
        expectedContentPages++;
        expect(
            (await getContentPages(afterRestart)).length,
            `Round ${round}: the page added through the dialog did not reach the book`,
        ).toBe(expectedContentPages);
    }

    // The dialog still offers the same pages after all that, and closes with its Close button.
    await openAddPageDialog(afterRestart);
    await expectOnlyStandardPagesOffered(
        afterRestart,
        "After the adds",
        expectedLabels,
        bloomApp.collectionDir,
    );
    await closeAddPageDialog(afterRestart);

    // Nothing in the standard template changed on disk.
    expect(
        fingerprintFolder(standardTemplateFolder),
        `Bloom changed files in the "${STANDARD_TEMPLATE}" template folder`,
    ).toEqual(filesBefore);
});
