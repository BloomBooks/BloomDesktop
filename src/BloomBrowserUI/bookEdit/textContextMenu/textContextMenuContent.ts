// What the text context menu should offer for a given right-click. Kept out of the React
// component, in the same spirit as noIndent.ts, so that the decision can be unit tested
// against a plain DOM.

import { IMenuItemWithSubmenu } from "../js/canvasElementManager/canvasControlMenuRendering";
import {
    CloseMenuFunction,
    getInlineImageMenuItemsForClick,
} from "../js/inlineImageInteractions";
import { findParagraphForTextContextMenu } from "./noIndent";

export interface ITextContextMenuContent {
    // The paragraph the paragraph-level commands act on, or undefined when the right-click was
    // not in a paragraph. An inline image is the case that arises: it sits among the
    // paragraphs of the text box, never inside one.
    paragraph?: HTMLElement;
    // What inline images contribute for this click; empty when they have nothing to offer.
    // For an existing image this is the standard image menu (dividers and submenus
    // included), which is why the shape is richer than plain ILocalizableMenuItemProps.
    inlineImageItems: IMenuItemWithSubmenu[];
}

/**
 * Decides what a right-click should put on the text context menu, or returns undefined if the
 * menu should not open at all -- in which case the caller must leave the event alone, so that
 * whatever else would handle it (WebView2's own menu) still can.
 *
 * Call this once per right-click, not once per render of the menu: working out the inline
 * image's commands also selects the image they will act on. See
 * getInlineImageMenuItemsForClick.
 *
 * closeMenu is what the inline-image commands call to dismiss the menu (the paragraph
 * commands close through the component instead); the no-op default is for tests that only
 * inspect the items.
 */
export function getTextContextMenuContent(
    target: EventTarget | null,
    closeMenu: CloseMenuFunction = () => {},
): ITextContextMenuContent | undefined {
    const paragraph = findParagraphForTextContextMenu(target);
    const inlineImageItems = getInlineImageMenuItemsForClick(
        target instanceof HTMLElement ? target : undefined,
        closeMenu,
    );
    if (!paragraph && inlineImageItems.length === 0) return undefined;
    return { paragraph, inlineImageItems };
}
