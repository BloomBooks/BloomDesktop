import * as React from "react";
import Typography from "@material-ui/core/Typography";
import { useL10n } from "../../../react_components/l10nHooks";
import { IMenuItem } from "./comicTool";
import { hex } from "color-convert";
import * as tinycolor from "tinycolor2";

// Displays a color bar menu item with optional localizable text.
export const MenuColorBar: React.FunctionComponent<IMenuItem> = props => {
    const localizedText = props.l10nKey
        ? useL10n(props.text as string, props.l10nKey)
        : "";
    const baseColor = props.colors ? props.colors : ["white"]; // An array of strings representing colors

    // 'initialColorString' will be transparent if props.color represents a gradient (2 colors).
    // Otherwise, it could be a name of a color (OldLace) or a hex value starting with '#'
    const initialColorString =
        baseColor.length === 1 ? baseColor[0] : "transparent";
    const opacity = props.opacity ? props.opacity : 1.0;
    // 'backgroundColorString' will end up being a named color (including 'transparent' for gradients),
    // or an rgba string (with possible opacity values).
    let backgroundColorString: string = initialColorString;
    if (initialColorString.startsWith("#")) {
        const rgbArray = hex.rgb(initialColorString);
        backgroundColorString = `rgba(${rgbArray[0]}, ${rgbArray[1]}, ${
            rgbArray[2]
        }, ${opacity})`;
    }
    const gradientString =
        baseColor.length > 1
            ? "linear-gradient(to bottom, " +
              baseColor[0] +
              ", " +
              baseColor[1] +
              ")"
            : "none";
    const styleObject: React.CSSProperties = {
        backgroundColor: backgroundColorString,
        backgroundImage: gradientString
    };

    const isDark = (colorString: string): boolean => {
        const color = tinycolor(colorString); // handles named colors, rgba() and hex strings!!!
        return color.isDark();
    };

    let textColor = "";
    if (gradientString === "none") {
        if (isDark(backgroundColorString)) {
            textColor = "white";
        }
    } else {
        if (isDark(baseColor[0]) || isDark(baseColor[1])) {
            textColor = "white";
        }
    }

    const textStyleObject: React.CSSProperties = {
        color: textColor
    };

    const classes = "colorSwatch" + (props.name === "new" ? " newItem" : "");

    return (
        <div className={classes} style={styleObject}>
            <Typography color="textPrimary" style={textStyleObject}>
                {localizedText}
            </Typography>
        </div>
    );
};
