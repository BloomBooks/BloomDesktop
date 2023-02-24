/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { useState, useEffect } from "react";
import { ThemeProvider } from "@mui/material/styles";
import { kBloomYellow, lightTheme } from "../../bloomMaterialUITheme";
import { Div } from "../../react_components/l10nComponents";
import { MuiCheckbox } from "../../react_components/muiCheckBox";
import StyleEditor from "./StyleEditor";
import { useL10n } from "../../react_components/l10nHooks";

// The "Highlighting" page of the Format dialog launched from the cog control
// in a text box.
export const AudioHighlightPage: React.FunctionComponent<{
    styleName: string;
    hiliteBgColor: string;
    hiliteTextColor: string | undefined; // undefined or empty if not wanted
    // ordinary text color, when not hilited.
    // Default for hiliteTextColor box when not checked.
    color: string;
    onHilitePropsChanged: (
        hiliteTextColor: string | undefined,
        hiliteBgColor: string
    ) => void;
}> = props => {
    const chooserTitleBg = useL10n(
        "Background color",
        "EditTab.FormatDialog.BackgroundColor"
    );
    const chooserTitleText = useL10n(
        "Text color",
        "EditTab.FormatDialog.TextColor"
    );
    const colorBoxCss = color => css`
        height: 20px;
        width: 80px;
        background-color: ${color};
        border: solid black 1px;
        margin-top: 5px; // helps align it
    `;
    const colorRowCss = css`
        display: flex;
        input {
            margin-right: 10px;
        }
        justify-content: space-between;
        margin-right: 20px;
    `;
    return (
        <ThemeProvider theme={lightTheme}>
            <Div
                l10nKey="EditTab.FormatDialog.HowToHighlightAudio"
                css={css`
                    max-width: 290px;
                    line-height: 16px;
                    margin-bottom: 10px;
                `}
                l10nParam0={props.styleName}
            >
                Choose how to highlight text of style "%0" while its audio is
                playing:
            </Div>
            <div css={colorRowCss}>
                <MuiCheckbox
                    label="Background Color"
                    l10nKey="EditTab.FormatDialog.BackgroundColor"
                    checked={props.hiliteBgColor !== "transparent"}
                    onCheckChanged={checked => {
                        const newColor = checked ? kBloomYellow : "transparent";
                        props.onHilitePropsChanged(
                            props.hiliteTextColor,
                            newColor
                        );
                    }}
                ></MuiCheckbox>
                <div
                    css={colorBoxCss(props.hiliteBgColor)}
                    onClick={() => {
                        StyleEditor.showColorPicker(
                            props.hiliteBgColor,
                            chooserTitleBg,
                            newColor => {
                                props.onHilitePropsChanged(
                                    props.hiliteTextColor,
                                    newColor
                                );
                            }
                        );
                    }}
                />
            </div>
            <div css={colorRowCss}>
                <MuiCheckbox
                    label="Text Color"
                    l10nKey="EditTab.FormatDialog.TextColor"
                    checked={!!props.hiliteTextColor}
                    onCheckChanged={checked => {
                        const newColor = checked ? props.color : undefined;
                        props.onHilitePropsChanged(
                            newColor,
                            props.hiliteBgColor
                        );
                    }}
                ></MuiCheckbox>
                <div
                    css={colorBoxCss(props.hiliteTextColor)}
                    onClick={() => {
                        StyleEditor.showColorPicker(
                            props.hiliteBgColor,
                            chooserTitleText,
                            newColor => {
                                props.onHilitePropsChanged(
                                    newColor,
                                    props.hiliteBgColor
                                );
                            }
                        );
                    }}
                ></div>
            </div>
        </ThemeProvider>
    );
};
