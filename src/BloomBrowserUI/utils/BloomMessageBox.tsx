/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import BloomButton from "../react_components/bloomButton";
import { BloomApi } from "./bloomApi";
import WarningOutlinedIcon from "@material-ui/icons/WarningOutlined";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle
} from "../react_components/BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../react_components/BloomDialog/BloomDialogPlumbing";

export interface MessageBoxButton {
    text: string;
    id: string;
    default: boolean; // Only one button should have this true
}

// Designed to be a partial replacement for a WinForms messageBox, both from C# and Typescript (eventually...needs work).
// More flexible in that buttons can be fully configured, and uses our MaterialUI dialog look and feel.
export const BloomMessageBox: React.FunctionComponent<{
    messageHtml: string; // The localized message to notify the user about. Can contain HTML.
    rightButtons: MessageBoxButton[];
    icon?: "warning" | undefined; // Effectively an enumeration, which we will add to as needed
    dialogEnvironment?: IBloomDialogEnvironmentParams;
    // For use from Typescript, provide a callback to invoke when a button is clicked?
    // Probably also need a way to control whether it is open.
    // And maybe turn off the BloomApi behavior.
    // callback? : (messageId: string, buttonId:string) => void
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    const closeDialogForButton = buttonId => {
        // Enhance: do something else if called from Typescript. Close the dialog and somehow
        // report what was clicked.
        BloomApi.postString("common/closeReactDialog", buttonId);
    };
    const rightButtons = props.rightButtons.map(button => (
        <BloomButton
            className={button.default ? "initialFocus" : ""}
            key={button.id}
            enabled={true}
            l10nKey=""
            alreadyLocalized={true}
            hasText={true}
            variant={button.default ? "contained" : "outlined"}
            onClick={() => closeDialogForButton(button.id)}
            disableRipple // I have nothing against ripple, but when a default buttons shows with a ripple even though your'e not clicking or even pointing at it... it looks dumb.
        >
            {button.text}
        </BloomButton>
    ));

    return (
        <BloomDialog {...propsForBloomDialog}>
            {/* We need a element title to make things space correctly because BloomDialog expects to have one, even though at the moment we aren't including a title */}
            <DialogTitle title={""}></DialogTitle>
            <DialogMiddle>
                <div
                    id="root"
                    css={css`
                        display: flex;
                    `}
                >
                    {/* InnerHTML is used so that we can insert markup like <br> into the message. */}

                    {props.icon === "warning" && (
                        <WarningOutlinedIcon
                            css={css`
                                font-size: 3rem !important;
                                color: orange;
                            `}
                        />
                    )}
                    <div
                        css={css`
                            -moz-user-select: text; // Firefox before v69
                            user-select: text;
                            margin-left: 20px;
                            padding-top: 7px;
                        `}
                        dangerouslySetInnerHTML={{
                            __html: props.messageHtml || ""
                        }}
                    />
                </div>
            </DialogMiddle>
            <DialogBottomButtons>{rightButtons}</DialogBottomButtons>
        </BloomDialog>
    );
};

WireUpForWinforms(BloomMessageBox);
