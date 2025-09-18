import { css } from "@emotion/react";
import * as React from "react";
import { ThemeProvider, Theme } from "@mui/material/styles";
import { makeStyles } from "@mui/styles";
import { lightTheme } from "../bloomMaterialUITheme";
import {
    FormControl,
    MenuProps,
    Select,
    SelectChangeEvent,
} from "@mui/material";

declare module "@mui/styles/defaultTheme" {
    // eslint-disable-next-line @typescript-eslint/no-empty-object-type, @typescript-eslint/no-empty-interface
    interface DefaultTheme extends Theme {}
}

// This seems to be the only way to affect the css of the popped up list, since it's a completely
// separate html element from this component.
const useStyles = makeStyles(() => ({
    menuPaper: {
        maxHeight: 225,
        padding: 3,
    },
}));

interface FormsSelectProps {
    // Originally used to distinguish multiple Selects on one container, but as we move to having
    // more and more of Bloom in React, it's probably better that each Select have its own key.
    idKey?: string;
    onChangeHandler: (event: SelectChangeEvent) => void;
    currentValue: string;
    // Use this if you need to modify the style of popup menus by increasing z-index
    // (e.g., to make the popup be in front of the bloom font dialog)
    popoverZindex?: string;
}

// This component initially attempted to imitate a winforms combobox in React.
// Since we're moving away from winforms, that restriction has relaxed somewhat.
const WinFormsStyleSelect: React.FunctionComponent<FormsSelectProps> = (
    props,
) => {
    const selectMenuProps: Partial<MenuProps> = {
        classes: {
            paper: useStyles().menuPaper,
        },
    };

    if (props.popoverZindex) {
        if (lightTheme?.components)
            lightTheme.components.MuiPopover = {
                ...lightTheme.components.MuiPopover,
                styleOverrides: {
                    root: {
                        zIndex: props.popoverZindex + " !important",
                    },
                },
            };
    }

    // Match the border color of the other selects in the Edit tab cog Format dialog.
    // Without this, the gray border on this one is too light in comparison to the others.
    const matchingBorderColor = "border-color: #808080 !important;";

    const finalKey = props.idKey ? props.idKey.toString() : "";

    return (
        <ThemeProvider theme={lightTheme}>
            <FormControl
                variant="outlined"
                margin="dense"
                css={css`
                    // Some of the following "!important"s are needed when the Style tab is present,
                    // oddly enough!
                    min-width: 180px !important;
                    max-width: 220px !important;
                    margin-right: 12px !important;
                    margin-top: 3px !important;
                    & > div {
                        border-radius: 0;
                    }
                    fieldset {
                        ${matchingBorderColor}
                    }
                    // I can't get this to work putting it anywhere else. This is only for the case
                    // where the menu item is a FontDisplayBar.
                    .font-display-bar svg {
                        padding-right: 15px !important; // make room for dropdown arrow
                    }
                `}
            >
                <Select
                    id={`select-${finalKey}`}
                    MenuProps={selectMenuProps}
                    onChange={props.onChangeHandler}
                    value={props.currentValue}
                    variant="outlined"
                    css={css`
                        #select-${finalKey} {
                            display: flex;
                            flex: 1;
                            flex-direction: row;
                            justify-content: space-between;
                            background-color: #fdfdfd;
                            // try to match the font size input
                            padding: 0 12px 0 8px !important;
                            margin: 1px 0 !important;
                        }
                    `}
                >
                    {props.children}
                </Select>
            </FormControl>
        </ThemeProvider>
    );
};

export default WinFormsStyleSelect;
