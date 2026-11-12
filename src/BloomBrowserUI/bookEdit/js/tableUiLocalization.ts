import { setGetLocalizations } from "bloom-table";
import { getLocalizations } from "../../lib/localizationManager/getLocalizations";

let started = false;

/**
 * Have the bloom-table library show its menus, buttons and tooltips in the user's
 * UI language, the way Bloom localizes every library it hosts (see
 * getLocalizations). The library holds its UI back until the translations
 * arrive. Called once the content types are registered, since their names are
 * among the strings the library asks for.
 */
export function startTableUiLocalization(): void {
    if (started) return;
    started = true;
    void setGetLocalizations(getLocalizations);
}
