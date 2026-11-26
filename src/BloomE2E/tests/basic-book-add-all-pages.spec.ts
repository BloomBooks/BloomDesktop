// A Basic Book can take every kind of page Bloom comes with. The Add Page dialog of a Basic Book
// offers the Basic Book's own pages first and then the pages of the other templates installed with
// Bloom (Arithmetic, Comic, Games, Sign Language, Special); this test adds each of them in turn
// through the dialog, and then checks that the saved book holds one page made from each. What the dialog must offer is read
// from the template files on disk.
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
// The folders of the templates installed with Bloom whose pages Add Page offers to a Basic Book.
// Activity and Games both show under "Games".
const INSTALLED_TEMPLATES_WITH_PAGES = [
    BASIC_BOOK,
    "Activity",
    "Arithmetic Template",
    "Digital Comic Book",
    "Games",
    "Paper Comic Book",
    "Sign Language",
    "Special",
];

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
            const groups = await getAddPageDialogGroups(page);
            // Go by each page's own template folder, not its group's: the dialog groups templates
            // by title, so a downloaded template that shares a title with an installed one has its
            // pages in the same group.
            const offered: IOfferedPage[] = [];
            for (const group of groups)
                for (const offeredPage of group.pages)
                    if (
                        isInsideFolder(
                            offeredPage.templateBookFolder,
                            installedTemplatesFolder,
                        )
                    )
                        offered.push({
                            label: offeredPage.label,
                            templateBookFolder: offeredPage.templateBookFolder,
                            groupFolderName: Path.basename(
                                group.templateBookFolder,
                            ),
                            templatePageId: "",
                        });
            // The oracle is the template files on disk: each template the dialog shows must have
            // every one of its template pages for a book of this orientation on offer. Without this
            // the test would pass when the dialog stopped offering a page.
            const describeOffered = (pages: { label: string }[]) =>
                pages.map((p) => oneLine(p.label));
            const expectedByTemplate: Record<string, string[]> = {};
            const offeredByTemplate: Record<string, string[]> = {};
            for (const folder of new Set(
                offered.map((o) => o.templateBookFolder),
            )) {
                const templatePages = (
                    await readPagesOnDisk(page, folder)
                ).filter(
                    (p) =>
                        p.isTemplatePage &&
                        ["", orientation].includes(p.onlyForOrientation),
                );
                const offeredFromIt = offered.filter(
                    (o) => o.templateBookFolder === folder,
                );
                const folderName = Path.basename(folder);
                expectedByTemplate[folderName] = describeOffered(templatePages);
                offeredByTemplate[folderName] = describeOffered(offeredFromIt);
                // The dialog shows a template's pages in the file's order, so the two lists pair
                // up; the comparison below fails when they do not.
                offeredFromIt.forEach((o, i) => {
                    o.templatePageId = templatePages[i]?.id ?? "";
                });
            }
            expect(
                offeredByTemplate,
                "Add Page did not offer exactly the template pages that the files of the templates it shows hold",
            ).toEqual(expectedByTemplate);
            // The files cannot say which templates the dialog should show at all, so name them.
            expect(
                Object.keys(offeredByTemplate),
                "Add Page did not offer pages from every template installed with Bloom that has pages to add",
            ).toEqual(expect.arrayContaining(INSTALLED_TEMPLATES_WITH_PAGES));
            // The book's own template comes first: every one of the Basic Book's pages is in the
            // first group. A template of the person's own with the same title would share that
            // group, so look only at the pages from the templates installed with Bloom.
            expect(
                groups[0].pages
                    .filter((p) =>
                        isInsideFolder(
                            p.templateBookFolder,
                            installedTemplatesFolder,
                        ),
                    )
                    .map(
                        (p) =>
                            `${Path.basename(p.templateBookFolder)}: ${oneLine(p.label)}`,
                    ),
                `the first group in Add Page does not hold exactly ${BASIC_BOOK}'s pages`,
            ).toEqual(
                expectedByTemplate[BASIC_BOOK].map(
                    (label) => `${BASIC_BOOK}: ${label}`,
                ),
            );
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
