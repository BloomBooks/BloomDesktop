// Inline (Word-style) images: a picture that lives inside a text block so that the text of the
// block wraps around it. See INLINE-IMAGES-PLAN.md, and bookEdit/js/inlineImages.ts for the
// edit-time code these helpers drive.
//
// The feature has no dialog. Everything a person does to an inline image they do with the mouse,
// on the picture itself: the right-click menu of the text block adds one, a drag of the picture
// decides which side it docks to and how far down the block it starts, and a drag of a corner
// handle decides how wide it is. Selecting one also puts up the same toolbar an image has on a
// canvas. So nearly every helper here is a real mouse gesture, and each one waits for the state
// Bloom ends up in rather than for the gesture to finish.
//
// WHAT "THE STATE OF AN INLINE IMAGE" IS. One image is one wrapper div per language, all sharing
// an identity attribute, and everything about its position and size is either a dock class or a
// custom property in the wrapper's style attribute -- nothing else (the GEOMETRY MODEL comment in
// content/bookLayout/inlineImages.less). getInlineImages reads exactly that, per language, so a
// test can say "every language's copy is docked left at 40%" without naming a class or a property.
//
// WHY THE READERS ALSO REPORT COMPUTED STYLE. The CSS is half the feature: the float and its wrap
// shape are what make the text flow beside the picture, display:flow-root is what keeps the float
// inside its block, and a rule hides the copies in every language but the first visible one. None
// of that can be seen in the markup, and none of it is exercised by the vitest suite, which runs
// in jsdom. So `shown`, `float` and `wrapShape` come from getComputedStyle.

import { expect, type Locator, type Page } from "@playwright/test";
import * as fs from "node:fs";
import * as Path from "node:path";
import { apiPost } from "./api";
import { bookHtmlPath } from "./bookHtml";
import { editablePageFrame } from "./bookMaking";
import type { IRect } from "./geometry";
import { realClick, realClickAt } from "./realClick";

/**
 * Where an inline image sits in its block, in the words the feature uses: docked against the left
 * or right edge with the text wrapping beside it, a full-width band with text above and below, or
 * below the text altogether.
 */
export type InlineImageDock = "left" | "right" | "middle" | "bottom";

/** The four corner handles of a selected inline image, by compass direction. */
export type InlineImageCorner = "nw" | "ne" | "sw" | "se";

/** The localization id of the "Add Image" command on a text block's right-click menu. */
const kAddImageCommand = "EditTab.InlineImage.AddImage";

/** The localization id of the Delete command on an inline image's right-click menu. */
const kDeleteCommand = "Common.Delete";

/** The localization id of the command that opens the image chooser on an existing inline image. */
const kChooseImageCommand = "EditTab.Image.ChooseImage";

// The markup, all of it confined to this file. A test never names any of these.
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
    /** The identity every language's copy of this image shares. */
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
    /** The file name in the picture's src; "placeHolder.png" while no picture has been chosen. */
    fileName: string;
    /**
     * The wrapper's contenteditable attribute. It must stay "false" in the saved markup: that is
     * the only thing stopping Bloom's language-stamping sweep from treating the wrapper as a text
     * box (TranslationGroupManager.cs).
     */
    contentEditable: string | null;
    /** Which slot the wrapper holds among the block's children, and how many children there are. */
    slot: { index: number; childCount: number };
    /** True when this copy is the selected object, so its corner handles are showing. */
    selected: boolean;
    /** How many corner handles the copy has. Four when it is selected, none when it is not. */
    handleCount: number;
    /**
     * True when a reader would see this copy of the picture. False when the CSS is hiding the copy
     * itself, which it does in all but the first showing language, and false when the copy's whole
     * block is hidden -- so the copy in the lang="z" prototype block is never shown.
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
     * the float escapes the bottom of the block (the FLOAT CONTAINMENT comment in
     * inlineImages.less).
     */
    display: string;
    images: IInlineImageState[];
}

/**
 * Every inline image of one translation group on the page being edited, one entry per language
 * block, in the order the blocks appear in the markup. `groupSelector` picks the group, e.g.
 * ".bloom-translationGroup" for the only one on a Just Text page.
 *
 * The lang="z" prototype block is included, because a copy in it is how a language added later
 * inherits the image with no C# involvement (insertInlineImage in inlineImages.ts).
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
                            // The wrapper always carries exactly one dock class; a missing one
                            // could only come from hand-edited markup, and saying so beats
                            // reporting a dock the image is not in.
                            dock: dockEntry ? dockEntry[0] : "(no dock class)",
                            widthPercent: readNumber(markup.widthProperty),
                            offsetPx: readNumber(markup.offsetProperty),
                            aspectRatio: wrapper.style
                                .getPropertyValue(markup.aspectRatioProperty)
                                .trim(),
                            // Without the query: Bloom appends "?transparent=yes" to an image
                            // on a page with a coloured background (getImageTransparencyMode),
                            // so a picture chosen on the front cover has one and the same
                            // picture on a content page does not. Which file it is is the
                            // question here.
                            fileName: decodeURIComponent(
                                (source.split("?")[0].split("/").pop() ??
                                    source) as string,
                            ),
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
                            // checkVisibility(), not display alone: a copy in a hidden block
                            // has no display of its own to give it away.
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
 * One language's copy of one inline image. Throws, naming what the group does hold, when there is
 * no such copy -- which is the failure a test wants spelled out, since "the copy is missing" and
 * "the copy has the wrong geometry" are different bugs.
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
 * Add Image. Returns the identity of the new image, which every language's copy of it shares.
 *
 * The command deliberately does not open the image chooser (a picture is chosen afterwards, from
 * the same menu), so what arrives is a placeholder, docked right at the default width. It arrives
 * in EVERY language's block at once, prototype included, and this returns once it has.
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
    await clickInlineImageMenuCommand(page, kAddImageCommand);
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
                    `Add Image did not put a new inline image in all ${wanted} language blocks ` +
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
 * Delete an inline image the way a person does: right-click the picture and choose Delete. That
 * takes this image's copy out of every language, and leaves any other inline image in the block
 * alone. Returns once every copy has gone.
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
 * The commands an inline image's right-click menu offers, by localization id, each paired with
 * whether it is enabled. Leaves the menu open; close it with closeInlineImageMenu.
 *
 * A command behind a subscription tier the collection does not have counts as not enabled: Bloom
 * keeps such an item clickable and marks it with data-subscription-gated instead of disabling it
 * (see helpers/canvasElements.ts, which makes the same distinction for the canvas element menu).
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
 * The buttons showing on the selected inline image's toolbar, by control id, each paired with
 * whether it is enabled. Empty when no toolbar is up, which is what "nothing is selected" looks
 * like.
 *
 * The "..." button that opens the menu is not a control and is left out; read the menu itself
 * with getInlineImageMenuCommands. Which buttons appear depends on the picture: the one that
 * warns about missing copyright and license information, for instance, only appears while that
 * information is missing. So a test should ask for the buttons it cares about rather than for a
 * whole list.
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
 * Where the inline image toolbar is on the screen, or undefined when no toolbar is up. Use it to
 * check that the bar belongs to the picture the person selected, which is the part a list of
 * button names cannot show.
 */
export async function getInlineImageToolbarRect(
    page: Page,
): Promise<IRect | undefined> {
    const bar = editablePageFrame(page).locator(kToolbarSelector);
    if (!(await bar.isVisible().catch(() => false))) return undefined;
    return (await bar.boundingBox()) ?? undefined;
}

/** True when the block's right-click menu offers to add an inline image at all. */
export async function textBlockOffersAddImage(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<boolean> {
    await openInlineImageMenuInText(page, groupSelector, languageTag);
    const offered =
        (await editablePageFrame(page)
            .locator(
                `${kMenuSelector} >> li[data-testid="${kAddImageCommand}"]`,
            )
            .count()) > 0;
    await closeInlineImageMenu(page);
    return offered;
}

/**
 * Dismiss the text block's right-click menu without choosing anything, the way clicking away from
 * it does.
 *
 * A bare Escape key press does not do it. The menu is opened with focus left alone, so that the
 * caret stays in the text the person right-clicked on (disableAutoFocus in TextContextMenu), and
 * a key press therefore goes to the page rather than to the menu. So this clicks the invisible
 * backdrop the menu lays over the page, which is what a click away from the menu lands on, and
 * presses Escape on the menu itself only if there is no backdrop to click.
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
 * four corner handles are showing. Selecting is what a drag or a resize starts from, and both
 * gestures below do it for themselves; call this directly only when the selection IS the subject.
 */
export async function selectInlineImage(
    page: Page,
    groupSelector: string,
    languageTag: string,
    id: string,
): Promise<void> {
    const picture = inlineImagePicture(page, groupSelector, languageTag, id);
    // A real press, not Playwright's own click: the picture sits inside a contenteditable that
    // CKEditor manages, and the code that selects it listens for pointerdown in the capture phase
    // (setupInlineImageInteractions), having cancelled the mousedown that would put the caret in
    // the text instead.
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
 * Put the block's scroll back at the top, so that what is at the top of its text is where a
 * person would be looking at it.
 *
 * Typing leaves the caret at the end of the text, and a block too small for its text is
 * scrolled to the caret, which puts the top of the block -- and any picture sitting there --
 * out of sight. A gesture aimed at a picture that is scrolled out of view lands on whatever
 * is showing instead, and reads as the picture refusing to move. This is setup, not a gesture
 * under test, so it sets scrollTop rather than driving the wheel.
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
 * Drag an inline image UP the block, the way a person does, and return the offset it ends at.
 *
 * Separate from dragInlineImageDown because the failure it reports is a different one: an image
 * that will not come back up is stuck, and a block too small for its text is exactly when that
 * used to happen. Fails with the offset the image is still at rather than timing out.
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
 * How much more room the block's text needs than the block has, in layout pixels. Zero when the
 * text fits. This is what Bloom's overflow warning is about, and a test that means to work on an
 * overflowing block should assert it before it starts.
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
 * Whether Bloom has marked this block as holding more text than it can show -- the red marking
 * and the warning the person sees, as opposed to the arithmetic getBlockScrollOverflowPx does.
 * OverflowChecker puts the "overflow" class on, and checks every editable when the page loads
 * (AddOverflowHandlers ends by scheduling a check for each one), so this reports the state a
 * person arrives at a page to find.
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
 * Drag an inline image to a dock, the way a person does: press on the picture and move it into
 * the part of the block that dock belongs to, then let go. Returns once every language's copy is
 * in that dock.
 *
 * Which dock a drop means is decided from where the PICTURE ends up, not the cursor, and by
 * thirds of the block's width: the outer thirds are the side docks and the middle third is the
 * band. In the middle third the bottom dock takes over where the band can no longer fit the
 * picture inside the block's content, and anything below the block is the bottom dock whatever
 * the horizontal position (computeInlineImageDock). This aims at the middle of the region for
 * the dock asked for, so a caller states an intent rather than a coordinate.
 *
 * Bloom refuses a move that would push the block into overflow, putting the image back where it
 * started. When that happens this fails with the dock the image is still in, rather than timing
 * out on a wait: the caller's block is too small for what it asked.
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
    // How many copies there are to check, counted before the drag: the point of this wait is
    // that EVERY language ends up in the new dock, and a count taken afterwards could not tell
    // "all of them" from "the one the drag happened in".
    const copyCount = (
        await getInlineImageInEveryLanguage(page, groupSelector, id)
    ).length;
    expect(
        copyCount,
        `inline image ${id} should exist in at least one language before the drag`,
    ).toBeGreaterThan(0);
    // The middle of the region that means this dock, horizontally; the picture keeps the height it
    // already had, because moving it sideways is the whole intent here and a drop lower down the
    // block would also change how far down the picture starts. The bottom dock is the exception:
    // it is claimed just below the block, which is unambiguous however tall the block is and
    // whatever else is in it.
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
 * below the top of the block it now starts. Dragging down within a side dock is how the picture
 * is moved past the first lines of text, and the distance is kept in one custom property.
 *
 * Bloom clamps the distance so the whole picture stays inside the block, so what comes back may
 * be less than what was asked for; that is the answer, not a failure. The wait is for the
 * distance to change at all.
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
 * Drag an inline image down its block by `pixels` and report where that left it, without
 * insisting that it moved anywhere.
 *
 * Separate from dragInlineImageDown, which fails when the picture does not move, because this is
 * for sweeping a picture down the block a step at a time to find out which positions the block
 * offers at all: a step that re-docks the picture, or that the picture refuses, is an answer here
 * rather than a failure.
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
 * Press on the picture and hold, so a drag is under way and the button stays down. Pair it with
 * moveInlineImageDragTo and endInlineImageDrag.
 *
 * The other drag helpers do a whole gesture and hand back the result, which cannot answer a
 * question about what the page does WHILE the picture is being held -- whether the block scrolls
 * to follow it, for one.
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
 * Whether the page still thinks a picture is being dragged. The drag puts a class on the page's
 * body and takes it off when the gesture ends, so this is how a test asks whether a gesture that
 * should have finished actually did.
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
 * holds, in the page's own pixels. That room is whatever text still follows the picture, so
 * "less than one line" means the picture has reached the end of the block's content -- which is
 * the question behind "can I put the picture as far down the block as I like".
 *
 * Measured against the block's CONTENT and not its rectangle: a block too small for its text
 * scrolls, and the text below the window is still text the picture can be put after.
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
            // "normal" has no number in it; the ratio Chromium uses for it is close enough to
            // 1.2 for a test that asks "is there a line's worth of room left".
            return lineHeight.endsWith("px")
                ? parseFloat(lineHeight)
                : parseFloat(getComputedStyle(block).fontSize) * 1.2;
        });
}

/**
 * Make an inline image wider or narrower by dragging one of its corner handles, the way a person
 * does, and return the width it reaches as a percentage of the block. Only the horizontal
 * movement counts: the picture keeps its aspect ratio, so pulling a corner sideways is the whole
 * gesture (computeInlineImageWidthPercent).
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
    // Outward is away from the picture: rightward for the eastern corners, leftward for the
    // western ones (getInlineImageHandleHorizontalSign).
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
 * This is SETUP, on the same footing as chooseImageFile in helpers/images.ts and for the same
 * reason: the production route ends in a native file picker, which hangs a run
 * (AUTOMATION-DEBT.md, "Native OS dialogs hang automation"). It takes the route the chooser takes
 * once a picture has been chosen -- Bloom's imageGallery/imageGalleryResult endpoint copies the
 * file into the book, and changeImageByElement applies it to the very img the chooser was opened
 * on -- so everything Bloom does after a picture is chosen still runs, including the copying of
 * the new picture onto the other languages' wrappers (handleInlineImageChanged).
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
    // As in dragInlineImageToDock: the number of copies to expect the new picture in, counted
    // before the change, so that losing a copy cannot read as success.
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
 * True when the text of a block flows beside the picture rather than starting below it: some line
 * of text has its top above the bottom of the picture, and lies to the side of it.
 *
 * This is the point of the whole feature, and it can only be seen in a real renderer -- the float
 * and its wrap shape do nothing in jsdom. It measures the client rectangles of the text itself
 * (one per line), not the paragraph box, which spans the full width of the block whether the text
 * wraps or not.
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
            // Every line box of the block's text, which is what a Range over a text node reports.
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
    /** The classes on the wrapper, which is where the dock lives. */
    classes: string[];
    /** The whole style attribute, which is where the geometry lives. */
    style: string;
    /** The picture's src, relative to the book folder. */
    source: string;
    contentEditable: string | null;
    /** Which slot the wrapper holds among the block's children, and how many there are. */
    slot: { index: number; childCount: number };
}

/**
 * Every inline image in the saved book on disk, in markup order. This is the product's own record
 * of the page, and the only place to check what survives a save: the editing DOM carries
 * edit-time decoration that never reaches the file (the corner handles, the selection marker),
 * and Bloom writes a page only when the book leaves it -- so call goToPage first.
 *
 * `page` is borrowed as an HTML parser only, the way helpers/bookHtml.ts borrows it.
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
 * coordinates. A test uses these to say that the picture stays inside its block -- which is what
 * Bloom promises about a drag, and what only a real renderer can answer.
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
 * Click on the TEXT of a block, clear of every inline image in it, the way a person puts the
 * caret back in the text. That ends the picture's turn as the selected object, so its corner
 * handles go away. Returns once no inline image in the group is selected.
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

// --- the parts a test does not call -----------------------------------------

// The text context menu MUI puts into the page's own document (renderTextContextMenu), not into
// the shell: it is anchored at the mouse position inside the page being edited.
// The menu itself, and only the one that is actually showing. There is normally more than one
// MUI menu in the page: the selected inline image's toolbar keeps its own "..." menu mounted
// while closed (keepMounted in CanvasElementContextControls), and that one comes first in the
// document, so a plain .first() would settle on a menu that can never become visible.
const kMenuSelector = '.MuiMenu-root [role="menu"] >> visible=true';

// The invisible layer a MUI menu puts over everything behind it. A click on it is how the menu is
// dismissed; it also stops that click from reaching the page.
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
        // What the page itself says about the element, so that the failure names the reason
        // rather than only the symptom.
        const asThePageSeesIt = await picture.evaluate((element) => {
            const style = getComputedStyle(element);
            const rect = element.getBoundingClientRect();
            return `display ${style.display}, visibility ${style.visibility}, rect ${Math.round(rect.width)}x${Math.round(rect.height)} at ${Math.round(rect.left)},${Math.round(rect.top)}, natural ${(element as HTMLImageElement).naturalWidth}x${(element as HTMLImageElement).naturalHeight}, src ${(element as HTMLImageElement).getAttribute("src")}`;
        });
        // The frame's own box too. A box measured through a frame is nothing when the frame
        // itself has none, which is what an element that the page says has a good rectangle
        // means; waitForEditablePage waits for that, and this says so when it has not held.
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
 * Press on the picture and move it to a point, then let go. The gesture has to begin with a real
 * press on the picture, because that is what selects the image and records the undo point a drop
 * commits; and it has to move in steps, because the dock and the offset are worked out from each
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
 * Right-click in the TEXT of one language's block, clear of every inline image in it, and wait
 * for the menu. Finding a clear spot is the whole difficulty: a docked picture covers part of its
 * block, and a right-click that lands on it gets the picture's menu instead of the block's.
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
 * A point in one language's block that is on the text and clear of every inline image in it.
 * Finding one is the whole difficulty of pointing at the text: a docked picture covers part of
 * its block, and a click that lands on the picture means the picture, not the text.
 */
async function clearTextPoint(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<{ x: number; y: number }> {
    const block = inlineImageBlock(page, groupSelector, languageTag);
    const onScreen = await blockRect(page, groupSelector, languageTag);
    const found = await block.evaluate(
        (element, markup) => {
            // Every line the block's text occupies, which is what a Range over a text node
            // reports. The paragraph box is no use here: it spans the block's full width whether
            // the text wraps or not, so its middle can be over the picture.
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
            // Along each line, from its middle outward. A line beside a docked picture is partly
            // over it, so several points per line are worth trying.
            const candidates = lines
                .filter((line) => line.width > 2 && line.height > 2)
                .flatMap((line) =>
                    [0.5, 0.25, 0.75, 0.1, 0.9].map((along) => ({
                        x: line.left + line.width * along,
                        y: line.top + line.height / 2,
                    })),
                );
            const clear = candidates.find((point) => {
                // The condition the production handler itself applies: what is under the pointer
                // has to be part of this text block, and not the picture, and not a piece of
                // editing furniture such as the format gear -- which sits at the bottom left
                // corner of the focused block and would otherwise swallow the right-click.
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
    // The block's own document measures from the top left of the frame it is in; the mouse
    // measures from the top left of Bloom's window. The two differ by where the frame sits, which
    // is what comparing the same block in both coordinate systems gives.
    return {
        x: onScreen.x + (found.point.x - found.blockLeft),
        y: onScreen.y + (found.point.y - found.blockTop),
    };
}

/**
 * A real right-click at a point in the page's own coordinates. Bloom's own menu replaces
 * WebView2's, and it is raised from the contextmenu event, so the press has to be a real one.
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
 * The text of every sentence that the Talking Book tool has marked for recording INSIDE an inline
 * image wrapper, in the whole page. It must always be empty: the wrapper is
 * contenteditable="false" content of the text field, so a recordable sentence in it would give the
 * reader a picture to record and would put an audio-sentence span into saved picture markup.
 *
 * A caller must open the toolbox first (openToolboxWithTalkingBook), because the marking is that
 * tool's work, not the page's.
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

export { kAddImageCommand, kChooseImageCommand, kDeleteCommand };
