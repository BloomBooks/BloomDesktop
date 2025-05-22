/** @jsx jsx **/
import { jsx, css, ThemeProvider } from "@emotion/react";
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
            page.classList.add(`${gameThemePrefix}${newTheme}`);
            setCurrentTheme(newTheme);
        }
    };
    useEffect(() => {
        const page = getPage();
        const pageThemeClass = Array.from(page.classList)
            .find(c => c.startsWith(gameThemePrefix))
            ?.substring(gameThemePrefix.length);
        if (
            currentTheme &&
            !pageThemeClass &&
            page.getAttribute("data-tool-id") === "game"
        ) {
            // it's a new game page and we've been on a game page this session.
            // Instead of updating our control, update the page to match
            // the theme of the page we were on previously.
            page.classList.add(`${gameThemePrefix}${currentTheme}`);
        } else {
            setCurrentTheme(pageThemeClass || "blue-on-white");
        }

        // Figure out the values for the theme menu. We do this by finding ALL the style
        // definitions in the page that have a selector starting with game-theme-. The ones we expect
        // come from gameThemes.less, but if a user defines one in customStyles.css, we will find it
        // and offer it.
        const minmalThemes = ["blue-on-white"];
        if (pageThemeClass) {
            minmalThemes.push(pageThemeClass);
        }
        getVariationsOnClass(
            gameThemePrefix,
            page.ownerDocument,
            setThemes,
            minmalThemes
        );

        // We don't need to run again if currentTheme changes, since it can only change to something
        // that's already in the list (except just possibly when pageGeneration changes).
    }, [props.pageGeneration]);
    return (
        <ThemeProvider theme={toolboxMenuPopupTheme}>
            <Select
                variant="standard"
                value={currentTheme}
                onChange={event => {
                    handleChooseTheme(event);
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
                            {theme}
                        </Div>
                    </MenuItem>
                ))}
            </Select>
        </ThemeProvider>
    );
};
