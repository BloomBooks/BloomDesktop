/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import React = require("react");
import * as ReactDOM from "react-dom";
import { useEffect, useRef, useState } from "react";
import { getEditTabBundleExports } from "../../bookEdit/js/bloomFrames";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { lightTheme } from "../../bloomMaterialUITheme";
import { get, postJson } from "../../utils/bloomApi";
import ColorPicker from "./colorPicker";
import * as tinycolor from "tinycolor2";
import { IColorInfo, normalizeColorInfoColorsAsHex } from "./colorSwatch";
import { getRgbaColorStringFromColorAndOpacity } from "../../utils/colorUtils";
import {
    BloomPalette,
    getColorInfoFromSpecialNameOrColorString,
    getDefaultColorsFromPalette,
    getSpecialColorName
} from "./bloomPalette";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle
} from "../BloomDialog/BloomDialog";
import {
    DialogCancelButton,
    DialogOkButton
} from "../BloomDialog/commonDialogComponents";

export interface IColorPickerDialogProps {
    open?: boolean;
    close?: (result: DialogResult) => void;
    localizedTitle: string;
    transparency?: boolean;
    noGradientSwatches?: boolean;
    initialColor: IColorInfo;
    palette: BloomPalette;
    isForOverlay?: boolean;
    onChange: (color: IColorInfo) => void;
    onDefaultClick?: () => void;
    onInputFocus: (input: HTMLElement) => void;
    includeDefault?: boolean;
    //defaultColor?: IColorInfo; eventually we'll need this
}

let externalSetOpen: React.Dispatch<React.SetStateAction<boolean>>;

const ColorPickerDialog: React.FC<IColorPickerDialogProps> = props => {
    const MAX_SWATCHES = 21;
    const [open, setOpen] = useState(
        props.open === undefined ? true : props.open
    );
    const [currentColor, setCurrentColor] = useState(props.initialColor);

    const [swatchColorArray, setSwatchColorArray] = useState(
        getDefaultColorsFromPalette(props.palette)
    );

    externalSetOpen = setOpen;
    const dlgRef = useRef<HTMLDivElement>(null);

    function addCustomColors(endpoint: string): void {
        get(endpoint, result => {
            const jsonArray = result.data;
            if (!jsonArray.map) {
                return; // this means the conversion string -> JSON didn't work. Bad JSON?
            }
            const customColors = convertJsonColorArrayToColorInfos(jsonArray);
            addNewColorsToArrayIfNecessary(customColors);
        });
    }

    useEffect(() => {
        if (props.open || open) {
            setSwatchColorArray(getDefaultColorsFromPalette(props.palette));
            addCustomColors(
                `settings/getCustomPaletteColors?palette=${props.palette}`
            );
            // Before we introduced the concept of a custom palette (BL-11433),
            // the overlay tool color pickers would display all colors currently in
            // use in any overlays in the book. Rather than try some fancy migration,
            // we just continue to display those along with the custom palette colors.
            // Thus, users won't see colors disappear from their pickers.
            if (props.isForOverlay)
                addCustomColors("editView/getColorsUsedInBookOverlays");
            setCurrentColor(props.initialColor);
        }
    }, [open, props.open]);

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
        jsonArray: IColorInfo[]
    ): IColorInfo[] => {
        return jsonArray.map((colorInfo: IColorInfo) => {
            const colorArray = colorInfo.colors;
            // check for a special color or gradient
            let colorKey = getSpecialColorName(colorArray);
            if (!colorKey) {
                // Not a gradient or other "known" color, so there'll only be one color.
                colorKey = colorArray[0];
                if (colorInfo.opacity !== 1) {
                    // The colorInfo may have been saved as a 6-digit hex with opacity only in the opacity field.
                    // But later code assumes that colors with opacity are in rgba format.
                    colorKey = getRgbaColorStringFromColorAndOpacity(
                        colorKey,
                        colorInfo.opacity
                    );
                }
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
                normalizeColorInfoColorsAsHex(currentColor);
                postJson(
                    `settings/addCustomPaletteColor?palette=${props.palette}`,
                    currentColor
                );
                addNewColorsToArrayIfNecessary([currentColor]);
            }
        }
        if (props.close) {
            props.close(result);
        }
    };

    // We come to here on opening to add colors already in the book and we come here on closing to see
    // if our new current color needs to be added to our array.
    // Enhance: What if the number of distinct colors already used in the book that we get back, plus the number
    // of other default colors is more than will fit in our array (current 21)? When we get colors from the book,
    // we should maybe start with the current page, to give them a better chance of being included in the picker.
    const addNewColorsToArrayIfNecessary = (newColors: IColorInfo[]) => {
        // Every time we reference the current swatchColorArray inside
        // this setter, we must use previousSwatchColorArray.
        // Otherwise, we add to a stale array.
        setSwatchColorArray(previousSwatchColorArray => {
            const newColorsAdded: IColorInfo[] = [];
            const lengthBefore = previousSwatchColorArray.length;
            let numberToDelete = 0;
            // CustomColorPicker is going to filter these colors out anyway.
            let numberToSkip = previousSwatchColorArray.filter(color =>
                willSwatchColorBeFilteredOut(color)
            ).length;
            newColors.forEach(newColor => {
                if (isColorInThisArray(newColor, previousSwatchColorArray)) {
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
                const defaultNumber = getDefaultColorsFromPalette(props.palette)
                    .length;
                const indexToRemove =
                    swatchColorArray.length - defaultNumber - numberToDelete;
                if (indexToRemove >= 0) {
                    newSwatchColorArray.splice(indexToRemove, numberToDelete);
                } else {
                    const excess = indexToRemove * -1; // index went negative; excess is absolute value
                    newSwatchColorArray.splice(0, numberToDelete - excess);
                    newColorsAdded.splice(
                        newColorsAdded.length - excess,
                        excess
                    );
                }
            }
            const result = newColorsAdded.concat(previousSwatchColorArray);
            //console.log(result);
            return result;
        });
    };

    const isColorInCurrentSwatchColorArray = (color: IColorInfo): boolean =>
        isColorInThisArray(color, swatchColorArray);

    const willSwatchColorBeFilteredOut = (color: IColorInfo): boolean => {
        if (!props.transparency && color.opacity !== 1) {
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

    return (
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
                <BloomDialog
                    className="bloomModalDialog"
                    css={css`
                        z-index: 60001; // dialogZIndexPlusOne (yuck!)
                        .MuiBackdrop-root {
                            // We want the overlay barely visible so it doesn't interfere with picking colors.
                            background-color: rgba(0, 0, 0, 0.05) !important;
                        }
                        .MuiDialog-paperScrollPaper {
                            max-height: unset;
                        }
                        overflow-y: unset !important; // something from bloomModalDialog that we don't want
                        .MuiDialogActions-spacing {
                            padding: 10px 14px 10px 10px; // maintain same spacing all around dialog content and between header/footer
                        }
                    `}
                    open={props.open === undefined ? open : props.open}
                    ref={dlgRef}
                    onClose={(
                        _event,
                        reason: "backdropClick" | "escapeKeyDown"
                    ) => {
                        if (reason === "backdropClick")
                            onClose(DialogResult.OK);
                        if (reason === "escapeKeyDown")
                            onClose(DialogResult.Cancel);
                    }}
                >
                    <DialogTitle
                        title={props.localizedTitle}
                        css={css`
                            text-align: center;
                            padding: 14px 24px; // maintain same spacing all around dialog content
                        `}
                    />
                    <DialogMiddle>
                        <ColorPicker
                            onChange={handleOnChange}
                            currentColor={currentColor}
                            swatchColors={swatchColorArray}
                            transparency={props.transparency}
                            noGradientSwatches={props.noGradientSwatches}
                            includeDefault={props.includeDefault}
                            onDefaultClick={props.onDefaultClick}
                            //defaultColor={props.defaultColor}
                        />
                    </DialogMiddle>
                    <DialogBottomButtons>
                        <DialogOkButton
                            enabled={true}
                            default={true}
                            onClick={() => onClose(DialogResult.OK)}
                        />
                        <DialogCancelButton
                            default={false}
                            onClick_DEPRECATED={() =>
                                onClose(DialogResult.Cancel)
                            }
                        />
                    </DialogBottomButtons>
                </BloomDialog>
            </ThemeProvider>
        </StyledEngineProvider>
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

// The following interface and function provide a simpler interface to the color
// choose dialog which doesn't depend on IColorInfo.
export interface ISimpleColorPickerDialogProps {
    localizedTitle: string;
    transparency?: boolean;
    initialColor: string;
    palette: BloomPalette;
    onChange: (color: string) => void;
    onInputFocus: (input: HTMLElement) => void;
    container?: Element;
}

export const showSimpleColorPickerDialog = (
    props: ISimpleColorPickerDialogProps
) => {
    const fullProps: IColorPickerDialogProps = {
        localizedTitle: props.localizedTitle,
        transparency: props.transparency,
        noGradientSwatches: true,
        initialColor: getColorInfoFromSpecialNameOrColorString(
            props.initialColor
        ),
        palette: props.palette,
        onChange: (color: IColorInfo) => props.onChange(color.colors[0]),
        onInputFocus: props.onInputFocus
    };
    showColorPickerDialog(fullProps, props.container);
};

export interface IColorDisplayButtonProps {
    // This is slightly more than an initial color. The button will change color
    // independently of this to follow the state of the color picker dialog;
    // but if the client re-renders with a DIFFERENT initialColor, the button
    // will change to match.
    initialColor: string;
    localizedTitle: string;
    transparency: boolean;
    width?: number;
    disabled?: boolean;
    onClose: (result: DialogResult, newColor: string) => void;
    palette: BloomPalette;
}

export const ColorDisplayButton: React.FC<IColorDisplayButtonProps> = props => {
    const [dialogOpen, setDialogOpen] = useState(false);
    const [currentButtonColor, setCurrentButtonColor] = useState(
        props.initialColor
    );
    const widthString = props.width ? `width: ${props.width}px;` : "";

    useEffect(() => {
        if (currentButtonColor !== props.initialColor) {
            setCurrentButtonColor(props.initialColor);
        }
        // If the client changes the initial color, it should take effect.
        // Otherwise, it can follow what's going on in the dialog.
        // (For this to work, in defiance of lint, we MUST NOT put currentButtonColor
        // in the dependencies list; we want it NOT to get reset when something
        // other than a new props value changes it. )
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [props.initialColor]);
    return (
        <div>
            <div
                css={css`
                    border: solid 1px black;
                    background-color: white;
                    padding: 2px;
                    height: 19px;
                    ${widthString}
                `}
            >
                <div
                    css={css`
                        background-color: ${currentButtonColor};
                        height: 19px;
                        ${widthString}
                    `}
                    onClick={() => {
                        if (props.disabled) return;
                        setDialogOpen(true);
                    }}
                />
            </div>
            <ColorPickerDialog
                open={dialogOpen}
                close={(result: DialogResult) => {
                    setDialogOpen(false);
                    props.onClose(
                        result,
                        result === DialogResult.OK
                            ? currentButtonColor
                            : props.initialColor
                    );
                }}
                localizedTitle={props.localizedTitle}
                transparency={props.transparency}
                palette={props.palette}
                initialColor={getColorInfoFromSpecialNameOrColorString(
                    props.initialColor
                )}
                onInputFocus={() => {}}
                onChange={(color: IColorInfo) =>
                    setCurrentButtonColor(color.colors[0])
                }
            />
        </div>
    );
};
