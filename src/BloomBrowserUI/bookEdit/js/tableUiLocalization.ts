import { setTableLocalizer, tableUiStrings } from "bloom-table";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";

// Every string id the bloom-table library uses is looked up in Bloom's XLF under
// this prefix. tableUiLocalization.test.ts checks that each one has an entry.
export const kTableUiStringIdPrefix = "EditTab.Table.";

let started = false;

/**
 * Have the bloom-table library show its menus, buttons and tooltips in the user's
 * UI language. The library builds that UI synchronously, so the translations are
 * fetched once into the localization manager's dictionary, and only then is the
 * localizer installed; until it arrives the library shows its own English.
 * Installing it relabels the chrome the library has already built, and every
 * menu is built afresh when it opens.
 */
export function startTableUiLocalization(): void {
    if (started) return;
    started = true;
    const englishById: { [id: string]: string } = {};
    Object.entries(tableUiStrings).forEach(([id, english]) => {
        englishById[kTableUiStringIdPrefix + id] = english;
    });
    void theOneLocalizationManager
        .loadStringsPromise(englishById, null)
        .then(() =>
            setTableLocalizer((englishLabel: string, id: string) =>
                theOneLocalizationManager.getText(
                    kTableUiStringIdPrefix + id,
                    englishLabel,
                ),
            ),
        );
}
