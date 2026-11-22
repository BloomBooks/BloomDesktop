// Splitting a page's sections in Change Layout mode, giving each new section a type, and dragging
// the splitters: every split puts the new, empty section on the side whose "+" was pressed, keeps
// what the old section held, and survives leaving the mode and saving.
//
// Automates "Change Layout Splits" (Test Case ID 366). Canvas and HTML Widget are offered only
// when their features are on (createTypeSelectors in origami.ts), so the collection is Enterprise.
//
// The case's "Verify that the text appears and looks normal" is checked as the text being in its
// box and the box being drawn; how it looks is left to a person.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { readBook } from "../helpers/bookHtml";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { kEdgeTolerance } from "../helpers/geometry";
import { chooseImageFile } from "../helpers/images";
import {
    chooseSectionType,
    describeSections,
    dragSplitterOf,
    isChangeLayoutMode,
    type ISectionDescription,
    type SectionContent,
    sections,
    setChangeLayoutMode,
    splitSection,
} from "../helpers/origami";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "change-layout-splits",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

type Rect = ISectionDescription["rect"];
const right = (r: Rect) => r.x + r.width;
const bottom = (r: Rect) => r.y + r.height;

/** The index of the one section that `matches` picks out; throws, listing them all, otherwise. */
async function findSection(
    page: Page,
    what: string,
    matches: (s: ISectionDescription) => boolean,
): Promise<number> {
    const all = await describeSections(page);
    const found = all
        .map((s, index) => ({ s, index }))
        .filter(({ s }) => matches(s));
    if (found.length !== 1)
        throw new Error(
            `Expected exactly one section that is ${what}, found ${found.length}. The sections: ` +
                JSON.stringify(all),
        );
    return found[0].index;
}

const holding = (content: SectionContent) => (s: ISectionDescription) =>
    s.content === content;

async function section(
    page: Page,
    index: number,
): Promise<ISectionDescription> {
    return (await describeSections(page))[index];
}

test("splitting sections of a Basic Text & Image page, typing them and dragging the splitters [Test Case ID 366]", async ({
    page,
}) => {
    test.setTimeout(420000);
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Basic Text & Image");
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    await chooseImageFile(page, fixtureImage("bird.png"));
    await typeInGroup(
        page,
        ".bloom-translationGroup",
        "en",
        "Typed before the splits.",
    );
    await setChangeLayoutMode(page, true);
    expect(await describeSections(page)).toHaveLength(2);

    // Split the image, adding a new section below it.
    await splitSection(
        page,
        "bottom",
        await findSection(page, "the picture", holding("image")),
    );
    expect(
        await sections(page).count(),
        "The split should make a third section.",
    ).toBe(3);
    let image = await section(
        page,
        await findSection(page, "the picture", holding("image")),
    );
    let empty = await section(
        page,
        await findSection(page, "the new section", holding("empty")),
    );
    expect(image.imageFile, "The picture should survive the split.").toBe(
        "bird.png",
    );
    expect(
        bottom(image.rect),
        "The picture should stay above the new section.",
    ).toBeLessThanOrEqual(empty.rect.y + kEdgeTolerance);

    // Make the new section a Canvas.
    await chooseSectionType(
        page,
        "canvas",
        await findSection(page, "the new section", holding("empty")),
    );
    await findSection(page, "a canvas", holding("canvas"));

    // Split the picture again from its left edge: the picture goes to the top right.
    await splitSection(
        page,
        "left",
        await findSection(page, "the picture", holding("image")),
    );
    image = await section(
        page,
        await findSection(page, "the picture", holding("image")),
    );
    empty = await section(
        page,
        await findSection(page, "the new section", holding("empty")),
    );
    const canvas = await section(
        page,
        await findSection(page, "the canvas", holding("canvas")),
    );
    expect(
        image.imageFile,
        "The picture should survive the second split.",
    ).toBe("bird.png");
    expect(
        image.rect.x,
        "The picture should be to the right of the new section.",
    ).toBeGreaterThanOrEqual(right(empty.rect) - kEdgeTolerance);
    expect(
        bottom(image.rect),
        "The picture should still be above the canvas.",
    ).toBeLessThanOrEqual(canvas.rect.y + kEdgeTolerance);

    // Make the new section a Video.
    await chooseSectionType(
        page,
        "video",
        await findSection(page, "the new section", holding("empty")),
    );
    const videoIndex = await findSection(page, "a video", holding("video"));
    await expect(
        sections(page)
            .nth(videoIndex)
            .locator(".bloom-videoContainer.bloom-noVideoSelected"),
        "A new video section should wait for a video to be chosen.",
    ).toHaveCount(1);

    // Split the text box from its right edge.
    const oldTextIndex = await findSection(
        page,
        "the text box",
        holding("text"),
    );
    await splitSection(page, "right", oldTextIndex);
    const oldText = await section(
        page,
        await findSection(page, "the text box", holding("text")),
    );
    empty = await section(
        page,
        await findSection(page, "the new section", holding("empty")),
    );
    expect(
        empty.rect.x,
        "The new section should be to the right of the text box.",
    ).toBeGreaterThanOrEqual(right(oldText.rect) - kEdgeTolerance);
    expect(Math.abs(empty.rect.y - oldText.rect.y)).toBeLessThanOrEqual(
        kEdgeTolerance,
    );
    expect(
        oldText.text,
        "The text typed in the box should survive the split.",
    ).toBe("Typed before the splits.");

    // Make the new section a Text box.
    await chooseSectionType(
        page,
        "text",
        await findSection(page, "the new section", holding("empty")),
    );
    const isNewText = (s: ISectionDescription) =>
        s.content === "text" && s.rect.x > oldText.rect.x + kEdgeTolerance;
    await findSection(page, "the new text box", isNewText);

    // Split the new text box from its top edge, and leave the new section above it with no type.
    await splitSection(
        page,
        "top",
        await findSection(page, "the new text box", isNewText),
    );
    const newText = await section(
        page,
        await findSection(page, "the new text box", isNewText),
    );
    empty = await section(
        page,
        await findSection(page, "the untyped section", holding("empty")),
    );
    expect(
        bottom(empty.rect),
        "The new section should be above the split text box.",
    ).toBeLessThanOrEqual(newText.rect.y + kEdgeTolerance);
    expect(Math.abs(empty.rect.x - newText.rect.x)).toBeLessThanOrEqual(
        kEdgeTolerance,
    );

    // Drag the splitter between the picture row and the canvas down: the canvas gets shorter.
    const canvasIndex = await findSection(
        page,
        "the canvas",
        holding("canvas"),
    );
    const canvasBefore = (await section(page, canvasIndex)).rect;
    await dragSplitterOf(page, sections(page).nth(canvasIndex), 0, 40);
    await expect
        .poll(async () => (await section(page, canvasIndex)).rect.height, {
            message:
                "Dragging the splitter above the canvas down should make the canvas shorter.",
        })
        .toBeLessThan(canvasBefore.height - 10);
    // Drag the splitter between the video and the picture to the right: the picture gets narrower.
    const imageIndex = await findSection(page, "the picture", holding("image"));
    const imageBefore = (await section(page, imageIndex)).rect;
    const videoBefore = (await section(page, videoIndex)).rect;
    await dragSplitterOf(page, sections(page).nth(imageIndex), 40, 0);
    await expect
        .poll(async () => (await section(page, imageIndex)).rect.width, {
            message:
                "Dragging the splitter left of the picture to the right should make it narrower.",
        })
        .toBeLessThan(imageBefore.width - 10);
    expect(
        (await section(page, videoIndex)).rect.width,
        "The video beside the picture should get wider by what the picture lost.",
    ).toBeGreaterThan(videoBefore.width + 10);

    await setChangeLayoutMode(page, false);

    // Type into the new text box.
    const groupIndex = await editablePageFrame(page)
        .locator(".bloom-translationGroup")
        .evaluateAll((groups) => {
            const xs = groups.map((g) => g.getBoundingClientRect().x);
            return xs.indexOf(Math.max(...xs));
        });
    await typeInGroup(
        page,
        ".bloom-translationGroup",
        "en",
        "Typed in the new box.",
        groupIndex,
    );
    const typed = await section(
        page,
        await findSection(
            page,
            "the new text box",
            (s) => s.text === "Typed in the new box.",
        ),
    );
    expect(
        typed.rect.width,
        "The new text box should be drawn.",
    ).toBeGreaterThan(20);
    expect(
        typed.rect.height,
        "The new text box should be drawn.",
    ).toBeGreaterThan(20);

    // Choose a picture for the Canvas.
    await chooseImageFile(
        page,
        fixtureImage("house-landscape.png"),
        sections(page).nth(
            await findSection(page, "the canvas", holding("canvas")),
        ),
    );
    expect(
        (
            await section(
                page,
                await findSection(page, "the canvas", holding("canvas")),
            )
        ).imageFile,
    ).toBe("house-landscape.png");

    await switchTab(page, "collection");
    const saved = (await readBook(page, bookFolder)).pages.find(
        (p) => p.id === contentPage.id,
    );
    expect(
        saved?.layout,
        "The page should be saved with all five splits.",
    ).toHaveLength(5);
    expect(saved?.text).toContain("Typed before the splits.");
    expect(saved?.text).toContain("Typed in the new box.");
    expect(saved?.imageSources.map((src) => Path.basename(src))).toEqual(
        expect.arrayContaining(["bird.png", "house-landscape.png"]),
    );
});

test("a new Custom page starts in Change Layout mode and keeps the sections it is split into [Test Case ID 366]", async ({
    page,
}) => {
    test.setTimeout(300000);
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Custom");
    const [custom] = await getContentPages(page);
    // Bloom shows a page it has just added, as it does after the Add Page dialog. Going to it
    // again would save it first, and saving takes Change Layout mode off.
    expect(
        await getShownPageId(page),
        "Bloom should show the Custom page it just added.",
    ).toBe(custom.id);
    expect(
        await isChangeLayoutMode(page),
        "A new Custom page should open in Change Layout mode.",
    ).toBe(true);

    await splitSection(page, "bottom", 0);
    await splitSection(page, "right", 0);
    // The sections are now top left, top right and bottom, in document order.
    const [topLeft, topRight, wholeBottom] = await describeSections(page);
    expect(topRight.rect.x).toBeGreaterThanOrEqual(
        right(topLeft.rect) - kEdgeTolerance,
    );
    expect(wholeBottom.rect.y).toBeGreaterThanOrEqual(
        bottom(topLeft.rect) - kEdgeTolerance,
    );

    await chooseSectionType(page, "widget", 2);
    await chooseSectionType(page, "image", 0);
    await chooseSectionType(page, "text", 1);

    const bottomBefore = (await section(page, 2)).rect;
    await dragSplitterOf(page, sections(page).nth(2), 0, -40);
    await expect
        .poll(async () => (await section(page, 2)).rect.height, {
            message:
                "Dragging the splitter above the widget up should make the widget taller.",
        })
        .toBeGreaterThan(bottomBefore.height + 10);
    const leftBefore = (await section(page, 0)).rect;
    await dragSplitterOf(page, sections(page).nth(0), 40, 0);
    await expect
        .poll(async () => (await section(page, 0)).rect.width, {
            message:
                "Dragging the splitter right of the picture to the right should make it wider.",
        })
        .toBeGreaterThan(leftBefore.width + 10);

    await setChangeLayoutMode(page, false);
    const after = await describeSections(page);
    expect(after.map((s) => s.content)).toEqual(["image", "text", "widget"]);
    expect(after[1].rect.x).toBeGreaterThanOrEqual(
        right(after[0].rect) - kEdgeTolerance,
    );
    expect(after[2].rect.y).toBeGreaterThanOrEqual(
        bottom(after[0].rect) - kEdgeTolerance,
    );
    expect(after[2].rect.y).toBeGreaterThanOrEqual(
        bottom(after[1].rect) - kEdgeTolerance,
    );

    await switchTab(page, "collection");
    const saved = (await readBook(page, bookFolder)).pages.find(
        (p) => p.id === custom.id,
    );
    expect(
        saved?.layout,
        "The page should be saved with both splits.",
    ).toHaveLength(2);
});
