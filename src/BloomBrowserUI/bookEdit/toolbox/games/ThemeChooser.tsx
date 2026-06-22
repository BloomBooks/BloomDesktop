import { css, ThemeProvider } from "@emotion/react";
import * as React from "react";
import { useEffect, useState } from "react";
import { ToolBox } from "../toolbox";
import { getVariationsOnClass } from "../../../utils/getVariationsOnClass";
import {
    kOptionPanelBackgroundColor,
    toolboxMenuPopupTheme,
} from "../../../bloomMaterialUITheme";
import MenuItem from "@mui/material/MenuItem";
import Divider from "@mui/material/Divider";
import { Div } from "../../../react_components/l10nComponents";
import { InfoIconUrl } from "../../../react_components/icons/InfoIconUrl";
import BloomSelect from "../../../react_components/bloomSelect";
import EditIcon from "@mui/icons-material/Edit";
import IconButton from "@mui/material/IconButton";
import { getAsync } from "../../../utils/bloomApi";
import {
    showGameThemeEditor,
    showNewGameThemeEditor,
    showCustomizeGameThemeEditor,
    isGameThemeEditorOpen,
    subscribeGameThemeEditorOpen,
    isFactoryThemeSlug,
    resolveThemeHeaderColors,
} from "./gameThemeEditorHost";

// Sentinel values for the "New…" and "Customize…" items in the theme dropdown (not real themes).
const kNewThemeValue = "__new_game_theme__";
const kCustomizeThemeValue = "__customize_game_theme__";

const getPage = () => {
    const pageBody = ToolBox.getPage();
    return pageBody?.getElementsByClassName("bloom-page")[0] as HTMLElement;
};

const kMissingThemeDataAttribute = "data-missing-game-theme";
const kDefaultTheme = "blue-on-white";

const isMissingTheme = (themeName: string): boolean => {
    const page = getPage();
    const missingTheme = page?.getAttribute(kMissingThemeDataAttribute);
    return missingTheme === themeName;
};

function getThemeLabel(themeName: string): string {
    return themeName
        .split("-")
        .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
        .join(" ");
}

export const ThemeChooser: React.FunctionComponent<{
    pageGeneration: number; // to force re-render when the page changes
}> = (props) => {
    // The set of possible themes, derived from stylesheet rules that start with a dot
    // followed by gameThemePrefix
    const [themes, setThemes] = useState<string[]>([]);
    const gameThemePrefix = "game-theme-";
    // The theme of the current page, derived from any class on the .bloom-page that starts with
    // gameThemePrefix, or "default" if there is none (but migration code and this tool makes sure
    // that game pages always do).
    const [currentTheme, setCurrentTheme] = useState("");
    // While the floating theme editor is open it owns theme changes, so we disable the
    // dropdown to avoid switching themes out from under it. The editor can be closed from
    // its own controls, so we track its open state via the host's subscription.
    const [editorOpen, setEditorOpen] = useState(isGameThemeEditorOpen());
    // Bumped when the editor closes, so we re-scan the available themes: a save may have added
    // or renamed one (a rename removes the old name's rule from the page).
    const [themesRefreshKey, setThemesRefreshKey] = useState(0);
    useEffect(() => {
        setEditorOpen(isGameThemeEditorOpen());
        return subscribeGameThemeEditorOpen(() => {
            const open = isGameThemeEditorOpen();
            setEditorOpen(open);
            if (!open) setThemesRefreshKey((k) => k + 1);
        });
    }, []);
    // Whether Bloom is running from source (developers can edit factory themes). Non-developers
    // don't get the edit button on factory themes, since they can't change them.
    const [isDeveloper, setIsDeveloper] = useState(false);
    useEffect(() => {
        getAsync("gameThemeEditor/canSaveToFactorySource").then((result) => {
            setIsDeveloper(!!(result && (result as { data?: boolean }).data));
        });
    }, []);
    const currentThemeIsFactory = React.useMemo(
        () => isFactoryThemeSlug(currentTheme),
        // Re-evaluate when the page changes too, since the available stylesheets can change.
        // eslint-disable-next-line react-hooks/exhaustive-deps
        [currentTheme, props.pageGeneration],
    );
    // Each dropdown item previews its theme by using that theme's header colors (the menu
    // items only render while the dropdown is open, so this styling shows only then).
    const headerColors = React.useMemo(() => {
        const map: Record<string, { bg: string; color: string }> = {};
        for (const theme of themes)
            map[theme] = resolveThemeHeaderColors(theme);
        return map;
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [themes, props.pageGeneration]);
    // Which themes are factory (built-in) vs custom. We only run the localization system on
    // factory names; custom theme names are user-defined and not in our localization system, so
    // looking them up just clutters the screen with "untranslated" warnings.
    const themeIsFactory = React.useMemo(() => {
        const map: Record<string, boolean> = {};
        for (const theme of themes) map[theme] = isFactoryThemeSlug(theme);
        return map;
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [themes, props.pageGeneration]);
    // The dropdown lists factory themes first, then a divider and "New…", then the user's
    // custom themes. Partition the (already sorted) themes accordingly.
    const factoryThemes = React.useMemo(
        () => themes.filter((theme) => themeIsFactory[theme]),
        [themes, themeIsFactory],
    );
    const customThemes = React.useMemo(
        () => themes.filter((theme) => !themeIsFactory[theme]),
        [themes, themeIsFactory],
    );
    // Render one theme as a dropdown item, previewing it with its own header colors.
    const renderThemeItem = (theme: string) => (
        <MenuItem
            value={theme}
            key={theme}
            disabled={false}
            // Inline style so the theme's header colors win over MUI's hover/selected background.
            style={{
                backgroundColor: headerColors[theme]?.bg || undefined,
                color: headerColors[theme]?.color || undefined,
            }}
        >
            {themeIsFactory[theme] ? (
                <Div
                    l10nKey={`EditTab.Toolbox.Games.Themes.${theme}`}
                    // Suppress the "untranslated" warning: dev-added source
                    // themes won't have localization entries either.
                    temporarilyDisableI18nWarning={true}
                >
                    {isMissingTheme(theme)
                        ? `(Missing) ${getThemeLabel(theme)}`
                        : getThemeLabel(theme)}
                </Div>
            ) : (
                // Custom theme: render the name as-is; never run localization.
                <span>
                    {isMissingTheme(theme)
                        ? `(Missing) ${getThemeLabel(theme)}`
                        : getThemeLabel(theme)}
                </span>
            )}
        </MenuItem>
    );
    // A fresh, unused "Untitled Theme N" name for a brand-new or customized theme.
    const nextUntitledName = () => {
        let n = 1;
        while (themes.includes(`untitled-theme-${n}`)) n++;
        return `Untitled Theme ${n}`;
    };
    // "New…": a brand-new theme based on the default factory theme (Blue On White).
    const handleNewTheme = () => showNewGameThemeEditor(nextUntitledName());
    // "Customize…": a new theme that starts as a copy of the theme currently applied.
    const handleCustomizeTheme = () =>
        showCustomizeGameThemeEditor(nextUntitledName());
    const handleChooseTheme = (event) => {
        const newTheme = event.target.value;
        if (newTheme === kNewThemeValue) {
            handleNewTheme();
            return;
        }
        if (newTheme === kCustomizeThemeValue) {
            handleCustomizeTheme();
            return;
        }
        if (newTheme === currentTheme) {
            return;
        }
        const page = getPage();
        if (page) {
            // When all goes well, it should be enough to just remove the theme class that
            // corresponds to the current theme. But when things go wrong, some other theme
            // class might be hanging around, and having two of them produces unpredictable
            // results. So play safe and remove any game theme class.
            for (const className of Array.from(page.classList)) {
                if (className.startsWith(gameThemePrefix)) {
                    page.classList.remove(className);
                }
            }

            if (isMissingTheme(newTheme)) {
                // If selecting a missing theme, actually apply kDefaultTheme but show the missing theme as selected
                page.classList.add(`${gameThemePrefix}${kDefaultTheme}`);
                setCurrentTheme(newTheme);
            } else {
                // Clear any missing theme data when selecting an available theme
                page.removeAttribute(kMissingThemeDataAttribute);
                page.classList.add(`${gameThemePrefix}${newTheme}`);
                setCurrentTheme(newTheme);
            }
        }
    };
    useEffect(() => {
        const page = getPage();
        const missingThemeName = page?.getAttribute(kMissingThemeDataAttribute);
        const pageThemeName =
            missingThemeName ||
            Array.from(page.classList)
                .find((c) => c.startsWith(gameThemePrefix))
                ?.substring(gameThemePrefix.length);
        if (
            currentTheme &&
            !pageThemeName &&
            page.getAttribute("data-tool-id") === "game"
        ) {
            // it's a new game page and we've been on a game page this session.
            // Instead of updating our control, update the page to match
            // the theme of the page we were on previously.
            page.classList.add(`${gameThemePrefix}${currentTheme}`);
        } else {
            setCurrentTheme(pageThemeName || kDefaultTheme);
        }

        // Figure out the values for the theme menu. We do this by finding ALL the style
        // definitions in the page that have a selector starting with game-theme-. The ones we expect
        // come from gameThemes.less, but if a user defines one in customStyles.css, we will find it
        // and offer it.
        getVariationsOnClass(
            gameThemePrefix,
            page.ownerDocument,
            (stylesheetThemes) => {
                // Ensure kDefaultTheme is always available
                const availableThemes = stylesheetThemes.includes(kDefaultTheme)
                    ? stylesheetThemes
                    : [kDefaultTheme, ...stylesheetThemes];

                const isThemeAvailableInStylesheets =
                    pageThemeName && stylesheetThemes.includes(pageThemeName);

                if (pageThemeName && !isThemeAvailableInStylesheets) {
                    // Theme is missing - store it and fall back to kDefaultTheme
                    page.setAttribute(
                        kMissingThemeDataAttribute,
                        pageThemeName,
                    );
                    page.classList.remove(`${gameThemePrefix}${pageThemeName}`);
                    page.classList.add(`${gameThemePrefix}${kDefaultTheme}`);
                    setCurrentTheme(pageThemeName);

                    // Add the missing theme to the list for display
                    const themesWithMissing = [
                        ...availableThemes,
                        pageThemeName,
                    ];
                    setThemes(themesWithMissing.sort());
                } else {
                    if (missingThemeName && isThemeAvailableInStylesheets) {
                        // Theme was missing; now it isn't.
                        page.removeAttribute(kMissingThemeDataAttribute);
                        page.classList.remove(
                            `${gameThemePrefix}${kDefaultTheme}`,
                        );
                        page.classList.add(
                            `${gameThemePrefix}${pageThemeName}`,
                        );
                    }
                    setThemes(availableThemes.sort());
                }
            },
        );

        // We don't need to run again if currentTheme changes, since it can only change to something
        // that's already in the list (except just possibly when pageGeneration changes). We also
        // re-run when the editor closes (themesRefreshKey), since a save/rename may have changed
        // the set of themes.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [props.pageGeneration, themesRefreshKey]);
    return (
        <ThemeProvider theme={toolboxMenuPopupTheme}>
            <div
                css={css`
                    display: flex;
                `}
            >
                <Div
                    css={css`
                        margin-top: 10px;
                        margin-bottom: 5px;
                    `}
                    l10nKey="EditTab.Toolbox.Games.Theme"
                ></Div>
                <InfoIconUrl
                    href="https://docs.bloomlibrary.org/custom-game-theme"
                    l10nKey="Common.LearnMore"
                    css={css`
                        margin: 5px 10px 0 5px;
                        max-width: 100px;
                    `}
                    placement="top"
                    slotProps={{
                        // This pulls the whole thing closer to the icon, which I think looks better,
                        // and may help make it easier to move the mouse to the link in the tooltip.
                        // without it going away.
                        popper: {
                            modifiers: [
                                {
                                    name: "offset",
                                    options: {
                                        offset: [0, -8],
                                    },
                                },
                            ],
                        },
                        // This fiddle, suggested by copilot, prevents the tooltip from
                        // being much wider than the text inside it.
                        tooltip: {
                            sx: {
                                maxWidth: "fit-content",
                                width: "auto",
                                whiteSpace: "nowrap",
                            },
                        },
                    }}
                />
            </div>
            <div
                css={css`
                    display: flex;
                    align-items: center;
                `}
            >
                <BloomSelect
                    variant="standard"
                    value={currentTheme}
                    disabled={editorOpen}
                    onChange={(event) => {
                        handleChooseTheme(event);
                    }}
                    inputProps={{
                        name: "style",
                        id: "game-theme-dropdown",
                    }}
                    css={css`
                        flex: 1;
                        // Allow the select to shrink below its content width so a long theme
                        // name truncates (with an ellipsis) instead of pushing the edit button
                        // out of the narrow toolbox.
                        min-width: 0;
                        .MuiSelect-select {
                            overflow: hidden;
                            text-overflow: ellipsis;
                            white-space: nowrap;
                        }
                        // While the editor is open the dropdown is disabled (so themes can't be
                        // switched out from under it), but MUI's default disabled styling greys the
                        // text to near-invisible on the dark toolbox. Keep it readable.
                        .MuiSelect-select.Mui-disabled {
                            color: white !important;
                            -webkit-text-fill-color: white !important;
                        }
                        svg.MuiSvgIcon-root {
                            color: white !important;
                        }
                        ul {
                            background-color: ${kOptionPanelBackgroundColor} !important;
                        }
                        fieldset {
                            border-color: rgba(255, 255, 255, 0.5) !important;
                        }
                    `}
                    size="small"
                >
                    {/* Factory themes first, then a divider, then "New…", then custom themes. */}
                    {factoryThemes.map(renderThemeItem)}
                    <Divider />
                    {/* Not themes: "New…" starts from Blue On White; "Customize…" copies the
                        current theme. Both open the editor on a new, unsaved theme. */}
                    <MenuItem value={kNewThemeValue} key={kNewThemeValue}>
                        <Div l10nKey="EditTab.Toolbox.Games.NewTheme">New…</Div>
                    </MenuItem>
                    <MenuItem
                        value={kCustomizeThemeValue}
                        key={kCustomizeThemeValue}
                    >
                        <Div l10nKey="EditTab.Toolbox.Games.CustomizeTheme">
                            Customize…
                        </Div>
                    </MenuItem>
                    {customThemes.map(renderThemeItem)}
                </BloomSelect>
                {/* Non-developers can't change factory themes, so don't offer the edit button
                    for them on a factory theme (they can still use "New…" to make a copy). */}
                {(isDeveloper || !currentThemeIsFactory) && (
                    <IconButton
                        id="game-theme-edit-button"
                        size="small"
                        // Opens the floating game theme editor over the live page so it can recolor
                        // the real game in real time. Mounted from here (toolbox) into the page document.
                        onClick={() => showGameThemeEditor()}
                        title="Edit theme colors"
                        css={css`
                            color: white !important;
                            margin-left: 4px;
                            flex-shrink: 0;
                        `}
                    >
                        <EditIcon fontSize="small" />
                    </IconButton>
                )}
            </div>
        </ThemeProvider>
    );
};
