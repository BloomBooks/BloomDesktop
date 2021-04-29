/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";

import theme from "../../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import { Dialog } from "@material-ui/core";
import CloseOnEscape from "react-close-on-escape";

import { kDialogPadding } from "../../bloomMaterialUITheme";
import { BloomApi } from "../../utils/bloomApi";

export const BloomDialog: React.FunctionComponent<{
    open: boolean;
    // true if the caller is wrapping in a winforms dialog already
    omitOuterFrame: boolean;
}> = props => (
    <CloseOnEscape
        onEscape={() => {
            CloseDialog();
        }}
    >
        <ThemeProvider theme={theme}>
            {props.omitOuterFrame ? (
                <div>{props.children}</div>
            ) : (
                // TODO: handle open/closed
                <Dialog open={props.open}>
                    <div
                        css={css`
                            display: flex;
                            flex-direction: column;
                            padding-left: ${kDialogPadding};
                            padding-right: ${kDialogPadding};
                            padding-bottom: ${kDialogPadding};
                        `}
                    >
                        {props.children}
                    </div>
                </Dialog>
            )}
        </ThemeProvider>
    </CloseOnEscape>
);

/* TODO: this is fine for winforms-hosted dialogs, but not for dialogs sharing a browser */
function CloseDialog() {
    BloomApi.post("common/closeReactDialog");
}

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

export const DialogMiddle: React.FunctionComponent<{}> = props => {
    return (
        <div
            css={css`
                overflow-y: auto;
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
