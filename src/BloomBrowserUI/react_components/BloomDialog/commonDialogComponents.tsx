/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { kDialogPadding, kBloomBlue } from "../../bloomMaterialUITheme";
import { BloomApi } from "../../utils/bloomApi";
import BloomButton from "../bloomButton";

import InfoIcon from "@material-ui/icons/Info";
import WarningIcon from "@material-ui/icons/Warning";
import ErrorIcon from "@material-ui/icons/Error";

export const kErrorBoxColor = "#eb3941";
const kWarningBoxColor = "#FEBF00"; // isn't exactly bloom gold. This looks more orangy, intentionally.
const kTextOverWarningColor = "#000000CC"; // black with 20% transparency
const kLightBlueBackground = "#e5f9f0";
const kBorderRadiusForSpecialBlocks = "3px";

// just puts a rounded rectangle around the children
export const DialogControlGroup: React.FunctionComponent<{}> = props => (
    <div
        css={css`
            border: solid 1px grey;
            border-radius: 4px; // 4 is the radius used by material buttons
            padding: ${kDialogPadding};
            margin-bottom: ${kDialogPadding};
            & > *:first-child {
                margin-top: 0; // don't add extra space, we already have our padding
            }
            & > *:last-child {
                margin-bottom: 0; // don't add extra space, we already have our padding
            }
        `}
        {...props} // allows defining more css rules from container
    >
        {props.children}
    </div>
);

export const DialogFolderChooser: React.FunctionComponent<{
    path: string;
    apiCommandToChooseAndSetFolder: string;
}> = props => (
    <div>
        <div
            css={css`
                min-height: 2em;
                padding: 10px;
                box-sizing: border-box;
                background-color: #f0f0f0;
                width: 100%;
            `}
        >
            {props.path}
        </div>

        <div
            css={css`
                // all these rules to place the button on the right
                display: flex;
                width: 100%;
                align-items: flex-end;
                flex-direction: column;
            `}
        >
            <BloomButton
                l10nKey="Common.ChooseFolder"
                temporarilyDisableI18nWarning={true}
                enabled={true}
                hasText={true}
                variant="text"
                onClick={() =>
                    BloomApi.post(
                        props.apiCommandToChooseAndSetFolder,
                        // nothing to do either on success or failure, including possible timeout,
                        // or the user canceling. This is because the "result" comes back to browser-land
                        // via a websocket that sets the new result. This approach is needed because otherwise
                        // the browser would time out while waiting for the user to finish using the system folder-choosing dialog.
                        () => {},
                        () => {}
                    )
                }
            >
                Choose Folder
            </BloomButton>
        </div>
    </div>
);

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
export const DialogReportButton: React.FunctionComponent<{
    className?: string; // also supports Emotion CSS
    shortMessage: string;
    messageGenerator: () => string;
}> = props => (
    <BloomButton
        className={props.className}
        l10nKey="ErrorReport.Report"
        hasText={true}
        enabled={true}
        variant="text"
        onClick={() =>
            BloomApi.postJson("problemReport/showDialog", {
                shortMessage: props.shortMessage,
                message: props.messageGenerator()
            })
        }
        css={css`
            span {
                color: ${kErrorBoxColor};
            }
        `}
    >
        Report
    </BloomButton>
);

export const NoteBox: React.FunctionComponent<{}> = props => (
    // using <p> so that we get normal spacing when we intermix prose with the warning
    <p
        css={css`
            display: flex;
            background-color: ${kLightBlueBackground};
            border-radius: ${kBorderRadiusForSpecialBlocks};
            padding: ${kDialogPadding};
            color: ${kBloomBlue};
            font-weight: 500;
        `}
        {...props} // allows defining more css rules from container
    >
        <InfoIcon
            color="primary"
            css={css`
                margin-right: ${kDialogPadding};
            `}
        />
        {props.children}
    </p>
);
export const WarningBox: React.FunctionComponent<{}> = props => (
    // using <p> so that we get normal spacing when we intermix prose with the warning
    <p
        css={css`
            display: flex;
            &,
            * {
                color: ${kTextOverWarningColor} !important;
            }
            background-color: ${kWarningBoxColor};
            border-radius: ${kBorderRadiusForSpecialBlocks};
            padding: ${kDialogPadding};
            font-weight: 500;
        `}
        {...props} // allows defining more css rules from container
    >
        <WarningIcon
            css={css`
                margin-right: ${kDialogPadding};
                color: ${kTextOverWarningColor};
            `}
        />
        {props.children}
    </p>
);

export const ErrorBox: React.FunctionComponent<{}> = props => (
    // using <p> so that we get normal spacing when we intermix prose with the warning
    <p
        css={css`
            display: flex;
            background-color: ${kErrorBoxColor};
            border-radius: ${kBorderRadiusForSpecialBlocks};
            padding: ${kDialogPadding};
            &,
            * {
                color: white;
            }
        `}
        {...props} // allows defining more css rules from container
    >
        <ErrorIcon
            css={css`
                margin-right: ${kDialogPadding};
            `}
        />
        {props.children}
    </p>
);
