import * as React from "react";
import Typography from "@material-ui/core/Typography";
import { useL10n } from "../../../react_components/l10nHooks";
import { IMenuItem } from "./comicTool";
import * as tinycolor from "tinycolor2";

// Displays a color bar menu item with optional localizable text.
export const MenuColorBar: React.FunctionComponent<IMenuItem> = (
    props: IMenuItem
) => {
    const localizedText = props.l10nKey
        ? useL10n(props.text as string, props.l10nKey, "", props.l10nParam)
        : "";
    const baseColor = props.colors; // An array of strings representing colors

    // 'initialColorString' will be 'gradient' if props.color represents a gradient (2 colors).
    // Otherwise, it could be a name of a color (OldLace) or a hex value starting with '#'
    const initialColorString =
        baseColor.length === 1 ? baseColor[0] : "gradient";
    const opacity = props.opacity ? props.opacity : 1.0;
    // 'backgroundColorString' will end up being a named color, a linear-gradient string,
    // or an rgba string (with possible opacity values).
    let backgroundColorString: string = initialColorString;
    if (initialColorString.startsWith("#")) {
        const rgb = tinycolor(initialColorString).toRgb();
        backgroundColorString = `rgba(${rgb.r}, ${rgb.g}, ${
            rgb.b
        }, ${opacity})`;
    }
    if (initialColorString === "gradient") {
        backgroundColorString =
            "linear-gradient(" + baseColor[0] + ", " + baseColor[1] + ")";
    }

    const isDark = (colorString: string): boolean => {
        const color = tinycolor(colorString); // handles named colors, rgba() and hex strings!!!
        return color.isDark();
    };

    let textColor = "black";
    if (initialColorString === "gradient") {
        if (isDark(baseColor[0]) || isDark(baseColor[1])) {
            textColor = "white";
        }
    } else {
        if (isDark(backgroundColorString)) {
            textColor = "white";
        }
    }

    const classes = "menuColorBar" + (props.name === "new" ? " newItem" : "");

    return (
        <div
            className={classes}
            style={{ minHeight: 25, background: backgroundColorString }}
        >
            <Typography color="textPrimary" style={{ color: textColor }}>
                {localizedText}
            </Typography>
        </div>
    );
};
