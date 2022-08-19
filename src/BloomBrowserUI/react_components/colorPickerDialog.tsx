import React = require("react");
import * as ReactDOM from "react-dom";
import { useRef, useState } from "react";
import Dialog from "@material-ui/core/Dialog";
import {
    Button,
    DialogTitle,
    DialogActions,
    DialogContent,
    Paper
} from "@material-ui/core";
import CloseOnEscape from "react-close-on-escape";
import { getEditTabBundleExports } from "../bookEdit/js/bloomFrames";
import { useL10n } from "./l10nHooks";
import { ThemeProvider } from "@material-ui/styles";
import { lightTheme } from "../bloomMaterialUITheme";
import { BloomApi } from "../utils/bloomApi";
import CustomColorPicker from "./customColorPicker";
import * as tinycolor from "tinycolor2";
import { IColorInfo, getColorInfoFromString } from "./colorSwatch";
import Draggable from "react-draggable";
import "./colorPickerDialog.less";
import { kBloomGray } from "../utils/colorUtils";

export enum BloomPalette {
    Text = "text",
    CoverBackground = "cover-background",
    BloomReaderBookshelf = "bloom-reader-bookshelf",
    OverlayText = "overlay-text",
    OverlayBackground = "overlay-background"
}

export interface IColorPickerDialogProps {
    localizedTitle: string;
    noAlphaSlider?: boolean;
    noGradientSwatches?: boolean;
    initialColor: IColorInfo;
    palette?: BloomPalette;
    defaultSwatchColors?: IColorInfo[]; // deprecated, soon to be removed
    onChange: (color: IColorInfo) => void;
    onInputFocus: (input: HTMLElement) => void;
}

function PaperComponent(props) {
    return (
        <Draggable
            bounds="parent"
            handle="#draggable-color-picker-title"
            cancel={'[class*="MuiDialogContent-root"]'}
        >
            <Paper {...props} />
        </Draggable>
    );
}

let externalSetOpen: React.Dispatch<React.SetStateAction<boolean>>;

const ColorPickerDialog: React.FC<IColorPickerDialogProps> = props => {
    const MAX_SWATCHES = 21;
    const [open, setOpen] = useState(true);
    const [currentColor, setCurrentColor] = useState(props.initialColor);

    let defaultSwatchColors: IColorInfo[];
    if (props.defaultSwatchColors)
        defaultSwatchColors = props.defaultSwatchColors;
    else if (props.palette)
        defaultSwatchColors = getDefaultColorsFromPalette(props.palette);
    else defaultSwatchColors = [];

    const [swatchColorArray, setSwatchColorArray] = useState(
        defaultSwatchColors
    );

    externalSetOpen = setOpen;
    const dlgRef = useRef<HTMLElement>(null);

    React.useEffect(() => {
        if (open) {
            let getColorsApiCall: string;
            if (props.palette) {
                getColorsApiCall = `settings/getCustomPaletteColors?palette=${props.palette}`;
            } else {
                getColorsApiCall = "editView/getBookColors";
            }
            BloomApi.get(getColorsApiCall, result => {
                const jsonArray = result.data;
                if (!jsonArray.map) {
                    return; // this means the conversion string -> JSON didn't work. Bad JSON?
                }
                const customColors = convertJsonColorArrayToColorInfos(
                    jsonArray
                );
                addNewColorsToArrayIfNecessary(customColors);
            });
            setCurrentColor(props.initialColor);
        }
    }, [open]);

    const focusFunc = (ev: FocusEvent) => {
        props.onInputFocus(ev.currentTarget as HTMLElement);
    };

    React.useEffect(() => {
        const parent = dlgRef.current;
        if (!parent) {
            return;
        }

        // When we make incremental color changes while editing one of these inputs,
        // the process of applying the changed color to the overlay moves the focus
        // to the overlay. This makes it painfully necessary to click back in the input
        // box after each keystroke. This code arranges that when one of our inputs gets
        // focused, we pass that information to our client, which uses it to refocus
        // the appropriate control once things stabilize in the overlay.
        const inputs = Array.from(parent.getElementsByTagName("input"));
        inputs.forEach(input => input.addEventListener("focus", focusFunc));

        // In addition to this cleanup, I feel as if we should be doing something like
        // calling props.onInputFocus(null) when the input is no longer focused.
        // This is not easy. We can't simply add an onBlur; the reason for keeping track
        // of the focused input is that focus is being undesirably moved away and we want
        // to put it back. Also, we may have moved focus to another input and already
        // updated input focus to point to that. An onblur with a time delay is conceivable,
        // but it's hard to know what delay.
        // In any case, I don't think it's actually necessary. The focus is only put back
        // to an element passed to onInputFocus as a result of the dialog sending a color change.
        // Once the dialog closes (which it will if anything outside is clicked), it won't be
        // sending color changes so nothing will be done with the input focus. And there is
        // nothing else to focus inside the dialog (unless conceivably the direct manipulation
        // controls might be focused for accessibility? But someone who prefers keyboard is
        // much more likely to want to type into the boxes.)
        // If somehow something messes with color some other way, well, if the dialog is not
        // open I'm pretty sure the system won't set focus to one of its controls.
        return () => {
            const inputs = Array.from(parent!.getElementsByTagName("input"));
            inputs.forEach(input =>
                input.removeEventListener("focus", focusFunc)
            );
        };
    }, [dlgRef.current]);

    const convertJsonColorArrayToColorInfos = (
        jsonArray: { colors: string[] }[]
    ): IColorInfo[] => {
        return jsonArray.map((colorString: { colors: string[] }) => {
            const colorArray = colorString.colors;
            // check for a special color or gradient
            let colorKey = getSpecialColorName(colorArray);
            if (!colorKey) {
                // Not a gradient or other "known" color, so there'll only be one color.
                colorKey = colorArray[0];
            }
            return getColorInfoFromSpecialNameOrColorString(colorKey);
        });
    };

    const onClose = (result: DialogResult) => {
        setOpen(false);
        if (result === DialogResult.Cancel) {
            props.onChange(props.initialColor);
            setCurrentColor(props.initialColor);
        } else {
            if (!isColorInCurrentSwatchColorArray(currentColor)) {
                if (props.palette) {
                    const setColorsApiCall = `settings/addCustomPaletteColor?palette=${props.palette}`;
                    BloomApi.postJson(setColorsApiCall, currentColor);
                }
                addNewColorsToArrayIfNecessary([currentColor]);
            }
        }
    };

    // We come to here on opening to add colors already in the book and we come here on closing to see
    // if our new current color needs to be added to our array.
    // Enhance: What if the number of distinct colors already used in the book that we get back, plus the number
    // of other default colors is more than will fit in our array (current 21)? When we get colors from the book,
    // we should maybe start with the current page, to give them a better chance of being included in the picker.
    const addNewColorsToArrayIfNecessary = (newColors: IColorInfo[]) => {
        const newColorsAdded: IColorInfo[] = [];
        const lengthBefore = swatchColorArray.length;
        let numberToDelete = 0;
        // CustomColorPicker is going to filter these colors out anyway.
        let numberToSkip = swatchColorArray.filter(color =>
            willSwatchColorBeFilteredOut(color)
        ).length;
        newColors.forEach(newColor => {
            if (isColorInCurrentSwatchColorArray(newColor)) {
                return; // This one is already in our array of swatch colors
            }
            if (isColorInThisArray(newColor, newColorsAdded)) {
                return; // We don't need to add the same color more than once!
            }
            // At first I wanted to do this filtering outside the loop, but some of them might be pre-filtered
            // by the above two conditions.
            if (willSwatchColorBeFilteredOut(newColor)) {
                numberToSkip++;
            }
            if (
                lengthBefore + newColorsAdded.length + 1 >
                MAX_SWATCHES + numberToSkip
            ) {
                numberToDelete++;
            }
            newColorsAdded.unshift(newColor); // add newColor to the beginning of the array.
        });
        const newSwatchColorArray = swatchColorArray.slice(); // Get a new array copy of the old (a different reference)
        if (numberToDelete > 0) {
            // Remove 'numberToDelete' swatches from oldest custom swatches
            const defaultNumber = defaultSwatchColors.length;
            const indexToRemove =
                swatchColorArray.length - defaultNumber - numberToDelete;
            if (indexToRemove >= 0) {
                newSwatchColorArray.splice(indexToRemove, numberToDelete);
            } else {
                const excess = indexToRemove * -1; // index went negative; excess is absolute value
                newSwatchColorArray.splice(0, numberToDelete - excess);
                newColorsAdded.splice(newColorsAdded.length - excess, excess);
            }
        }
        setSwatchColorArray(newColorsAdded.concat(newSwatchColorArray));
    };

    const isColorInCurrentSwatchColorArray = (color: IColorInfo): boolean =>
        isColorInThisArray(color, swatchColorArray);

    const willSwatchColorBeFilteredOut = (color: IColorInfo): boolean => {
        if (props.noAlphaSlider && color.opacity !== 1) {
            return true;
        }
        if (props.noGradientSwatches && color.colors.length > 1) {
            return true;
        }
        return false;
    };

    // Use a compare function to see if the color in question matches on already in this list or not.
    const isColorInThisArray = (
        color: IColorInfo,
        arrayOfColors: IColorInfo[]
    ): boolean => !!arrayOfColors.find(colorCompareFunc(color));

    // Function for comparing a color with an array of colors to see if the color is already
    // in the array. We pass this function to .find().
    const colorCompareFunc = (colorA: IColorInfo) => (
        colorB: IColorInfo
    ): boolean => {
        if (colorB.colors.length !== colorA.colors.length) {
            return false; // One is a gradient and the other is not.
        }
        if (colorA.colors.length > 1) {
            // In the case of both being gradients, check the second color first.
            const gradientAColor2 = tinycolor(colorA.colors[1]);
            const gradientBColor2 = tinycolor(colorB.colors[1]);
            if (gradientAColor2.toHex() !== gradientBColor2.toHex()) {
                return false;
            }
        }
        const gradientAColor1 = tinycolor(colorA.colors[0]);
        const gradientBColor1 = tinycolor(colorB.colors[0]);
        return (
            gradientAColor1.toHex() === gradientBColor1.toHex() &&
            colorA.opacity === colorB.opacity
        );
    };

    const handleOnChange = (color: IColorInfo) => {
        setCurrentColor(color);
        props.onChange(color);
    };

    const OkText = useL10n("OK", "Common.OK");
    const CancelText = useL10n("Cancel", "Common.Cancel");

    return (
        <ThemeProvider theme={lightTheme}>
            <CloseOnEscape onEscape={() => onClose(DialogResult.Cancel)}>
                <Dialog
                    className="bloomModalDialog color-picker-dialog"
                    open={open}
                    ref={dlgRef}
                    PaperComponent={PaperComponent}
                    onClick={e => e.stopPropagation()}
                    onClose={(_event, reason) => {
                        if (reason === "backdropClick")
                            onClose(DialogResult.OK); // BL-9930
                    }}
                >
                    <DialogTitle
                        id="draggable-color-picker-title"
                        style={{ cursor: "move" }}
                        onClick={e => e.stopPropagation()}
                    >
                        {props.localizedTitle}
                    </DialogTitle>
                    <DialogContent>
                        <CustomColorPicker
                            onChange={handleOnChange}
                            currentColor={currentColor}
                            swatchColors={swatchColorArray}
                            noAlphaSlider={props.noAlphaSlider}
                            noGradientSwatches={props.noGradientSwatches}
                        />
                    </DialogContent>
                    <DialogActions>
                        <Button
                            onClick={() => onClose(DialogResult.OK)}
                            color="primary"
                        >
                            {OkText}
                        </Button>
                        <Button
                            onClick={() => onClose(DialogResult.Cancel)}
                            color="primary"
                        >
                            {CancelText}
                        </Button>
                    </DialogActions>
                </Dialog>
            </CloseOnEscape>
        </ThemeProvider>
    );
};

export enum DialogResult {
    OK,
    Cancel
}

export const showColorPickerDialog = (
    props: IColorPickerDialogProps,
    container?: Element | null
) => {
    doRender(props, container);
    externalSetOpen(true);
};

const doRender = (
    props: IColorPickerDialogProps,
    container?: Element | null
) => {
    let modalContainer;
    if (container) modalContainer = container;
    else modalContainer = getEditTabBundleExports().getModalDialogContainer();
    try {
        ReactDOM.render(<ColorPickerDialog {...props} />, modalContainer);
    } catch (error) {
        console.error(error);
    }
};

// This array provides a useful default palette for the color picker dialog.
// See the code below for mapping an array of strings like this into an array of
// IColorInfo objects.
export const TextColorPalette: string[] = [
    // black to white
    "black",
    //"gray",
    //"lightgray",
    "white",
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
    "#ff914d"
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
    "#FFAAD4"
];

// These colors are what the overlay tool has been using for the text color chooser.
export const OverlayTextColorPalette: string[] = [
    "black",
    "gray",
    "lightgray",
    "white"
];

const specialColors: IColorInfo[] = [
    // #DFB28B is the color Comical has been using as the default for captions.
    // It's fairly close to the "Calico" color defined at https://www.htmlcsscolor.com/hex/D5B185 (#D5B185)
    // so I decided it was the best choice for keeping that option.
    {
        name: "whiteToCalico",
        colors: ["white", "#DFB28B"],
        opacity: 1
    },
    // https://www.htmlcsscolor.com/hex/ACCCDD
    {
        name: "whiteToFrenchPass",
        colors: ["white", "#ACCCDD"],
        opacity: 1
    },
    // https://encycolorpedia.com/7b8eb8
    {
        name: "whiteToPortafino",
        colors: ["white", "#7b8eb8"],
        opacity: 1
    }
];
const plainColors: IColorInfo[] = [
    { name: "black", colors: ["black"], opacity: 1 },
    { name: "white", colors: ["white"], opacity: 1 },
    { name: "partialTransparent", colors: [kBloomGray], opacity: 0.5 }
];

// These colors are what the overlay tool has been using for the background color chooser.
export const OverlayBackgroundColors: IColorInfo[] = plainColors.concat(
    specialColors
);

function getDefaultColorsFromPalette(paletteType: BloomPalette): IColorInfo[] {
    if (paletteType === BloomPalette.OverlayBackground)
        return JSON.parse(JSON.stringify(OverlayBackgroundColors));

    let palette;
    switch (paletteType) {
        case BloomPalette.BloomReaderBookshelf:
        case BloomPalette.CoverBackground:
            palette = CoverBackgroundPalette;
            break;
        case BloomPalette.OverlayText:
            palette = OverlayTextColorPalette;
            break;
        case BloomPalette.Text:
            palette = TextColorPalette;
            break;
    }
    return palette.map((color: string) =>
        getColorInfoFromSpecialNameOrColorString(color)
    );
}

// Handles all types of color strings: special-named, hex, rgb(), or rgba().
// If color entails opacity, this string should be of the form "rgba(r, g, b, a)".
export const getColorInfoFromSpecialNameOrColorString = (
    specialNameOrColorString: string
): IColorInfo => {
    if (isSpecialColorName(specialNameOrColorString)) {
        // A "special" color gradient, get our colorInfo from the definitions.
        // It "has" to be there, because we just checked to see if the name was a special color!
        return specialColors.find(
            color => color.name === specialNameOrColorString
        ) as IColorInfo;
    }
    return getColorInfoFromString(specialNameOrColorString);
};

const isSpecialColorName = (colorName: string): boolean =>
    !!specialColors.find(item => item.name === colorName);

export const getSpecialColorName = (
    colorArray: string[]
): string | undefined => {
    const special = specialColors.find(
        elem => elem.colors[1] === colorArray[1]
    );
    return special ? special.name : undefined;
};

// The following interface and function provide a simpler interface to the color
// choose dialog which doesn't depend on IColorInfo.
export interface ISimpleColorPickerDialogProps {
    localizedTitle: string;
    noAlphaSlider?: boolean;
    initialColor: string;
    palette?: BloomPalette;
    onChange: (color: string) => void;
    onInputFocus: (input: HTMLElement) => void;
}

export const showSimpleColorPickerDialog = (
    props: ISimpleColorPickerDialogProps
) => {
    const fullProps: IColorPickerDialogProps = {
        localizedTitle: props.localizedTitle,
        noAlphaSlider: props.noAlphaSlider,
        noGradientSwatches: true,
        initialColor: getColorInfoFromSpecialNameOrColorString(
            props.initialColor
        ),
        palette: props.palette,
        onChange: (color: IColorInfo) => props.onChange(color.colors[0]),
        onInputFocus: props.onInputFocus
    };
    showColorPickerDialog(fullProps);
};
