/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { useState, useEffect } from "react";
import { ThemeProvider } from "@mui/material/styles";
import { kBloomYellow, lightTheme } from "../../bloomMaterialUITheme";
import { Div } from "../../react_components/l10nComponents";
import { BloomCheckbox } from "../../react_components/BloomCheckBox";
import StyleEditor from "./StyleEditor";
import { useL10n } from "../../react_components/l10nHooks";
import ReactDOM = require("react-dom");
import { ColorDisplayButton } from "../../react_components/color-picking/colorPickerDialog";
import { BloomPalette } from "../../react_components/color-picking/bloomPalette";

// The "Highlighting" page of the Format dialog launched from the cog control
// in a text box.
export const AudioHilitePage: React.FunctionComponent<{
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
    const colorRowCss = css`
        display: flex;
        input {
            margin-right: 10px;
        }
        justify-content: space-between;
        margin-right: 20px;
        align-items: center;
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
                <BloomCheckbox
                    label="Background Color"
                    l10nKey="EditTab.FormatDialog.BackgroundColor"
                    checked={props.hiliteBgColor !== "transparent"}
                    onCheckChanged={checked => {
                        props.onHilitePropsChanged(
                            props.hiliteTextColor,
                            checked ? kBloomYellow : "transparent"
                        );
                    }}
                ></BloomCheckbox>
                <ColorDisplayButton
                    initialColor={props.hiliteBgColor}
                    localizedTitle={chooserTitleBg}
                    width={84}
                    transparency={false}
                    palette={BloomPalette.HighlightBackground}
                    onClose={(result, newColor) =>
                        props.onHilitePropsChanged(
                            props.hiliteTextColor,
                            newColor
                        )
                    }
                />
            </div>
            <div css={colorRowCss}>
                <BloomCheckbox
                    label="Text Color"
                    l10nKey="EditTab.FormatDialog.TextColor"
                    checked={!!props.hiliteTextColor}
                    onCheckChanged={checked => {
                        props.onHilitePropsChanged(
                            checked ? props.color : undefined,
                            props.hiliteBgColor
                        );
                    }}
                ></BloomCheckbox>
                <ColorDisplayButton
                    initialColor={props.hiliteTextColor || props.color}
                    localizedTitle={chooserTitleText}
                    width={84}
                    transparency={false}
                    palette={BloomPalette.Text}
                    onClose={(result, newColor) =>
                        props.onHilitePropsChanged(
                            newColor,
                            props.hiliteBgColor
                        )
                    }
                />
            </div>
        </ThemeProvider>
    );
};

export function RenderRoot(
    uiStyleName: string,
    color: string,
    hiliteTextColor: string | undefined,
    hiliteBgColor: string,
    changeHiliteProps: (textColor: string | undefined, bgColor: string) => void
) {
    const root = document.getElementById("audioHilitePage");
    // This test is for paranoia. At one point it was briefly possible to be trying to
    // update this after the dialog closed. If we can't find the place to render into,
    // just don't do it.
    if (root) {
        ReactDOM.render(
            <AudioHilitePage
                styleName={uiStyleName ?? ""}
                color={color}
                hiliteBgColor={hiliteBgColor}
                hiliteTextColor={hiliteTextColor}
                onHilitePropsChanged={(textColor, bgColor) =>
                    changeHiliteProps(textColor, bgColor)
                }
            />,
            root
        );
    }
}
