import { css } from "@emotion/react";

import * as React from "react";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import BloomButton from "../react_components/bloomButton";
import { postString } from "./bloomApi";
import WarningOutlinedIcon from "@mui/icons-material/WarningOutlined";
import InfoIcon from "@mui/icons-material/Info";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle,
} from "../react_components/BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog,
} from "../react_components/BloomDialog/BloomDialogPlumbing";
import HtmlHelpLink from "../react_components/htmlHelpLink";
import { kBloomBlue } from "../bloomMaterialUITheme";

export interface IMessageBoxButton {
    text: string;
    id: string;
    default: boolean; // Only one button should have this true
}

// This function is only used from Typescript-land to give us control over repeated opening and closing.
export let showBloomMessageBox: () => void = () => {
    window.alert("showBloomMessageBox is not set up yet.");
};

// Designed to be a partial replacement for a WinForms messageBox, both from C# and Typescript (eventually...needs work).
// More flexible in that buttons can be fully configured, and uses our MaterialUI dialog look and feel.
export const BloomMessageBox: React.FunctionComponent<{
    messageHtml: string; // The localized message to notify the user about. Can contain HTML.
    rightButtonDefinitions: IMessageBoxButton[];

    // If defined, creates a single "Learn More" button on the left side.
    // This is intended to look for a file whose source is "BloomBrowserUI/help/{helpButtonFileId}-en.md".
    helpButtonFileId?: string;
    // Effectively an enumeration, which we will add to as needed
    // Interestingly enough, (MessageBoxIcon.Information).toString() in C# comes out as "Asterisk"!
    icon?: "warning" | "asterisk" | undefined;
    dialogEnvironment?: IBloomDialogEnvironmentParams;

    // If defined (and true) the request came from C# and the response is via an api call.
    closeWithAPICall?: boolean;
    // When called from Typescript, we could provide a callback like this, but we haven't needed it yet.
    //buttonClicked?: (buttonId: string) => void;
}> = (props) => {
    const { showDialog, closeDialog, propsForBloomDialog } =
        useSetupBloomDialog(props.dialogEnvironment);
    showBloomMessageBox = showDialog;

    const closeDialogForButton = (buttonId) => {
        if (props.closeWithAPICall) {
            postString("common/closeReactDialog", buttonId);
        } else {
            closeDialog();
            // if (props.buttonClicked) {
            //     props.buttonClicked(buttonId);
            // }
        }
    };

    const rightButtons = (props.rightButtonDefinitions ?? []).map((button) => (
        <BloomButton
            className={button.default ? "initialFocus" : ""}
            key={button.id}
            enabled={true}
            l10nKey=""
            alreadyLocalized={true}
            hasText={true}
            variant={button.default ? "contained" : "outlined"}
            onClick={() => closeDialogForButton(button.id)}
            // I have nothing against ripple, but when a default button shows with a ripple even though
            // you're not clicking or even pointing at it... it looks dumb.
            disableRipple
        >
            {button.text}
        </BloomButton>
    ));

    const helpButton = props.helpButtonFileId ? (
        <DialogBottomLeftButtons>
            <HtmlHelpLink
                l10nKey="Common.LearnMore"
                fileid={props.helpButtonFileId}
            >
                Learn More
            </HtmlHelpLink>
        </DialogBottomLeftButtons>
    ) : undefined;

    const icon = (): JSX.Element | undefined => {
        switch (props.icon) {
            case "warning":
                return (
                    <WarningOutlinedIcon
                        css={css`
                            font-size: 3rem !important;
                            color: orange;
                        `}
                    />
                );
            case "asterisk":
                return (
                    <InfoIcon
                        css={css`
                            font-size: 3rem !important;
                            color: ${kBloomBlue};
                        `}
                    />
                );
            default:
                return undefined;
        }
    };

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
                    {icon()}
                    {/* InnerHTML is used so that we can insert markup like <br> into the message. */}
                    <div
                        css={css`
                            -moz-user-select: text; // Firefox before v69
                            user-select: text;
                            margin-left: 20px;
                            padding-top: 7px;
                        `}
                        dangerouslySetInnerHTML={{
                            __html: props.messageHtml || "",
                        }}
                    />
                </div>
            </DialogMiddle>
            <DialogBottomButtons>
                {props.helpButtonFileId && helpButton}
                {rightButtons}
            </DialogBottomButtons>
        </BloomDialog>
    );
};

WireUpForWinforms(BloomMessageBox);
