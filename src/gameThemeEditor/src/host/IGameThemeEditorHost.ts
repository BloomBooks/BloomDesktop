// The contract between the (self-contained) Game Theme Editor project and whatever
// host embeds it. This file is the ONLY surface the host needs to implement, plus the
// mount()/unmount() functions exported from ../index. The editor itself imports nothing
// from the host application; all coupling flows through this interface, which the host
// supplies as a concrete object at mount() time.
//
// In Bloom, the host implementation lives in the editable-page iframe so that it can
// recolor the live .bloom-page element directly (see gameThemeEditorHost.ts on the
// Bloom side). But nothing here is Bloom-specific: a standalone harness can implement
// IGameThemeEditorHost to drive the editor against any preview surface.

/** A theme is a named set of CSS custom-property overrides. */
export interface Theme {
    /** url-safe identifier, e.g. "blue-on-white"; also the game-theme-<slug> class suffix. */
    slug: string;
    /** human-friendly name shown in the dropdown, e.g. "Blue On White". */
    displayName: string;
    /** the CSS custom properties this theme sets explicitly, e.g. {"--game-primary-color": "#0058cc"}. */
    variables: Record<string, string>;
}

export interface IGameThemeEditorHost {
    /** The slug of the theme currently applied to the page when the editor opens. */
    getCurrentThemeName(): string;

    /**
     * When the editor was opened via "New" (to create a copy of the current theme), the
     * suggested name for the new theme (e.g. "Untitled Theme 3"); otherwise null. In "new"
     * mode the editor seeds its colors from the current theme but saves under a new slug and
     * never deletes the original.
     */
    getNewThemeName(): string | null;

    /**
     * Whether the theme being edited is a built-in/factory theme (defined in the shipped games
     * stylesheet, not in a book/collection custom stylesheet). Factory themes cannot be renamed.
     */
    isFactoryTheme(): boolean;

    /**
     * Where the theme being edited currently lives, so the editor can default to the matching
     * Save button (e.g. a theme from the collection defaults to "Save to Collection").
     */
    getThemeSource(): "factory" | "collection" | "book" | "none";

    /** Which custom stylesheets currently contain the theme, so the editor can offer to delete it. */
    getThemeLocations(): { inCollection: boolean; inBook: boolean };

    /** Whether a theme with this slug already exists anywhere, so saving won't clobber another theme. */
    themeExists(slug: string): boolean;

    /** Delete the theme's rule from the collection stylesheet and update the live page. */
    deleteFromCollection(slug: string): Promise<void>;

    /** Delete the theme's rule from this book's stylesheet and update the live page. */
    deleteFromBook(slug: string): Promise<void>;

    /**
     * The effective (computed) value of every theme variable on the live page right now.
     * Used to seed the color swatches so they show what the user actually sees.
     */
    readAppliedVariables(): Record<string, string>;

    /**
     * The variables a given theme sets EXPLICITLY (as opposed to inheriting via the cascade),
     * read from the stylesheet rule for that theme. Used to seed which swatches are "part of"
     * the theme so save round-trips correctly and Reset knows what to clear.
     */
    getThemeDefinition(slug: string): Record<string, string>;

    /**
     * Live preview: set (or, when value is undefined, clear) a CSS custom property on the
     * live page. The browser's native cascade derives all dependent variables, so the real
     * game recolors immediately. This is why there is no separate preview component.
     */
    setLiveVariable(name: string, value: string | undefined): void;

    /** Clear ALL live preview overrides at once, reverting the page to its saved theme. */
    clearLiveOverrides(): void;

    /**
     * Persist the theme to the current book only (Bloom: customBookStyles2.css) and apply it.
     * When renameFrom is given and differs from the theme's slug, the old theme rule is removed
     * (an in-place rename rather than a duplicate).
     */
    saveToBook(theme: Theme, renameFrom?: string): Promise<void>;

    /** Persist the theme collection-wide (Bloom: customCollectionStyles.css) and apply it. */
    saveToCollection(theme: Theme, renameFrom?: string): Promise<void>;

    /** Developer-only: persist into the factory theme source (Bloom: gamesThemes.less). */
    saveToFactorySource(theme: Theme, renameFrom?: string): Promise<void>;

    /** Whether the developer "save to factory themes" path is available (running from source). */
    canSaveToFactorySource(): Promise<boolean>;

    /** Tear down the editor (host removes the container and reverts unsaved live overrides). */
    close(): void;
}
