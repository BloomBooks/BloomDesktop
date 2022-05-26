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
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle
} from "../react_components/BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../react_components/BloomDialog/BloomDialogPlumbing";
import HtmlHelpLink from "../react_components/htmlHelpLink";

export interface IMessageBoxButton {
    text: string;
    id: string;
    default: boolean; // Only one button should have this true
    style: "contained" | "text" | "outlined";
}

// Designed to be a partial replacement for a WinForms messageBox, both from C# and Typescript (eventually...needs work).
// More flexible in that buttons can be fully configured, and uses our MaterialUI dialog look and feel.
export const BloomMessageBox: React.FunctionComponent<{
    messageHtml: string; // The localized message to notify the user about. Can contain HTML.
    rightButtonDefinitions: IMessageBoxButton[];
    helpButtonUrl?: string; // If defined, creates a single "Learn More" button on the left side.
    icon?: "warning" | undefined; // Effectively an enumeration, which we will add to as needed
    dialogEnvironment?: IBloomDialogEnvironmentParams;

    // If defined (and true) the request came from C# and the response is via an api call.
    closeWithAPICall?: boolean;
    // When called from Typescript, provide the callback below to invoke when a button is
    // clicked?
    buttonClicked?: (buttonId: string) => void;
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    const closeDialogForButton = buttonId => {
        if (props.closeWithAPICall) {
            BloomApi.postString("common/closeReactDialog", buttonId);
        } else {
            closeDialog();
            if (props.buttonClicked) {
                props.buttonClicked(buttonId);
            }
        }
    };

    const rightButtons = props.rightButtonDefinitions.map(button => (
        <BloomButton
            className={button.default ? "initialFocus" : ""}
            key={button.id}
            enabled={true}
            l10nKey=""
            alreadyLocalized={true}
            hasText={true}
            variant={button.style}
            onClick={() => closeDialogForButton(button.id)}
            // I have nothing against ripple, but when a default button shows with a ripple even though
            // you're not clicking or even pointing at it... it looks dumb.
            disableRipple
        >
            {button.text}
        </BloomButton>
    ));

    const helpButton = props.helpButtonUrl ? (
        <DialogBottomLeftButtons>
            <HtmlHelpLink
                l10nKey="Common.LearnMore"
                fileid={props.helpButtonUrl}
            >
                Learn More
            </HtmlHelpLink>
        </DialogBottomLeftButtons>
    ) : (
        undefined
    );

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
            <DialogBottomButtons>
                {props.helpButtonUrl && helpButton}
                {rightButtons}
            </DialogBottomButtons>
        </BloomDialog>
    );
};

WireUpForWinforms(BloomMessageBox);
