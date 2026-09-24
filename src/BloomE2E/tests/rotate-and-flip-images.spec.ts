// Rotating and mirroring pictures and other canvas items in the Edit tab: the Rotate right command,
// the Flip submenu, and the round knob above a selected item that rotates it to any angle (BL-16741).
// Automates the manual test "Rotate and Flip Pictures" (Test Case ID 827).
//
// The book has two Canvas pages. The first has only the page's background picture, which Rotate
// right rotates inside its box. The second has an overlay picture, a text box, a speech bubble and a
// video dropped from the Canvas tool's palette; Rotate right rotates an overlay's whole box.
//
// What stays manual, and why, is on the card: whether a rotated picture looks right, which is a
// judgement about pixels; crop and move drags on a rotated picture; cursors and tooltips on rotated
// handles; and how the book looks in BloomPUB, ePUB and PDF.
//
// The tests are serial because each one starts from the book the one before it left behind. Set
// BLOOM_E2E_SCREENSHOT_DIR to a folder to have the run save the card's pictures there.

import type { Page } from "@playwright/test";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    canvas,
    canvasElement,
    canvasElementMenuPanels,
    closeCanvasElementMenu,
    dragPaletteItemOntoCanvas,
    dragRotateHandle,
    expectRotateHandleShown,
    flipSelectedImage,
    getCanvasElementCount,
    getCanvasElementMenuGroups,
    getCanvasElementMenuItems,
    getCanvasElementPlacement,
    getCanvasElementRotation,
    getOpenCanvasElementMenuCount,
    openCanvasElementMenu,
    openCanvasElementSubmenu,
    resetSelectedImage,
    rotateSelectedImageRight90Degrees,
    selectCanvasElement,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    chooseImageFile,
    cropImage,
    getImagePlacement,
    getPictureInlineLayout,
    getPictureRotation,
    kUprightPicture,
    mirroredOnScreen,
    type IPictureRotation,
} from "../helpers/images";
import { saveScreenshotIfAsked } from "../helpers/screenshot";
import { undo } from "../helpers/workspace";

// The Canvas tool is a Pro feature, so the collection is on the Test enterprise subscription.
test.use({
    collectionSpec: {
        name: "rotate-and-flip-images",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test.describe.configure({ mode: "serial" });

// A picture twice as wide as it is tall, with an obvious top (a sun in the top left corner, the
// ground along the bottom, the word TOP), so that a rotation changes the shape of the page's picture
// area and a mirror is visible. Shipped with the suite.
const PICTURE_FILE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "house-landscape.png",
);
const PICTURE_NAME = "house-landscape.png";

// How a picture looks after one Rotate right, and after two and three.
const ROTATED_90: IPictureRotation = { a: 0, b: 1, c: -1, d: 0 };
const ROTATED_180: IPictureRotation = { a: -1, b: 0, c: 0, d: -1 };
const ROTATED_270: IPictureRotation = { a: 0, b: -1, c: 1, d: 0 };

const ROTATE_RIGHT = "EditTab.Image.RotateRight";
const FLIP = "EditTab.Image.Flip";
const RESET_IMAGE = "EditTab.Image.Reset";

// The two pages, and where each item sits on the second, in the order the palette added them.
// The page's background picture is always canvas element 0. Set by the first test.
let backgroundPage: IBookPage;
let itemsPage: IBookPage;
const BACKGROUND = 0;
let overlayPicture: number;
let textBox: number;
let speechBubble: number;
let video: number;

/** Whether the selected item's menu offers Reset Image as a command that can be clicked. */
const isResetImageEnabled = async (page: Page) => {
    const items = await getCanvasElementMenuItems(page);
    await closeCanvasElementMenu(page);
    const reset = items.find((i) => i.id === RESET_IMAGE);
    expect(
        reset,
        "The picture's menu has no Reset Image command.",
    ).toBeTruthy();
    return reset!.enabled;
};

/**
 * Save a picture of the selected item's "..." menu for the card, when the run saves pictures at all,
 * and shut the menu again.
 */
const saveMenuScreenshot = async (page: Page, name: string) => {
    if (!process.env.BLOOM_E2E_SCREENSHOT_DIR) return;
    await openCanvasElementMenu(page);
    await saveScreenshotIfAsked([canvasElementMenuPanels(page).first()], name);
    await closeCanvasElementMenu(page);
};

test.describe("rotating and flipping pictures", () => {
    test("builds a book with a background picture page and a page of overlay items", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Canvas", 2);
        [backgroundPage, itemsPage] = await getContentPages(page);

        await goToPage(page, backgroundPage.id);
        await chooseImageFile(page, PICTURE_FILE);
        expect((await getImagePlacement(page)).fileName).toBe(PICTURE_NAME);
        await expect
            .poll(async () => getPictureRotation(page))
            .toEqual(kUprightPicture);

        await goToPage(page, itemsPage.id);
        overlayPicture = await dragPaletteItemOntoCanvas(page, "image", {
            xFraction: 0.3,
            yFraction: 0.3,
        });
        await chooseImageFile(
            page,
            PICTURE_FILE,
            canvasElement(page, overlayPicture),
        );
        textBox = await dragPaletteItemOntoCanvas(page, "none", {
            xFraction: 0.72,
            yFraction: 0.3,
        });
        speechBubble = await dragPaletteItemOntoCanvas(page, "speech", {
            xFraction: 0.3,
            yFraction: 0.75,
        });
        video = await dragPaletteItemOntoCanvas(page, "video", {
            xFraction: 0.72,
            yFraction: 0.75,
        });

        // Sanity check the page the rest of the file rests on: its placeholder background and the
        // four items, the picture upright and not rotated.
        expect(await getCanvasElementCount(page)).toBe(5);
        const picture = canvasElement(page, overlayPicture);
        // Bloom gives the second copy of one file in a book a number: house-landscape1.png.
        expect((await getImagePlacement(page, picture)).fileName).toMatch(
            /^house-landscape\d*\.png$/,
        );
        await expect
            .poll(async () => getCanvasElementRotation(picture))
            .toBe(0);
        await expect
            .poll(async () => getPictureRotation(page, picture))
            .toEqual(kUprightPicture);
    });

    test("the rotation knob is offered for a picture and a text box, but not a video, a speech bubble or the background picture [Test Case ID 827]", async ({
        page,
    }) => {
        await goToPage(page, itemsPage.id);
        await selectCanvasElement(page, overlayPicture);
        await expectRotateHandleShown(page, true, "an overlay picture");
        await saveScreenshotIfAsked([canvas(page)], "01-knob-on-picture");

        await selectCanvasElement(page, textBox);
        await expectRotateHandleShown(page, true, "a text box");
        await saveScreenshotIfAsked([canvas(page)], "02-knob-on-text-box");

        await selectCanvasElement(page, video);
        await expectRotateHandleShown(page, false, "a video");
        await saveScreenshotIfAsked([canvas(page)], "02b-no-knob-on-video");

        await selectCanvasElement(page, speechBubble);
        await expectRotateHandleShown(page, false, "a speech bubble");
        await saveScreenshotIfAsked(
            [canvas(page)],
            "03-no-knob-on-speech-bubble",
        );

        await goToPage(page, backgroundPage.id);
        await selectCanvasElement(page, BACKGROUND);
        await expectRotateHandleShown(
            page,
            false,
            "the page's background picture",
        );
        await saveScreenshotIfAsked([canvas(page)], "04-no-knob-on-background");
    });

    test("the picture menu groups Rotate right and Flip with Reset Image, which comes last, and Escape shuts it [Test Case ID 827]", async ({
        page,
    }) => {
        await goToPage(page, itemsPage.id);
        await selectCanvasElement(page, overlayPicture);
        const groups = await getCanvasElementMenuGroups(page);
        const group = groups.find((g) => g.includes(ROTATE_RIGHT));
        expect(
            group,
            `No group of the menu holds Rotate right. The groups: ${JSON.stringify(groups)}`,
        ).toBeTruthy();
        expect(group).toContain(FLIP);
        expect(group!.indexOf(ROTATE_RIGHT)).toBeLessThan(group!.indexOf(FLIP));
        expect(group![group!.length - 1]).toBe(RESET_IMAGE);
        await saveScreenshotIfAsked(
            [canvasElementMenuPanels(page).first()],
            "05-menu-groups",
        );

        await openCanvasElementSubmenu(page, FLIP);
        await saveScreenshotIfAsked(
            [
                canvasElementMenuPanels(page).first(),
                canvasElementMenuPanels(page).last(),
            ],
            "06-flip-submenu",
        );
        // THE ACTION UNDER TEST: Escape, pressed into the page the way a person presses it, with
        // the Flip submenu open beside the menu. Both have to go.
        expect(await getOpenCanvasElementMenuCount(page)).toBe(2);
        await closeCanvasElementMenu(page);
        expect(await getOpenCanvasElementMenuCount(page)).toBe(0);
    });

    test("Rotate right rotates an overlay picture's whole box 90 degrees clockwise, and four rotations bring it upright [Test Case ID 827]", async ({
        page,
    }) => {
        await goToPage(page, itemsPage.id);
        const picture = await selectCanvasElement(page, overlayPicture);
        const placement = await getCanvasElementPlacement(picture);

        await rotateSelectedImageRight90Degrees(page);
        await expect
            .poll(async () => getCanvasElementRotation(picture))
            .toBe(90);
        await expect
            .poll(async () => getPictureRotation(page, picture))
            .toEqual(ROTATED_90);
        // A rotation is about the box's centre, so the box keeps its place and its size.
        await expect
            .poll(async () => getCanvasElementPlacement(picture))
            .toEqual(placement);
        await saveScreenshotIfAsked([canvas(page)], "07-overlay-rotated-90");

        await rotateSelectedImageRight90Degrees(page);
        await expect
            .poll(async () => getPictureRotation(page, picture))
            .toEqual(ROTATED_180);
        await rotateSelectedImageRight90Degrees(page);
        await expect
            .poll(async () => getPictureRotation(page, picture))
            .toEqual(ROTATED_270);
        await rotateSelectedImageRight90Degrees(page);
        await expect
            .poll(async () => getCanvasElementRotation(picture))
            .toBe(0);
        await expect
            .poll(async () => getPictureRotation(page, picture))
            .toEqual(kUprightPicture);
        // Upright again leaves no rotation in the book at all, the same as never rotated.
        expect(
            (await getPictureInlineLayout(page, picture)).box.transform,
        ).toBe("");
        await expect
            .poll(async () => getCanvasElementPlacement(picture))
            .toEqual(placement);
        await saveScreenshotIfAsked([canvas(page)], "08-overlay-upright-again");
    });

    test("Flip horizontal and Flip vertical mirror a picture without moving its box [Test Case ID 827]", async ({
        page,
    }) => {
        await goToPage(page, itemsPage.id);
        const picture = await selectCanvasElement(page, overlayPicture);
        expect(
            await getPictureRotation(page, picture),
            "The Rotate right test should have left the overlay picture upright.",
        ).toEqual(kUprightPicture);
        const placement = await getCanvasElementPlacement(picture);

        await flipSelectedImage(page, "horizontal");
        // The click on Flip horizontal shuts the submenu along with the menu, though the pointer
        // has not moved off where the submenu was.
        expect(
            await getOpenCanvasElementMenuCount(page),
            "The Flip submenu is still showing after its command was clicked.",
        ).toBe(0);
        await expect
            .poll(async () => getPictureRotation(page, picture))
            .toEqual(mirroredOnScreen(kUprightPicture, "horizontal"));
        await expect
            .poll(async () => getCanvasElementPlacement(picture))
            .toEqual(placement);
        await saveScreenshotIfAsked([canvas(page)], "09-flip-horizontal");
        await flipSelectedImage(page, "horizontal");
        await expect
            .poll(async () => getPictureRotation(page, picture))
            .toEqual(kUprightPicture);

        await flipSelectedImage(page, "vertical");
        await expect
            .poll(async () => getPictureRotation(page, picture))
            .toEqual(mirroredOnScreen(kUprightPicture, "vertical"));
        await expect
            .poll(async () => getCanvasElementPlacement(picture))
            .toEqual(placement);
        await saveScreenshotIfAsked([canvas(page)], "10-flip-vertical");
        await flipSelectedImage(page, "vertical");
        await expect
            .poll(async () => getPictureRotation(page, picture))
            .toEqual(kUprightPicture);
    });

    test("Undo puts back a Rotate right and a Flip, one step each [Test Case ID 827]", async ({
        page,
    }) => {
        await goToPage(page, itemsPage.id);
        const picture = await selectCanvasElement(page, overlayPicture);
        expect(
            await getPictureRotation(page, picture),
            "The Flip test should have left the overlay picture upright and unmirrored.",
        ).toEqual(kUprightPicture);
        await rotateSelectedImageRight90Degrees(page);
        await flipSelectedImage(page, "horizontal");
        const rotatedAndMirrored = mirroredOnScreen(ROTATED_90, "horizontal");
        await expect
            .poll(async () => getPictureRotation(page, picture))
            .toEqual(rotatedAndMirrored);

        await undo(page);
        await expect
            .poll(async () => getPictureRotation(page, picture), {
                message: "Undo did not take away the mirror.",
            })
            .toEqual(ROTATED_90);
        await saveScreenshotIfAsked([canvas(page)], "11-undo-flip");

        await undo(page);
        await expect
            .poll(async () => getCanvasElementRotation(picture), {
                message: "A second Undo did not take away the rotation.",
            })
            .toBe(0);
        await expect
            .poll(async () => getPictureRotation(page, picture))
            .toEqual(kUprightPicture);
        await saveScreenshotIfAsked([canvas(page)], "12-undo-rotate");
    });

    test("Reset Image clears a crop and a mirror, but leaves a box rotated with the knob [Test Case ID 827]", async ({
        page,
    }) => {
        await goToPage(page, itemsPage.id);
        const picture = await selectCanvasElement(page, overlayPicture);
        expect(
            await isResetImageEnabled(page),
            "Reset Image should be greyed out on a picture nothing has been done to.",
        ).toBe(false);
        await saveMenuScreenshot(page, "13-reset-disabled");

        await cropImage(page, "e", 40, picture);
        expect(await dragRotateHandle(page, 88)).toBe(90);
        await flipSelectedImage(page, "horizontal");
        // Sanity check what Reset Image is about to clear.
        await expect
            .poll(async () => (await getImagePlacement(page, picture)).cropped)
            .toBe(true);
        await expect
            .poll(async () => getPictureRotation(page, picture))
            .toEqual(mirroredOnScreen(ROTATED_90, "horizontal"));
        expect(await isResetImageEnabled(page)).toBe(true);
        await saveMenuScreenshot(page, "14a-reset-enabled");
        await saveScreenshotIfAsked([canvas(page)], "14-before-reset");

        await resetSelectedImage(page);
        await expect
            .poll(async () => (await getImagePlacement(page, picture)).cropped)
            .toBe(false);
        // The mirror is gone, and the box is still rotated the 90 degrees the knob gave it.
        await expect
            .poll(async () => getCanvasElementRotation(picture))
            .toBe(90);
        await expect
            .poll(async () => getPictureRotation(page, picture))
            .toEqual(ROTATED_90);
        await saveScreenshotIfAsked([canvas(page)], "15-after-reset");
    });

    test("on a box rotated with the knob, Flip horizontal still swaps left and right on screen [Test Case ID 827]", async ({
        page,
    }) => {
        await goToPage(page, itemsPage.id);
        const picture = await selectCanvasElement(page, overlayPicture);
        // Left rotated 90 degrees by the test before.
        await expect
            .poll(async () => getCanvasElementRotation(picture))
            .toBe(90);

        await flipSelectedImage(page, "horizontal");
        await expect
            .poll(async () => getPictureRotation(page, picture))
            .toEqual(mirroredOnScreen(ROTATED_90, "horizontal"));
        await saveScreenshotIfAsked([canvas(page)], "16-flip-rotated-overlay");
    });

    test("dragging the knob rotates an item, snapping to multiples of 45 degrees unless Ctrl is held, and Undo takes back each drag [Test Case ID 827]", async ({
        page,
    }) => {
        await goToPage(page, itemsPage.id);
        const box = await selectCanvasElement(page, textBox);
        expect(
            await getCanvasElementRotation(box),
            "No test before this one should have rotated the text box.",
        ).toBe(0);

        // The 45 and the 14 below are rotationSnapInterval and rotationSnapTolerance in
        // src/BloomBrowserUI/bookEdit/js/canvasElementManager/CanvasSnapProvider.ts: the angle snaps
        // to the nearest multiple of 45 degrees when it is within 14 of one. Change them there, and
        // the angles here have to change with them.
        //
        // 25 degrees is more than 14 from both 0 and 45, so it does not snap.
        const unsnapped = await dragRotateHandle(page, 25);
        expect(Math.abs(unsnapped - 25)).toBeLessThan(1.5);
        await saveScreenshotIfAsked([canvas(page)], "17a-unsnapped-25");
        // On to 80 degrees, which is within 14 of 90, so it snaps there.
        expect(await dragRotateHandle(page, 55)).toBe(90);
        await saveScreenshotIfAsked([canvas(page)], "17-snapped-90");
        // Back 52 to 38 degrees, which is within 14 of 45, so it snaps to 45.
        expect(await dragRotateHandle(page, -52)).toBe(45);
        await saveScreenshotIfAsked([canvas(page)], "18-snapped-45");
        // The same 38 degrees with Ctrl held stays 38.
        const free = await dragRotateHandle(page, -7, { withCtrl: true });
        expect(Math.abs(free - 38)).toBeLessThan(1.5);
        await saveScreenshotIfAsked([canvas(page)], "19-ctrl-38");

        // One Undo for each drag, however far it went.
        await undo(page);
        await expect.poll(async () => getCanvasElementRotation(box)).toBe(45);
        await undo(page);
        await expect.poll(async () => getCanvasElementRotation(box)).toBe(90);
        await undo(page);
        await expect
            .poll(async () => getCanvasElementRotation(box))
            .toBeCloseTo(unsnapped, 1);
        await undo(page);
        await expect.poll(async () => getCanvasElementRotation(box)).toBe(0);
        await saveScreenshotIfAsked([canvas(page)], "20-undo-knob");
    });

    test("Rotate right rotates the page's background picture inside its area, which changes shape, and four rotations put it back [Test Case ID 827]", async ({
        page,
    }) => {
        await goToPage(page, backgroundPage.id);
        const background = await selectCanvasElement(page, BACKGROUND);
        expect(
            await getPictureRotation(page),
            "No test before this one should have rotated the background picture.",
        ).toEqual(kUprightPicture);
        const before = await getCanvasElementPlacement(background);
        // The picture is twice as wide as it is tall, and so is its area.
        expect(before.width / before.height).toBeCloseTo(2, 1);
        await saveScreenshotIfAsked([canvas(page)], "21-background-before");

        await rotateSelectedImageRight90Degrees(page);
        await expect
            .poll(async () => getPictureRotation(page))
            .toEqual(ROTATED_90);
        // The background's box cannot rotate; its shape changes to the rotated picture's instead.
        await expect
            .poll(async () => getCanvasElementRotation(background))
            .toBe(0);
        const rotated = await getCanvasElementPlacement(background);
        expect(rotated.width / rotated.height).toBeCloseTo(0.5, 1);
        await saveScreenshotIfAsked([canvas(page)], "22-background-rotated-90");

        await rotateSelectedImageRight90Degrees(page);
        await expect
            .poll(async () => getPictureRotation(page))
            .toEqual(ROTATED_180);
        await rotateSelectedImageRight90Degrees(page);
        await expect
            .poll(async () => getPictureRotation(page))
            .toEqual(ROTATED_270);
        await rotateSelectedImageRight90Degrees(page);
        await expect
            .poll(async () => getPictureRotation(page))
            .toEqual(kUprightPicture);
        const after = await getCanvasElementPlacement(background);
        for (const side of ["left", "top", "width", "height"] as const)
            expect(
                Math.abs(after[side] - before[side]),
                `After four rotations the background's ${side} is ${after[side]}, not ${before[side]}.`,
            ).toBeLessThan(1);
        await saveScreenshotIfAsked(
            [canvas(page)],
            "23-background-four-rotations",
        );
    });

    test("Undo after Rotate right on a cropped background picture puts the picture, its crop and its area back exactly [Test Case ID 827]", async ({
        page,
    }) => {
        await goToPage(page, backgroundPage.id);
        expect(
            await getPictureRotation(page),
            "Four rotations in the test before should have left the background picture upright.",
        ).toEqual(kUprightPicture);
        expect(
            (await getImagePlacement(page)).cropped,
            "No test before this one should have cropped the background picture.",
        ).toBe(false);
        await cropImage(page, "e", 60);
        const cropped = await getPictureInlineLayout(page);
        await expect
            .poll(async () => (await getImagePlacement(page)).cropped)
            .toBe(true);
        await saveScreenshotIfAsked([canvas(page)], "24-cropped-background");

        await rotateSelectedImageRight90Degrees(page);
        await expect
            .poll(async () => getPictureRotation(page))
            .toEqual(ROTATED_90);
        expect(await getPictureInlineLayout(page)).not.toEqual(cropped);
        await saveScreenshotIfAsked([canvas(page)], "25-cropped-rotated");

        await undo(page);
        await expect
            .poll(async () => getPictureInlineLayout(page), {
                message:
                    "Undo did not put the picture and its area back as they were before the rotation.",
            })
            .toEqual(cropped);
        await expect
            .poll(async () => getPictureRotation(page))
            .toEqual(kUprightPicture);
        await saveScreenshotIfAsked([canvas(page)], "26-undo-restores");
    });

    test("Reset Image takes the rotation and the crop off the background picture [Test Case ID 827]", async ({
        page,
    }) => {
        await goToPage(page, backgroundPage.id);
        await selectCanvasElement(page, BACKGROUND);
        // Left cropped by the test before.
        await expect
            .poll(async () => (await getImagePlacement(page)).cropped)
            .toBe(true);
        await rotateSelectedImageRight90Degrees(page);
        await expect
            .poll(async () => getPictureRotation(page))
            .toEqual(ROTATED_90);

        await resetSelectedImage(page);
        await expect
            .poll(async () => getPictureRotation(page))
            .toEqual(kUprightPicture);
        await expect
            .poll(async () => (await getImagePlacement(page)).cropped)
            .toBe(false);
        expect(
            await isResetImageEnabled(page),
            "Reset Image should be greyed out again once it has put the picture back.",
        ).toBe(false);
        await saveMenuScreenshot(page, "27b-background-reset-disabled");
        await saveScreenshotIfAsked([canvas(page)], "27-background-reset");
    });

    test("on a background picture rotated by Rotate right, Flip horizontal still swaps left and right on screen [Test Case ID 827]", async ({
        page,
    }) => {
        await goToPage(page, backgroundPage.id);
        await selectCanvasElement(page, BACKGROUND);
        expect(
            await getPictureRotation(page),
            "The Reset image test should have left the background picture upright.",
        ).toEqual(kUprightPicture);
        await rotateSelectedImageRight90Degrees(page);
        await flipSelectedImage(page, "horizontal");
        await expect
            .poll(async () => getPictureRotation(page))
            .toEqual(mirroredOnScreen(ROTATED_90, "horizontal"));
        await saveScreenshotIfAsked(
            [canvas(page)],
            "28-background-rotated-flipped",
        );
    });

    test("rotated and mirrored items keep their angle and their place when you leave the page and come back [Test Case ID 827]", async ({
        page,
    }) => {
        await goToPage(page, itemsPage.id);
        const textBoxBefore = await selectCanvasElement(page, textBox);
        expect(
            await getCanvasElementRotation(textBoxBefore),
            "The knob test's Undos should have left the text box upright.",
        ).toBe(0);
        const freeAngle = await dragRotateHandle(page, 30, { withCtrl: true });
        // 30 is not a multiple of 45, so this also proves the angle is saved as it is, not snapped.
        expect(Math.abs(freeAngle - 30)).toBeLessThan(1.5);

        // What the two pages look like now: the overlay picture rotated and mirrored by the tests
        // before, the text box at an angle that is not a multiple of 45, and the background
        // picture rotated and mirrored.
        const picture = canvasElement(page, overlayPicture);
        const text = canvasElement(page, textBox);
        const items = {
            pictureAngle: await getCanvasElementRotation(picture),
            pictureRotation: await getPictureRotation(page, picture),
            picturePlace: await getCanvasElementPlacement(picture),
            textAngle: await getCanvasElementRotation(text),
            textPlace: await getCanvasElementPlacement(text),
        };
        expect(items.pictureAngle).toBe(90);
        await goToPage(page, backgroundPage.id);
        const background = {
            rotation: await getPictureRotation(page),
            place: await getCanvasElementPlacement(
                canvasElement(page, BACKGROUND),
            ),
        };
        expect(background.rotation).toEqual(
            mirroredOnScreen(ROTATED_90, "horizontal"),
        );

        // Leaving a page is what saves it, and coming back loads what was saved. Three visits,
        // because an item that moves a little on each would be hard to see after one.
        for (let visit = 1; visit <= 3; visit++) {
            await goToPage(page, itemsPage.id);
            expect(
                {
                    pictureAngle: await getCanvasElementRotation(picture),
                    pictureRotation: await getPictureRotation(page, picture),
                    picturePlace: await getCanvasElementPlacement(picture),
                    textAngle: await getCanvasElementRotation(text),
                    textPlace: await getCanvasElementPlacement(text),
                },
                `The items page changed by visit ${visit}.`,
            ).toEqual(items);
            await goToPage(page, backgroundPage.id);
            expect(
                {
                    rotation: await getPictureRotation(page),
                    place: await getCanvasElementPlacement(
                        canvasElement(page, BACKGROUND),
                    ),
                },
                `The background page changed by visit ${visit}.`,
            ).toEqual(background);
        }
        await saveScreenshotIfAsked(
            [canvas(page)],
            "29-background-after-visits",
        );
        await goToPage(page, itemsPage.id);
        await saveScreenshotIfAsked([canvas(page)], "30-items-after-visits");
    });
});
