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
    /** The Collection Settings label for the role, used as the entry's tooltip. */
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

/** Whether every group shows a role, none does, or only some do. */
export type RoleState = "all" | "none" | "some";

/** How the role stands across `groups`, for drawing its check box. */
export function roleState(
    groups: HTMLElement[],
    role: LanguageRole,
    languages: ICellLanguage[],
): RoleState {
    const count = groups.filter((group) =>
        rolesOfGroup(group, languages).includes(role),
    ).length;
    if (count === 0) return "none";
    if (count === groups.length) return "all";
    return "some";
}

/**
 * True when the role's box may not be unticked: every group shows it, and in at least one it is
 * the only language, which unticking would leave with none.
 */
export function roleIsLastLanguageSomewhere(
    groups: HTMLElement[],
    role: LanguageRole,
    languages: ICellLanguage[],
): boolean {
    return (
        roleState(groups, role, languages) === "all" &&
        groups.some((group) => rolesOfGroup(group, languages).length === 1)
    );
}

/**
 * Make `group` store and show exactly `roles`. Where the group has no bloom-editable in one of the
 * roles' languages yet (a cell made since the page loaded has only its first one), one is added.
 * Returns the added ones, which need the wiring that page load gives the rest.
 */
export function showRolesInGroup(
    group: HTMLElement,
    roles: LanguageRole[],
    languages: ICellLanguage[],
): HTMLElement[] {
    group.setAttribute(
        "data-default-languages",
        kRoleOrder.filter((role) => roles.includes(role)).join(","),
    );
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
 * to every group; unticking takes it out of every group where it is not the only language.
 */
export function toggleRole(
    groups: HTMLElement[],
    role: LanguageRole,
    languages: ICellLanguage[],
    onNewEditables: (editables: HTMLElement[]) => void,
): void {
    const tick = roleState(groups, role, languages) !== "all";
    const changing = groups.filter((group) => {
        const roles = rolesOfGroup(group, languages);
        if (tick) return !roles.includes(role);
        return roles.includes(role) && roles.length > 1;
    });
    if (changing.length === 0) return;

    // The entry finds its groups by their place among the page's translation groups, and holds
    // none of them: undoing a table operation rebuilds the table's cells, so after an Add Row and
    // its Undo, the groups this change was made to are gone, and new ones stand in their places.
    // The stack's order means the page is back to how this change left it when its undo runs.
    const pageDocument = changing[0].ownerDocument;
    const allGroups = () =>
        Array.from(
            pageDocument.querySelectorAll<HTMLElement>(
                ".bloom-translationGroup",
            ),
        );
    const pageGroups = allGroups();
    const changes = changing.map((group) => {
        const before = rolesOfGroup(group, languages);
        return {
            place: pageGroups.indexOf(group),
            // Undo puts back the attribute exactly as it was, including no attribute at all.
            beforeAttribute: group.getAttribute("data-default-languages"),
            after: tick
                ? [...before, role]
                : before.filter((other) => other !== role),
        };
    });
    const showAfter = (targets: HTMLElement[]) =>
        onNewEditables(
            targets.flatMap((group, index) =>
                showRolesInGroup(group, changes[index].after, languages),
            ),
        );
    showAfter(changing);
    const afterAttributes = changing.map((group) =>
        group.getAttribute("data-default-languages"),
    );
    const groupsNow = (expected: (string | null)[]): HTMLElement[] => {
        const all = allGroups();
        return changes.map((change, index) => {
            const group = all[change.place];
            if (
                !group ||
                group.getAttribute("data-default-languages") !== expected[index]
            ) {
                throw new Error(
                    "A table cell's languages changed after the change being undone or redone",
                );
            }
            return group;
        });
    };
    theOneUndoStack.push({
        label: "Change the languages of table cells",
        kind: "custom",
        // In Change Layout mode the table is faded and cannot be edited, as with table operations.
        isAvailable: () => !isInChangeLayoutMode(pageDocument),
        undo: () => {
            groupsNow(afterAttributes).forEach((group, index) => {
                const beforeAttribute = changes[index].beforeAttribute;
                // Hiding languages never adds an editable.
                showRolesInGroup(
                    group,
                    rolesFromSetting(beforeAttribute, languages),
                    languages,
                );
                if (beforeAttribute === null) {
                    group.removeAttribute("data-default-languages");
                } else {
                    group.setAttribute(
                        "data-default-languages",
                        beforeAttribute,
                    );
                }
            });
        },
        redo: () =>
            showAfter(
                groupsNow(changes.map((change) => change.beforeAttribute)),
            ),
    });
}
