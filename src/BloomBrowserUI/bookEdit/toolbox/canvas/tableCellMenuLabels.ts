// Bloom's wording for the bloom-table library's Cell menu items.
//
// The library renders those items itself, with its exported CellMenuItems component,
// and Bloom drops that component into the canvas context menu (see
// CanvasElementContextControls.tsx) so a picture in a calendar month grid gets the
// cell's items beside the image commands. The library's labels are English, because
// it does not localize; this is what it asks a host for instead.
//
// Mapping an item id to a Bloom string is the one thing to add here. Until a string
// exists for an item, the menu shows the English the library gives, which is what the
// library's own popup shows.
import theOneLocalizationManager from "../../../lib/localizationManager/localizationManager";

// The library's item ids to Bloom's string ids. An item of the Content Type row is
// "<choice id>:<option id>", e.g. "contentType:image", and the section's heading is
// "tableCell". Empty for now: Bloom has no strings of its own for these items yet.
const bloomStringIdOfItem: { [itemId: string]: string } = {};

/**
 * Bloom's wording for one Cell menu item, given the English the library supplies and
 * the item's id. Returns the English while the item has no Bloom string, and while a
 * string it does have has not been loaded for the current language.
 */
export const localizeTableCellMenuLabel = (
    englishLabel: string,
    itemId: string,
): string => {
    const stringId = bloomStringIdOfItem[itemId];
    if (!stringId) {
        return englishLabel;
    }
    return theOneLocalizationManager.getText(stringId, englishLabel);
};
