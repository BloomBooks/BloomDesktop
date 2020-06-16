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

export const getSwatchFromHex = (
    hexColor: string,
    opacity?: number
): ISwatchDefn => {
    return {
        colors: [hexColor],
        opacity: opacity ? opacity : 1
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
