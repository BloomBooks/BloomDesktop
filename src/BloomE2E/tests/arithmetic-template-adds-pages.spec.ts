// A book made from the Arithmetic Template takes every page that template offers. Its Add Page
// dialog lists the Arithmetic pages first; the test adds each of them through the dialog, checks
// that each new page has the problems of the template page it came from, and then uses every box
// on it: a heading, a picture in each picture box, and an equation typed into each problem's three
// equation boxes, reaching each box with Tab. Last it checks that the saved book holds all of it.
// Which pages there are, and what each holds, is read from the template's own file.
//
// Automates the manual test "Arithmetic Template Adds Pages" (Test Case ID 58). The manual case's
// "work correctly" is read here as: each page has its template page's problems, and every heading,
// picture box and equation box on it takes what is put in it, keeps it on saving, and shows no
// overflow. Typing into an equation box reached with Tab once put the text outside the box's
// paragraph and marked the box as overflowing (BL-9012); the test checks for that too.
//
// Not checked: the case's first step, "Turn on Performance Measuring for all tests and monitor".
// It asks the tester to watch Bloom's speed and memory while working, which has no pass or fail.

import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPageFromDialog,
    getAddPageDialogGroups,
    openAddPageDialog,
} from "../helpers/addPageDialog";
import {
    arithmeticProblems,
    getArithmeticLayoutShown,
    getEquationBoxText,
    kArithmeticHeadingGroup,
    problemEquationBoxes,
    problemPictureBoxes,
    readArithmeticLayoutsOnDisk,
    readSavedArithmeticPages,
    tabInto,
    type ISavedArithmeticPage,
} from "../helpers/arithmeticPages";
import { readPagesOnDisk, type IPageOnDisk } from "../helpers/bookHtml";
import {
    clickInGroup,
    getContentPages,
    getFactoryTemplateFolder,
    getPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForSnapshotStreamIdle,
} from "../helpers/bookMaking";
import { isInsideFolder } from "../helpers/files";
import {
    chooseImageFile,
    getImagePlacement,
    makeJpegFile,
} from "../helpers/images";
import { typeWithKeys } from "../helpers/keys";
import { expectNoOverflowMarks } from "../helpers/overflowMarks";
import { getPageSize } from "../helpers/pageSize";
import { setUiStateViaApi } from "../helpers/uiLanguage";

const kArithmeticTemplate = "Arithmetic Template";
const kLanguage = "en";

test.use({
    collectionSpec: {
        name: "arithmetic-template-adds-pages",
        languages: [kLanguage],
    },
});

/** A page the test added, and what it put on it. */
interface IAddedPage {
    templatePage: IPageOnDisk;
    id: string;
    saved: ISavedArithmeticPage;
}

test("a book made from the Arithmetic Template offers its pages first, and each one adds and takes its heading, pictures and equations [Test Case ID 58]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(900000);

    // The Add Page helpers find the dialog and its buttons by their English names.
    await step("Show Bloom in English", () =>
        setUiStateViaApi(bloomApp, "en", false),
    );
    const page = bloomApp.page;

    const bookFolder = await step(
        "Make a book from the Arithmetic Template",
        () => makeBookFromTemplate(page, kArithmeticTemplate),
    );
    if (!bookFolder) throw new Error("No book was made.");

    const template = await step(
        "Read the template's pages and their problems from its file",
        async () => {
            const folder = await getFactoryTemplateFolder(
                page,
                kArithmeticTemplate,
            );
            const orientation = (await getPageSize(page)).endsWith("Landscape")
                ? "landscape"
                : "portrait";
            const pages = (await readPagesOnDisk(page, folder)).filter(
                (p) =>
                    p.isTemplatePage &&
                    ["", orientation].includes(p.onlyForOrientation),
            );
            expect(
                pages.length,
                "sanity check: the Arithmetic Template should offer several pages",
            ).toBeGreaterThan(1);
            const layouts = await readArithmeticLayoutsOnDisk(page, folder);
            for (const p of pages)
                expect(
                    layouts[p.id]?.length ?? 0,
                    `sanity check: the template's "${p.label}" page should have problems`,
                ).toBeGreaterThan(0);
            expect(
                await getContentPages(page),
                "sanity check: a book made from a template starts with no content page",
            ).toEqual([]);
            return { folder, pages, layouts };
        },
    );
    if (!template) throw new Error("The template was not read.");

    await step(
        "Open Add Page: the Arithmetic pages are the first group, in the template's order",
        async () => {
            await openAddPageDialog(page);
            const groups = await getAddPageDialogGroups(page);
            expect(
                Path.basename(groups[0].templateBookFolder),
                "the first group in Add Page should be the Arithmetic Template's",
            ).toBe(kArithmeticTemplate);
            // A template of the person's own with the same title would share that group, so look
            // only at the pages from the template installed with Bloom.
            expect(
                groups[0].pages
                    .filter((p) =>
                        isInsideFolder(p.templateBookFolder, template.folder),
                    )
                    .map((p) => p.label),
                "the first group in Add Page should hold exactly the Arithmetic Template's pages",
            ).toEqual(template.pages.map((p) => p.label));
            // And no group after it offers the template's pages again.
            expect(
                groups
                    .slice(1)
                    .filter((g) =>
                        g.pages.some((p) =>
                            isInsideFolder(
                                p.templateBookFolder,
                                template.folder,
                            ),
                        ),
                    )
                    .map((g) => g.title),
                "no later group in Add Page should offer the Arithmetic Template's pages",
            ).toEqual([]);
        },
    );

    const added: IAddedPage[] = [];
    for (const [pageIndex, templatePage] of template.pages.entries()) {
        const expectedLayout = template.layouts[templatePage.id];
        await step(
            `Add a "${templatePage.label}" page: it has the template page's problems`,
            async () => {
                const before = await getPages(page);
                // The dialog is already open for the first page; each add closes it.
                if (pageIndex > 0) await openAddPageDialog(page);
                await addPageFromDialog(
                    page,
                    templatePage.label,
                    kArithmeticTemplate,
                );
                const newPages = (await getPages(page)).filter(
                    (p) => !before.some((b) => b.id === p.id),
                );
                expect(
                    newPages.length,
                    `adding a "${templatePage.label}" page should give the book one new page`,
                ).toBe(1);
                const id = newPages[0].id;
                expect(
                    await getShownPageId(page),
                    `after adding a "${templatePage.label}" page, Bloom is not showing it`,
                ).toBe(id);
                expect(
                    await getArithmeticLayoutShown(page),
                    `the new "${templatePage.label}" page should have the template page's problems`,
                ).toEqual(expectedLayout);
                added.push({
                    templatePage,
                    id,
                    saved: { heading: "", problems: [] },
                });
            },
        );

        const addedPage = added[added.length - 1];
        await step(
            `Type a heading, and an equation in each problem, reaching each equation box with Tab`,
            async () => {
                const heading = `Sums ${pageIndex + 1}`;
                await typeInGroup(
                    page,
                    kArithmeticHeadingGroup,
                    kLanguage,
                    heading,
                );
                addedPage.saved.heading = heading;
                // Start from the heading, as a person would, and Tab through the page from there.
                await clickInGroup(page, kArithmeticHeadingGroup, kLanguage);
                const problems = arithmeticProblems(page);
                for (let i = 0; i < expectedLayout.length; i++) {
                    const boxes = problemEquationBoxes(problems.nth(i));
                    // Different numbers on every page and problem, so a swap cannot pass unseen.
                    const equation = [
                        `${pageIndex + 1}${i + 1}`,
                        "+",
                        `${i + 2}`,
                    ];
                    for (const [j, text] of equation.entries()) {
                        await tabInto(
                            page,
                            boxes.nth(j),
                            `equation box ${j + 1} of problem ${i + 1}`,
                        );
                        await typeWithKeys(page, text);
                    }
                    addedPage.saved.problems.push({
                        pictures: [],
                        equations: equation,
                    });
                }
                await waitForSnapshotStreamIdle(page);
                for (let i = 0; i < expectedLayout.length; i++) {
                    const boxes = problemEquationBoxes(problems.nth(i));
                    for (const [j, text] of addedPage.saved.problems[
                        i
                    ].equations.entries())
                        expect(
                            await getEquationBoxText(boxes.nth(j)),
                            `equation box ${j + 1} of problem ${i + 1} should hold "${text}" in its one paragraph`,
                        ).toEqual({
                            paragraphs: [text],
                            textOutsideParagraphs: "",
                        });
                }
                await expectNoOverflowMarks(
                    page,
                    `the "${templatePage.label}" page should show no overflow after typing its equations`,
                );
            },
        );

        await step(
            `Put a picture in each picture box of the "${templatePage.label}" page`,
            async () => {
                const problems = arithmeticProblems(page);
                for (let i = 0; i < expectedLayout.length; i++) {
                    const pictureBoxes = problemPictureBoxes(problems.nth(i));
                    for (let j = 0; j < expectedLayout[i].pictureBoxes; j++) {
                        // A picture of its own for every box, so the saved book shows which box
                        // took which.
                        const fileName = `p${pageIndex + 1}-problem${i + 1}-picture${j + 1}.jpg`;
                        const file = testInfo.outputPath(fileName);
                        await makeJpegFile(page, file, 120 + 10 * j, 90);
                        await chooseImageFile(page, file, pictureBoxes.nth(j));
                        addedPage.saved.problems[i].pictures.push(fileName);
                    }
                }
                // Every box still shows its own picture once all are in.
                for (let i = 0; i < expectedLayout.length; i++) {
                    const pictureBoxes = problemPictureBoxes(problems.nth(i));
                    for (const [j, fileName] of addedPage.saved.problems[
                        i
                    ].pictures.entries())
                        expect(
                            (await getImagePlacement(page, pictureBoxes.nth(j)))
                                .fileName,
                            `picture box ${j + 1} of problem ${i + 1} should show its own picture`,
                        ).toBe(fileName);
                }
                await expectNoOverflowMarks(
                    page,
                    `the "${templatePage.label}" page should show no overflow with its pictures in`,
                );
            },
        );
    }

    await step("Leave the last page, so that Bloom saves it", async () => {
        const cover = (await getPages(page))[0];
        await goToPage(page, cover.id);
    });

    await step(
        "Check the saved book holds each page, made from its template page, with its heading, pictures and equations",
        async () => {
            const ids = added.map((a) => a.id);
            // A page Bloom adds records, last in its lineage, the id of the template page it was
            // made from. Each page went in after the one before it, so the book has them in the
            // order they were added.
            await expect
                .poll(
                    async () =>
                        (await readPagesOnDisk(page, bookFolder))
                            .filter((p) => ids.includes(p.id))
                            .map(
                                (p) =>
                                    `${p.id} from ${p.lineage[p.lineage.length - 1]}`,
                            ),
                    {
                        timeout: 60000,
                        message:
                            "the saved book never held the added pages, each made from its template page, in order",
                    },
                )
                .toEqual(added.map((a) => `${a.id} from ${a.templatePage.id}`));
            const expected: Record<string, ISavedArithmeticPage> = {};
            for (const a of added) expected[a.id] = a.saved;
            await expect
                .poll(
                    () =>
                        readSavedArithmeticPages(
                            page,
                            bookFolder,
                            ids,
                            kLanguage,
                        ),
                    {
                        timeout: 60000,
                        message:
                            "the saved book never held each page's heading, pictures and equations",
                    },
                )
                .toEqual(expected);
        },
    );
});
