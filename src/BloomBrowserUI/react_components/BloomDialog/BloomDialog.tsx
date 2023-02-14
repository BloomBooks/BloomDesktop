/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { forwardRef, useEffect } from "react";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { Dialog, DialogProps, Paper, PaperProps } from "@mui/material";
import CloseIcon from "@mui/icons-material/Close";
import CloseOnEscape from "react-close-on-escape";
import {
    kBloomBlue50Transparent,
    kDialogPadding,
    kUiFontStack,
    lightTheme
} from "../../bloomMaterialUITheme";
import { useL10n } from "../l10nHooks";
import Draggable from "react-draggable";

// The <BloomDialog> component and its children provides consistent layout across Bloom Dialogs.
// It can be used either inside of a winforms dialog, or as a MaterialUI Dialog.
// See the accompanying storybook story for usage.
// If the dialog content contains something with class initialFocus, it will be automatically
// focused initially. This should usually be the default button.

// Enhance: Would be interested to see if this would improve if we used https://github.com/eBay/nice-modal-react

const kDialogTopPadding = "24px";
const kDialogSidePadding = "24px";
const kDialogBottomPadding = "10px"; // per material, the bottom buttons are supposed to be closer to the edges

export interface IBloomDialogProps extends DialogProps {
    open: boolean;
    // true if the caller is wrapping in a winforms dialog already
    dialogFrameProvidedExternally?: boolean;
    // If it is desired to have a close button in the top right corner of the dialog, see DialogTitle.
    // The available reasons come from MUI's DialogProps.
    onClose: (evt?: object, reason?: "escapeKeyDown" | "backdropClick") => void;
    // we know of at least one scenario (CopyrightAndLicenseDialog) which needs to do
    // this because enabling it causes a react render loop. Our theory is that there is
    // a focus war going on.
    disableDragging?: boolean;
}

export const BloomDialog: React.FunctionComponent<IBloomDialogProps> = forwardRef(
    (props, ref) => {
        // About custom styling:
        // We need the parent to be able to specify things about the size of the dialog. Example:
        //     <BloomDialog
        //          fullWidth={true}
        //          maxWidth="lg"
        // >
        // NB: If you make any changes to this, make sure that the TeamCollectionDialog still takes up most of the screen and the History tab
        // still has lots of room for history and still scrolls as needed.
        // The material props like `fullWidth={true}` and `maxWidth="lg"` get spread to the Dialog component like you would expect.
        // Callers should set height on DialogMiddle which is where we want a scrollbar if it is needed.
        const inner = (
            <div
                css={css`
                    background-color: white;
                    display: flex;
                    flex-direction: column;
                    padding-left: ${kDialogSidePadding};
                    padding-right: ${kDialogSidePadding};
                    padding-bottom: ${kDialogBottomPadding};
                    // dialogFrameProvidedExternally means that we're inside of a winforms dialog.
                    /// So we grow to fit it, and we supply a single black border for some reason (?)
                    ${props.dialogFrameProvidedExternally
                        ? `height: 100%; border: solid thin black; box-sizing: border-box;`
                        : ""}

                    * {
                        // This value is the same as that given in bloomMaterialUITheme.  For some
                        // reason, it is not being applied here.  See BL-10208 and BL-10228.
                        font-family: ${kUiFontStack};
                    }
                    // This will correctly allow the DialogMiddle to add its scrollbar when needed.
                    // Callers should set dialog height by setting the height of DialogMiddle.
                    overflow: auto;
                `}
                className={props.className} // any emotion css from the parent
            >
                {props.children}
            </div>
        );

        // If the dialog content contains something with class initialFocus, this will give it
        // initial focus.
        useEffect(() => {
            // Focusing the default button allows operating that button by pressing Enter.
            // I think this is better than creating a high-level handler for keypress because
            // it allows the user to tab to some other button and activate THAT by pressing
            // Enter.
            // UseEffect allows this to happen just once (so the user can later move focus)
            // but AFTER react has created the actual DOM so we can find the element we want
            // to focus.
            const initialFocus = document.getElementsByClassName(
                "initialFocus"
            )[0] as HTMLButtonElement;
            if (!initialFocus) {
                return; // Enter won't do anything, unless the user tabs to focus a button.
            }
            if (!initialFocus.tabIndex) {
                // not sure if we need this, but in some browser versions I think focus() won't
                // do anything to some kinds of element if they don't have a tab index.
                initialFocus.tabIndex = -1;
            }
            initialFocus?.focus();
        }, []);

        const {
            dialogFrameProvidedExternally,
            disableDragging,
            ...propsToPass
        } = props;

        return (
            <CloseOnEscape
                onEscape={() => {
                    props.onClose(undefined, "escapeKeyDown");
                }}
            >
                <StyledEngineProvider injectFirst>
                    <ThemeProvider theme={lightTheme}>
                        {dialogFrameProvidedExternally ? (
                            inner
                        ) : (
                            <Dialog
                                PaperComponent={
                                    disableDragging ? undefined : DraggablePaper
                                }
                                css={css`
                                    flex-grow: 1; // see note on the display property on PaperComponent
                                    [role="dialog"] {
                                        overflow: hidden; // only the middle should scroll. The DialogTitle and DialogBottomButtons should not.
                                    }
                                `}
                                ref={ref}
                                {...propsToPass} // get fullWidth, maxWidth, open etc. Note that css doesn't end up anywhere useful in the HTML (try the paper?)
                            >
                                {inner}
                            </Dialog>
                        )}
                    </ThemeProvider>
                </StyledEngineProvider>
            </CloseOnEscape>
        );
    }
);

export const DialogTitle: React.FunctionComponent<{
    backgroundColor?: string;
    color?: string;
    icon?: string;
    title: string; // note, this is prop instead of just a child so that we can ensure vertical alignment and bar height, which are easy to mess up.
    disableDragging?: boolean;
    // If closeButtonOptions is defined, the title will have a close button at the far right side.
    closeButtonOptions?: {
        onClose: (evt?: object, reason?: "closeButtonClick") => void;
    };
}> = props => {
    const color = props.color || "black";
    const background = props.backgroundColor || "transparent";
    const cursor = props.disableDragging ? "unset" : "move";
    const closeText = useL10n("Close", "Common.Close");

    // This is lame, but it's really what looks right to me. When there is a color bar, it looks better to have less padding at the top.
    const titleTopPadding =
        background === "transparent" ? kDialogTopPadding : kDialogPadding;
    return (
        <div
            id="draggable-dialog-title"
            css={css`
                color: ${color};
                background-color: ${background};
                display: flex;
                cursor: ${cursor};
                padding-left: ${kDialogTopPadding};
                padding-right: ${kDialogTopPadding};
                padding-top: ${titleTopPadding};
                padding-bottom: ${kDialogPadding};
                margin-left: -${kDialogTopPadding};
                margin-right: -${kDialogTopPadding};
                margin-bottom: ${kDialogPadding};
                * {
                    font-size: 16px;
                    font-weight: bold;
                }
            `}
        >
            {props.icon && (
                <img
                    src={props.icon}
                    alt="Decorative Icon"
                    css={css`
                        margin-right: ${kDialogPadding};
                        color: ${color};
                    `}
                />
            )}
            <h1
                css={css`
                    margin-top: auto;
                    margin-bottom: auto;
                `}
            >
                {props.title}
            </h1>
            {/* Example child would be a Spinner in a progress dialog. */}
            {props.children}
            {props.closeButtonOptions && (
                <CloseIcon
                    color="primary"
                    css={css`
                        margin-left: auto;
                        margin-top: auto;
                        margin-bottom: auto;
                        display: inline-block;
                        cursor: default;
                        border: 1px solid ${kBloomBlue50Transparent};
                        border-radius: 6px;
                        :hover {
                            background-color: ${kBloomBlue50Transparent};
                        }
                    `}
                    titleAccess={closeText}
                    onClick={e => {
                        props.closeButtonOptions?.onClose(
                            e,
                            "closeButtonClick"
                        );
                    }}
                />
            )}
        </div>
    );
};

// The height of this is determined by what is inside of it. If the content might grow (e.g. a progress box), then it's up to the
// client to set maxes or fixed dimensions. See <ProgressDialog> for an example.
export const DialogMiddle: React.FunctionComponent<{}> = props => {
    return (
        <div
            css={css`
                overflow-y: auto;
                display: flex;
                flex-direction: column;
                flex-grow: 1;
                font-size: 14px;

                p {
                    margin-block-start: 0;
                    margin-block-end: 1em;
                }

                // This was 100px. Not sure why we need it at all, but 100px is too much for
                // dialogs like the number-of-duplicates dialogs or simple message boxes.
                min-height: 50px;
            `}
            {...props}
        >
            {props.children}
        </div>
    );
};

// should be a child of DialogBottomButtons
export const DialogBottomLeftButtons: React.FunctionComponent<{}> = props => (
    <div
        css={css`
            margin-right: auto;
            display: flex;

            /* -- button separation -- */
            // this is better but Firefox doesn't support it until FF 63:  gap: ${kDialogPadding};
            button{
                margin-right: ${kDialogPadding};
            }
             //padding-left: 0;//  would be good, if we could only apply it to un-outlined material buttons to make them left-align
             // or margin-left:-8px, which left-aligns such buttons but keeps the padding, which is used in hover effects.

             button{
                 margin-left:0 !important;
             }
        `}
    >
        {props.children}
    </div>
);

// normally one or more buttons. 1st child can also be <DialogBottomLeftButtons> if you have left-aligned buttons to show
export const DialogBottomButtons: React.FunctionComponent<{}> = props => {
    return (
        <div
            css={css`
                margin-left: auto;
                margin-top: auto; // push to bottom
                padding-top: 20px; // leave room between us and the content above us
                display: flex;
                justify-content: flex-end; // make buttons line up on the right, unless wrapped in <DialogBottomLeftButtons>
                      // this is better but Firefox doesn't support it until FF 63:  gap: ${kDialogPadding};

                /* -- button separation -- */
                button{
                margin-left: ${kDialogPadding};
            }

                // As per material (https://i.imgur.com/REsXU1C.png), we actually should be closer to the right than
                // the content.
                // no it looks ugly! width: calc(100% + 10px);

                width: 100%;
            `}
            {...props}
        >
            {props.children}
        </div>
    );
};

// For some reason, making this a FunctionComponent rather than just a function
// which returns a component makes a significant difference.
// When this was a function, typing 3 digits in the hex box would cause
// a loss of focus which caused it to convert, e.g. FFF to FFFFFF.
// See BL-11406.
const DraggablePaper: React.FunctionComponent<PaperProps> = props => {
    return (
        <Draggable
            handle="#draggable-dialog-title"
            cancel={'[class*="MuiDialogContent-root"]'}
        >
            <Paper
                css={css`
                    // Allows setting the Dialog height here on the Paper and
                    // the children can grow into it.
                    display: flex;
                `}
                {...props}
            />
        </Draggable>
    );
};
