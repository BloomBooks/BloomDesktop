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

import { ILanguageNameValues } from "../toolbox/canvas/canvasControlTypes";
import { theOneUndoStack } from "../undo/UndoStack";
import { isInChangeLayoutMode } from "../undo/redoKeyBinding";

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

// The language tag of a role a cell stores, or undefined when the collection gives that role no
// language: a cell keeps its roles when its collection drops its third language, say.
function tagOfStoredRole(
    role: LanguageRole,
    languages: ICellLanguage[],
): string | undefined {
    return languages.find((other) => other.role === role)?.tag;
}

// The language tag of a role the menu lists, which always has one.
function tagOfRole(role: LanguageRole, languages: ICellLanguage[]): string {
    const tag = tagOfStoredRole(role, languages);
    if (tag === undefined) {
        throw new Error(`The collection gives role ${role} no language`);
    }
    return tag;
}

// The languages a group shows, by tag, each once. Two roles can name one language: V, the
// toolbar's first language, is often the collection's L2 as well. A stored role with no language
// shows nothing.
function tagsOfGroup(group: HTMLElement, languages: ICellLanguage[]): string[] {
    const tags = rolesOfGroup(group, languages)
        .map((role) => tagOfStoredRole(role, languages))
        .filter((tag): tag is string => tag !== undefined);
    return [...new Set(tags)];
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
 * Make `group` store and show exactly `roles`. Returns the editables added, as showSettingInGroup
 * does.
 */
export function showRolesInGroup(
    group: HTMLElement,
    roles: LanguageRole[],
    languages: ICellLanguage[],
): HTMLElement[] {
    return showSettingInGroup(group, settingOfRoles(roles), languages);
}

/**
 * Make `group` store `setting` exactly as given, including no setting at all, and show the
 * languages it stands for. Where the group has no bloom-editable in one of those languages yet (a
 * cell made since the page loaded has only its first one), one is added. Returns the added ones,
 * which need the wiring that page load gives the rest.
 */
export function showSettingInGroup(
    group: HTMLElement,
    setting: string | null,
    languages: ICellLanguage[],
): HTMLElement[] {
    if (setting === null) {
        group.removeAttribute("data-default-languages");
    } else {
        group.setAttribute("data-default-languages", setting);
    }
    const roles = rolesFromSetting(setting, languages);
    const wanted = languages.filter((language) =>
        roles.includes(language.role),
    );
    const added = wanted
        .filter((language) => !editableInLanguage(group, language.tag))
        .map((language) => addEditable(group, language, languages));
    const wantedTags = wanted.map((language) => language.tag);
    Array.from(
        group.querySelectorAll<HTMLElement>(":scope > .bloom-editable"),
    ).forEach((editable) => {
        Array.from(editable.classList)
            .filter((name) => name.startsWith("bloom-visibility-code-"))
            .forEach((name) => editable.classList.remove(name));
        if (wantedTags.includes(editable.getAttribute("lang") ?? "")) {
            editable.classList.add("bloom-visibility-code-on");
        }
    });
    return added;
}

function editableInLanguage(
    group: HTMLElement,
    tag: string,
): HTMLElement | undefined {
    return (
        group.querySelector<HTMLElement>(
            `:scope > .bloom-editable[lang="${tag}"]`,
        ) ?? undefined
    );
}

// A new, empty editable in `language`, with the paragraph style of the group's other editables,
// placed among them in role order so the languages read L1, L2, L3 down the cell.
function addEditable(
    group: HTMLElement,
    language: ICellLanguage,
    languages: ICellLanguage[],
): HTMLElement {
    const sibling = group.querySelector<HTMLElement>(
        ":scope > .bloom-editable",
    );
    const styleClasses = sibling
        ? Array.from(sibling.classList).filter((name) =>
              name.endsWith("-style"),
          )
        : ["normal-style"];
    const editable = group.ownerDocument.createElement("div");
    editable.classList.add("bloom-editable", ...styleClasses);
    editable.setAttribute("lang", language.tag);
    editable.innerHTML = "<p></p>";
    const later = languages
        .slice(languages.indexOf(language) + 1)
        .map((other) => editableInLanguage(group, other.tag))
        .find((other) => other !== undefined);
    group.insertBefore(editable, later ?? null);
    return editable;
}

/**
 * Tick or untick `role` across the text cells of `groups`, as one undoable step. Ticking adds it
 * to every group that does not show its language; unticking takes every role naming that language
 * out of every group where it is not the only language.
 */
export function toggleRole(
    groups: HTMLElement[],
    role: LanguageRole,
    languages: ICellLanguage[],
    onNewEditables: (editables: HTMLElement[]) => void,
): void {
    const tick = roleState(groups, role, languages) !== "all";
    const tag = tagOfRole(role, languages);
    const changing = groups.filter((group) => {
        const tags = tagsOfGroup(group, languages);
        if (tick) return !tags.includes(tag);
        return tags.includes(tag) && tags.length > 1;
    });
    if (changing.length === 0) return;

    // The entry finds its groups by their place among the page's translation groups, and holds
    // none of them: undoing a table operation rebuilds the table's cells, so after an Add Row and
    // its Undo, the groups this change was made to are gone, and new ones stand in their places.
    // The stack's order means the page is back to how this change left it when its undo runs.
    const pageDocument = changing[0].ownerDocument;
    const places = placesOnPage(changing);
    const changes = changing.map((group, index) => {
        const before = rolesOfGroup(group, languages);
        const after = tick
            ? [...before, role]
            : before.filter(
                  (other) => tagOfStoredRole(other, languages) !== tag,
              );
        return {
            place: places[index],
            // Undo puts back the attribute exactly as it was, including no attribute at all.
            beforeSetting: group.getAttribute("data-default-languages"),
            afterSetting: settingOfRoles(after),
        };
    });
    // Make each changed group store its setting from `from` again; check first that every one
    // stores its setting from the other side of the change.
    const show = (
        from: "beforeSetting" | "afterSetting",
        expected: "beforeSetting" | "afterSetting",
    ) => {
        const all = translationGroupsOf(pageDocument);
        const groups = changes.map((change) => {
            const group = all[change.place];
            if (
                !group ||
                group.getAttribute("data-default-languages") !==
                    change[expected]
            ) {
                throw new Error(
                    "A table cell's languages changed after the change being undone or redone",
                );
            }
            return group;
        });
        onNewEditables(
            groups.flatMap((group, index) =>
                showSettingInGroup(group, changes[index][from], languages),
            ),
        );
    };
    onNewEditables(
        changing.flatMap((group, index) =>
            showSettingInGroup(group, changes[index].afterSetting, languages),
        ),
    );
    theOneUndoStack.push({
        label: "Change the languages of table cells",
        kind: "custom",
        // In Change Layout mode the table is faded and cannot be edited, as with table operations.
        isAvailable: () => !isInChangeLayoutMode(pageDocument),
        // Hiding languages never adds an editable, so undo's call of onNewEditables does nothing.
        undo: () => show("beforeSetting", "afterSetting"),
        redo: () => show("afterSetting", "beforeSetting"),
    });
}

function translationGroupsOf(pageDocument: Document): HTMLElement[] {
    return Array.from(
        pageDocument.querySelectorAll<HTMLElement>(".bloom-translationGroup"),
    );
}

// The place of each group among the page's translation groups. A function of its own, so that
// the list of every group on the page is not kept alive by the undo entry's closures.
function placesOnPage(groups: HTMLElement[]): number[] {
    const all = translationGroupsOf(groups[0].ownerDocument);
    return groups.map((group) => all.indexOf(group));
}
