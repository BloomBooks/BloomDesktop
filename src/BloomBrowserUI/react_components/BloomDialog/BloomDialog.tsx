import { css, SerializedStyles } from "@emotion/react";
import { forwardRef, useEffect } from "react";
import { FunctionComponent } from "react";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import {
    Dialog,
    DialogProps,
    IconButton,
    Paper,
    PaperProps
} from "@mui/material";
import CloseIcon from "@mui/icons-material/Close";
import {
    kDialogPadding,
    kUiFontStack,
    lightTheme
} from "../../bloomMaterialUITheme";
import { useL10n } from "../l10nHooks";
import Draggable from "react-draggable";
import { hookupLinkHandler } from "../../utils/linkHandler";
import * as React from "react";

// The <BloomDialog> component and its children provides consistent layout across Bloom Dialogs.
// It can be used either inside of a winforms dialog, or as a MaterialUI Dialog.
// See the accompanying storybook story for usage.
// If the dialog content contains something with class initialFocus, it will be automatically
// focused initially. This should usually be the default button.

// Enhance: Would be interested to see if this would improve if we used https://github.com/eBay/nice-modal-react

export const kDialogTopPadding = "24px";
const kDialogSidePadding = "24px";
const kDialogBottomPadding = "10px"; // per material, the bottom buttons are supposed to be closer to the edges

export interface IBloomDialogProps extends DialogProps {
    open: boolean;
    // true if the caller is wrapping in a winforms dialog already
    dialogFrameProvidedExternally?: boolean;
    // If it is desired to have a close button in the top right corner of the dialog, see DialogTitle.
    // The available reasons come from MUI's DialogProps.
    onClose: (evt?: object, reason?: "escapeKeyDown" | "backdropClick") => void;

    // If you define this, then ways of leaving the dialog other than the OK/accept button (escape, clicking out,
    // the cancel button, the BloomTitle close button) will call it.
    onCancel?: (
        reason?:
            | "escapeKeyDown"
            | "backdropClick"
            | "titleCloseClick"
            | "cancelClicked"
    ) => void;

    // we know of at least one scenario (CopyrightAndLicenseDialog) which needs to do
    // this because enabling it causes a react render loop. Our theory is that there is
    // a focus war going on.
    disableDragging?: boolean;

    //cssForDialogContents?: SerializedStyles;
}

export const BloomDialog: FunctionComponent<IBloomDialogProps> = forwardRef(
    // a named function here instead of a lamda to avoid the "Component definition is missing display name"
    function BloomDialog(props, ref) {
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

        useEffect(() => hookupLinkHandler(), []);

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
            disableDragging: disableDraggingProp,
            onClose,
            onCancel,
            ...propsToPass
        } = props;

        const disableDragging =
            disableDraggingProp !== undefined
                ? disableDraggingProp
                : !!dialogFrameProvidedExternally;

        function hasChildOfType(typeName: string) {
            return React.Children.toArray(props.children).some(c => {
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                return (c as any)?.type?.name === typeName;
            });
        }

        const hasTitle = hasChildOfType("DialogTitle");

        function getPaperComponent() {
            if (disableDragging) {
                return undefined;
            } else {
                return DraggablePaperLimited;
            }
        }
        return (
            <StyledEngineProvider injectFirst>
                <ThemeProvider theme={lightTheme}>
                    {dialogFrameProvidedExternally ? (
                        inner
                    ) : (
                        <BloomDialogContext.Provider
                            value={{
                                onCancel: props.onCancel,
                                disableDragging: disableDragging
                            }}
                        >
                            <Dialog
                                onClose={(
                                    event: object,
                                    reason: "escapeKeyDown" | "backdropClick"
                                ) => {
                                    // MUI.Dialog onClose() is only called if you click outside the dialog or escape, so that's
                                    // the same as canceling for dialogs that have a notion of canceling.
                                    if (props.onCancel) props.onCancel(reason);
                                    else props.onClose(event, reason);
                                }}
                                // maxWidth={false} Instead, if you want more than the default (600px?) then add maxWidth={false} in the props.
                                PaperComponent={getPaperComponent()}
                                aria-labelledby={
                                    "draggable-dialog-" + hasTitle
                                        ? "title"
                                        : "middle"
                                }
                                css={css`
                                    flex-grow: 1; // see note on the display property on PaperComponent
                                    [role="dialog"] {
                                        overflow: hidden; // only the middle should scroll. The DialogTitle and DialogBottomButtons should not.
                                    }
                                    // without this, you can't get the dialog close to the edge
                                    // because there is a huge invisible margin around the dialog
                                    // Note that we want to restrict this to just this top-level paper thing, not *all* off them
                                    .MuiDialog-container > .MuiPaper-root {
                                        margin: 0 !important;
                                    }
                                `}
                                ref={ref}
                                {...propsToPass} // get fullWidth, maxWidth, open etc. Note that css doesn't end up anywhere useful in the HTML (try the paper?)
                            >
                                {inner}
                            </Dialog>
                        </BloomDialogContext.Provider>
                    )}
                </ThemeProvider>
            </StyledEngineProvider>
        );
    }
);

export const DialogTitle: FunctionComponent<{
    backgroundColor?: string;
    color?: string;
    // If this is a string, it is used as the source of an img.
    // Otherwise it is used directly as a component.
    // (Normally, a string would simply work as a ReactNode. React just shows the text.
    // So declaring it separately is redundant. But I wanted to emphasize that string
    // gets special treatment. An earlier version of this class only allowed a string.
    // Now that we take a react component, it is somewhat surprising that a string has
    // the special behavior that used to be the only behavior. So I wanted to make it
    // explicit. I don't think it is likely that someone wants to just show a bit of
    // text as a title icon, and any other way I thought of to enhance this component
    // to show an arbitrary React thing for the icon seemed uglier.)
    icon?: React.ReactNode | string;
    title: string; // note, this is prop instead of just a child so that we can ensure vertical alignment and bar height, which are easy to mess up.
    // true: no close button. otherwise: close button iff BloomDialogContext has onCancel.
    preventCloseButton?: boolean;
}> = props => {
    const color = props.color || "black";
    const background = props.backgroundColor || "transparent";

    const closeText = useL10n("Close", "Common.Close");

    // This is lame, but it's really what looks right to me. When there is a color bar, it looks better to have less padding at the top.
    const titleTopPadding =
        background === "transparent" ? kDialogTopPadding : kDialogPadding;
    let icon = props.icon;
    if (typeof icon === "string") {
        icon = (
            <img
                src={icon}
                alt="Decorative Icon"
                css={css`
                    margin-right: ${kDialogPadding};
                    color: ${color};
                `}
            />
        );
    } else if (icon) {
        icon = (
            <div
                css={css`
                    margin-right: ${kDialogPadding};
                `}
            >
                {icon}
            </div>
        );
    } else {
        icon = false; // React doesn't render anything, even an empty div with some padding.
    }

    const context = React.useContext(BloomDialogContext);
    const cursor = context.disableDragging ? "unset" : "move";
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
            {icon}
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
            {context.onCancel && !props.preventCloseButton && (
                <IconButton
                    aria-label={closeText}
                    title={closeText}
                    css={css`
                        position: relative;
                        margin-left: auto !important;
                        margin-top: auto;
                        margin-bottom: auto;
                        padding: unset;
                        display: flex;
                    `}
                    onClick={() => context.onCancel?.("titleCloseClick")}
                >
                    <CloseIcon />
                </IconButton>
            )}
        </div>
    );
};
DialogTitle.displayName = "DialogTitle";

// The height of this is determined by what is inside of it. If the content might grow (e.g. a progress box), then it's up to the
// client to set maxes or fixed dimensions. See <ProgressDialog> for an example.
export const DialogMiddle: FunctionComponent<{}> = props => {
    return (
        <div
            id="draggable-dialog-middle"
            css={css`
                overflow-y: auto;
                display: flex;
                flex-direction: column;
                flex-grow: 1;
                font-size: 14px;
                // When the dialog doesn't have a title we want some padding above the content.
                &:first-child {
                    margin-top: ${kDialogTopPadding};
                }

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
export const DialogBottomLeftButtons: FunctionComponent<{}> = props => (
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
export const DialogBottomButtons: FunctionComponent<{}> = props => {
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

interface EnhancedPaperProps extends PaperProps {
    handleId: string;
}

// Don't be tempted to make this an anonymous function that returns a JSX.Element
// (instead of a FunctionComponent), it causes focus problems. (BL-11406).
// (Probably the same for the things below that use it.)
const DraggablePaperCore: FunctionComponent<EnhancedPaperProps> = props => {
    const { handleId, ...paperProps } = props;
    return (
        <Draggable
            handle={"#" + handleId}
            cancel={'[class*="MuiDialogContent-root"]'}
            bounds="body" // don't let the dialog be dragged outside the window
        >
            <Paper
                css={css`
                    // Allows setting the Dialog height here on the Paper and
                    // the children can grow into it.
                    display: flex;
                `}
                {...paperProps}
            />
        </Draggable>
    );
};

// We need things that just take PaperProps to pass to the PaperComponent
// property of Dialog. So the above component gets instantiated several ways.

const DraggablePaperLimited: FunctionComponent<PaperProps> = props => {
    return <DraggablePaperCore handleId="draggable-dialog-title" {...props} />;
};

// This used to pass down things to components of the dialog
type BloomDialogContextArgs = {
    onCancel?: (
        reason?:
            | "escapeKeyDown"
            | "backdropClick"
            | "titleCloseClick"
            | "cancelClicked" // an instance of DialogCancelButton was clicked
    ) => void;
    disableDragging: boolean;
};
export const BloomDialogContext = React.createContext<BloomDialogContextArgs>({
    onCancel: undefined,
    disableDragging: true
});
