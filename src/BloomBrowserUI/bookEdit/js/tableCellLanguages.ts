// The languages a table's text cell shows (BL-17105).
//
// A text cell holds a bloom-translationGroup, and the roles it shows are stored on that group in
// data-default-languages, with the meanings that Bloom gives them in every translation group
// (TranslationGroupManager.ShouldNormallyShowEditable in C#): V for the first language ticked in the
// toolbar, N1 for the collection's second language and N2 for its third, whatever the toolbar
// shows, or a list such as "V,N1". Roles rather than language tags, so that a cell keeps its role
// when the book is translated in another collection. A group with no setting shows the languages
// ticked in the toolbar, as any translation group does. C# applies these rules when a page loads,
// and this module applies them at once when the user changes a cell.

import { tableHistoryManager } from "bloom-table";
import { ILanguageNameValues } from "../toolbox/canvas/canvasControlTypes";
import {
    setUpBoxesBuiltAfterLoad,
    showLanguagesInTranslationGroup,
} from "./translationGroupLanguages";

export type LanguageRole = "V" | "N1" | "N2";

const kRoleOrder: LanguageRole[] = ["V", "N1", "N2"];

/** One of the collection's languages, as a table cell can show it. */
export interface ICellLanguage {
    role: LanguageRole;
    /** What the menu calls the role: L1, L2 or L3. */
    shortName: string;
    tag: string;
    name: string;
    /** The Collection Settings label for the language, used as the entry's tooltip. */
    tooltipEnglish: string;
    tooltipL10nId: string;
    /** True when the language is ticked in the toolbar, so a cell with no setting shows it. */
    tickedInToolbar: boolean;
}

/**
 * The languages a table cell can show, in role order: every role the collection gives a language,
 * even one whose language is the same as an earlier role's, since a cell can have that role
 * stored (it was set in another collection) and must still show its language. The menu lists
 * each language once; see menuLanguages.
 *
 * `toolbarTags` are the languages ticked in the toolbar, the first one first. V is that first
 * language, which need not be the collection's first; its tooltip is the Collection Settings
 * label of the collection language it is.
 */
export function cellLanguagesOfCollection(
    names: ILanguageNameValues,
    toolbarTags: string[],
): ICellLanguage[] {
    const collectionLanguages = [
        {
            tag: names.language1Tag,
            name: names.language1Name,
            tooltipEnglish: "Local Language",
            tooltipL10nId:
                "CollectionSettingsDialog.LanguageTab.VernacularLanguageLabel",
        },
        {
            tag: names.language2Tag,
            name: names.language2Name,
            tooltipEnglish: "Language 2 (e.g. National Language)",
            tooltipL10nId:
                "CollectionSettingsDialog.LanguageTab._language2Label",
        },
        {
            tag: names.language3Tag ?? "",
            name: names.language3Name ?? "",
            tooltipEnglish: "Language 3 (e.g. Regional Language)   (Optional)",
            tooltipL10nId:
                "CollectionSettingsDialog.LanguageTab._language3Label",
        },
    ];
    const toolbarFirst = collectionLanguages.find(
        (language) => language.tag === toolbarTags[0],
    );
    if (!toolbarFirst) {
        throw new Error(
            `The toolbar's first language, "${toolbarTags[0]}", is not one of the collection's languages`,
        );
    }
    const candidates: Omit<ICellLanguage, "tickedInToolbar">[] = [
        { role: "V", shortName: "L1", ...toolbarFirst },
        {
            role: "N1",
            shortName: "L2",
            ...collectionLanguages[1],
        },
        { role: "N2", shortName: "L3", ...collectionLanguages[2] },
    ];
    return candidates
        .filter((candidate) => !!candidate.tag)
        .map((candidate) => ({
            ...candidate,
            tickedInToolbar: toolbarTags.includes(candidate.tag),
        }));
}

/**
 * The languages the Languages submenu lists: `languages` without a role whose language is the
 * same as an earlier role's, so the menu does not list one language twice.
 */
export function menuLanguages(languages: ICellLanguage[]): ICellLanguage[] {
    return languages.filter(
        (language, index) =>
            languages.findIndex((other) => other.tag === language.tag) ===
            index,
    );
}

/** The translation group of a text cell, or undefined when the cell holds something else. */
export function textGroupOfCell(cell: HTMLElement): HTMLElement | undefined {
    return (
        cell.querySelector<HTMLElement>(":scope > .bloom-translationGroup") ??
        undefined
    );
}

/**
 * The roles a text cell's group shows, in role order. A group with no setting shows the languages
 * ticked in the toolbar.
 */
export function rolesOfGroup(
    group: HTMLElement,
    languages: ICellLanguage[],
): LanguageRole[] {
    return rolesFromSetting(
        group.getAttribute("data-default-languages"),
        languages,
    );
}

/**
 * The roles that a value of data-default-languages on a text cell's group stands for. No setting,
 * like "auto", stands for the languages ticked in the toolbar, each by the role that the menu
 * lists it under.
 */
export function rolesFromSetting(
    setting: string | null,
    languages: ICellLanguage[],
): LanguageRole[] {
    const tokens = (setting ?? "")
        .split(/[,\s]+/)
        .filter((token) => token.length > 0)
        .map((token) => (token === "L1" ? "V" : token));
    if (tokens.length === 0 || tokens[0].toLowerCase() === "auto") {
        return menuLanguages(languages)
            .filter((language) => language.tickedInToolbar)
            .map((language) => language.role);
    }
    return kRoleOrder.filter((role) => tokens.includes(role));
}

/** Whether every group shows a language, none does, or only some do. */
export type RoleState = "all" | "none" | "some";

// The language tag of a role the menu lists, which always has one.
function tagOfRole(role: LanguageRole, languages: ICellLanguage[]): string {
    const language = languages.find((other) => other.role === role);
    if (!language) {
        throw new Error(`The collection gives role ${role} no language`);
    }
    return language.tag;
}

// The languages, among `languages`, that `roles` name. A role the collection gives no language
// names none: a cell keeps its roles when its collection drops its third language, say.
function languagesOfRoles(
    roles: LanguageRole[],
    languages: ICellLanguage[],
): ICellLanguage[] {
    return languages.filter((language) => roles.includes(language.role));
}

// The languages a group shows, by tag, each once. Two roles can name one language: V, the
// toolbar's first language, is often the collection's L2 as well.
function tagsOfGroup(group: HTMLElement, languages: ICellLanguage[]): string[] {
    return [
        ...new Set(
            languagesOfRoles(rolesOfGroup(group, languages), languages).map(
                (language) => language.tag,
            ),
        ),
    ];
}

/**
 * How the role's language stands across `groups`, for drawing its check box. A group shows the
 * language when any of its roles names it.
 */
export function roleState(
    groups: HTMLElement[],
    role: LanguageRole,
    languages: ICellLanguage[],
): RoleState {
    const tag = tagOfRole(role, languages);
    const count = groups.filter((group) =>
        tagsOfGroup(group, languages).includes(tag),
    ).length;
    if (count === 0) return "none";
    if (count === groups.length) return "all";
    return "some";
}

/**
 * True when some group shows only one language. A box whose language every group shows may then
 * not be unticked, since unticking would leave that group with none.
 */
export function someGroupHasOneLanguage(
    groups: HTMLElement[],
    languages: ICellLanguage[],
): boolean {
    return groups.some((group) => tagsOfGroup(group, languages).length === 1);
}

// The value of data-default-languages that stores `roles`.
function settingOfRoles(roles: LanguageRole[]): string {
    return kRoleOrder.filter((role) => roles.includes(role)).join(",");
}

/**
 * Make `group` store `setting` exactly as given, including no setting at all, and show the
 * languages it stands for, in role order. Where the group has no bloom-editable in one of those
 * languages yet (a cell made since the page loaded has only its first one), one is added. Returns
 * what showLanguagesInTranslationGroup returns: the added boxes and the boxes shown again, both
 * for setUpBoxesBuiltAfterLoad once they are in the page, best in one call for every group changed
 * together.
 */
export function showSettingInGroup(
    group: HTMLElement,
    setting: string | null,
    languages: ICellLanguage[],
): { added: HTMLElement[]; shown: HTMLElement[] } {
    if (setting === null) {
        group.removeAttribute("data-default-languages");
    } else {
        group.setAttribute("data-default-languages", setting);
    }
    const wanted = languagesOfRoles(
        rolesFromSetting(setting, languages),
        languages,
    );
    return showLanguagesInTranslationGroup(
        group,
        wanted.map((language) => language.tag),
        languages.map((language) => language.tag),
    );
}

/**
 * The settings that ticking or unticking `role` across the text cells of `groups` gives them, one
 * for each group the toggle changes. Ticking adds the role to every group that does not show its
 * language; unticking takes every role naming that language out of every group where it is not
 * the only language.
 */
function settingsForToggle(
    groups: HTMLElement[],
    role: LanguageRole,
    languages: ICellLanguage[],
): { group: HTMLElement; setting: string }[] {
    const tick = roleState(groups, role, languages) !== "all";
    const tag = tagOfRole(role, languages);
    // Every role that names this language: V and N1 often name the same one.
    const rolesNamingTag = languages
        .filter((language) => language.tag === tag)
        .map((language) => language.role);
    return groups
        .filter((group) => {
            const tags = tagsOfGroup(group, languages);
            if (tick) return !tags.includes(tag);
            return tags.includes(tag) && tags.length > 1;
        })
        .map((group) => {
            const before = rolesOfGroup(group, languages);
            const after = tick
                ? [...before, role]
                : before.filter((other) => !rolesNamingTag.includes(other));
            return { group, setting: settingOfRoles(after) };
        });
}

/**
 * Tick or untick `role` across the text cells of `groups`, which are all in one table (see
 * settingsForToggle), as one undoable step.
 *
 * The step is one of the table library's operations, so it is undone as every table operation is:
 * by putting the table back as it was, whatever has happened to the page around it since. Like the
 * library's other operations it announces itself when done, and that announcement both puts it on
 * Bloom's undo stack (noteTableHistoryUpdate) and sets up the boxes the change added and tags the
 * ones it showed again (wireBloomContentOfNewCells), all in one pass.
 */
export function toggleRole(
    groups: HTMLElement[],
    role: LanguageRole,
    languages: ICellLanguage[],
): void {
    const changes = settingsForToggle(groups, role, languages);
    if (changes.length === 0) return;
    tableHistoryManager.addHistoryEntry(
        changes[0].group.closest<HTMLElement>(".bloom-table")!,
        "Change the languages of table cells",
        () =>
            changes.forEach((change) =>
                showSettingInGroup(change.group, change.setting, languages),
            ),
    );
}

// Give each of `groups` the setting at the same index, then set up the boxes that adds, and tag
// the boxes it shows again, in one call for all the groups. Setting up and tagging measure the
// boxes, so doing them group by group would lay the page out once per cell. The groups' boxes must
// be in the page, where a box's direction can be read.
export function showSettingsInGroups(
    groups: HTMLElement[],
    settings: (string | null)[],
    languages: ICellLanguage[],
): void {
    const results = groups.map((group, index) =>
        showSettingInGroup(group, settings[index], languages),
    );
    setUpBoxesBuiltAfterLoad(
        results.flatMap((result) => result.added),
        results.flatMap((result) => result.shown),
    );
}
