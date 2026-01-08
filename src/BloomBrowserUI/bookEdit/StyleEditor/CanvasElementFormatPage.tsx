import { css } from "@emotion/react";
import * as React from "react";
import { useState, useEffect } from "react";
import { ThemeProvider } from "@mui/material/styles";
import { kBloomYellow, lightTheme } from "../../bloomMaterialUITheme";
import { Div } from "../../react_components/l10nComponents";
import { BloomCheckbox } from "../../react_components/BloomCheckBox";
import StyleEditor from "./StyleEditor";
import { useL10n } from "../../react_components/l10nHooks";
import * as ReactDOM from "react-dom";
import { ColorDisplayButton } from "../../react_components/color-picking/colorPickerDialog";
import { BloomPalette } from "../../react_components/color-picking/bloomPalette";
import { NoteBox, WarningBox } from "../../react_components/boxes";
import Select from "@mui/material/Select";
import MenuItem from "@mui/material/MenuItem";

// The "Canvas element" page of the Format dialog launched from the cog control
// in a text box that is embedded in a canvas element.
export const CanvasElementFormatPage: React.FunctionComponent<{
    padding: string;
    onPropsChanged: (padding: string) => void;
}> = (props) => {
    return (
        <ThemeProvider theme={lightTheme}>
            <Div
                l10nKey="EditTab.FormatDialog.Padding"
                // "Padding: space between the text and its border"
            ></Div>
            <Select
                css={css`
                    margin-bottom: 10px;
                    // Without this it adds way too much vertical padding, proably aimed at touch devices.
                    .MuiSelect-select {
                        padding: 7px 11px;
                    }
                `}
                variant="outlined"
                value={props.padding}
                onChange={(e) => {
                    props.onPropsChanged(e.target.value as string);
                }}
            >
                <MenuItem value="0px">0 px</MenuItem>
                <MenuItem value="1px">1 px</MenuItem>
                <MenuItem value="2px">2 px</MenuItem>
                <MenuItem value="3px">3 px</MenuItem>
                <MenuItem value="4px">4 px</MenuItem>
                <MenuItem value="5px">5 px</MenuItem>
            </Select>
            <NoteBox>
                <Div
                    l10nKey="EditTab.FormatDialog.VisitEachPage"
                    css={css`
                        // Otherwise it makes one very long line and the dialog grows enormously to fit it.
                        max-width: 300px;
                    `}
                />
            </NoteBox>
        </ThemeProvider>
    );
};

export function RenderCanvasElementRoot(
    padding: string,
    changeProps: (padding: string) => void,
) {
    const root = document.getElementById("canvasFormatPage");
    // This tab is deleted when we are not in a canvas element, so we need to check for its existence.
    if (root) {
        ReactDOM.render(
            <CanvasElementFormatPage
                padding={padding ?? ""}
                onPropsChanged={(padding) => changeProps(padding)}
            />,
            root,
        );
    }
}
