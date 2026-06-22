// Bloom-side host for the self-contained game theme editor project (src/gameThemeEditor).
//
// This runs in the TOOLBOX iframe (which is served live by vite dev), and reaches the live
// editable page cross-frame via ToolBox.getPage() — exactly the pattern ThemeChooser already
// uses to apply theme classes. It mounts the editor panel into a bloom-ui container inside the
// editable-page document so the panel floats over the real game and can recolor it in real time.
//
// We deliberately do NOT route through the page-frame bundle (editablePageBundle): on this
// branch that bundle is not served by vite dev (see ViteReadMe.txt), so a page-frame export
// would be stale/missing during development. Driving from the toolbox works in dev and prod.
//
// Coupling to the editor is one-directional: we import its mount/unmount + types + the variable
// name list, and we hand it a concrete IGameThemeEditorHost. The editor imports nothing from Bloom.

import { mount, unmount, themeVariableNames } from "gameThemeEditor";
import type { IGameThemeEditorHost, Theme } from "gameThemeEditor";
import { getAsync, postJsonAsync } from "../../../utils/bloomApi";
import { ToolBox } from "../toolbox";

const kGameThemePrefix = "game-theme-";
const kEditorContainerClass = "bloom-ui-game-theme-editor";
// Live-preview <style> element: marked bloom-ui so Bloom never saves it into the book.
const kPreviewStyleId = "gameThemeEditorPreviewStyle";

// The live .bloom-page element in the editable-page iframe (reached cross-frame from the toolbox).
const getLivePage = (): HTMLElement | null => {
    const pageBody = ToolBox.getPage();
    return (
        (pageBody?.getElementsByClassName("bloom-page")[0] as HTMLElement) ??
        null
    );
};

const getCurrentSlug = (page: HTMLElement): string => {
    const missing = page.getAttribute("data-missing-game-theme");
    if (missing) return missing;
    const cls = Array.from(page.classList).find((c) =>
        c.startsWith(kGameThemePrefix),
    );
    return cls ? cls.substring(kGameThemePrefix.length) : "";
};

/** Where a theme is defined. "none" means new/unsaved (only an injected <style>, or absent). */
export type ThemeSource = "factory" | "collection" | "book" | "none";

const classifyStylesheet = (
    href: string | null,
): "collection" | "book" | "factory" | "inline" => {
    if (!href) return "inline"; // an injected <style> (transient)
    if (href.includes("customCollectionStyles")) return "collection";
    if (href.includes("customBookStyles")) return "book";
    return "factory"; // a shipped file such as Games.css
};

/** Which stylesheets currently define a theme slug. */
export interface ThemeLocations {
    inCollection: boolean;
    inBook: boolean;
    inFactory: boolean;
}

/** Find which stylesheet kinds (collection/book/factory) currently define a theme slug. */
export const getThemeLocationsForSlug = (slug: string): ThemeLocations => {
    const result: ThemeLocations = {
        inCollection: false,
        inBook: false,
        inFactory: false,
    };
    const page = getLivePage();
    if (!page || !slug) return result;
    const re = new RegExp(`\\.${kGameThemePrefix}${slug}(?![\\w-])`);
    for (const sheet of Array.from(page.ownerDocument.styleSheets)) {
        let rules: CSSRuleList;
        try {
            rules = sheet.cssRules;
        } catch {
            continue; // cross-origin; skip
        }
        let definesSlug = false;
        for (const rule of Array.from(rules)) {
            // Duck-type instead of `instanceof CSSStyleRule`: these rules belong to the page
            // document's realm, so instanceof against this (toolbox) realm's CSSStyleRule fails.
            const selector = (rule as CSSStyleRule).selectorText;
            if (selector && re.test(selector)) {
                definesSlug = true;
                break;
            }
        }
        if (!definesSlug) continue;
        const where = classifyStylesheet(sheet.href);
        if (where === "collection") result.inCollection = true;
        else if (where === "book") result.inBook = true;
        else if (where === "factory") result.inFactory = true;
    }
    return result;
};

/**
 * Classify where a theme slug is defined. A custom location (the user's book or collection)
 * wins over the shipped factory stylesheet, since that's the copy the user controls. Collection
 * wins over book if (unusually) both exist.
 */
export const getThemeSourceForSlug = (slug: string): ThemeSource => {
    const loc = getThemeLocationsForSlug(slug);
    if (loc.inCollection) return "collection";
    if (loc.inBook) return "book";
    if (loc.inFactory) return "factory";
    return "none";
};

/**
 * Whether a theme is a built-in/factory theme (defined only in a shipped stylesheet). Themes the
 * user has saved to a book or collection are "custom" and may be renamed; factory themes may not.
 */
export const isFactoryThemeSlug = (slug: string): boolean =>
    getThemeSourceForSlug(slug) === "factory";

/**
 * Remove a theme's rule from the live page (used after a rename so the old name disappears
 * without a reload): drop our injected <style> for it and delete any matching rules from the
 * loaded stylesheets. The authoritative copy on disk is updated separately by the API.
 */
const removeLiveThemeRule = (
    page: HTMLElement,
    slug: string,
    where?: "collection" | "book",
) => {
    const doc = page.ownerDocument;
    // Only drop our injected <style> when removing everywhere (a rename or a full delete);
    // a targeted delete leaves it so any copy in the other location keeps rendering.
    if (!where) doc.getElementById(`gameThemeEditorSaved-${slug}`)?.remove();
    const re = new RegExp(`\\.${kGameThemePrefix}${slug}(?![\\w-])`);
    for (const sheet of Array.from(doc.styleSheets)) {
        if (where && classifyStylesheet(sheet.href) !== where) continue;
        let rules: CSSRuleList;
        try {
            rules = sheet.cssRules;
        } catch {
            continue;
        }
        for (let i = rules.length - 1; i >= 0; i--) {
            const selector = (rules[i] as CSSStyleRule).selectorText;
            if (selector && re.test(selector)) {
                try {
                    sheet.deleteRule(i);
                } catch {
                    // some stylesheets disallow deletion; ignore
                }
            }
        }
    }
};

// The theme to fall back to when the current theme is deleted entirely. blue-on-white is a
// built-in factory theme that always exists. It's also the base for a brand-"New" theme.
const kDefaultThemeSlug = "blue-on-white";

// When the editor is opened on a NEW theme, we switch the live page to the base theme so the
// editor reads its colors and previews it. This remembers the theme that was showing so we can
// restore it if the user cancels; a successful save clears it (the saved theme is applied instead).
let themeToRestoreOnClose: string | null = null;

const switchPageToTheme = (page: HTMLElement, slug: string) => {
    for (const c of Array.from(page.classList))
        if (c.startsWith(kGameThemePrefix)) page.classList.remove(c);
    page.removeAttribute("data-missing-game-theme");
    page.classList.add(`${kGameThemePrefix}${slug}`);
};

// After deleting a theme from one location, if it no longer exists anywhere, clean up any
// leftover live rule and move the page to the default theme so it isn't left referencing nothing.
const afterDelete = (page: HTMLElement, slug: string) => {
    const loc = getThemeLocationsForSlug(slug);
    if (!loc.inCollection && !loc.inBook && !loc.inFactory) {
        removeLiveThemeRule(page, slug);
        switchPageToTheme(page, kDefaultThemeSlug);
    }
};

/**
 * Resolve a theme's header colors (background + text) so the chooser can preview each theme.
 * We measure them off a throwaway .bloom-page element carrying the theme's class, letting the
 * browser's real cascade derive the values exactly as it would on a real page.
 */
export const resolveThemeHeaderColors = (
    slug: string,
): { bg: string; color: string } => {
    const page = getLivePage();
    if (!page || !slug) return { bg: "", color: "" };
    const doc = page.ownerDocument;
    const probe = doc.createElement("div");
    probe.className = `bloom-ui bloom-page ${kGameThemePrefix}${slug}`;
    probe.style.cssText =
        "position:absolute;left:-9999px;top:-9999px;width:0;height:0;visibility:hidden;";
    doc.body.appendChild(probe);
    const cs = doc.defaultView!.getComputedStyle(probe);
    const colors = {
        bg: cs.getPropertyValue("--game-header-bg-color").trim(),
        color: cs.getPropertyValue("--game-header-color").trim(),
    };
    probe.remove();
    return colors;
};

// Rebuild the live-preview style element from the current override map. We use !important on the
// custom properties so the preview wins over the theme rule regardless of selector specificity or
// stylesheet order, and keep it in a bloom-ui element so it is never persisted into the saved page.
const previewOverrides: Record<string, string> = {};
const applyPreview = (page: HTMLElement) => {
    const doc = page.ownerDocument;
    let style = doc.getElementById(kPreviewStyleId) as HTMLStyleElement | null;
    if (!style) {
        style = doc.createElement("style");
        style.id = kPreviewStyleId;
        style.className = "bloom-ui";
        doc.head.appendChild(style);
    }
    const decls = Object.entries(previewOverrides)
        .map(([name, value]) => `  ${name}: ${value} !important;`)
        .join("\n");
    style.textContent = `.bloom-page {\n${decls}\n}`;
};

// Forget all pending preview overrides. Done separately from removing the <style> so we can
// always clear the map even if the page is gone, preventing stale overrides leaking into a
// later session/page.
const clearPreviewOverrides = () => {
    for (const key of Object.keys(previewOverrides))
        delete previewOverrides[key];
};

const clearPreview = (page: HTMLElement) => {
    clearPreviewOverrides();
    const style = page.ownerDocument.getElementById(kPreviewStyleId);
    if (style) style.remove();
};

const generateRuleCss = (theme: Theme): string => {
    const lines = [`.bloom-page.game-theme-${theme.slug} {`];
    Object.keys(theme.variables)
        .sort()
        .forEach((name) => lines.push(`  ${name}: ${theme.variables[name]};`));
    lines.push("}");
    return lines.join("\n");
};

// Inject the saved theme's real rule as a bloom-ui <style> so the game-theme-<slug> class
// resolves immediately (and the theme is discoverable by ThemeChooser) without a reload; the
// authoritative copy is on disk. Then switch the page to that theme class and drop the preview.
const applySavedTheme = (page: HTMLElement, theme: Theme) => {
    const doc = page.ownerDocument;
    const id = `gameThemeEditorSaved-${theme.slug}`;
    let style = doc.getElementById(id) as HTMLStyleElement | null;
    if (!style) {
        style = doc.createElement("style");
        style.id = id;
        style.className = "bloom-ui";
        doc.head.appendChild(style);
    }
    style.textContent = generateRuleCss(theme);

    for (const c of Array.from(page.classList))
        if (c.startsWith(kGameThemePrefix)) page.classList.remove(c);
    page.removeAttribute("data-missing-game-theme");
    page.classList.add(`${kGameThemePrefix}${theme.slug}`);

    clearPreview(page);
    // The new theme is now applied, so there's nothing to restore on close.
    themeToRestoreOnClose = null;
};

// opts.newName is set when the editor was opened via "New": the editor seeds its colors from
// the current theme but presents this name, treats the theme as custom (renamable), and saves
// under a new slug without deleting the original.
const makeHost = (
    page: HTMLElement,
    opts?: { newName?: string },
): IGameThemeEditorHost => ({
    getCurrentThemeName: () => getCurrentSlug(page),

    getNewThemeName: () => opts?.newName ?? null,

    isFactoryTheme: () =>
        opts?.newName ? false : isFactoryThemeSlug(getCurrentSlug(page)),

    getThemeSource: () =>
        opts?.newName ? "none" : getThemeSourceForSlug(getCurrentSlug(page)),

    getThemeLocations: () =>
        opts?.newName
            ? { inCollection: false, inBook: false }
            : (() => {
                  const l = getThemeLocationsForSlug(getCurrentSlug(page));
                  return { inCollection: l.inCollection, inBook: l.inBook };
              })(),

    readAppliedVariables: () => {
        const computed = page.ownerDocument.defaultView!.getComputedStyle(page);
        const result: Record<string, string> = {};
        for (const name of themeVariableNames)
            result[name] = computed.getPropertyValue(name).trim();
        return result;
    },

    getThemeDefinition: (slug: string) => {
        const result: Record<string, string> = {};
        if (!slug) return result;
        const needle = `${kGameThemePrefix}${slug}`;
        // Scan the PAGE document's stylesheets (where gamesThemes.css and customCollectionStyles.css
        // are loaded), not the toolbox's.
        for (const sheet of Array.from(page.ownerDocument.styleSheets)) {
            let rules: CSSRuleList;
            try {
                rules = sheet.cssRules;
            } catch {
                continue; // cross-origin stylesheet; skip
            }
            for (const rule of Array.from(rules)) {
                // Duck-type rather than `instanceof CSSStyleRule`: rules come from the page
                // document's realm, where instanceof against this realm's class fails.
                const styleRule = rule as CSSStyleRule;
                if (!styleRule.selectorText || !styleRule.style) continue;
                // Match the theme's own class but not a longer name that merely starts with it.
                const re = new RegExp(`\\.${needle}(?![\\w-])`);
                if (!re.test(styleRule.selectorText)) continue;
                for (let i = 0; i < styleRule.style.length; i++) {
                    const prop = styleRule.style[i];
                    if (prop.startsWith("--game"))
                        result[prop] = styleRule.style
                            .getPropertyValue(prop)
                            .trim();
                }
            }
        }
        return result;
    },

    setLiveVariable: (name: string, value: string | undefined) => {
        if (value === undefined) delete previewOverrides[name];
        else previewOverrides[name] = value;
        applyPreview(page);
    },

    clearLiveOverrides: () => clearPreview(page),

    saveToBook: async (theme: Theme, renameFrom?: string) => {
        await postJsonAsync("gameThemeEditor/saveToBook", {
            ...theme,
            renameFrom,
        });
        applySavedTheme(page, theme);
        if (renameFrom && renameFrom !== theme.slug)
            removeLiveThemeRule(page, renameFrom);
    },

    saveToCollection: async (theme: Theme, renameFrom?: string) => {
        await postJsonAsync("gameThemeEditor/saveToCollection", {
            ...theme,
            renameFrom,
        });
        applySavedTheme(page, theme);
        if (renameFrom && renameFrom !== theme.slug)
            removeLiveThemeRule(page, renameFrom);
    },

    saveToFactorySource: async (theme: Theme, renameFrom?: string) => {
        await postJsonAsync("gameThemeEditor/saveToFactorySource", {
            ...theme,
            renameFrom,
        });
        applySavedTheme(page, theme);
        if (renameFrom && renameFrom !== theme.slug)
            removeLiveThemeRule(page, renameFrom);
    },

    themeExists: (slug: string) => {
        const l = getThemeLocationsForSlug(slug);
        return l.inCollection || l.inBook || l.inFactory;
    },

    deleteFromCollection: async (slug: string) => {
        await postJsonAsync("gameThemeEditor/deleteFromCollection", { slug });
        removeLiveThemeRule(page, slug, "collection");
        afterDelete(page, slug);
    },

    deleteFromBook: async (slug: string) => {
        await postJsonAsync("gameThemeEditor/deleteFromBook", { slug });
        removeLiveThemeRule(page, slug, "book");
        afterDelete(page, slug);
    },

    canSaveToFactorySource: async () => {
        const result = await getAsync("gameThemeEditor/canSaveToFactorySource");
        return !!(result && (result as { data?: boolean }).data);
    },

    close: () => hideGameThemeEditor(),
});

// Whether the floating editor is currently open, with a tiny subscription so the toolbox
// (ThemeChooser) can disable the theme dropdown while the editor owns theme changes.
let gameThemeEditorOpen = false;
const openListeners = new Set<() => void>();
const notifyOpenChange = () => openListeners.forEach((l) => l());

/** Whether the floating game theme editor is currently open. */
export const isGameThemeEditorOpen = (): boolean => gameThemeEditorOpen;

/** Subscribe to open/close changes; returns an unsubscribe function. */
export const subscribeGameThemeEditorOpen = (
    listener: () => void,
): (() => void) => {
    openListeners.add(listener);
    return () => {
        openListeners.delete(listener);
    };
};

// The document the floating panel mounts into. We use the TOP-LEVEL workspace document
// (not the small #page iframe) so the panel can be dragged/resized across the whole Bloom
// window and is never clipped by the page iframe. The live recoloring still targets the page
// document via the host closure, independent of where the panel is shown. All Bloom frames are
// same-origin, so window.top.document is reachable.
const getPanelDocument = (): Document => {
    try {
        if (window.top && window.top.document) return window.top.document;
    } catch {
        // cross-origin (not expected in Bloom) — fall back below
    }
    return document;
};

// Open the editor over the live page; opts.newName starts it as a copy ("New") of the current
// theme under that name. Shared by the normal and "New" entry points below.
const openEditor = (opts?: { newName?: string; baseSlug?: string }): void => {
    const page = getLivePage();
    if (!page) return;
    // For a NEW theme based on a specific theme, switch the live page to that base theme so the
    // editor reads its colors and previews it. Remember the theme that was showing so we can put
    // it back if the user cancels (a successful save applies the new theme instead).
    if (
        opts?.newName &&
        opts.baseSlug &&
        opts.baseSlug !== getCurrentSlug(page)
    ) {
        themeToRestoreOnClose = getCurrentSlug(page);
        switchPageToTheme(page, opts.baseSlug);
    }
    const doc = getPanelDocument();
    let container = doc.getElementsByClassName(
        kEditorContainerClass,
    )[0] as HTMLElement;
    if (!container) {
        container = doc.createElement("div");
        // bloom-ui => never saved into the book; second class lets us find/remove it.
        container.classList.add("bloom-ui", kEditorContainerClass);
        // A full-viewport overlay so the floating panel (react-rnd) can be dragged/resized across
        // the window and never lost off-screen. pointer-events none lets clicks outside the panel
        // fall through to the page; the panel itself re-enables pointer-events.
        container.style.cssText =
            "position:fixed;inset:0;z-index:6000;pointer-events:none;";
        doc.body.appendChild(container);
    }
    mount(container, makeHost(page, opts));
    gameThemeEditorOpen = true;
    notifyOpenChange();
};

/** Open the editor to edit the theme currently applied to the page. Called from the Games toolbox. */
export const showGameThemeEditor = (): void => openEditor();

/**
 * Open the editor on a brand-new theme, pre-named (e.g. "Untitled Theme 3"), based on the
 * default factory theme (Blue On White) rather than whatever theme is currently applied.
 */
export const showNewGameThemeEditor = (newName: string): void =>
    openEditor({ newName, baseSlug: kDefaultThemeSlug });

/**
 * Open the editor on a new theme that starts as a copy of the theme currently applied to the
 * page (so the user can customize an existing theme under a new name).
 */
export const showCustomizeGameThemeEditor = (newName: string): void => {
    const page = getLivePage();
    openEditor({
        newName,
        baseSlug: page ? getCurrentSlug(page) : kDefaultThemeSlug,
    });
};

/** Close the editor, removing its container and reverting any unsaved live overrides. */
export const hideGameThemeEditor = (): void => {
    const page = getLivePage();
    if (page) {
        clearPreview(page);
        // If we switched to a base theme for a "New"/"Customize" theme and the user didn't save,
        // put the originally-showing theme back.
        if (themeToRestoreOnClose)
            switchPageToTheme(page, themeToRestoreOnClose);
    } else clearPreviewOverrides(); // page gone: at least forget overrides so they can't leak
    themeToRestoreOnClose = null;
    const container = getPanelDocument().getElementsByClassName(
        kEditorContainerClass,
    )[0] as HTMLElement;
    if (container) {
        unmount(container);
        container.remove();
    }
    gameThemeEditorOpen = false;
    notifyOpenChange();
};
