// Cropping a picture on a Canvas page, with the side handles of the selected picture's frame, and
// what goes with a crop: the round handle in the middle that moves the picture inside its box,
// resizing a cropped picture, and Reset image. Automates the manual test "Crop Image in Canvas"
// (Test Case ID 307).
//
// A crop changes the picture's box and leaves the picture where it is, so most checks compare two
// rectangles on screen: the box (the canvas element) and the picture inside it (its <img>), which
// is wider or taller than the box when cropped.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    canvasElement,
    dragCanvasElementCorner,
    dragPaletteItemOntoCanvas,
    resetSelectedImage,
    selectCanvasElement,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    chooseImageFile,
    cropImage,
    getImagePlacement,
} from "../helpers/images";

// The Canvas tool is a Pro feature, so the collection is on the Test enterprise subscription.
test.use({
    collectionSpec: {
        name: "crop-canvas-image",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

// A picture twice as wide as it is tall. Shipped with the suite.
const PICTURE_FILE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "house-landscape.png",
);

type Side = "n" | "s" | "e" | "w";

interface IBox {
    left: number;
    top: number;
    right: number;
    bottom: number;
}

/** Where a locator is drawn on screen. */
async function getBox(locator: Locator, what: string): Promise<IBox> {
    const box = await locator.boundingBox();
    if (!box) throw new Error(`${what} has no box on screen.`);
    return {
        left: box.x,
        top: box.y,
        right: box.x + box.width,
        bottom: box.y + box.height,
    };
}

/** The handle on one side of the selected picture's frame. */
function sideHandle(page: Page, side: Side): Locator {
    return editablePageFrame(page).locator(
        `#canvas-element-control-frame .bloom-ui-canvas-element-side-handle-${side}`,
    );
}

/** The round handle in the middle of a cropped picture, which moves the picture inside its box. */
function moveCropHandle(page: Page): Locator {
    return editablePageFrame(page).locator(
        "#canvas-element-control-frame .bloom-ui-canvas-element-move-crop-handle",
    );
}

/** Drag something with the real mouse by (dx, dy), from its centre, holding Ctrl if asked. */
async function dragBy(
    page: Page,
    target: Locator,
    dx: number,
    dy: number,
    withCtrl = false,
): Promise<void> {
    await target.waitFor({ state: "visible", timeout: 30000 });
    const box = (await target.boundingBox())!;
    const x = box.x + box.width / 2;
    const y = box.y + box.height / 2;
    await page.mouse.move(x, y);
    if (withCtrl) await page.keyboard.down("Control");
    await page.mouse.down();
    await page.mouse.move(x + dx, y + dy, { steps: 12 });
    await page.mouse.up();
    if (withCtrl) await page.keyboard.up("Control");
}

/** Expect two numbers to be within `tolerance` pixels of each other. */
function expectNear(
    actual: number,
    expected: number,
    message: string,
    tolerance = 2,
): void {
    expect(Math.abs(actual - expected), message).toBeLessThanOrEqual(tolerance);
}

test("crop a picture from each side, move it inside its box, resize it and reset it [Test Case ID 307]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);

    const index = await step("Put a picture on a Canvas page", async () => {
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Canvas");
        const [canvasPage] = await getContentPages(page);
        await goToPage(page, canvasPage.id);
        const added = await dragPaletteItemOntoCanvas(page, "image", {
            xFraction: 0.5,
            yFraction: 0.4,
        });
        await chooseImageFile(page, PICTURE_FILE, canvasElement(page, added));
        await selectCanvasElement(page, added);
        // Large enough that, cropped on every side, its handles still stand clear of each other.
        // The drag stays on the canvas: one that ends beyond it leaves nothing selected.
        await dragCanvasElementCorner(page, "se", 60, 30);
        return added;
    });
    const element = canvasElement(page, index!);
    const picture = element.locator(".bloom-imageContainer img").first();

    await step(
        "See no move handle while the picture is not cropped",
        async () => {
            expect(
                (await getImagePlacement(page, element)).cropped,
                "Sanity check: the picture should start uncropped",
            ).toBe(false);
            await expect(
                moveCropHandle(page),
                "An uncropped picture should not show the move handle",
            ).toBeHidden();
        },
    );

    const inward: Record<Side, [number, number]> = {
        e: [-1, 0],
        w: [1, 0],
        n: [0, 1],
        s: [0, -1],
    };
    for (const side of ["e", "w", "n", "s"] as Side[]) {
        await step(`Crop from the ${side} side`, async () => {
            const boxBefore = await getBox(element, "the picture's box");
            const pictureBefore = await getBox(picture, "the picture");
            await cropImage(page, side, 20, element);
            const edge = { e: "right", w: "left", n: "top", s: "bottom" }[
                side
            ] as keyof IBox;
            await expect
                .poll(
                    async () =>
                        Math.abs(
                            (await getBox(element, "the picture's box"))[edge] -
                                boxBefore[edge],
                        ),
                    {
                        timeout: 15000,
                        message: `Cropping from the ${side} side should move that edge of the box in`,
                    },
                )
                .toBeGreaterThan(10);
            const boxAfter = await getBox(element, "the picture's box");
            const [dx, dy] = inward[side];
            expect(
                (boxAfter[edge] - boxBefore[edge]) * (dx + dy),
                `The ${side} edge should have moved inward`,
            ).toBeGreaterThan(0);
            for (const other of ["left", "top", "right", "bottom"] as const) {
                if (other === edge) continue;
                expectNear(
                    boxAfter[other],
                    boxBefore[other],
                    `Cropping from the ${side} side should not move the box's ${other} edge`,
                );
            }
            const pictureAfter = await getBox(picture, "the picture");
            for (const edgeName of ["left", "top", "right", "bottom"] as const)
                expectNear(
                    pictureAfter[edgeName],
                    pictureBefore[edgeName],
                    `Cropping from the ${side} side should not move the picture (${edgeName} edge)`,
                );
        });
    }

    await step(
        "Drag the east handle past the picture's edge, and see it stop at the picture's edge",
        async () => {
            // The handle is read while the mouse is still down, well beyond the picture's edge: that is
            // where the card says it must not go. The crop is 20 pixels, so 60 is far past the edge
            // while still on the canvas (a drag that ends beyond the canvas deselects the picture,
            // BL-17170).
            const handleBox = await getBox(
                sideHandle(page, "e"),
                "the east handle",
            );
            const x = (handleBox.left + handleBox.right) / 2;
            const y = (handleBox.top + handleBox.bottom) / 2;
            await page.mouse.move(x, y);
            await page.keyboard.down("Control");
            await page.mouse.down();
            await page.mouse.move(x + 60, y, { steps: 12 });
            const pictureBox = await getBox(picture, "the picture");
            await expect
                .poll(
                    async () => {
                        const handle = await getBox(
                            sideHandle(page, "e"),
                            "the east handle",
                        );
                        return Math.abs(
                            (handle.left + handle.right) / 2 - pictureBox.right,
                        );
                    },
                    {
                        timeout: 15000,
                        message:
                            "Dragged past the picture's edge, the east handle should stop at the picture's edge",
                    },
                )
                .toBeLessThanOrEqual(4);
            await page.mouse.up();
            await page.keyboard.up("Control");
            expectNear(
                (await getBox(element, "the picture's box")).right,
                pictureBox.right,
                "Dragging outward should uncrop the east side up to the picture's edge",
            );
        },
    );

    await step(
        "See the move handle, and move the picture inside its box",
        async () => {
            await expect(
                moveCropHandle(page),
                "A cropped picture should show the move handle",
            ).toBeVisible();
            const boxBefore = await getBox(element, "the picture's box");
            const pictureBefore = await getBox(picture, "the picture");
            // Still cropped on the west, north and south, so the picture can move right and down.
            await dragBy(page, moveCropHandle(page), 8, 8);
            for (const edge of ["left", "top"] as const) {
                const direction = edge === "left" ? "right" : "down";
                await expect
                    .poll(
                        async () =>
                            (await getBox(picture, "the picture"))[edge] -
                            pictureBefore[edge],
                        {
                            timeout: 15000,
                            message: `Dragging the move handle ${direction} should move the picture ${direction}`,
                        },
                    )
                    .toBeGreaterThan(2);
            }
            const boxAfter = await getBox(element, "the picture's box");
            for (const edge of ["left", "top", "right", "bottom"] as const)
                expectNear(
                    boxAfter[edge],
                    boxBefore[edge],
                    `Moving the picture should not move or resize its box (${edge} edge)`,
                );
        },
    );

    await step("Try to move the picture past its edges", async () => {
        // Each drag is three times the 20-pixel crop, well past where the picture's edge would
        // show, and stays on the canvas (see BL-17170). So the picture should go as far as it can:
        // right and down until its left and top edges reach the box's, then left and up until its
        // right and bottom edges do.
        const drags: [number, number, (keyof IBox)[]][] = [
            [60, 60, ["left", "top"]],
            [-60, -60, ["right", "bottom"]],
        ];
        for (const [dx, dy, edgesAtLimit] of drags) {
            const pictureBefore = await getBox(picture, "the picture");
            await dragBy(page, moveCropHandle(page), dx, dy);
            const box = await getBox(element, "the picture's box");
            const pictureBox = await getBox(picture, "the picture");
            for (const edge of edgesAtLimit) {
                expect(
                    Math.abs(pictureBox[edge] - pictureBefore[edge]),
                    `Sanity check: the drag should have moved the picture's ${edge} edge`,
                ).toBeGreaterThan(2);
                expectNear(
                    pictureBox[edge],
                    box[edge],
                    `Dragged past its limit, the picture's ${edge} edge should stop at the box's ${edge} edge`,
                );
            }
            expect(
                pictureBox.left,
                "The picture should still cover the box's left side",
            ).toBeLessThanOrEqual(box.left + 1);
            expect(
                pictureBox.top,
                "The picture should still cover the box's top",
            ).toBeLessThanOrEqual(box.top + 1);
            expect(
                pictureBox.right,
                "The picture should still cover the box's right side",
            ).toBeGreaterThanOrEqual(box.right - 1);
            expect(
                pictureBox.bottom,
                "The picture should still cover the box's bottom",
            ).toBeGreaterThanOrEqual(box.bottom - 1);
        }
    });

    await step(
        "Resize the cropped picture, and see the crop kept",
        async () => {
            const boxBefore = await getBox(element, "the picture's box");
            const pictureBefore = await getBox(picture, "the picture");
            await dragCanvasElementCorner(page, "se", 40, 40);
            const boxAfter = await getBox(element, "the picture's box");
            const pictureAfter = await getBox(picture, "the picture");
            const ratio = (b: IBox) => (b.right - b.left) / (b.bottom - b.top);
            expect(
                Math.abs(ratio(boxAfter) / ratio(boxBefore) - 1),
                "Resizing should keep the cropped box's shape",
            ).toBeLessThan(0.02);
            const share = (p: IBox, b: IBox) =>
                (b.right - b.left) / (p.right - p.left);
            expect(
                Math.abs(
                    share(pictureAfter, boxAfter) /
                        share(pictureBefore, boxBefore) -
                        1,
                ),
                "Resizing should keep how much of the picture is cropped away",
            ).toBeLessThan(0.02);
            // The same part of the picture should show: what is cropped off the left and the top
            // grows by as much as the picture does.
            const growth =
                (pictureAfter.right - pictureAfter.left) /
                (pictureBefore.right - pictureBefore.left);
            for (const edge of ["left", "top"] as const) {
                const croppedBefore = boxBefore[edge] - pictureBefore[edge];
                expect(
                    croppedBefore,
                    `Sanity check: the picture should be cropped on the ${edge} before resizing`,
                ).toBeGreaterThan(10);
                expectNear(
                    boxAfter[edge] - pictureAfter[edge],
                    croppedBefore * growth,
                    `Resizing should keep the same part of the picture showing (${edge} crop)`,
                );
            }
            expect(
                (await getImagePlacement(page, element)).cropped,
                "The picture should still be cropped after resizing",
            ).toBe(true);
        },
    );

    await step("Reset the picture from its menu", async () => {
        await resetSelectedImage(page);
        await expect
            .poll(
                async () => (await getImagePlacement(page, element)).cropped,
                {
                    timeout: 15000,
                    message: "Reset image should leave the picture uncropped",
                },
            )
            .toBe(false);
        await expect(
            moveCropHandle(page),
            "Once reset, the picture should not show the move handle",
        ).toBeHidden();
    });
});
