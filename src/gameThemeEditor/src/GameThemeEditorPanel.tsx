/** @jsxImportSource @emotion/react */
import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useMemo, useRef, useState } from "react";
import type { IGameThemeEditorHost, Theme } from "./host/IGameThemeEditorHost";
import {
    rootVariableNames,
    slugify,
    slugToDisplayName,
    themeVariables,
} from "./themeModel";
import { DraggableResizableFrame } from "./DraggableResizableFrame";
import { ColorsOutline } from "./ColorsOutline";
import { ContrastChecker } from "./ContrastChecker";
import { parseColorToRgba } from "./contrastUtils";
import { buildThemeSwatches } from "./colorUtils";
import { bloom } from "./bloomTheme";

// True when two CSS color strings denote the same color, comparing by RGB so that "black",
// "#000000", and "rgb(0,0,0)" all match. Used to spot a color that already equals what it
// would derive to if we didn't specify it.
const sameColor = (a: string, b: string): boolean => {
    const [ar, ag, ab, aa] = parseColorToRgba(a);
    const [br, bg, bb, ba] = parseColorToRgba(b);
    return ar === br && ag === bg && ab === bb && aa === ba;
};

// Each variable's parent in the derivation hierarchy (the chain .apply-game-theme() defines,
// e.g. --game-text-color derives from --game-primary-color). Built once from the static model.
const parentByName: Record<string, string> = {};
themeVariables.forEach((v) => {
    if (v.parent) parentByName[v.name] = v.parent;
});

// A small trash-bin glyph for the Delete links, inline so the editor stays dependency-free.
const TrashIcon: React.FunctionComponent = () => (
    <svg
        width="14"
        height="14"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
    >
        <path d="M3 6h18" />
        <path d="M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2" />
        <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
        <path d="M10 11v6M14 11v6" />
    </svg>
);

// The whole editor UI: a Colors outline plus an Elements (contrast) pane. It holds the working color values and which variables are "explicit" (part of
// the theme), pushes every edit to the host for live recoloring, re-reads the resolved
// values from the live page so derived colors update everywhere, and builds a Theme to
// hand back on save. No CSS variable names or raw CSS are shown.
export const GameThemeEditorPanel: React.FunctionComponent<{
    host: IGameThemeEditorHost;
}> = (props) => {
    const host = props.host;
    const startingSlug = host.getCurrentThemeName();
    // "New" mode (a copy of the current theme): present a fresh name, treat as custom, and on
    // save write a new slug without deleting the original.
    const newThemeName = host.getNewThemeName();
    const isNewTheme = newThemeName !== null;
    // Factory themes (shipped in source) can't be renamed and can only be saved back to source.
    const isFactory = isNewTheme ? false : host.isFactoryTheme();
    // When renaming an existing custom theme, the slug to replace (empty for new themes/factory).
    const renameFromSlug = isNewTheme ? "" : startingSlug;
    // Default the Save button to wherever the theme already lives: a collection theme defaults to
    // "Save to Collection"; a factory theme to source; otherwise (book/new) to "Save to Book".
    const themeSource = isNewTheme ? "none" : host.getThemeSource();
    const defaultTarget: "source" | "collection" | "book" = isFactory
        ? "source"
        : themeSource === "collection"
          ? "collection"
          : "book";
    // Where this (non-factory) theme can be deleted from. New themes aren't saved anywhere yet.
    const locations = isNewTheme
        ? { inCollection: false, inBook: false }
        : host.getThemeLocations();

    const [displayName, setDisplayName] = useState(
        isNewTheme ? newThemeName : slugToDisplayName(startingSlug || "custom"),
    );
    // Effective color of every variable (seeded from the live page's computed style).
    const [values, setValues] = useState<Record<string, string>>(() =>
        host.readAppliedVariables(),
    );
    // Which variables are explicitly part of the theme as loaded (the theme's own rule plus
    // the two roots, which are always written). Captured once so "Reset" can restore it.
    const initialExplicit = useMemo(
        () =>
            new Set<string>([
                ...rootVariableNames,
                ...Object.keys(host.getThemeDefinition(startingSlug)),
            ]),
        [host, startingSlug],
    );
    const [explicit, setExplicit] = useState<Set<string>>(
        () => new Set(initialExplicit),
    );
    const [canDev, setCanDev] = useState(false);
    const [status, setStatus] = useState<string>("");
    const [busy, setBusy] = useState(false);
    // Variables used by the preview/contrast item the user last clicked; the Colors outline
    // highlights and reveals them.
    const [highlightedVars, setHighlightedVars] = useState<string[]>([]);

    // The picker palette: black, white, and the theme's own current colors (deduped so colors
    // that look the same aren't offered twice). Recomputed as the live colors change.
    const swatches = useMemo(
        () => buildThemeSwatches(Object.values(values)),
        [values],
    );

    useEffect(() => {
        host.canSaveToFactorySource().then(setCanDev);
    }, [host]);

    // Drop any explicit color that already equals what it would derive to if we hadn't specified
    // it (its parent's resolved value in the derivation hierarchy — the same chain the Colors
    // outline is built from), keeping the theme minimal. To "drop" one we make it DERIVE from its
    // parent (override it to var(--parent)) rather than clearing the override: clearing would fall
    // back to the theme's own stylesheet rule, which may pin a different color, so the page would
    // visibly change. Deriving shows the same color, tracks the parent, and isn't saved (it's no
    // longer in "explicit"). Returns the pruned set; order-independent since each variable is
    // compared to its parent's resolved value and deriving a redundant one doesn't change a value.
    const dropRedundantColors = (currentExplicit: Set<string>): Set<string> => {
        const resolved = host.readAppliedVariables();
        const next = new Set(currentExplicit);
        for (const def of themeVariables) {
            // Roots/independent colors have no parent to derive from, so keep them.
            if (!def.parent || !next.has(def.name)) continue;
            if (sameColor(resolved[def.name], resolved[def.parent])) {
                host.setLiveVariable(def.name, `var(${def.parent})`);
                next.delete(def.name);
            }
        }
        return next;
    };

    // On open, prune any colors the loaded theme specifies redundantly (already equal to what
    // they'd derive to), so the editor starts from a minimal theme and the outline shows only
    // genuinely-customized colors. Runs once.
    useEffect(() => {
        setExplicit(dropRedundantColors(explicit));
        setValues(host.readAppliedVariables());
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    // A freshly-created "Untitled …" theme wants a real name, so highlight the field on open
    // so the user can just start typing. We focus immediately and again after a short delay,
    // because when opened from the theme dropdown the closing menu restores focus to the
    // dropdown right after we mount, which would otherwise steal it back.
    const nameInputRef = useRef<HTMLInputElement>(null);
    useEffect(() => {
        if (!displayName.startsWith("Untitled")) return;
        const selectName = () => {
            nameInputRef.current?.focus();
            nameInputRef.current?.select();
        };
        selectName();
        const timer = setTimeout(selectName, 150);
        return () => clearTimeout(timer);
        // Only on first open.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    const handleChange = (name: string, hex: string) => {
        host.setLiveVariable(name, hex);
        // Re-read all values: changing a root cascades to its derived descendants on the
        // live page, and we want those new colors reflected in the outline/preview/contrast.
        // Then drop any color (the one just set, or a descendant) that now matches what it
        // would derive to anyway.
        setExplicit(dropRedundantColors(new Set(explicit).add(name)));
        setValues(host.readAppliedVariables());
        setStatus("");
    };

    const handleReset = (name: string) => {
        // Make the variable derive from its parent again. We override it to var(--parent) rather
        // than clearing the override, because clearing would fall back to the theme's stylesheet
        // rule (which may pin a different color) instead of the derived value the user expects.
        const parent = parentByName[name];
        host.setLiveVariable(name, parent ? `var(${parent})` : undefined);
        setExplicit((prev) => {
            const next = new Set(prev);
            next.delete(name);
            return next;
        });
        // The browser has recomputed the derived value; re-read so swatches show it.
        setValues(host.readAppliedVariables());
        setStatus("");
    };

    // Discard all edits made in this session, restoring the theme exactly as it was loaded.
    const handleResetAll = () => {
        host.clearLiveOverrides();
        setExplicit(new Set(initialExplicit));
        setValues(host.readAppliedVariables());
        setStatus("");
    };

    const buildTheme = (): Theme => {
        const variables: Record<string, string> = {};
        explicit.forEach((name) => {
            variables[name] = values[name];
        });
        return { slug: slugify(displayName), displayName, variables };
    };

    const del = async (where: "collection" | "book") => {
        setBusy(true);
        setStatus("Deleting…");
        try {
            if (where === "collection")
                await host.deleteFromCollection(startingSlug);
            else await host.deleteFromBook(startingSlug);
            host.close();
        } catch (e) {
            setStatus("Delete failed: " + (e as Error).message);
        } finally {
            setBusy(false);
        }
    };

    const save = async (target: "book" | "collection" | "source") => {
        const theme = buildTheme();
        if (!theme.slug) {
            setStatus("Please enter a theme name.");
            return;
        }
        // Don't clobber a different existing theme by renaming/creating onto its name.
        if (theme.slug !== startingSlug && host.themeExists(theme.slug)) {
            setStatus(
                `A theme named "${theme.displayName}" already exists. Please choose another name.`,
            );
            return;
        }
        setBusy(true);
        setStatus("Saving…");
        try {
            if (target === "source")
                await host.saveToFactorySource(theme, renameFromSlug);
            else if (target === "book")
                await host.saveToBook(theme, renameFromSlug);
            else await host.saveToCollection(theme, renameFromSlug);
            // A successful save applies the theme to the page, so we're done: close the dialog.
            host.close();
        } catch (e) {
            setStatus("Save failed: " + (e as Error).message);
        } finally {
            setBusy(false);
        }
    };

    return (
        <DraggableResizableFrame
            title="Game Theme Editor"
            onClose={host.close}
            defaultWidth={1040}
            defaultHeight={600}
            minWidth={300}
            minHeight={300}
            storageKey="game-theme-editor"
        >
            <div
                css={css`
                    padding: 10px 12px;
                    border-bottom: 1px solid #e0e0e0;
                    flex-shrink: 0;
                    display: flex;
                    align-items: flex-end;
                    gap: 8px;
                `}
            >
                <div>
                    <label
                        css={css`
                            display: block;
                            font-size: 12px;
                            color: #555;
                            margin-bottom: 3px;
                        `}
                    >
                        Theme name
                        {isFactory ? " (factory theme — can't rename)" : ""}
                    </label>
                    <input
                        ref={nameInputRef}
                        type="text"
                        value={displayName}
                        disabled={isFactory}
                        onChange={(e) => setDisplayName(e.target.value)}
                        css={css`
                            width: 21ch;
                            max-width: 100%;
                            box-sizing: border-box;
                            padding: 5px 7px;
                            font-size: 14px;
                            border: 1px solid #c0c0c0;
                            border-radius: 4px;
                            &:disabled {
                                background: #f3f3f3;
                                color: #777;
                            }
                        `}
                    />
                </div>
            </div>

            {/* The three panes flow: a wide (short) dialog lays them out side by side; a tall,
                thin dialog wraps them into a single column. Each pane has a flex-basis so wrapping
                is driven by the available width. */}
            <div
                css={css`
                    flex: 1;
                    min-height: 0;
                    overflow: auto;
                    padding: 12px;
                    display: flex;
                    flex-wrap: wrap;
                    align-items: flex-start;
                    gap: 12px;
                `}
            >
                {/* Elements pane on the left; clicking an element reveals its settings in the
                    Colors outline on the right. */}
                <div css={paneStyle}>
                    <ContrastChecker
                        resolvedValues={values}
                        onSelectVars={setHighlightedVars}
                    />
                </div>
                <div css={paneStyle}>
                    <ColorsOutline
                        resolvedValues={values}
                        isVariableOverridden={(n) => explicit.has(n)}
                        onColorChange={handleChange}
                        onReset={handleReset}
                        onResetAll={handleResetAll}
                        highlightedVars={highlightedVars}
                        swatches={swatches}
                    />
                </div>
            </div>

            <div
                css={css`
                    border-top: 1px solid #e0e0e0;
                    padding: 8px 12px;
                    display: flex;
                    align-items: center;
                    flex-wrap: wrap;
                    gap: 8px;
                    flex-shrink: 0;
                `}
            >
                {/* Borderless delete links on the far left, for a non-factory theme that exists
                    in the collection and/or this book. */}
                {!isFactory && locations.inCollection && (
                    <button
                        type="button"
                        disabled={busy}
                        onClick={() => del("collection")}
                        css={borderlessButtonStyle}
                    >
                        <TrashIcon />
                        Delete from Collection
                    </button>
                )}
                {!isFactory && locations.inBook && (
                    <button
                        type="button"
                        disabled={busy}
                        onClick={() => del("book")}
                        css={borderlessButtonStyle}
                    >
                        <TrashIcon />
                        Delete from Book
                    </button>
                )}
                {/* Developer-only, set apart on the far left: writes the factory source. Only
                    shown for an actual factory theme (it overwrites that theme's source); for a
                    factory theme it's the only available save, so make it prominent. In the custom
                    theme scenario it's hidden rather than shown disabled. */}
                {canDev && isFactory && (
                    <button
                        type="button"
                        disabled={busy}
                        onClick={() => save("source")}
                        css={buttonStyle("primary")}
                    >
                        Save Factory Theme to Source
                    </button>
                )}
                <span
                    css={css`
                        flex: 1 1 40px;
                        min-width: 0;
                        font-size: 12px;
                        color: #666;
                    `}
                >
                    {status}
                </span>
                {/* Factory themes can only be saved back to source (above), not to a book or
                    collection; to customize one, use "New" to make an editable copy. */}
                {/* Save to Collection is temporarily disabled.
                <button
                    type="button"
                    disabled={busy || isFactory}
                    onClick={() => save("collection")}
                    title="Save this theme so it can be used by every book in this collection."
                    css={buttonStyle(
                        defaultTarget === "collection" ? "primary" : "outline",
                    )}
                >
                    Save to Collection
                </button>
                */}
                <button
                    type="button"
                    disabled={busy || isFactory}
                    onClick={() => save("book")}
                    title="Save this theme in this book only."
                    css={buttonStyle(
                        defaultTarget === "book" ? "primary" : "outline",
                    )}
                >
                    Save to Book
                </button>
                <button
                    type="button"
                    disabled={busy}
                    onClick={host.close}
                    title="Discard your changes and close the editor."
                    css={buttonStyle("outline")}
                >
                    Cancel
                </button>
            </div>
        </DraggableResizableFrame>
    );
};

// Each of the three panes; flex-basis drives the wrap from a row (wide) to a column (thin).
const paneStyle = css`
    flex: 1 1 300px;
    min-width: 260px;
    display: flex;
    flex-direction: column;
    min-height: 0;
`;

// MUI-like buttons using Bloom's palette (contained = filled primary; outlined = bordered).
// A borderless text button with a leading icon (for the Delete links). Neutral color, not red.
const borderlessButtonStyle = css`
    border: none;
    background: transparent;
    padding: 6px 8px;
    font-size: 13px;
    cursor: pointer;
    color: #555;
    white-space: nowrap;
    display: inline-flex;
    align-items: center;
    gap: 5px;
    &:hover:not(:disabled) {
        text-decoration: underline;
    }
    &:disabled {
        opacity: 0.5;
        cursor: default;
    }
`;

const buttonStyle = (variant: "primary" | "outline") => css`
    padding: 6px 16px;
    font-size: 13px;
    font-weight: 500;
    text-transform: uppercase;
    letter-spacing: 0.03em;
    border-radius: 4px;
    cursor: pointer;
    white-space: nowrap;
    border: 1px solid ${variant === "primary" ? bloom.blue : bloom.blue};
    background: ${variant === "primary" ? bloom.blue : "transparent"};
    color: ${variant === "primary" ? "white" : bloom.blueText};
    &:hover:not(:disabled) {
        background: ${variant === "primary"
            ? bloom.blueText
            : "rgba(29, 148, 164, 0.08)"};
    }
    &:disabled {
        opacity: 0.5;
        cursor: default;
    }
`;
