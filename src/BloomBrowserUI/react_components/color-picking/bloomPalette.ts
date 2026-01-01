import { IColorInfo, getColorInfoFromString } from "./colorSwatch";
import { kBloomGray } from "../../utils/colorUtils";
import { getWithPromise } from "../../utils/bloomApi";

export enum BloomPalette {
    Text = "text",
    CoverBackground = "cover-background",
    BloomReaderBookshelf = "bloom-reader-bookshelf",
    TextBackground = "overlay-background",
    HighlightBackground = "highlight-background",
    PageColors = "page-colors",
}

// This array provides a useful default palette for the color picker dialog.
// See the code below for mapping an array of strings like this into an array of
// IColorInfo objects.
export const TextColorPalette: string[] = [
    //NB: do not use names, as some pickers may expect numbers only
    // black to white
    "#000000", // black
    "#FFFFFF", // white
    // red to purple
    "#ff1616",
    "#ff5757",
    "#ff66c4",
    "#cb6ce6",
    "#8c52ff",
    // teal to blue
    "#03989e",
    "#00c2cb",
    "#5ce1e6",
    "#5271ff",
    "#004aad",
    // green to orange
    "#007f37",
    "#7ed957",
    "#c9e265",
    "#ffde59",
    "#ff914d",
];

// copied from colorChooser.tsx
export const CoverBackgroundPalette: string[] = [
    "#E48C84",
    "#B0DEE4",
    "#98D0B9",
    "#C2A6BF",
    "#FFFFA4",
    "#FEBF00",
    "#7BDCB5",
    "#B2CC7D",
    "#F8B576",
    "#D29FEF",
    "#ABB8C3",
    "#C1EF93",
    "#FFD4D4",
    "#FFAAD4",
];

export const HighlightBackgroundPalette: string[] = [
    "#FEBF00",
    "#FFFF00",
    "#FBDBCF",
    "#BBF4BB",
    "#C5F0FF",
];

// Light background colors suitable for page backgrounds.
// (Users can still pick any color, but these are the suggested defaults.)
export const PageColorsPalette: string[] = [
    "#FFFFFF", // white
    "#F7F7F7", // very light gray
    "#FFF7E6", // warm cream
    "#FFF1F2", // very light pink
    "#FCE7F3", // pale rose
    "#F3E8FF", // pale lavender
    "#EDE9FE", // pale purple
    "#E0F2FE", // pale sky
    "#E0F7FA", // pale cyan
    "#E6FFFA", // pale teal
    "#ECFDF3", // pale green
    "#F7FEE7", // pale lime
    "#FFFBEB", // pale amber
    "#FEF3C7", // light beige
];

const specialColors: IColorInfo[] = [
    // #DFB28B is the color Comical has been using as the default for captions.
    // It's fairly close to the "Calico" color defined at https://www.htmlcsscolor.com/hex/D5B185 (#D5B185)
    // so I decided it was the best choice for keeping that option.
    {
        name: "whiteToCalico",
        colors: ["white", "#DFB28B"],
        opacity: 1,
    },
    // https://www.htmlcsscolor.com/hex/ACCCDD
    {
        name: "whiteToFrenchPass",
        colors: ["white", "#ACCCDD"],
        opacity: 1,
    },
    // https://encycolorpedia.com/7b8eb8
    {
        name: "whiteToPortafino",
        colors: ["white", "#7b8eb8"],
        opacity: 1,
    },
];
const plainColors: IColorInfo[] = [
    { name: "black", colors: ["black"], opacity: 1 },
    { name: "white", colors: ["white"], opacity: 1 },
    { name: "partialTransparent", colors: [kBloomGray], opacity: 0.5 },
];

// These colors are what the canvas element tool has been using for the background color chooser.
export const TextBackgroundColors: IColorInfo[] =
    plainColors.concat(specialColors);

// all colors, whether factory or "custom"
// no leading "#", no gradients
export async function getHexColorsForPalette(
    palette: BloomPalette,
): Promise<string[]> {
    let factoryColors: string[];
    switch (palette) {
        case BloomPalette.HighlightBackground:
            factoryColors = HighlightBackgroundPalette;
            break;
        case BloomPalette.BloomReaderBookshelf:
        case BloomPalette.CoverBackground:
            factoryColors = CoverBackgroundPalette;
            break;
        case BloomPalette.PageColors:
            factoryColors = PageColorsPalette;
            break;
        case BloomPalette.Text:
            factoryColors = TextColorPalette;
            break;
        case BloomPalette.TextBackground:
            throw new Error(
                "getColorHexNumbersFromPalette cannot currently handle TextBackground because it contains gradients",
            );
    }
    return getWithPromise(
        `settings/getCustomPaletteColors?palette=${palette}`,
    ).then((result) => {
        let customColors: Array<string> = [];
        if (result && result.data) {
            // {"data":[{"colors":["#0071ff"],"opacity":1},{"colors":["#0000ff"],"opacity":1}]
            // Note: it's highly unexpected for result to be falsy or lack data.
            // It would signify that our API call failed somehow. But getWithPromise should
            // already have reported it, so we don't need to, and doing so again can have some bad
            // results, e.g., BL-11657. The worst consequence here is that we revert to factory
            // colors. Hopefully, any new customizations will be saved in the current way and work.
            customColors = (
                result.data as Array<{
                    colors: string[];
                }>
            ).map((c) => c.colors[0]);
        }
        return [...factoryColors, ...customColors].map((c) =>
            c.replace("#", ""),
        );
    });
}

export function getDefaultColorsFromPalette(
    paletteType: BloomPalette,
): IColorInfo[] {
    if (paletteType === BloomPalette.TextBackground)
        return JSON.parse(JSON.stringify(TextBackgroundColors));

    let palette;
    switch (paletteType) {
        case BloomPalette.HighlightBackground:
            palette = HighlightBackgroundPalette;
            break;
        case BloomPalette.BloomReaderBookshelf:
        case BloomPalette.CoverBackground:
            palette = CoverBackgroundPalette;
            break;
        case BloomPalette.PageColors:
            palette = PageColorsPalette;
            break;
        case BloomPalette.Text:
            palette = TextColorPalette;
            break;
    }
    return palette.map((color: string) =>
        getColorInfoFromSpecialNameOrColorString(color),
    );
}

// Handles all types of color strings: special-named, hex, rgb(), or rgba().
// If color entails opacity, this string should be of the form "rgba(r, g, b, a)".
export const getColorInfoFromSpecialNameOrColorString = (
    specialNameOrColorString: string,
): IColorInfo => {
    if (isSpecialColorName(specialNameOrColorString)) {
        // A "special" color gradient, get our colorInfo from the definitions.
        // It "has" to be there, because we just checked to see if the name was a special color!
        return specialColors.find(
            (color) => color.name === specialNameOrColorString,
        ) as IColorInfo;
    }
    return getColorInfoFromString(specialNameOrColorString);
};

const isSpecialColorName = (colorName: string): boolean =>
    !!specialColors.find((item) => item.name === colorName);

export const getSpecialColorName = (
    colorArray: string[],
): string | undefined => {
    const special = specialColors.find(
        (elem) => elem.colors[1] === colorArray[1],
    );
    return special ? special.name : undefined;
};
