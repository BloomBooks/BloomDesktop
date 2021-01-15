import tinycolor = require("tinycolor2");
import { ISwatchDefn } from "../../../react_components/colorSwatch";

export const specialColors: ISwatchDefn[] = [
    // #DFB28B is the color Comical has been using as the default for captions.
    // It's fairly close to the "Calico" color defined at https://www.htmlcsscolor.com/hex/D5B185 (#D5B185)
    // so I decided it was the best choice for keeping that option.
    {
        name: "whiteToCalico",
        colors: ["white", "#DFB28B"]
    },
    // https://www.htmlcsscolor.com/hex/ACCCDD
    {
        name: "whiteToFrenchPass",
        colors: ["white", "#ACCCDD"]
    },
    // https://encycolorpedia.com/7b8eb8
    {
        name: "whiteToPortafino",
        colors: ["white", "#7b8eb8"]
    }
];

export const defaultTextColors: ISwatchDefn[] = [
    {
        name: "black",
        colors: ["black"]
    },
    {
        name: "gray",
        colors: ["gray"] // #808080
    },
    {
        name: "lightgray",
        colors: ["lightgray"] // #D3D3D3
    },
    { name: "white", colors: ["white"] }
];

const temp: ISwatchDefn[] = [
    {
        name: "black",
        colors: ["black"]
    },
    { name: "white", colors: ["white"] },
    {
        name: "partialTransparent",
        colors: ["#575757"], // bloom-gray
        opacity: 0.5
    }
];

// We insert the Super Bible gradients after our partial transparency menu item
export const defaultBackgroundColors = temp.concat(specialColors);

// Handles all types of color strings: special-named, hex, rgb(), or rgba().
// If BubbleSpec entails opacity, this string should be of the form "rgba(r, g, b, a)".
export const getSwatchFromBubbleSpecColor = (
    bubbleSpecColor: string
): ISwatchDefn => {
    if (isSpecialColorName(bubbleSpecColor)) {
        // A "special" color gradient, get our swatch from the definitions.
        // It "has" to be there, because we just checked to see if the name was a special color!
        return specialColors.find(
            color => color.name === bubbleSpecColor
        ) as ISwatchDefn;
    }
    // If currentBubbleSpec has transparency, the background color will be an rgba() string.
    // We need to pull out the "opacity" and add it to the swatch here.
    const colorStruct = tinycolor(bubbleSpecColor);
    const opacity = colorStruct.getAlpha();
    return {
        colors: [`#${colorStruct.toHex()}`],
        opacity: opacity
    };
};

export const isSpecialColorName = (colorName: string): boolean =>
    !!specialColors.find(item => item.name === colorName);

export const getSpecialColorName = (
    colorArray: string[]
): string | undefined => {
    const special = specialColors.find(
        elem => elem.colors[1] === colorArray[1]
    );
    return special ? special.name : undefined;
};

export const getRgbaColorStringFromColorAndOpacity = (
    color: string,
    opacity: number
): string => {
    let rgbColor = tinycolor(color).toRgb();
    rgbColor.a = opacity;
    return tinycolor(rgbColor).toRgbString(); // actually format is "rgba(r, g, b, a)"
};
