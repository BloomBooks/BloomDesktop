// Inline (Word-style) images are pictures placed inside a text block, with the text of the
// block wrapping around them. The header of bookEdit/js/inlineImages.ts describes the design
// and the editing code these helpers drive.
//
// The feature has no dialog. A person does everything to an inline image with the mouse, on the
// picture itself. The text block's right-click menu adds one. Dragging the picture decides which
// side it docks to and how far down the block it starts, and dragging a corner handle decides
// how wide it is. Selecting a picture also shows the same toolbar an image on a canvas has. So
// nearly every helper here makes a real mouse gesture and then waits until Bloom reaches the
// state the gesture should produce.
//
// Each inline image has one wrapper div in each language's block, and all the copies share the
// same value of data-bloom-inline-image-id. Everything about the picture's position and size is
// stored in one of the dock classes and in custom properties in the wrapper's style attribute
// (see the header of bookEdit/js/inlineImages.ts). getInlineImages reads those for each
// language, so a test can check that every language's copy is docked left at 40% without
// naming a class or a property.
//
// getInlineImages also reports some computed style, because much of the feature is in the CSS.
// The float and its shape-outside make the text flow beside the picture, display:flow-root
// keeps the float inside its block, and a rule hides the copies in every language except the
// first visible one. None of that shows in the markup, and the vitest suite cannot check it
// because it runs in jsdom. So `shown`, `float` and `wrapShape` come from getComputedStyle.

import { expect, type Locator, type Page } from "@playwright/test";
import * as fs from "node:fs";
import * as Path from "node:path";
import { apiPost } from "./api";
import { bookHtmlPath } from "./bookHtml";
import { editablePageFrame } from "./bookMaking";
import type { IRect } from "./geometry";
import { realClick, realClickAt } from "./realClick";

/**
 * Where an inline image sits in its block. "left" and "right" dock it against that edge with the
 * text wrapping beside it, "middle" makes it a full-width band with text above and below, and
 * "bottom" puts it below all the text.
 */
export type InlineImageDock = "left" | "right" | "middle" | "bottom";

/** The four corner handles of a selected inline image, by compass direction. */
export type InlineImageCorner = "nw" | "ne" | "sw" | "se";

/** The localization id of the "Insert Image" command on a text block's right-click menu. */
const kInsertImageCommand = "EditTab.InlineImage.InsertImage";

/** The localization id of the Delete command on an inline image's right-click menu. */
const kDeleteCommand = "Common.Delete";

/** The localization id of the command that opens the image chooser on an existing inline image. */
const kChooseImageCommand = "EditTab.Image.ChooseImage";

/**
 * The localization id of "Set Image Information...", the command on an existing inline image's menu
 * that opens the Copyright and License dialog for the picture.
 */
const kSetImageInformationCommand = "EditTab.Image.EditMetadataOverlay";

/** The localization ids of the Flip submenu on an inline image's menu, and of its two commands. */
const kFlipSubmenu = "EditTab.Image.Flip";
const kFlipCommand = {
    horizontal: "EditTab.Image.FlipHorizontal",
    vertical: "EditTab.Image.FlipVertical",
};

/** The localization id of Reset Image, which removes a flip. */
const kResetImageCommand = "EditTab.Image.Reset";

// The class names and attributes of the inline image markup. Only this file uses them; tests
// do not name any of them.
const kWrapperClass = "bloom-inlineImage";
const kIdAttribute = "data-bloom-inline-image-id";
const kSelectedClass = "bloom-inlineImage-selected";
const kHandleClass = "bloom-ui-inlineImage-handle";
// The toolbar under the selected picture, and the prefix Bloom puts on each of its buttons.
const kToolbarSelector = "#inline-image-context-controls";
const kToolbarButtonPrefix = "toolbar-";
const kDockClass: Record<InlineImageDock, string> = {
    left: "bloom-inlineImageLeft",
    right: "bloom-inlineImageRight",
    middle: "bloom-inlineImageMiddle",
    bottom: "bloom-inlineImageBottom",
};
const kWidthProperty = "--inline-image-width";
const kOffsetProperty = "--inline-image-offset";
const kAspectRatioProperty = "--inline-image-aspect-ratio";

/** One language's copy of one inline image, as the page being edited shows it. */
export interface IInlineImageState {
    /** The data-bloom-inline-image-id that every language's copy of this image shares. */
    id: string;
    /** The language of the text block this copy is in. */
    languageTag: string;
    /** Which side of the block the image is docked to. */
    dock: InlineImageDock;
    /** How wide the picture is, as a percentage of the block. */
    widthPercent: number;
    /** How far below the top of the block the picture starts, in the page's own pixels. */
    offsetPx: number;
    /** The picture's natural width/height, as the wrapper records it, e.g. "4 / 3". */
    aspectRatio: string;
    /**
     * The img element's CSS transform, which is where Flip records a mirror image. It is
     * "scale(-1, 1)" after Flip horizontal, "scale(1, -1)" after Flip vertical, and "" for a
     * picture that has not been flipped.
     */
    pictureTransform: string;
    /** The file name in the picture's src; "placeHolder.png" while no picture has been chosen. */
    fileName: string;
    /**
     * The picture's data-copyright attribute, which is where Bloom records the image's credits on
     * the page; empty when it has none. The toolbar's missing-information warning shows while it
     * is empty.
     */
    copyright: string;
    /**
     * The wrapper's contenteditable attribute. It must stay "false" in the saved markup. Without
     * it, the code in TranslationGroupManager.cs that sets the lang attribute on the editable
     * elements of a block would treat the wrapper as a text box.
     */
    contentEditable: string | null;
    /** The wrapper's index among the block's children, and how many children there are. */
    slot: { index: number; childCount: number };
    /** True when this copy is selected, so its corner handles are showing. */
    selected: boolean;
    /** How many corner handles the copy has. Four when it is selected, none when it is not. */
    handleCount: number;
    /**
     * True when a reader would see this copy of the picture. It is false when the CSS hides the
     * copy, which it does in every visible language except the first, and false when the whole
     * block is hidden. So the copy in the lang="z" prototype block is never shown.
     */
    shown: boolean;
    /** The wrapper's computed float: "left", "right" or "none". */
    float: string;
    /** The wrapper's computed shape-outside, which is what makes the text follow the picture. */
    wrapShape: string;
    /** Every class on the wrapper, for the rare assertion that has to look at one by name. */
    classes: string[];
}

/** A text block of a translation group, and the inline images in it. */
export interface IBlockInlineImages {
    languageTag: string;
    /** True when Bloom is showing this language's block on the page. */
    visible: boolean;
    /**
     * The block's computed display. An editable holding an inline image must be "flow-root", or
     * the floated picture can extend below the bottom of the block (see the comment above
     * `display: flow-root` in content/bookLayout/inlineImages.less).
     */
    display: string;
    images: IInlineImageState[];
}

/**
 * Every inline image of one translation group on the page being edited, one entry per language
 * block, in the order the blocks appear in the markup. `groupSelector` picks the group, e.g.
 * ".bloom-translationGroup" for the only one on a Just Text page.
 *
 * The lang="z" prototype block is included, because a language added later gets the image by
 * copying that block, without any C# code having to add it (see insertInlineImage in
 * inlineImages.ts).
 */
export async function getInlineImages(
    page: Page,
    groupSelector: string,
): Promise<IBlockInlineImages[]> {
    const group = editablePageFrame(page).locator(groupSelector).first();
    await group.waitFor({ state: "attached", timeout: 30000 });
    return group.evaluate(
        (
            element,
            markup: {
                wrapperClass: string;
                idAttribute: string;
                selectedClass: string;
                handleClass: string;
                dockClass: Record<string, string>;
                widthProperty: string;
                offsetProperty: string;
                aspectRatioProperty: string;
            },
        ) => {
            const blocks = Array.from(
                element.querySelectorAll(":scope > .bloom-editable"),
            ) as HTMLElement[];
            return blocks.map((block) => {
                const children = Array.from(block.children);
                const wrappers = children.filter((child) =>
                    child.classList.contains(markup.wrapperClass),
                ) as HTMLElement[];
                return {
                    languageTag: block.getAttribute("lang") ?? "",
                    visible: block.classList.contains(
                        "bloom-visibility-code-on",
                    ),
                    display: window.getComputedStyle(block).display,
                    images: wrappers.map((wrapper) => {
                        const style = window.getComputedStyle(wrapper);
                        const picture = wrapper.querySelector("img");
                        const source = picture?.getAttribute("src") ?? "";
                        const dockEntry = Object.entries(markup.dockClass).find(
                            ([, className]) =>
                                wrapper.classList.contains(className),
                        );
                        const readNumber = (property: string) =>
                            parseFloat(
                                wrapper.style.getPropertyValue(property),
                            ) || 0;
                        return {
                            id: wrapper.getAttribute(markup.idAttribute) ?? "",
                            languageTag: block.getAttribute("lang") ?? "",
                            // The wrapper always has exactly one dock class. A missing one could
                            // only come from hand-edited markup, and reporting that is better
                            // than reporting a dock the image is not in.
                            dock: dockEntry ? dockEntry[0] : "(no dock class)",
                            widthPercent: readNumber(markup.widthProperty),
                            offsetPx: readNumber(markup.offsetProperty),
                            aspectRatio: wrapper.style
                                .getPropertyValue(markup.aspectRatioProperty)
                                .trim(),
                            pictureTransform: picture?.style.transform ?? "",
                            // The query is dropped. Bloom appends "?transparent=yes" to an image
                            // on a page with a coloured background (getImageTransparencyMode),
                            // so a picture chosen on the front cover has it and the same picture
                            // on a content page does not. This field only says which file it is.
                            fileName: decodeURIComponent(
                                (source.split("?")[0].split("/").pop() ??
                                    source) as string,
                            ),
                            copyright:
                                picture?.getAttribute("data-copyright") ?? "",
                            contentEditable:
                                wrapper.getAttribute("contenteditable"),
                            slot: {
                                index: children.indexOf(wrapper),
                                childCount: children.length,
                            },
                            selected: wrapper.classList.contains(
                                markup.selectedClass,
                            ),
                            handleCount: wrapper.querySelectorAll(
                                "." + markup.handleClass,
                            ).length,
                            // This uses checkVisibility() because the wrapper's own display
                            // stays the same when its block is hidden.
                            shown: wrapper.checkVisibility(),
                            float: style.float,
                            wrapShape: style.shapeOutside,
                            classes: Array.from(wrapper.classList),
                        };
                    }),
                };
            });
        },
        {
            wrapperClass: kWrapperClass,
            idAttribute: kIdAttribute,
            selectedClass: kSelectedClass,
            handleClass: kHandleClass,
            dockClass: kDockClass,
            widthProperty: kWidthProperty,
            offsetProperty: kOffsetProperty,
            aspectRatioProperty: kAspectRatioProperty,
        },
    ) as Promise<IBlockInlineImages[]>;
}

/**
 * One language's copy of one inline image. When there is no such copy, this throws an error that
 * lists what the group does hold, because a missing copy is a different bug from a copy in the
 * wrong position or size.
 */
export async function getInlineImage(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Promise<IInlineImageState> {
    const blocks = await getInlineImages(page, groupSelector);
    const found = blocks
        .find((block) => block.languageTag === languageTag)
        ?.images.find((image) => image.id === id);
    if (!found)
        throw new Error(
            `The "${languageTag}" block of "${groupSelector}" has no inline image ${id}. ` +
                `The group holds: ${describeBlocks(blocks)}.`,
        );
    return found;
}

/** Every copy of one inline image, one per language block that has it. */
export async function getInlineImageInEveryLanguage(
    page: Page,
    groupSelector: string,
    id: string,
): Promise<IInlineImageState[]> {
    const blocks = await getInlineImages(page, groupSelector);
    return blocks.flatMap((block) =>
        block.images.filter((image) => image.id === id),
    );
}

/**
 * Add an inline image to a text block the way a person does: right-click in its text and choose
 * Insert Image. Returns the new image's data-bloom-inline-image-id, which every language's copy
 * of it shares.
 *
 * The command does not open the image chooser on purpose (the person chooses a picture afterwards,
 * from the same menu), so the new image is a placeholder, docked right at the default width. It
 * appears in every language's block at once, including the prototype, and this waits until it
 * has.
 */
export async function addInlineImage(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<string> {
    const before = await getInlineImages(page, groupSelector);
    const known = new Set(
        before.flatMap((block) => block.images.map((image) => image.id)),
    );
    await openInlineImageMenuInText(page, groupSelector, languageTag);
    await clickInlineImageMenuCommand(page, kInsertImageCommand);
    const wanted = before.length;
    await expect
        .poll(
            async () =>
                (await getInlineImages(page, groupSelector)).filter((block) =>
                    block.images.some((image) => !known.has(image.id)),
                ).length,
            {
                timeout: 30000,
                message:
                    `Insert Image did not put a new inline image in all ${wanted} language blocks ` +
                    `of "${groupSelector}".`,
            },
        )
        .toBe(wanted);
    const after = await getInlineImages(page, groupSelector);
    const added = after
        .flatMap((block) => block.images)
        .find((image) => !known.has(image.id))!;
    return added.id;
}

/**
 * Delete an inline image the way a person does, by right-clicking the picture and choosing Delete.
 * That removes this image's copy from every language and leaves any other inline image in the
 * block alone. Returns once every copy has gone.
 */
export async function deleteInlineImage(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Promise<void> {
    await openInlineImageMenu(page, groupSelector, languageTag, id);
    await clickInlineImageMenuCommand(page, kDeleteCommand);
    await expect
        .poll(
            async () =>
                (await getInlineImageInEveryLanguage(page, groupSelector, id))
                    .length,
            {
                timeout: 30000,
                message: `Delete left copies of inline image ${id} behind.`,
            },
        )
        .toBe(0);
}

/**
 * Flip an inline image the way a person does, by right-clicking the picture, resting the pointer
 * on Flip, and choosing Flip horizontal or Flip vertical. Returns once every language's copy of
 * the picture has the same new transform.
 */
export async function flipInlineImage(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
    axis: "horizontal" | "vertical",
): Promise<void> {
    const before = (await getInlineImage(page, groupSelector, languageTag, id))
        .pictureTransform;
    await openInlineImageMenu(page, groupSelector, languageTag, id);
    await clickInlineImageSubmenuCommand(
        page,
        kFlipSubmenu,
        kFlipCommand[axis],
    );
    await expect
        .poll(
            async () => {
                const transforms = (
                    await getInlineImageInEveryLanguage(page, groupSelector, id)
                ).map((copy) => copy.pictureTransform);
                return transforms.every(
                    (one) => one === transforms[0] && one !== before,
                );
            },
            {
                timeout: 30000,
                message:
                    `Flip ${axis} did not leave every copy of inline image ${id} with one new ` +
                    `transform.`,
            },
        )
        .toBe(true);
}

/**
 * Remove a flip the way a person does, by right-clicking the picture and choosing Reset Image.
 * Returns once no copy of the picture has a transform.
 */
export async function resetInlineImage(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Promise<void> {
    await openInlineImageMenu(page, groupSelector, languageTag, id);
    await clickInlineImageMenuCommand(page, kResetImageCommand);
    await expect
        .poll(
            async () =>
                (
                    await getInlineImageInEveryLanguage(page, groupSelector, id)
                ).every((copy) => copy.pictureTransform === ""),
            {
                timeout: 30000,
                message: `Reset Image left a transform on a copy of inline image ${id}.`,
            },
        )
        .toBe(true);
}

/**
 * The commands an inline image's right-click menu offers, by localization id, each with whether it
 * is enabled. Leaves the menu open; close it with closeInlineImageMenu.
 *
 * A command that needs a subscription tier the collection does not have counts as not enabled.
 * Bloom leaves such an item clickable and marks it with data-subscription-gated instead of
 * disabling it (helpers/canvasElements.ts handles the canvas element menu the same way).
 */
export async function getInlineImageMenuCommands(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Promise<{ id: string; enabled: boolean }[]> {
    await openInlineImageMenu(page, groupSelector, languageTag, id);
    const frame = editablePageFrame(page);
    await expect(
        frame.locator(`${kMenuSelector} >> li[data-feature-status-pending]`),
        "Some inline image menu commands never learned whether their feature is on offer.",
    ).toHaveCount(0, { timeout: 30000 });
    return frame
        .locator(`${kMenuSelector} >> li[role="menuitem"]`)
        .evaluateAll((items) =>
            items.map((item) => ({
                id: item.getAttribute("data-testid") ?? "",
                enabled:
                    !item.classList.contains("Mui-disabled") &&
                    !item.hasAttribute("data-subscription-gated"),
            })),
        );
}

/**
 * The buttons showing on the selected inline image's toolbar, by control id, each with whether it
 * is enabled. The list is empty when no toolbar is showing, which means nothing is selected.
 *
 * The "..." button that opens the menu is left out because it is not one of the controls; read
 * the menu itself with getInlineImageMenuCommands. Which buttons appear depends on the picture.
 * For instance, the button that warns about missing copyright and license information only
 * appears while that information is missing. So a test should look for the buttons it cares
 * about instead of comparing the whole list.
 */
export async function getInlineImageToolbarButtons(
    page: Page,
): Promise<{ id: string; enabled: boolean }[]> {
    const bar = editablePageFrame(page).locator(kToolbarSelector);
    if (!(await bar.isVisible().catch(() => false))) return [];
    return bar
        .locator(`button[data-testid^="${kToolbarButtonPrefix}"]`)
        .evaluateAll((buttons) =>
            buttons.map((button) => ({
                id: (button.getAttribute("data-testid") ?? "").replace(
                    /^toolbar-/,
                    "",
                ),
                enabled: !(button as HTMLButtonElement).disabled,
            })),
        );
}

/**
 * Where the inline image toolbar is on the screen, or undefined when no toolbar is showing. Use it
 * to check that the toolbar is next to the picture the person selected, which a list of button
 * names cannot show.
 */
export async function getInlineImageToolbarRect(
    page: Page,
): Promise<IRect | undefined> {
    const bar = editablePageFrame(page).locator(kToolbarSelector);
    if (!(await bar.isVisible().catch(() => false))) return undefined;
    return (await bar.boundingBox()) ?? undefined;
}

/**
 * Open the Copyright and License dialog for an inline image's picture the way a person does, by
 * right-clicking the picture and choosing "Set Image Information...". The dialog opens in the
 * shell outside the page, so drive it with helpers/copyrightAndLicense.ts.
 */
export async function openInlineImageInformation(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Promise<void> {
    await openInlineImageMenu(page, groupSelector, languageTag, id);
    await clickInlineImageMenuCommand(page, kSetImageInformationCommand);
}

/** True when the block's right-click menu offers to add an inline image at all. */
export async function textBlockOffersInsertImage(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<boolean> {
    await openInlineImageMenuInText(page, groupSelector, languageTag);
    const offered =
        (await editablePageFrame(page)
            .locator(
                `${kMenuSelector} >> li[data-testid="${kInsertImageCommand}"]`,
            )
            .count()) > 0;
    await closeInlineImageMenu(page);
    return offered;
}

/**
 * Dismiss the text block's right-click menu without choosing anything, the way clicking away from
 * it does.
 *
 * Pressing Escape on the page does not close it. The menu opens without taking focus, so that the
 * caret stays in the text the person right-clicked on (disableAutoFocus in TextContextMenu), and
 * so a key press goes to the page and the menu never sees it. This clicks the invisible backdrop
 * the menu puts over the page, which is what a click away from the menu lands on. Only if there is
 * no backdrop does it press Escape on the menu itself.
 */
export async function closeInlineImageMenu(page: Page): Promise<void> {
    const frame = editablePageFrame(page);
    const menu = frame.locator(kMenuSelector).first();
    if (!(await menu.isVisible().catch(() => false))) return;
    const backdrop = frame.locator(kMenuBackdropSelector).first();
    if ((await backdrop.count()) > 0) await backdrop.click({ force: true });
    else await menu.press("Escape");
    await menu.waitFor({ state: "hidden", timeout: 30000 });
}

/**
 * Select an inline image the way a person does, with a click on the picture, and wait until its
 * four corner handles are showing. The drag and resize helpers below select the picture
 * themselves, so call this directly only when the test is about selecting.
 */
export async function selectInlineImage(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Promise<void> {
    const picture = inlineImagePicture(page, groupSelector, languageTag, id);
    // This uses a real mouse press because Playwright's own click does not work here. The picture
    // is inside a contenteditable that CKEditor manages, and the code that selects it listens for
    // pointerdown in the capture phase (setupInlineImageInteractions) and cancels the mousedown
    // that would otherwise put the caret in the text.
    await realClick(picture);
    await expect
        .poll(
            async () =>
                (await getInlineImage(page, groupSelector, languageTag, id))
                    .handleCount,
            {
                timeout: 30000,
                message: `Clicking inline image ${id} did not select it.`,
            },
        )
        .toBe(4);
}

/**
 * Scroll the block back to the top, so the start of its text is in view.
 *
 * Typing leaves the caret at the end of the text, and a block too small for its text scrolls to
 * the caret. That hides the top of the block and any picture there. A gesture aimed at a picture
 * that is scrolled out of view lands on whatever is showing instead, and looks as if the picture
 * would not move. Tests call this to set up a page, and scrolling is not what they test, so it
 * sets scrollTop directly instead of using the mouse wheel.
 */
export async function scrollBlockToTop(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<void> {
    await editablePageFrame(page)
        .locator(`${groupSelector} .bloom-editable[lang="${languageTag}"]`)
        .first()
        .evaluate((block) => {
            block.scrollTop = 0;
        });
}

/**
 * Scroll the page so that one language's copy of an inline image is at the top of the window,
 * which leaves the most room below it for a drag downward. In a small window, like the nightly
 * runner's, a zoomed page can otherwise put the picture, or the place a drag ends, out of sight,
 * and a real mouse can only press on what the window shows.
 */
export async function scrollInlineImageToTop(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Promise<void> {
    await inlineImagePicture(page, groupSelector, languageTag, id).evaluate(
        (picture) => picture.scrollIntoView({ block: "start" }),
    );
}

/**
 * Drag an inline image up the block, the way a person does, and return the offset it ends at.
 *
 * This is separate from dragInlineImageDown because its failure means something different: an
 * image that will not come back up is stuck. That is most likely in a block too small for its
 * text. If the image does not move, the failure message gives the offset it is still at.
 */
export async function dragInlineImageUp(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
    pixels: number,
): Promise<number> {
    const before = await getInlineImage(page, groupSelector, languageTag, id);
    const picture = await pictureRect(page, groupSelector, languageTag, id);
    await dragInlineImageTo(page, groupSelector, languageTag, id, {
        x: picture.x + picture.width / 2,
        y: picture.y + picture.height / 2 - pixels,
    });
    await expect
        .poll(
            async () =>
                (await getInlineImage(page, groupSelector, languageTag, id))
                    .offsetPx,
            {
                timeout: 30000,
                message:
                    `Dragging inline image ${id} up ${pixels}px did not move it: it still ` +
                    `starts ${before.offsetPx}px below the top of the block. An image that ` +
                    `cannot be dragged back up is stuck where it sits.`,
            },
        )
        .toBeLessThan(before.offsetPx);
    return (await getInlineImage(page, groupSelector, languageTag, id))
        .offsetPx;
}

/**
 * How much more height the block's text needs than the block has, in layout pixels. Zero when the
 * text fits. This is the condition Bloom's overflow warning is about, so a test that needs an
 * overflowing block should check this before it starts.
 */
export async function getBlockScrollOverflowPx(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<number> {
    return editablePageFrame(page)
        .locator(`${groupSelector} .bloom-editable[lang="${languageTag}"]`)
        .first()
        .evaluate((block) => block.scrollHeight - block.clientHeight);
}

/**
 * Whether Bloom has marked this block as holding more text than it can show, which is what gives
 * the person the red marking and the warning. getBlockScrollOverflowPx measures the overflow;
 * this reads Bloom's verdict. OverflowChecker adds the "overflow" class, and it checks every
 * editable when the page loads (AddOverflowHandlers ends by scheduling a check for each one), so
 * this reports what a person sees on arriving at the page.
 */
export async function blockIsMarkedOverflowing(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<boolean> {
    return editablePageFrame(page)
        .locator(`${groupSelector} .bloom-editable[lang="${languageTag}"]`)
        .first()
        .evaluate((block) => block.classList.contains("overflow"));
}

/**
 * Drag an inline image to a dock, the way a person does, by pressing on the picture, moving it into
 * the part of the block that means that dock, and letting go. Returns once every language's copy
 * is in that dock.
 *
 * computeInlineImageDock decides the dock from where the picture ends up, and the position of
 * the mouse pointer does not matter. It divides the block's width into thirds: the left and right
 * thirds mean the left and right docks, and the middle third means the full-width band. Within
 * the middle third, the bottom dock applies once the band no longer fits the picture inside the
 * block's content, and anywhere below the block means the bottom dock whatever the horizontal
 * position. This helper aims at the middle of the region for the dock asked for, so the caller
 * only names the dock.
 *
 * Bloom puts the image back where it started if the move would make the block overflow. When that
 * happens, the failure message gives the dock the image is still in. It means the caller's block
 * is too small for the move it asked for.
 */
export async function dragInlineImageToDock(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
    dock: InlineImageDock,
): Promise<void> {
    const block = await blockRect(page, groupSelector, languageTag);
    const picture = await pictureRect(page, groupSelector, languageTag, id);
    // Count the copies before the drag. The wait below checks that every language ends up in
    // the new dock, and a count taken afterwards could not tell all the copies from only the one
    // the drag happened in.
    const copyCount = (
        await getInlineImageInEveryLanguage(page, groupSelector, id)
    ).length;
    expect(
        copyCount,
        `inline image ${id} should exist in at least one language before the drag`,
    ).toBeGreaterThan(0);
    // Aim at the horizontal middle of the region that means this dock, and keep the picture at
    // the height it already had, because a drop lower down the block would also change how far
    // down the picture starts. The bottom dock is different: the drop goes just below the block,
    // which means the bottom dock however tall the block is and whatever else is in it.
    const acrossFraction = {
        left: 1 / 6,
        middle: 0.5,
        right: 5 / 6,
        bottom: 0.5,
    }[dock];
    await dragInlineImageTo(page, groupSelector, languageTag, id, {
        x: block.x + block.width * acrossFraction,
        y:
            dock === "bottom"
                ? block.y + block.height * 1.05
                : picture.y + picture.height / 2,
    });
    await expect
        .poll(
            async () =>
                (
                    await getInlineImageInEveryLanguage(page, groupSelector, id)
                ).filter((image) => image.dock === dock).length,
            {
                timeout: 30000,
                message:
                    `Dragging inline image ${id} into the ${dock} region of the block did not ` +
                    `dock it there in all ${copyCount} of the group's languages. Bloom puts a ` +
                    `move back where it started when the move would make the block overflow, ` +
                    `and syncs the languages only when the gesture commits.`,
            },
        )
        .toBe(copyCount);
}

/**
 * Drag an inline image down its block by `pixels`, the way a person does, and return how far
 * below the top of the block it now starts. Dragging down within a side dock moves the picture
 * past the first lines of text, and the distance is stored in the --inline-image-offset custom
 * property.
 *
 * Bloom limits the distance so the whole picture stays inside the block, so the result may be
 * less than what was asked for, and that is fine. This only waits for the distance to change.
 */
export async function dragInlineImageDown(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
    pixels: number,
): Promise<number> {
    const before = await getInlineImage(page, groupSelector, languageTag, id);
    const picture = await pictureRect(page, groupSelector, languageTag, id);
    await dragInlineImageTo(page, groupSelector, languageTag, id, {
        x: picture.x + picture.width / 2,
        y: picture.y + picture.height / 2 + pixels,
    });
    await expect
        .poll(
            async () =>
                (await getInlineImage(page, groupSelector, languageTag, id))
                    .offsetPx,
            {
                timeout: 30000,
                message:
                    `Dragging inline image ${id} down ${pixels}px did not move it: it still ` +
                    `starts ${before.offsetPx}px below the top of the block. Bloom puts a move ` +
                    `back where it started when the move would make the block overflow.`,
            },
        )
        .not.toBe(before.offsetPx);
    return (await getInlineImage(page, groupSelector, languageTag, id))
        .offsetPx;
}

/**
 * Drag an inline image down its block by `pixels` and report where it ended up, without requiring
 * that it moved.
 *
 * dragInlineImageDown fails when the picture does not move. This one is for moving a picture down
 * the block a step at a time to find out which positions the block allows, so a step that changes
 * the dock, or that Bloom refuses, is a result to report.
 */
export async function nudgeInlineImageDown(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
    pixels: number,
): Promise<{ dock: InlineImageDock; offsetPx: number; roomBelowPx: number }> {
    const picture = await pictureRect(page, groupSelector, languageTag, id);
    await dragInlineImageTo(page, groupSelector, languageTag, id, {
        x: picture.x + picture.width / 2,
        y: picture.y + picture.height / 2 + pixels,
    });
    const image = await getInlineImage(page, groupSelector, languageTag, id);
    return {
        dock: image.dock,
        offsetPx: image.offsetPx,
        roomBelowPx: await getRoomBelowInlineImagePx(
            page,
            groupSelector,
            languageTag,
            id,
        ),
    };
}

/**
 * Press on the picture and keep the button down, which starts a drag. Use it with
 * moveInlineImageDragTo and endInlineImageDrag.
 *
 * The other drag helpers make the whole gesture and return the result, so they cannot check what
 * the page does while the picture is being held, such as whether the block scrolls to follow it.
 */
export async function beginInlineImageDrag(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Promise<void> {
    const picture = await pictureRect(page, groupSelector, languageTag, id);
    await page.mouse.move(
        picture.x + picture.width / 2,
        picture.y + picture.height / 2,
    );
    await page.mouse.down();
}

/** Move the pointer during a drag begun with beginInlineImageDrag, without letting go. */
export async function moveInlineImageDragTo(
    page: Page,
    to: { x: number; y: number },
): Promise<void> {
    await page.mouse.move(to.x, to.y, { steps: 16 });
}

/** Let go, ending a drag begun with beginInlineImageDrag. */
export async function endInlineImageDrag(page: Page): Promise<void> {
    await page.mouse.up();
}

/**
 * Whether the page still thinks a picture is being dragged. A drag puts the
 * bloom-inlineImage-dragging class on the page's body and removes it when the drag ends, so a test
 * can use this to check that a drag that should have ended did end.
 */
export async function inlineImageDragIsInProgress(
    page: Page,
): Promise<boolean> {
    return editablePageFrame(page)
        .locator("body")
        .evaluate((body) =>
            body.classList.contains("bloom-inlineImage-dragging"),
        );
}

/**
 * How far the block's text is scrolled down, in the page's own pixels. Zero when the top of the
 * text is showing. A block only scrolls when its text does not fit it.
 */
export async function getBlockScrollTopPx(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<number> {
    return editablePageFrame(page)
        .locator(`${groupSelector} .bloom-editable[lang="${languageTag}"]`)
        .first()
        .evaluate((block) => block.scrollTop);
}

/**
 * How much room is left between the bottom of the picture and the end of everything its block
 * holds, in the page's own pixels. That room is taken up by the text that still follows the
 * picture, so less than one line means the picture has reached the end of the block's content.
 * Tests use this to check that a person can move the picture as far down the block as they want.
 *
 * This measures to the end of the block's content, which can be below the block's rectangle. A
 * block too small for its text scrolls, and the picture can still be put after the text that is
 * scrolled out of view.
 */
export async function getRoomBelowInlineImagePx(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Promise<number> {
    return inlineImageWrapper(page, groupSelector, languageTag, id).evaluate(
        (wrapper) => {
            const block = wrapper.closest(".bloom-editable") as HTMLElement;
            const blockBox = block.getBoundingClientRect();
            // The page is drawn at some zoom, so the block's rectangle and its clientHeight
            // measure the same edge in different units; their ratio converts between them.
            const viewportPxPerLayoutPx = blockBox.height / block.clientHeight;
            const contentBottomViewportPx =
                blockBox.top +
                (block.scrollHeight - block.scrollTop) * viewportPxPerLayoutPx;
            return (
                (contentBottomViewportPx -
                    wrapper.getBoundingClientRect().bottom) /
                viewportPxPerLayoutPx
            );
        },
    );
}

/** The height of one line of the block's text, in the page's own pixels. */
export async function getBlockLineHeightPx(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<number> {
    return editablePageFrame(page)
        .locator(`${groupSelector} .bloom-editable[lang="${languageTag}"]`)
        .first()
        .evaluate((block) => {
            const lineHeight = getComputedStyle(block).lineHeight;
            // "normal" has no number in it. The ratio Chromium uses for it is close enough to
            // 1.2 for a test that checks whether a line's worth of room is left.
            return lineHeight.endsWith("px")
                ? parseFloat(lineHeight)
                : parseFloat(getComputedStyle(block).fontSize) * 1.2;
        });
}

/**
 * Make an inline image wider or narrower by dragging one of its corner handles, the way a person
 * does, and return the width it reaches as a percentage of the block. Only the horizontal
 * movement matters, because the picture keeps its aspect ratio, so this pulls the corner sideways
 * (see computeInlineImageWidthPercent).
 *
 * `pixels` is how far the corner moves outward, which grows the picture; pass a negative number
 * to shrink it. Bloom keeps the width between 10% and 95% of the block.
 */
export async function resizeInlineImage(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
    corner: InlineImageCorner,
    pixels: number,
): Promise<number> {
    await selectInlineImage(page, groupSelector, languageTag, id);
    const before = await getInlineImage(page, groupSelector, languageTag, id);
    const handle = inlineImageWrapper(page, groupSelector, languageTag, id)
        .locator(`.${kHandleClass}-${corner}`)
        .first();
    await handle.waitFor({ state: "visible", timeout: 30000 });
    const box = await handle.boundingBox({ timeout: 30000 });
    if (!box)
        throw new Error(
            `The ${corner} handle of inline image ${id} has no box, so there is nowhere to ` +
                `drag from.`,
        );
    // Outward is away from the picture, which is to the right for the eastern corners and to the
    // left for the western ones (getInlineImageHandleHorizontalSign).
    const outward = corner === "ne" || corner === "se" ? 1 : -1;
    const x = box.x + box.width / 2;
    const y = box.y + box.height / 2;
    await page.mouse.move(x, y);
    await page.mouse.down();
    await page.mouse.move(x + outward * pixels, y, { steps: 12 });
    await page.mouse.up();
    await expect
        .poll(
            async () =>
                (await getInlineImage(page, groupSelector, languageTag, id))
                    .widthPercent,
            {
                timeout: 30000,
                message:
                    `Dragging the ${corner} handle of inline image ${id} by ${pixels}px did not ` +
                    `change its width: it is still ${before.widthPercent}% of the block.`,
            },
        )
        .not.toBe(before.widthPercent);
    return (await getInlineImage(page, groupSelector, languageTag, id))
        .widthPercent;
}

/**
 * Put the picture at `filePath` into an inline image, and wait until every language's copy shows
 * it.
 *
 * Like chooseImageFile in helpers/images.ts, this skips the user interface, because choosing a
 * picture in Bloom ends in a native file picker, which hangs a test run (AUTOMATION-DEBT.md,
 * "Native OS dialogs hang automation"). Instead it does what the chooser does after a picture has
 * been chosen. Bloom's imageGallery/imageGalleryResult endpoint copies the file into the book,
 * and changeImageByElement puts it in the img element the chooser was opened on. So everything
 * Bloom does after a picture is chosen still runs, including copying the new picture to the other
 * languages' wrappers (handleInlineImageChanged).
 */
export async function changeInlineImagePicture(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
    filePath: string,
): Promise<void> {
    const result = await apiPost(
        page,
        "imageGallery/imageGalleryResult",
        JSON.stringify({ localPath: filePath, provider: "local-disk" }),
        "application/json",
    );
    const info = JSON.parse(result.body) as { src: string };
    const picture = inlineImagePicture(page, groupSelector, languageTag, id);
    await picture.waitFor({ state: "attached", timeout: 30000 });
    // As in dragInlineImageToDock, count the copies before the change, so that losing a copy
    // cannot pass as success.
    const copyCount = (
        await getInlineImageInEveryLanguage(page, groupSelector, id)
    ).length;
    expect(
        copyCount,
        `inline image ${id} should exist in at least one language before its picture changes`,
    ).toBeGreaterThan(0);
    await picture.evaluate(
        (element, imageInfo) => {
            const bundle = (
                window as unknown as {
                    editablePageBundle: {
                        changeImageByElement: (
                            img: HTMLElement,
                            info: typeof imageInfo,
                        ) => void;
                    };
                }
            ).editablePageBundle;
            bundle.changeImageByElement(element as HTMLElement, imageInfo);
        },
        { ...info, undoable: "false" },
    );
    const fileName = Path.basename(decodeURIComponent(info.src));
    await expect
        .poll(
            async () => {
                const copies = await getInlineImageInEveryLanguage(
                    page,
                    groupSelector,
                    id,
                );
                return copies.filter((copy) => copy.fileName === fileName)
                    .length;
            },
            {
                timeout: 30000,
                message:
                    `${fileName} never reached all ${copyCount} of the languages' copies of ` +
                    `inline image ${id}.`,
            },
        )
        .toBe(copyCount);
}

/**
 * True when the text of a block flows beside the picture instead of starting below it, meaning
 * some line of text has its top above the bottom of the picture and lies to the side of it.
 *
 * Text wrapping is the purpose of the feature, and only a real browser can show it, because the
 * float and its shape-outside do nothing in jsdom. This measures the client rectangles of the text
 * itself, one per line. The paragraph's box would not do, because it spans the full width of the
 * block whether the text wraps or not.
 */
export async function textWrapsBesideInlineImage(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Promise<boolean> {
    const block = inlineImageBlock(page, groupSelector, languageTag);
    return block.evaluate(
        (element, markup) => {
            const wrapper = element.querySelector(
                `[${markup.idAttribute}="${markup.id}"]`,
            ) as HTMLElement | null;
            const picture = wrapper?.querySelector("img");
            if (!picture) return false;
            const pictureBox = picture.getBoundingClientRect();
            // A Range over a text node reports one rectangle for each line of it.
            const lines: DOMRect[] = [];
            const walker = document.createTreeWalker(
                element,
                NodeFilter.SHOW_TEXT,
            );
            while (walker.nextNode()) {
                const node = walker.currentNode;
                if (wrapper?.contains(node)) continue;
                if (!node.textContent?.trim()) continue;
                const range = document.createRange();
                range.selectNodeContents(node);
                lines.push(...Array.from(range.getClientRects()));
            }
            return lines.some(
                (line) =>
                    line.width > 0 &&
                    line.top < pictureBox.bottom - 1 &&
                    line.bottom > pictureBox.top + 1 &&
                    (line.right <= pictureBox.left + 1 ||
                        line.left >= pictureBox.right - 1),
            );
        },
        { idAttribute: kIdAttribute, id },
    );
}

/** One language's copy of one inline image, as the saved .htm file records it. */
export interface ISavedInlineImage {
    id: string;
    languageTag: string;
    /** The classes on the wrapper, which include the dock class. */
    classes: string[];
    /** The whole style attribute, which holds the picture's width, offset and aspect ratio. */
    style: string;
    /** The picture's src, relative to the book folder. */
    source: string;
    /** The picture's data-copyright attribute, which holds its credits; empty when there are none. */
    copyright: string;
    contentEditable: string | null;
    /** The wrapper's index among the block's children, and how many children there are. */
    slot: { index: number; childCount: number };
}

/**
 * Every inline image in the saved book on disk, in markup order. The saved file is the only place
 * to check what survives a save, because the page being edited also has things that are never
 * saved, such as the corner handles and the bloom-inlineImage-selected class. Bloom saves a page
 * only when the person leaves it, so call goToPage first.
 *
 * `page` is used only to parse the HTML, as helpers/bookHtml.ts does.
 */
export async function readSavedInlineImages(
    page: Page,
    bookFolder: string,
): Promise<ISavedInlineImage[]> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate(
        ({ source, idAttribute, wrapperClass }) => {
            const document = new DOMParser().parseFromString(
                source,
                "text/html",
            );
            return Array.from(
                document.querySelectorAll("." + wrapperClass),
            ).map((wrapper) => {
                const block = wrapper.parentElement;
                const children = Array.from(block?.children ?? []);
                return {
                    id: wrapper.getAttribute(idAttribute) ?? "",
                    languageTag: block?.getAttribute("lang") ?? "",
                    classes: Array.from(wrapper.classList),
                    style: wrapper.getAttribute("style") ?? "",
                    source:
                        wrapper.querySelector("img")?.getAttribute("src") ?? "",
                    copyright:
                        wrapper
                            .querySelector("img")
                            ?.getAttribute("data-copyright") ?? "",
                    contentEditable: wrapper.getAttribute("contenteditable"),
                    slot: {
                        index: children.indexOf(wrapper),
                        childCount: children.length,
                    },
                };
            });
        },
        {
            source: html,
            idAttribute: kIdAttribute,
            wrapperClass: kWrapperClass,
        },
    );
}

/**
 * Where the text block and one inline image's picture are on screen, in the page's own
 * coordinates. A test uses these to check that the picture stays inside its block, which Bloom
 * should ensure after any drag, and which only a real browser can show.
 */
export async function getInlineImageRects(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Promise<{ block: IRect; picture: IRect }> {
    return {
        block: await blockRect(page, groupSelector, languageTag),
        picture: await pictureRect(page, groupSelector, languageTag, id),
    };
}

/** The text of one language's block, with the inline images' markup left out. */
export async function getBlockText(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<string> {
    return inlineImageBlock(page, groupSelector, languageTag).evaluate(
        (block, wrapperClass) => {
            const copy = block.cloneNode(true) as HTMLElement;
            Array.from(copy.querySelectorAll("." + wrapperClass)).forEach(
                (wrapper) => wrapper.remove(),
            );
            return copy.textContent?.trim() ?? "";
        },
        kWrapperClass,
    );
}

/**
 * Click on the text of a block, away from every inline image in it, the way a person puts the
 * caret back in the text. That deselects the picture, so its corner handles go away. Returns once
 * no inline image in the group is selected.
 */
export async function clickInBlockText(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<void> {
    const point = await clearTextPoint(page, groupSelector, languageTag);
    await realClickAt(page, point.x, point.y);
    await expect
        .poll(
            async () =>
                (await getInlineImages(page, groupSelector))
                    .flatMap((block) => block.images)
                    .filter((image) => image.selected).length,
            {
                timeout: 30000,
                message:
                    `Clicking the text of the "${languageTag}" block left an inline image ` +
                    `selected.`,
            },
        )
        .toBe(0);
}

// --- private helpers, which tests do not call ---------------------------------

// renderTextContextMenu puts the text block's MUI context menu into the document of the page
// being edited, anchored at the mouse position, so it is not in the shell's document.
// This selector matches only a menu that is showing. There is normally more than one MUI menu in
// the page, because the selected inline image's toolbar keeps its own "..." menu in the document
// while it is closed (keepMounted in CanvasElementContextControls). That one comes first in the
// document, so a plain .first() would find a menu that never becomes visible.
const kMenuSelector = '.MuiMenu-root [role="menu"] >> visible=true';

// The invisible layer a MUI menu puts over everything behind it. Clicking it closes the menu, and
// the click does not reach the page.
const kMenuBackdropSelector = ".MuiMenu-root .MuiBackdrop-root >> visible=true";

/** The wrapper div of one language's copy of one inline image. */
function inlineImageWrapper(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Locator {
    return inlineImageBlock(page, groupSelector, languageTag).locator(
        `[${kIdAttribute}="${id}"]`,
    );
}

/** The picture inside one language's copy of one inline image. */
function inlineImagePicture(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Locator {
    return inlineImageWrapper(page, groupSelector, languageTag, id)
        .locator("img")
        .first();
}

/** One language's text block of a translation group. */
function inlineImageBlock(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Locator {
    return editablePageFrame(page)
        .locator(`${groupSelector} > .bloom-editable[lang="${languageTag}"]`)
        .first();
}

/** Where one language's text block is on screen. */
async function blockRect(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<IRect> {
    const block = inlineImageBlock(page, groupSelector, languageTag);
    await block.waitFor({ state: "visible", timeout: 30000 });
    const box = await block.boundingBox({ timeout: 30000 });
    if (!box)
        throw new Error(
            `The "${languageTag}" block of "${groupSelector}" has no box on screen, so there is ` +
                `nowhere to drag within it. Bloom may not be showing that language.`,
        );
    return box;
}

/** Where one inline image's picture is on screen. */
async function pictureRect(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Promise<IRect> {
    const picture = inlineImagePicture(page, groupSelector, languageTag, id);
    await picture.waitFor({ state: "visible", timeout: 30000 });
    const box = await picture.boundingBox({ timeout: 30000 });
    if (!box) {
        // Ask the page what it knows about the element, so the failure message can give the
        // reason as well as the symptom.
        const asThePageSeesIt = await picture.evaluate((element) => {
            const style = getComputedStyle(element);
            const rect = element.getBoundingClientRect();
            return `display ${style.display}, visibility ${style.visibility}, rect ${Math.round(rect.width)}x${Math.round(rect.height)} at ${Math.round(rect.left)},${Math.round(rect.top)}, natural ${(element as HTMLImageElement).naturalWidth}x${(element as HTMLImageElement).naturalHeight}, src ${(element as HTMLImageElement).getAttribute("src")}`;
        });
        // Get the frame's box too. Playwright reports no box for an element whose frame has no
        // box, even when the page gives the element a good rectangle. waitForEditablePage waits
        // for the frame to have a box, and this message says so when it no longer does.
        const frameBox = await page
            .locator("iframe#page")
            .boundingBox()
            .catch(() => null);
        throw new Error(
            `Inline image ${id} in the "${languageTag}" block has no box on screen. The page ` +
                `says: ${asThePageSeesIt}. The frame it is in has ` +
                `${frameBox ? `a box ${Math.round(frameBox.width)}x${Math.round(frameBox.height)} at ${Math.round(frameBox.x)},${Math.round(frameBox.y)}` : "no box at all"}. ` +
                `A picture with a rectangle of its own, in a frame with none, is a page that is ` +
                `still being rebuilt; one with no rectangle either is hidden by the CSS, which ` +
                `shows only the copy in the first visible language.`,
        );
    }
    return box;
}

/**
 * Press on the picture, move it to a point, and let go. The gesture has to start with a real press
 * on the picture, because that selects the image and records the state that undo returns to after
 * the drop. It has to move in steps, because Bloom works out the dock and the offset on each
 * pointermove.
 */
async function dragInlineImageTo(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
    to: { x: number; y: number },
): Promise<void> {
    const picture = await pictureRect(page, groupSelector, languageTag, id);
    const from = {
        x: picture.x + picture.width / 2,
        y: picture.y + picture.height / 2,
    };
    await page.mouse.move(from.x, from.y);
    await page.mouse.down();
    await page.mouse.move(to.x, to.y, { steps: 16 });
    await page.mouse.up();
}

/** Right-click one language's copy of an inline image, and wait for the menu. */
async function openInlineImageMenu(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Promise<void> {
    const picture = await pictureRect(page, groupSelector, languageTag, id);
    await rightClickAt(
        page,
        picture.x + picture.width / 2,
        picture.y + picture.height / 2,
    );
    await waitForInlineImageMenu(page, `inline image ${id}`);
}

/**
 * Right-click in the text of one language's block, away from every inline image in it, and wait
 * for the menu. clearTextPoint finds the spot, because a docked picture covers part of its block,
 * and a right-click on the picture gets the picture's menu instead of the block's.
 */
async function openInlineImageMenuInText(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<void> {
    const clear = await clearTextPoint(page, groupSelector, languageTag);
    await rightClickAt(page, clear.x, clear.y);
    await waitForInlineImageMenu(
        page,
        `the text of the "${languageTag}" block`,
    );
}

/**
 * A point in one language's block that is on the text and away from every inline image in it. A
 * docked picture covers part of its block, and a click that lands on the picture goes to the
 * picture instead of the text.
 */
async function clearTextPoint(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<{ x: number; y: number }> {
    const block = inlineImageBlock(page, groupSelector, languageTag);
    // elementFromPoint, below, finds nothing outside the window, and in a small window, like the
    // nightly runner's, the page can be scrolled so that the block is out of sight.
    await block.scrollIntoViewIfNeeded();
    const onScreen = await blockRect(page, groupSelector, languageTag);
    const found = await block.evaluate(
        (element, markup) => {
            // A Range over a text node reports one rectangle for each line of it. The paragraph's
            // box would not do, because it spans the block's full width whether the text wraps or
            // not, so its middle can be over the picture.
            const lines: DOMRect[] = [];
            const walker = document.createTreeWalker(
                element,
                NodeFilter.SHOW_TEXT,
            );
            while (walker.nextNode()) {
                const node = walker.currentNode;
                if (
                    (node.parentElement as HTMLElement | null)?.closest(
                        "." + markup.wrapperClass,
                    )
                )
                    continue;
                if (!node.textContent?.trim()) continue;
                const range = document.createRange();
                range.selectNodeContents(node);
                lines.push(...Array.from(range.getClientRects()));
            }
            // Try several points along each line, starting from its middle and moving outward,
            // because a line beside a docked picture is partly over it.
            const candidates = lines
                .filter((line) => line.width > 2 && line.height > 2)
                .flatMap((line) =>
                    [0.5, 0.25, 0.75, 0.1, 0.9].map((along) => ({
                        x: line.left + line.width * along,
                        y: line.top + line.height / 2,
                    })),
                );
            const clear = candidates.find((point) => {
                // This is the same check Bloom's right-click handler makes. The element under the
                // pointer has to be inside this text block, outside the picture, and outside the
                // bloom-ui editing controls. The format gear, for instance, sits at the bottom
                // left corner of the focused block and would otherwise get the right-click.
                const under = document.elementFromPoint(
                    point.x,
                    point.y,
                ) as HTMLElement | null;
                if (!under || !element.contains(under)) return false;
                if (under.closest("." + markup.wrapperClass)) return false;
                if (under.closest(".bloom-ui")) return false;
                return true;
            });
            const rect = element.getBoundingClientRect();
            return {
                point: clear,
                lineCount: lines.length,
                blockLeft: rect.left,
                blockTop: rect.top,
            };
        },
        { wrapperClass: kWrapperClass },
    );
    if (!found.point)
        throw new Error(
            `No spot on the text of the "${languageTag}" block of "${groupSelector}" is clear of ` +
                `its inline images and of the editing furniture, over ${found.lineCount} lines of ` +
                `text. A block with no text has nowhere to point at, so type something first.`,
        );
    // Coordinates inside the page are measured from the top left of its frame, and mouse
    // coordinates from the top left of Bloom's window. The difference is where the frame sits,
    // which comparing the block's position in both gives.
    return {
        x: onScreen.x + (found.point.x - found.blockLeft),
        y: onScreen.y + (found.point.y - found.blockTop),
    };
}

/**
 * A real right-click at a point in the page's own coordinates. Bloom shows its own menu in place
 * of WebView2's when it gets the contextmenu event, so the click has to be a real one.
 */
async function rightClickAt(page: Page, x: number, y: number): Promise<void> {
    await page.mouse.move(x, y);
    await page.mouse.click(x, y, { button: "right" });
}

async function waitForInlineImageMenu(page: Page, what: string): Promise<void> {
    await editablePageFrame(page)
        .locator(kMenuSelector)
        .first()
        .waitFor({ state: "visible", timeout: 30000 })
        .catch(() => {
            throw new Error(
                `Right-clicking ${what} did not open Bloom's own context menu. When a click has ` +
                    `nothing to offer, Bloom leaves the event alone and WebView2 shows its own ` +
                    `menu, which is not in the page (setupTextContextMenu).`,
            );
        });
}

/** Choose one command from the open menu, by localization id, and wait until the menu closes. */
async function clickInlineImageMenuCommand(
    page: Page,
    l10nId: string,
): Promise<void> {
    const frame = editablePageFrame(page);
    const item = frame
        .locator(`${kMenuSelector} >> li[data-testid="${l10nId}"]`)
        .first();
    if ((await item.count()) === 0) {
        const offered = await frame
            .locator(`${kMenuSelector} >> li[role="menuitem"]`)
            .evaluateAll((items) =>
                items.map((one) => one.getAttribute("data-testid") ?? "?"),
            );
        throw new Error(
            `The open menu has no "${l10nId}" command. It offers: ` +
                `${offered.join(", ") || "(nothing)"}.`,
        );
    }
    await item.click();
    await frame
        .locator(kMenuSelector)
        .first()
        .waitFor({ state: "hidden", timeout: 30000 });
}

/**
 * Choose one command from a submenu of the open menu, such as Flip, then Flip horizontal, and wait
 * until the menu closes. Both rows are named by localization id. A submenu stays open only while
 * the pointer rests on its parent row, so this hovers over the parent with the real mouse and then
 * goes straight to the command, because a path across other rows would close the submenu.
 */
async function clickInlineImageSubmenuCommand(
    page: Page,
    parentL10nId: string,
    l10nId: string,
): Promise<void> {
    const frame = editablePageFrame(page);
    const parent = frame
        .locator(`${kMenuSelector} >> li[data-testid="${parentL10nId}"]`)
        .first();
    if ((await parent.count()) === 0)
        throw new Error(`The open menu has no "${parentL10nId}" row.`);
    await parent.hover();
    const item = frame
        .locator(`${kMenuSelector} >> li[data-testid="${l10nId}"]`)
        .first();
    await item.waitFor({ state: "visible", timeout: 15000 }).catch(() => {
        throw new Error(
            `Resting the pointer on "${parentL10nId}" did not show a "${l10nId}" command.`,
        );
    });
    await item.click();
    await frame
        .locator(kMenuSelector)
        .first()
        .waitFor({ state: "hidden", timeout: 30000 });
}

/**
 * The text of every sentence on the page that the Talking Book tool has marked for recording
 * inside an inline image wrapper. The list must always be empty. The wrapper is a
 * contenteditable="false" element inside the text field, so a recordable sentence in it would ask
 * the person to record a picture and would put an audio-sentence span into the saved picture
 * markup.
 *
 * A caller must open the toolbox first (openToolboxWithTalkingBook), because the Talking Book
 * tool does the marking.
 */
export async function getNarrationInsideInlineImages(
    page: Page,
): Promise<string[]> {
    return editablePageFrame(page)
        .locator(`.${kWrapperClass} .audio-sentence`)
        .evaluateAll((elements) =>
            elements.map((element) => element.textContent ?? "(empty)"),
        );
}

/** One line per language block, for a failure message that says what the group does hold. */
function describeBlocks(blocks: IBlockInlineImages[]): string {
    if (blocks.length === 0) return "(no language blocks)";
    return blocks
        .map(
            (block) =>
                `${block.languageTag}: ${
                    block.images
                        .map((image) => `${image.id} (${image.dock})`)
                        .join(", ") || "no inline images"
                }`,
        )
        .join("; ");
}

export { kInsertImageCommand, kChooseImageCommand, kDeleteCommand };
