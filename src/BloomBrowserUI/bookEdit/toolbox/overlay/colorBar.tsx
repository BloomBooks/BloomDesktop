import * as React from "react";
import Typography from "@material-ui/core/Typography";
import { useL10n } from "../../../react_components/l10nHooks";
import {
    ISwatchDefn,
    getBackgroundFromSwatch
} from "../../../react_components/colorSwatch";
import * as tinycolor from "tinycolor2";
import { CSSProperties } from "react";

export interface IColorBarProps {
    id: string;
    // if defined, 'text' will display over the color bar in either white or black,
    // depending on the color bar's "perceived brightness".
    text?: string;
    onClick: () => void;
    swatch: ISwatchDefn;
}
// Displays a color bar menu item with optional localizable text.
export const ColorBar: React.FunctionComponent<IColorBarProps> = (
    props: IColorBarProps
) => {
    const baseColor = props.swatch.colors; // An array of strings representing colors

    const backgroundColorString = getBackgroundFromSwatch(props.swatch);

    const isDark = (colorString: string): boolean => {
        const color = tinycolor(colorString); // handles named colors, rgba() and hex strings!!!
        return color.isDark();
    };

    let textColor = "black";
    if (baseColor[1]) {
        if (isDark(baseColor[0]) || isDark(baseColor[1])) {
            textColor = "white";
        }
    } else {
        if (isDark(backgroundColorString)) {
            textColor = "white";
        }
    }

    const bloomToolboxWhite = "#d2d2d2";

    const barStyles: CSSProperties = {
        minHeight: 30,
        background: backgroundColorString,
        border: "1px solid",
        borderColor: bloomToolboxWhite,
        borderRadius: 4,
        display: "flex",
        justifyContent: "center"
    };

    const textStyles: CSSProperties = {
        color: textColor,
        paddingTop: 4
    };

    // 'MuiInput-formControl' may seem like an odd class to give my homegrown div, but some of the toolbox's
    // spacing rules depend on it being one of these.
    return (
        <div
            className="MuiInput-formControl"
            style={barStyles}
            onClick={props.onClick}
        >
            <Typography color="textPrimary" style={textStyles}>
                {props.text}
            </Typography>
        </div>
    );
};
