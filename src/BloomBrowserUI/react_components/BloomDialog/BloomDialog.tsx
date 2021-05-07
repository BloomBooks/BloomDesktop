/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import theme from "../../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import { Card, CardContent, Dialog } from "@material-ui/core";
import CloseOnEscape from "react-close-on-escape";
import { kDialogPadding } from "../../bloomMaterialUITheme";
import { BloomApi } from "../../utils/bloomApi";
import { useState } from "react";
import BloomButton from "../bloomButton";

import InfoIcon from "@material-ui/icons/Info";
// This component provides consistent layout across Bloom Dialogs.
// It can be used either inside of a winforms dialog, or as a MaterialUI Dialog.
// See the accompanying storybook story for usage.
//

const kDialogOuterPadding = "24px"; // per material?

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
                padding-left: ${kDialogOuterPadding};
                padding-right: ${kDialogOuterPadding};
                padding-bottom: ${kDialogOuterPadding};
                // todo: I can't understand why this "- 10px" is needed. This and all its parents have no margin, so I don't understand why it ends up being 10px larger than the available space
                ${props.omitOuterFrame
                    ? `height: 100%; border: solid thin darkgrey; box-sizing: border-box;`
                    : ""}
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

    // This is lame, but it's really what looks right to me. When there is a color bar, it looks better to have less padding at the top.
    const titleTopPadding =
        background === "transparent" ? kDialogOuterPadding : kDialogPadding;
    return (
        <div
            css={css`
                color: ${color};
                background-color: ${background};
                display: flex;

                padding-left: ${kDialogOuterPadding};
                padding-right: ${kDialogOuterPadding};
                padding-top: ${titleTopPadding};
                padding-bottom: ${kDialogPadding};
                margin-left: -${kDialogOuterPadding};
                margin-right: -${kDialogOuterPadding};
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
                        /* margin-left: ${kDialogPadding}; */
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
                p {
                    margin-block-start: 0;
                }
                /* p:first-of-type {
                    margin-block-start: 0;
                    background-color: yellow;
                } */
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
            // margin-left: -6px;   <--- tempting... makes un-outlined material buttons left-align
        `}
    >
        {props.children}
    </div>
);

export const DialogBottomButtons: React.FunctionComponent<{}> = props => {
    return (
        <div
            css={css`
                margin-left: auto;
                margin-top: auto; // push to bottom
                padding-top: ${kDialogPadding}; // leave room between us and the content above us
                display: flex;
                justify-content: flex-end; // make buttons line up on the right, unless wrapped in <DialogBottomLeftButtons>
                      // this is better but Firefox doesn't support it until FF 63:  gap: ${kDialogPadding};

                /* -- button separation -- */
                button{
                margin-left: ${kDialogPadding};
            }

                width: 100%; // needed to left left buttons actually get to the left
            `}
            {...props}
        >
            {props.children}
        </div>
    );
};
export const DialogCloseButton: React.FunctionComponent<{
    onClick: () => void;
    default?: boolean;
}> = props => (
    <BloomButton
        l10nKey="Common.Close"
        hasText={true}
        enabled={true}
        // close button defaults to being the default button
        variant={
            props.default === undefined || props.default === true
                ? "contained"
                : "outlined"
        }
        {...props}
    >
        Close
    </BloomButton>
);

export const DialogCancelButton: React.FunctionComponent<{
    onClick: () => void;
    default?: boolean;
}> = props => (
    <BloomButton
        l10nKey="Common.Cancel"
        hasText={true}
        enabled={true}
        // cancel button defaults to being the not-default default
        variant={
            !props.default || props.default === true ? "outlined" : "contained"
        }
        onClick={props.onClick}
    >
        Cancel
    </BloomButton>
);

export const NoteBox: React.FunctionComponent<{}> = props => (
    <Card style={{ backgroundColor: "#E5F9F0" }}>
        <CardContent
            css={css`
                display: flex;
            `}
        >
            <InfoIcon
                color="primary"
                css={css`
                    margin-right: ${kDialogPadding};
                `}
            />
            {props.children}
        </CardContent>
    </Card>
);

export function useMakeBloomDialog(omitOuterFrame?: boolean) {
    const [currentlyOpen, setOpen] = useState(true);
    function showDialog() {
        setOpen(true);
    }
    function closeDialog() {
        if (omitOuterFrame) BloomApi.post("common/closeReactDialog");
        else setOpen(false);
    }
    return {
        showDialog,
        closeDialog,
        propsForBloomDialog: {
            open: currentlyOpen,
            onClose: closeDialog,
            omitOuterFrame
        }
    };
}
