import React = require("react");
import * as ReactDOM from "react-dom";
import { useState } from "react";
import Dialog from "@material-ui/core/Dialog";
import {
    Button,
    DialogTitle,
    DialogActions,
    DialogContent,
    Paper
} from "@material-ui/core";
import CloseOnEscape from "react-close-on-escape";
import { getEditViewFrameExports } from "../bookEdit/js/bloomFrames";
import { useL10n } from "./l10nHooks";
import { ThemeProvider } from "@material-ui/styles";
import theme from "../bloomMaterialUITheme";
import { BloomApi } from "../utils/bloomApi";
import CustomColorPicker from "./customColorPicker";
import * as tinycolor from "tinycolor2";
import { ISwatchDefn } from "./colorSwatch";
import Draggable from "react-draggable";
import "./colorPickerDialog.less";

export interface IColorPickerDialogProps {
    localizedTitle: string;
    noAlphaSlider?: boolean;
    noGradientSwatches?: boolean;
    initialColor: ISwatchDefn;
    defaultSwatchColors: ISwatchDefn[];
    onChange: (color: ISwatchDefn) => void;
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

    React.useEffect(() => {
        if (open) {
            BloomApi.get("editView/getBookColors", result => {
                const jsonString = result.data;
                if (!jsonString.map) {
                    return; // this means the conversion string -> JSON didn't work. Bad JSON?
                }
                // Maybe we first used this for text colors and now we're using it for background colors or vice versa.
                // Add this usage's default colors, in case they weren't already there.
                addNewSwatchesToArrayIfNecessary(
                    props.defaultSwatchColors.concat(jsonString)
                );
            });
            setCurrentColor(props.initialColor);
        }
    }, [open]);

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
        newSwatches.forEach(newSwatch => {
            if (isSwatchInCurrentSwatchArray(newSwatch)) {
                return; // This one is already in our array of swatches
            }
            if (isSwatchInThisArray(newSwatch, newSwatchesAdded)) {
                return; // We don't need to add the same swatch more than once!
            }
            if (lengthBefore + newSwatchesAdded.length + 1 > MAX_SWATCHES) {
                numberToDelete++;
            }
            newSwatchesAdded.unshift(newSwatch); // add newSwatch to the beginning of the array.
        });
        const newSwatchArray = swatchArray.slice(); // Get a new array copy of the old (a different reference)
        if (numberToDelete > 0) {
            // Remove 'numberToDelete' swatches from oldest custom swatches
            const defaultNumber = props.defaultSwatchColors.length;
            const indexToRemove =
                MAX_SWATCHES - defaultNumber - newSwatchesAdded.length;
            if (indexToRemove > 0) {
                newSwatchArray.splice(indexToRemove, numberToDelete);
            }
        }
        setSwatchArray(newSwatchesAdded.concat(newSwatchArray));
    };

    const isSwatchInCurrentSwatchArray = (swatch: ISwatchDefn): boolean =>
        isSwatchInThisArray(swatch, swatchArray);

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
        const swatchOpacity = swatch.opacity ? swatch.opacity : 1;
        const itemOpacity = item.opacity ? item.opacity : 1;
        const swatchColor1 = tinycolor(swatch.colors[0]);
        const itemColor1 = tinycolor(item.colors[0]);
        return (
            swatchColor1.toHex() === itemColor1.toHex() &&
            swatchOpacity === itemOpacity
        );
    };

    const handleOnChange = (color: ISwatchDefn) => {
        setCurrentColor(color);
        props.onChange(color);
    };

    const OkText = useL10n("OK", "Common.OK");
    const CancelText = useL10n("Cancel", "Common.Cancel");

    return (
        <ThemeProvider theme={theme}>
            <CloseOnEscape onEscape={() => onClose(DialogResult.Cancel)}>
                <Dialog
                    className="bloomModalDialog color-picker-dialog"
                    open={open}
                    PaperComponent={PaperComponent}
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
                            // Temporary: When comical does alpha, chg to 'props.noAlphaSlider'.
                            // Unfortunately, with an alpha slider, the hex input will automatically switch to rgb
                            // the moment the user sets alpha to anything but max opacity.
                            noAlphaSlider={true}
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
    else modalContainer = getEditViewFrameExports().getModalDialogContainer();
    ReactDOM.render(<ColorPickerDialog {...props} />, modalContainer);
};
