// Every type that Change Layout offers an empty section can be chosen on a new Custom page, and what
// it puts there stays on the page after the mode is turned off and after going to another page and
// back. Choosing Image once put nothing on the page at all (a 6.2 regression), so the test chooses
// each type on a page of its own, the way the case does.
//
// Automates "Can add all kinds of blocks using Change Layout" (Test Case ID 768). Canvas and HTML
// Widget are offered only when their features are on, and Table only when the tables feature is
// (createTypeSelectors and createTableSelector in origami.ts), so the collection is Enterprise and
// has tables turned on.
//
// The case adds each page through the Add Page dialog; the test adds it by the same API that the
// dialog calls (addPage). "The grey flower placeholder picture" is checked as the box showing
// placeHolder.png and the flower drawn for it (the easel for a Canvas); how it looks is left to a person.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    type IBookPage,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { kEdgeTolerance } from "../helpers/geometry";
import {
    chooseSectionType,
    describeSections,
    getSectionTypesOffered,
    sections,
    setChangeLayoutMode,
    type SectionType,
} from "../helpers/origami";

test.use({
    collectionSpec: {
        name: "change-layout-section-types",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
    experimentalFeatures: ["tables"],
});

/** The types in the order the case tries them. */
const kTypes: SectionType[] = [
    "image",
    "canvas",
    "video",
    "text",
    "widget",
    "table",
];

/** Add a Custom page and return it; Bloom shows a page that it has just added. */
async function addCustomPage(page: Page): Promise<IBookPage> {
    const before = new Set((await getContentPages(page)).map((p) => p.id));
    await addPage(page, "Custom");
    const added = (await getContentPages(page)).find((p) => !before.has(p.id));
    if (!added)
        throw new Error("Adding a Custom page did not add a content page.");
    return added;
}

/** What the page's one section holds, failing when the page has any other number of sections. */
async function onlySectionContent(page: Page, when: string): Promise<string> {
    const all = await describeSections(page);
    expect(
        all,
        `The Custom page should have one section ${when}.`,
    ).toHaveLength(1);
    return all[0].content;
}

/**
 * Check that the one section holds a picture box whose picture is Bloom's placeholder, loaded, and
 * that the box fills the section.
 */
async function expectPlaceholderPictureFillsSection(
    page: Page,
    type: SectionType,
): Promise<void> {
    const section = sections(page).first();
    await expect(
        section.locator(":scope > .bloom-canvas img").first(),
        `Choosing ${type} should put a picture in the section.`,
    ).toHaveAttribute("src", /placeHolder\.png/);
    // Bloom hides the placeholder <img> and draws the placeholder as the background of its
    // .bloom-imageContainer instead (placeHolderImages.less): the flower for an Image and the easel
    // for a Canvas. The two are told apart by their SVG's viewBox.
    const viewBox = type === "canvas" ? "0 0 96 106" : "0 0 352 348";
    await expect
        .poll(
            async () =>
                section
                    .locator(":scope > .bloom-canvas .bloom-imageContainer")
                    .first()
                    .evaluate((el) => getComputedStyle(el).backgroundImage),
            {
                timeout: 30000,
                message: `The ${type} box should show the ${type === "canvas" ? "easel" : "flower"} placeholder.`,
            },
        )
        .toContain(viewBox);
    const sectionBox = await section.boundingBox();
    const box = await section
        .locator(":scope > .bloom-canvas")
        .first()
        .boundingBox();
    if (!sectionBox || !box)
        throw new Error(
            `The section, or the picture box that ${type} put in it, is not drawn.`,
        );
    for (const [what, inner, outer] of [
        ["width", box.width, sectionBox.width],
        ["height", box.height, sectionBox.height],
    ] as const)
        expect(
            Math.abs(inner - outer),
            `The ${type} box should fill the section: its ${what} is ${inner}, the section's is ${outer}.`,
        ).toBeLessThanOrEqual(kEdgeTolerance);
}

test("each type that Change Layout offers can be put on a Custom page and stays there [Test Case ID 768]", async ({
    page,
    step,
}) => {
    test.setTimeout(600000);
    await makeBookFromTemplate(page, "Basic Book");
    // A page to go to and come back from: the front cover, which every book has.
    const [cover] = await getPages(page);

    for (const type of kTypes) {
        await step(`Put ${type} on a new Custom page`, async () => {
            const custom = await addCustomPage(page);
            await setChangeLayoutMode(page, true);
            expect(
                await getSectionTypesOffered(page, 0),
                "The empty section of a new Custom page should offer every type.",
            ).toEqual(kTypes);

            await chooseSectionType(page, type, 0);
            expect(
                await onlySectionContent(page, `after choosing ${type}`),
                `Choosing ${type} should put a ${type} in the section.`,
            ).toBe(type);
            await expect(
                sections(page).first().locator(".selector-links"),
                `The row of types should be gone once ${type} is chosen.`,
            ).toHaveCount(0);
            if (type === "image" || type === "canvas")
                await expectPlaceholderPictureFillsSection(page, type);

            await setChangeLayoutMode(page, false);
            expect(
                await onlySectionContent(
                    page,
                    "after Change Layout is turned off",
                ),
                `The ${type} should still be on the page after Change Layout is turned off.`,
            ).toBe(type);

            await goToPage(page, cover.id);
            await goToPage(page, custom.id);
            expect(
                await onlySectionContent(
                    page,
                    "after going to another page and back",
                ),
                `The ${type} should still be on the page after going to another page and back.`,
            ).toBe(type);
        });
    }
});
