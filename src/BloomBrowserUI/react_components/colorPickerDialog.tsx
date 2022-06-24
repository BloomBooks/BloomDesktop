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
import { ISwatchDefn } from "./colorSwatch";
import {
    getSpecialColorName,
    getSwatchFromBubbleSpecColor
} from "../bookEdit/toolbox/overlay/overlayToolColorHelper";
import Draggable from "react-draggable";
import "./colorPickerDialog.less";

export interface IColorPickerDialogProps {
    localizedTitle: string;
    noAlphaSlider?: boolean;
    noGradientSwatches?: boolean;
    initialColor: ISwatchDefn;
    defaultSwatchColors: ISwatchDefn[];
    onChange: (color: ISwatchDefn) => void;
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
    const MAX_SWATCHES = 14;
    const [open, setOpen] = useState(true);
    const [currentColor, setCurrentColor] = useState(props.initialColor);
    const [swatchArray, setSwatchArray] = useState(props.defaultSwatchColors);
    externalSetOpen = setOpen;
    const dlgRef = useRef<HTMLElement>(null);

    React.useEffect(() => {
        if (open) {
            BloomApi.get("editView/getBookColors", result => {
                const jsonArray = result.data;
                if (!jsonArray.map) {
                    return; // this means the conversion string -> JSON didn't work. Bad JSON?
                }
                // Maybe we first used this for text colors and now we're using it for background colors or vice versa.
                // Add this usage's default colors, in case they weren't already there.
                const swatchArray = convertJsonColorsToSwatches(jsonArray);
                addNewSwatchesToArrayIfNecessary(
                    props.defaultSwatchColors.concat(swatchArray)
                );
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
        // to an elment passed to onInputFocus as a result of the dialog sending a color change.
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

    const convertJsonColorsToSwatches = (jsonArray: any): ISwatchDefn[] => {
        return jsonArray.map((bubbleSpecColor: { colors: string[] }) => {
            const colorArray = bubbleSpecColor.colors;
            // check for a special color or gradient
            let colorKey = getSpecialColorName(colorArray);
            if (!colorKey) {
                // Not a gradient or other "known" color, so there'll only be one color.
                colorKey = colorArray[0];
            }
            return getSwatchFromBubbleSpecColor(colorKey);
        });
    };

    const onClose = (result: DialogResult) => {
        setOpen(false);
        if (result === DialogResult.Cancel) {
            props.onChange(props.initialColor);
            setCurrentColor(props.initialColor);
        } else {
            addNewSwatchesToArrayIfNecessary([currentColor]);
        }
    };

    // We come to here on opening to add swatches already in the book and we come here on closing to see
    // if our new current color needs to be added to our array.
    // Enhance: What if the number of distinct swatches already used in the book that we get back, plus the number
    // of other default swatches is more than will fit in our array (current 14)? When we get swatches from the book,
    // we should maybe start with the current page, to give them a better chance of being included in the picker.
    const addNewSwatchesToArrayIfNecessary = (newSwatches: ISwatchDefn[]) => {
        const newSwatchesAdded: ISwatchDefn[] = [];
        const lengthBefore = swatchArray.length;
        let numberToDelete = 0;
        // CustomColorPicker is going to filter these swatches out anyway.
        let numberToSkip = swatchArray.filter(swatch =>
            willSwatchBeFilteredOut(swatch)
        ).length;
        newSwatches.forEach(newSwatch => {
            if (isSwatchInCurrentSwatchArray(newSwatch)) {
                return; // This one is already in our array of swatches
            }
            if (isSwatchInThisArray(newSwatch, newSwatchesAdded)) {
                return; // We don't need to add the same swatch more than once!
            }
            // At first I wanted to do this filtering outside the loop, but some of them might be pre-filtered
            // by the above two conditions.
            if (willSwatchBeFilteredOut(newSwatch)) {
                numberToSkip++;
            }
            if (
                lengthBefore + newSwatchesAdded.length + 1 >
                MAX_SWATCHES + numberToSkip
            ) {
                numberToDelete++;
            }
            newSwatchesAdded.unshift(newSwatch); // add newSwatch to the beginning of the array.
        });
        const newSwatchArray = swatchArray.slice(); // Get a new array copy of the old (a different reference)
        if (numberToDelete > 0) {
            // Remove 'numberToDelete' swatches from oldest custom swatches
            const defaultNumber = props.defaultSwatchColors.length;
            const indexToRemove =
                swatchArray.length - defaultNumber - numberToDelete;
            if (indexToRemove >= 0) {
                newSwatchArray.splice(indexToRemove, numberToDelete);
            } else {
                const excess = indexToRemove * -1; // index went negative; excess is absolute value
                newSwatchArray.splice(0, numberToDelete - excess);
                newSwatchesAdded.splice(
                    newSwatchesAdded.length - excess,
                    excess
                );
            }
        }
        setSwatchArray(newSwatchesAdded.concat(newSwatchArray));
    };

    const isSwatchInCurrentSwatchArray = (swatch: ISwatchDefn): boolean =>
        isSwatchInThisArray(swatch, swatchArray);

    const willSwatchBeFilteredOut = (swatch: ISwatchDefn): boolean => {
        if (props.noAlphaSlider && swatch.opacity !== 1) {
            return true;
        }
        if (props.noGradientSwatches && swatch.colors.length > 1) {
            return true;
        }
        return false;
    };

    // Use a compare function to see if the swatch in question matches on already in this list or not.
    const isSwatchInThisArray = (
        swatch: ISwatchDefn,
        arrayOfSwatches: ISwatchDefn[]
    ): boolean => !!arrayOfSwatches.find(swatchCompareFunc(swatch));

    // Function for comparing a swatch with an array of swatches to see if the swatch is already
    // in the array. We pass this function to .find().
    const swatchCompareFunc = (swatch: ISwatchDefn) => (
        item: ISwatchDefn
    ): boolean => {
        if (item.colors.length !== swatch.colors.length) {
            return false; // One is a gradient and the other is not.
        }
        if (swatch.colors.length > 1) {
            // In the case of both being gradients, check the second color first.
            const swatchColor2 = tinycolor(swatch.colors[1]);
            const itemColor2 = tinycolor(item.colors[1]);
            if (swatchColor2.toHex() !== itemColor2.toHex()) {
                return false;
            }
        }
        const swatchColor1 = tinycolor(swatch.colors[0]);
        const itemColor1 = tinycolor(item.colors[0]);
        return (
            swatchColor1.toHex() === itemColor1.toHex() &&
            swatch.opacity === item.opacity
        );
    };

    const handleOnChange = (color: ISwatchDefn) => {
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
                    onClose={(_event, reason) => {
                        if (reason === "backdropClick")
                            onClose(DialogResult.OK); // BL-9930
                    }}
                >
                    <DialogTitle
                        id="draggable-color-picker-title"
                        style={{ cursor: "move" }}
                    >
                        {props.localizedTitle}
                    </DialogTitle>
                    <DialogContent>
                        <CustomColorPicker
                            onChange={handleOnChange}
                            currentColor={currentColor}
                            swatchColors={swatchArray}
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
