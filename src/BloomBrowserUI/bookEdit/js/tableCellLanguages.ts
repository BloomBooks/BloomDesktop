// The languages a table's text cell shows (BL-17105).
//
// A text cell holds a bloom-translationGroup, and the roles it shows are stored on that group in
// data-default-languages: V for the collection's first language, N1 for its second and N2 for its
// third, or a list such as "V,N1". Roles rather than language tags, so that a cell keeps its role
// when the book is translated in another collection. A group with no setting shows the first
// language only. Unlike every other translation group, a table cell does not follow the toolbar's
// language choice; TranslationGroupManager.LanguagesOfTableTextCell (C#) applies the same rule when
// a page loads, and this module applies it at once when the user changes a cell.

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
}

/**
 * The languages a table cell can show, in role order: every role the collection gives a language,
 * even one whose language is the same as an earlier role's, since a cell can have that role
 * stored (it was set in another collection) and must still show its language. The menu lists
 * each language once; see menuLanguages.
 */
export function cellLanguagesOfCollection(
    names: ILanguageNameValues,
): ICellLanguage[] {
    const candidates: ICellLanguage[] = [
        {
            role: "V",
            shortName: "L1",
            tag: names.language1Tag,
            name: names.language1Name,
            tooltipEnglish: "Local Language",
            tooltipL10nId:
                "CollectionSettingsDialog.LanguageTab.VernacularLanguageLabel",
        },
        {
            role: "N1",
            shortName: "L2",
            tag: names.language2Tag,
            name: names.language2Name,
            tooltipEnglish: "Language 2 (e.g. National Language)",
            tooltipL10nId:
                "CollectionSettingsDialog.LanguageTab._language2Label",
        },
        {
            role: "N2",
            shortName: "L3",
            tag: names.language3Tag ?? "",
            name: names.language3Name ?? "",
            tooltipEnglish: "Language 3 (e.g. Regional Language)   (Optional)",
            tooltipL10nId:
                "CollectionSettingsDialog.LanguageTab._language3Label",
        },
    ];
    return candidates.filter((candidate) => !!candidate.tag);
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

/** The roles a text cell's group shows, in role order. A group with no setting shows L1. */
export function rolesOfGroup(group: HTMLElement): LanguageRole[] {
    return rolesFromSetting(group.getAttribute("data-default-languages"));
}

/** The roles that a value of data-default-languages on a text cell's group stands for. */
export function rolesFromSetting(setting: string | null): LanguageRole[] {
    const tokens = (setting ?? "")
        .split(/[,\s]+/)
        .filter((token) => token.length > 0)
        .map((token) => (token === "L1" ? "V" : token));
    if (tokens.length === 0 || tokens[0].toLowerCase() === "auto") {
        return ["V"];
    }
    return kRoleOrder.filter((role) => tokens.includes(role));
}

/** Whether every group shows a role, none does, or only some do. */
export type RoleState = "all" | "none" | "some";

/** How the role stands across `groups`, for drawing its check box. */
export function roleState(
    groups: HTMLElement[],
    role: LanguageRole,
): RoleState {
    const count = groups.filter((group) =>
        rolesOfGroup(group).includes(role),
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
): boolean {
    return (
        roleState(groups, role) === "all" &&
        groups.some((group) => rolesOfGroup(group).length === 1)
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
    const tick = roleState(groups, role) !== "all";
    const changing = groups.filter((group) => {
        const roles = rolesOfGroup(group);
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
        const before = rolesOfGroup(group);
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
                    rolesFromSetting(beforeAttribute),
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
