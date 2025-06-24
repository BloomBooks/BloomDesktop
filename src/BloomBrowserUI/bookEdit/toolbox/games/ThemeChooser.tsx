import { css, ThemeProvider } from "@emotion/react";
import * as React from "react";
import { useEffect, useState } from "react";
import { ToolBox } from "../toolbox";
import { getVariationsOnClass } from "../../../utils/getVariationsOnClass";
import {
    kOptionPanelBackgroundColor,
    toolboxMenuPopupTheme
} from "../../../bloomMaterialUITheme";
import Select from "@mui/material/Select";
import MenuItem from "@mui/material/MenuItem";
import { Div } from "../../../react_components/l10nComponents";

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

export const ThemeChooser: React.FunctionComponent<{
    pageGeneration: number; // to force re-render when the page changes
}> = props => {
    // The set of possible themes, derived from stylesheet rules that start with a dot
    // followed by gameThemePrefix
    const [themes, setThemes] = useState<string[]>([]);
    const gameThemePrefix = "game-theme-";
    // The theme of the current page, derived from any class on the .bloom-page that starts with
    // gameThemePrefix, or "default" if there is none (but migration code and this tool makes sure
    // that game pages always do).
    const [currentTheme, setCurrentTheme] = useState("");
    // State to track and control whether the dropdown is open.
    // We make it a controlled component so that we can close it when the tool closes.
    const [isSelectOpen, setIsSelectOpen] = useState(false);
    useEffect(() => {
        ToolBox.addWhenClosingToolTask(() => setIsSelectOpen(false));
    }, []);
    const handleChooseTheme = event => {
        const newTheme = event.target.value;
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
                .find(c => c.startsWith(gameThemePrefix))
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
            stylesheetThemes => {
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
                        pageThemeName
                    );
                    page.classList.remove(`${gameThemePrefix}${pageThemeName}`);
                    page.classList.add(`${gameThemePrefix}${kDefaultTheme}`);
                    setCurrentTheme(pageThemeName);

                    // Add the missing theme to the list for display
                    const themesWithMissing = [
                        ...availableThemes,
                        pageThemeName
                    ];
                    setThemes(themesWithMissing.sort());
                } else {
                    if (missingThemeName && isThemeAvailableInStylesheets) {
                        // Theme was missing; now it isn't.
                        page.removeAttribute(kMissingThemeDataAttribute);
                        page.classList.remove(
                            `${gameThemePrefix}${kDefaultTheme}`
                        );
                        page.classList.add(
                            `${gameThemePrefix}${pageThemeName}`
                        );
                    }
                    setThemes(availableThemes.sort());
                }
            }
        );

        // We don't need to run again if currentTheme changes, since it can only change to something
        // that's already in the list (except just possibly when pageGeneration changes).
    }, [props.pageGeneration]);
    return (
        <ThemeProvider theme={toolboxMenuPopupTheme}>
            <Select
                variant="standard"
                value={currentTheme}
                open={isSelectOpen}
                onOpen={() => setIsSelectOpen(true)}
                onClose={() => setIsSelectOpen(false)}
                onChange={event => {
                    handleChooseTheme(event);
                    setIsSelectOpen(false);
                }}
                inputProps={{
                    name: "style",
                    id: "game-theme-dropdown"
                }}
                css={css`
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
                {themes.map(theme => (
                    <MenuItem value={theme} key={theme} disabled={false}>
                        <Div l10nKey={`EditTab.Toolbox.Games.Themes.${theme}`}>
                            {isMissingTheme(theme)
                                ? `(Missing) ${theme}`
                                : theme}
                        </Div>
                    </MenuItem>
                ))}
            </Select>
        </ThemeProvider>
    );
};
