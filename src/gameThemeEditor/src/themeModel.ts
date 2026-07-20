// The game-theme CSS custom properties the editor exposes, with friendly names and the
// parent/child hierarchy that drives the outline tree. Ported from the standalone SPA's
// theme-utils.ts (cssVariables + buildHierarchyTree).
//
// We deliberately do NOT port that file's JS cascade-resolution engine
// (resolveVariableValue / getDerivationMap / defaultValues). In the real host the browser
// resolves the cascade natively: gamesThemes.less's .apply-game-theme() defines every
// derived variable as a var() chain rooted at the two "main" colors, so overriding a root
// on the live page makes all descendants cascade automatically. We read the resolved values
// back from the live page (getComputedStyle); the hierarchy here is only for drawing the tree.

export type { Theme } from "./host/IGameThemeEditorHost";

export interface ThemeVariableDef {
    /** the CSS custom property name, e.g. "--game-primary-color". */
    name: string;
    /** friendly label shown in the outline. */
    displayName: string;
    /** depth in the hierarchy (0 = a root/independent color). */
    level: number;
    /** the variable this one derives from, when not a root. */
    parent?: string;
}

/** A node in the outline tree: a variable plus its derived children. */
export interface HierarchyNode extends ThemeVariableDef {
    children: HierarchyNode[];
}

// Order and parent relationships mirror gamesThemes.less .apply-game-theme().
export const themeVariables: ThemeVariableDef[] = [
    { name: "--game-primary-color", displayName: "Primary", level: 0 },
    {
        name: "--game-primary-bg-color",
        displayName: "Primary Background",
        level: 1,
        parent: "--game-primary-color",
    },
    {
        name: "--game-button-correct-bg-color",
        displayName: "'Correct' Button Background",
        level: 2,
        parent: "--game-primary-bg-color",
    },
    {
        name: "--game-selected-checkbox-bg-color",
        displayName: "Selected Checkbox Background",
        level: 3,
        parent: "--game-button-correct-bg-color",
    },
    {
        name: "--game-selected-checkbox-outline-color",
        displayName: "Selected Checkbox Outline",
        level: 4,
        parent: "--game-selected-checkbox-bg-color",
    },
    {
        name: "--game-draggable-bg-color",
        displayName: "Draggable Background",
        level: 2,
        parent: "--game-primary-bg-color",
    },
    {
        name: "--game-draggable-target-outline-color",
        displayName: "Draggable Target Outline",
        level: 3,
        parent: "--game-draggable-bg-color",
    },
    {
        name: "--game-header-bg-color",
        displayName: "Header Background",
        level: 2,
        parent: "--game-primary-bg-color",
    },
    {
        name: "--game-control-button-bg-color",
        displayName: "Control Button Background",
        level: 2,
        parent: "--game-primary-bg-color",
    },
    {
        name: "--game-text-color",
        displayName: "Text",
        level: 1,
        parent: "--game-primary-color",
    },
    {
        name: "--game-page-number-color",
        displayName: "Page Number",
        level: 2,
        parent: "--game-text-color",
    },
    {
        name: "--game-checkbox-text-color",
        displayName: "Checkbox Text",
        level: 2,
        parent: "--game-text-color",
    },
    {
        name: "--game-checkbox-outline-color",
        displayName: "Checkbox Outline",
        level: 3,
        parent: "--game-checkbox-text-color",
    },
    {
        name: "--game-button-text-color",
        displayName: "Button Text",
        level: 1,
        parent: "--game-primary-color",
    },
    {
        name: "--game-button-outline-color",
        displayName: "Button Outline",
        level: 2,
        parent: "--game-button-text-color",
    },
    { name: "--game-secondary-color", displayName: "Secondary", level: 0 },
    {
        name: "--game-page-bg-color",
        displayName: "Page Background",
        level: 1,
        parent: "--game-secondary-color",
    },
    {
        name: "--page-background-color",
        displayName: "Appearance System Page Background",
        level: 2,
        parent: "--game-page-bg-color",
    },
    {
        name: "--game-button-bg-color",
        displayName: "Button Background",
        level: 1,
        parent: "--game-secondary-color",
    },
    {
        name: "--game-button-correct-color",
        displayName: "'Correct' Button Text/Icon",
        level: 1,
        parent: "--game-secondary-color",
    },
    {
        name: "--game-selected-checkbox-color",
        displayName: "Selected Checkbox Icon",
        level: 2,
        parent: "--game-button-correct-color",
    },
    {
        name: "--game-draggable-color",
        displayName: "Draggable Text/Icon",
        level: 1,
        parent: "--game-secondary-color",
    },
    {
        name: "--game-header-color",
        displayName: "Header Text",
        level: 1,
        parent: "--game-secondary-color",
    },
    {
        name: "--game-control-button-color",
        displayName: "Control Button Icon",
        level: 1,
        parent: "--game-secondary-color",
    },
    {
        name: "--game-button-wrong-color",
        displayName: "'Incorrect' Button Text/Icon",
        level: 0,
    },
    {
        name: "--game-button-wrong-bg-color",
        displayName: "'Incorrect' Button Background",
        level: 0,
    },
];

/** All theme variable names the editor exposes (handy for hosts seeding computed values). */
export const themeVariableNames = themeVariables.map((v) => v.name);

/** The two root colors that the rest of the theme derives from; always saved. */
export const rootVariableNames = [
    "--game-primary-color",
    "--game-secondary-color",
];

/** Build the outline tree (roots and their derived children) from the flat variable list. */
export const buildHierarchyTree = (): HierarchyNode[] => {
    const nodeMap: Record<string, HierarchyNode> = {};
    const roots: HierarchyNode[] = [];

    themeVariables.forEach((variable) => {
        nodeMap[variable.name] = { ...variable, children: [] };
    });

    themeVariables.forEach((variable) => {
        if (variable.parent && nodeMap[variable.parent]) {
            nodeMap[variable.parent].children.push(nodeMap[variable.name]);
        } else {
            roots.push(nodeMap[variable.name]);
        }
    });

    return roots;
};

/** Turn a slug like "blue-on-white" into a display name like "Blue On White". */
export const slugToDisplayName = (slug: string): string =>
    slug
        .split("-")
        .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
        .join(" ");

/** Turn a display name into a url-safe slug. */
export const slugify = (text: string): string =>
    text
        .toLowerCase()
        .replace(/\s+/g, "-")
        .replace(/[^\w-]+/g, "")
        .replace(/--+/g, "-")
        .replace(/^-+/, "")
        .replace(/-+$/, "");
