// Works out what the text context menu offers for a given right-click. This is kept out of
// the React component, as noIndent.ts is, so that it can be unit tested against a plain DOM.

import { IMenuItemWithSubmenu } from "../js/canvasElementManager/canvasControlMenuRendering";
import {
    CloseMenuFunction,
    getInlineImageMenuItemsForClick,
} from "../js/inlineImageInteractions";
import { findParagraphForTextContextMenu } from "./noIndent";

export interface ITextContextMenuContent {
    // The paragraph that commands such as "No Indent" act on, or undefined when the
    // right-click was not in a paragraph. That happens on an inline image, which sits between
    // the paragraphs of the text box and is never inside one, and in the empty space of the box.
    paragraph?: HTMLElement;
    // The inline image commands for this click, or an empty array when there are none. For a
    // click on an existing image this is the standard image menu, with its dividers and
    // submenus, which is why the type is IMenuItemWithSubmenu and not
    // ILocalizableMenuItemProps.
    inlineImageItems: IMenuItemWithSubmenu[];
}

/**
 * Decides what a right-click should put on the text context menu. Returns undefined if the
 * menu should not open at all. In that case the caller must leave the event alone, so that
 * WebView2's own menu can still handle it.
 *
 * Call this once for each right-click, and not each time the menu renders, because working
 * out the inline image commands also selects the image they will act on. See
 * getInlineImageMenuItemsForClick.
 *
 * The inline image commands call closeMenu to close the menu. The paragraph commands close it
 * through the component. The default does nothing, for tests that only look at the items.
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
