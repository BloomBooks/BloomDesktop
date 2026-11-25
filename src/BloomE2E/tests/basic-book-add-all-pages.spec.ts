// A Basic Book can take every kind of page Bloom comes with. The Add Page dialog of a Basic Book
// offers the Basic Book's own pages first and then the pages of the other templates installed with
// Bloom (Arithmetic, Comic, Games, Sign Language, Special); this test adds each of them in turn
// through the dialog, and then checks that the saved book holds one page made from each.
// Automates the manual test "Basic Book Add All Pages" (Test Case ID 59).
//
// The dialog also lists templates the person has downloaded or made, which differ from machine to
// machine, so the test adds only the pages of the templates in Bloom's own installed folder.

import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPageFromDialog,
    getAddPageDialogGroups,
    openAddPageDialog,
} from "../helpers/addPageDialog";
import { readPagesOnDisk } from "../helpers/bookHtml";
import {
    getFactoryTemplateFolder,
    getFactoryTemplatePageLabels,
    getPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { isInsideFolder } from "../helpers/files";
import { getPageSize } from "../helpers/pageSize";

// Canvas, the comic pages, the games and the book grid page need a subscription; without one the
// dialog shows a notice in place of its Add Page button for them.
test.use({
    collectionSpec: {
        name: "basic-book-add-all-pages",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const BASIC_BOOK = "Basic Book";

/** One page the dialog offers from a template installed with Bloom. */
interface IOfferedPage {
    label: string;
    /** The folder of the template book that holds the page. */
    templateBookFolder: string;
    /**
     * The folder name the dialog knows the page's group by. Two templates of one title share a
     * group (Activity and Games are both "Games"), and the group goes by the first one's folder.
     */
    groupFolderName: string;
    /** The id of the template page in the template's own file. */
    templatePageId: string;
}

/** A page the test added: what it was made from, and the id Bloom gave it. */
interface IAddedPage extends IOfferedPage {
    id: string;
}

/** A page label with its line breaks and indentation reduced to single spaces. */
function oneLine(label: string): string {
    return label.replace(/\s+/g, " ").trim();
}

/** Name an offered page for a step title or a failure: its label and its template's folder. */
function describe(offered: IOfferedPage): string {
    return `"${oneLine(offered.label)}" (${Path.basename(offered.templateBookFolder)})`;
}

test("every page of every template installed with Bloom can be added to a Basic Book [Test Case ID 59]", async ({
    page,
    step,
}) => {
    test.setTimeout(900000);

    const bookFolder = await step("Make a Basic Book", () =>
        makeBookFromTemplate(page, BASIC_BOOK),
    );
    if (!bookFolder) throw new Error("No book was made.");

    const pagesToAdd = await step(
        "Open Add Page and read the pages it offers",
        async () => {
            const basicBookFolder = await getFactoryTemplateFolder(
                page,
                BASIC_BOOK,
            );
            const installedTemplatesFolder = Path.dirname(basicBookFolder);
            // A template can hold a portrait and a landscape page under one label; the dialog
            // offers the one for this book.
            const orientation = (await getPageSize(page)).endsWith("Landscape")
                ? "landscape"
                : "portrait";
            await openAddPageDialog(page);
            const groups = (await getAddPageDialogGroups(page)).filter((g) =>
                isInsideFolder(g.templateBookFolder, installedTemplatesFolder),
            );
            const describeGroups = groups
                .map(
                    (g) =>
                        `${Path.basename(g.templateBookFolder)}: ` +
                        g.pages.map((p) => oneLine(p.label)).join(", "),
                )
                .join("; ");

            // Sanity checks, so that a dialog that offered little could not pass this test: the
            // Basic Book's own pages come first and are all there, and other templates follow.
            expect(
                Path.basename(groups[0].templateBookFolder),
                `the first group in Add Page is not ${BASIC_BOOK}'s. Groups: ${describeGroups}`,
            ).toBe(BASIC_BOOK);
            expect(
                groups[0].pages.map((p) => p.label),
                `the ${BASIC_BOOK} group did not offer the pages its template file holds`,
            ).toEqual(await getFactoryTemplatePageLabels(page, BASIC_BOOK));
            expect(
                groups.length,
                `Add Page offered pages from too few installed templates. Groups: ${describeGroups}`,
            ).toBeGreaterThan(3);

            const offered: IOfferedPage[] = [];
            for (const group of groups) {
                for (const offeredPage of group.pages) {
                    const templatePages = await readPagesOnDisk(
                        page,
                        offeredPage.templateBookFolder,
                    );
                    const matches = templatePages.filter(
                        (p) =>
                            p.isTemplatePage &&
                            oneLine(p.label) === oneLine(offeredPage.label) &&
                            ["", orientation].includes(p.onlyForOrientation),
                    );
                    expect(
                        matches.map((p) => p.id).length,
                        `the template in ${offeredPage.templateBookFolder} should hold exactly one template ` +
                            `page labelled "${oneLine(offeredPage.label)}" for a ${orientation} book. Its pages: ` +
                            templatePages
                                .map((p) => oneLine(p.label))
                                .join(", "),
                    ).toBe(1);
                    offered.push({
                        label: offeredPage.label,
                        templateBookFolder: offeredPage.templateBookFolder,
                        groupFolderName: Path.basename(
                            group.templateBookFolder,
                        ),
                        templatePageId: matches[0].id,
                    });
                }
            }
            return offered;
        },
    );
    if (!pagesToAdd) throw new Error("No pages were read from Add Page.");

    const added: IAddedPage[] = [];
    for (const [index, offered] of pagesToAdd.entries()) {
        await step(`Add ${describe(offered)}`, async () => {
            const before = await getPages(page);
            // The dialog is already open for the first page; each add closes it.
            if (index > 0) await openAddPageDialog(page);
            await addPageFromDialog(
                page,
                offered.label,
                offered.groupFolderName,
            );
            const newPages = (await getPages(page)).filter(
                (p) => !before.some((b) => b.id === p.id),
            );
            expect(
                newPages.length,
                `adding ${describe(offered)} should give the book one new page`,
            ).toBe(1);
            expect(
                await getShownPageId(page),
                `after adding ${describe(offered)}, Bloom is not showing the new page`,
            ).toBe(newPages[0].id);
            added.push({ ...offered, id: newPages[0].id });
        });
    }

    await step("Leave the last page, so that Bloom saves it", async () => {
        const cover = (await getPages(page))[0];
        await goToPage(page, cover.id);
    });

    await step(
        "Check the saved book holds a page made from each one, in the order added",
        async () => {
            // A page Bloom adds records, last in its lineage, the id of the template page it was
            // made from. Each page went in after the one before it, so the book has them in the
            // order they were added.
            const expected = added.map(
                (a) => `${a.id} from ${a.templatePageId} ${describe(a)}`,
            );
            await expect
                .poll(
                    async () => {
                        const saved = await readPagesOnDisk(page, bookFolder);
                        return saved
                            .filter((p) => added.some((a) => a.id === p.id))
                            .map((p) => {
                                const from = added.find((a) => a.id === p.id)!;
                                return `${p.id} from ${p.lineage[p.lineage.length - 1]} ${describe(from)}`;
                            });
                    },
                    {
                        timeout: 60000,
                        message:
                            "the saved book never held the added pages, each made from its template page",
                    },
                )
                .toEqual(expected);
        },
    );
});
