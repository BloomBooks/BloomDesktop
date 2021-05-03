/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import theme from "../../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import { Dialog } from "@material-ui/core";
import CloseOnEscape from "react-close-on-escape";
import { kDialogPadding } from "../../bloomMaterialUITheme";
import { BloomApi } from "../../utils/bloomApi";

// This component provides consistent layout across Bloom Dialogs.
// It can be used either inside of a winforms dialog, or as a MaterialUI Dialog.
// Simplest usage:
//               <BloomDialog open={true}>
//                   <DialogTitle title="hello world"/>
//                   <DialogMiddle>
//                      stuff
//                   </DialogMiddle>
//                   <DialogBottom><CloseButton/></DialogBottom>
//               </BloomDialog>
//
export const BloomDialog: React.FunctionComponent<{
    open: boolean;
    // true if the caller is wrapping in a winforms dialog already
    omitOuterFrame?: boolean;
    onClose: () => void;
}> = props => {
    const inner = (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                padding-left: ${kDialogPadding};
                padding-right: ${kDialogPadding};
                padding-bottom: ${kDialogPadding};
                // todo: I can't understand why this "- 10px" is needed. This and all its parents have no margin, so I don't understand why it ends up being 10px larger than the available space
                ${props.omitOuterFrame ? "height: calc(100% - 10px)" : ""}
            `}
        >
            {props.children}
        </div>
    );

    return (
        <CloseOnEscape
            onEscape={() => {
                props.onClose();
            }}
        >
            <ThemeProvider theme={theme}>
                {props.omitOuterFrame ? (
                    inner
                ) : (
                    <Dialog open={props.open}>{inner}</Dialog>
                )}
            </ThemeProvider>
        </CloseOnEscape>
    );
};

export const DialogTitle: React.FunctionComponent<{
    backgroundColor?: string;
    color?: string;
    icon?: string;
    title: string; // note, this is prop instead of just a child so that we can ensure vertical alignment and bar height, which are easy to mess up.
}> = props => {
    const color = props.color || "black";
    const background = props.backgroundColor || "transparent";

    return (
        <div
            css={css`
                color: ${color};
                background-color: ${background};
                display: flex;
                padding: ${kDialogPadding};
                margin-left: -${kDialogPadding};
                margin-right: -${kDialogPadding};
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
                        margin-left: ${kDialogPadding};
                        color: ${color};
                    `}
                />
            )}
            <h1
                css={css`
                    margin-top: auto;
                    margin-bottom: auto;
                    margin-left: ${kDialogPadding};
                `}
            >
                {props.title}
            </h1>
            {/* Example child would be a Spinner in a progress dialog*/}
            {props.children}
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
            `}
            {...props}
        >
            {props.children}
        </div>
    );
};

export const DialogBottom: React.FunctionComponent<{}> = props => {
    return (
        <div
            css={css`
                margin-top: auto; // push to bottom
                padding-top: ${kDialogPadding}; // leave room between us and the content above us
            `}
            {...props}
        >
            {props.children}
        </div>
    );
};
