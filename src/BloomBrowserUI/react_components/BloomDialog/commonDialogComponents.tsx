/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { kDialogPadding } from "../../bloomMaterialUITheme";
import { post, postJson } from "../../utils/bloomApi";
import BloomButton from "../bloomButton";

import { useSubscribeToWebSocketForObject } from "../../utils/WebSocketManager";
import { BloomDialogContext } from "./BloomDialog";

export const kErrorBoxColor = "#eb3941";

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

// This is extracted from DialogFolderChooser because CreateTeamCollection has a special api endpoint for it
export const DialogFolderChooserWithApi: React.FunctionComponent<{
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
                    post(
                        props.apiCommandToChooseAndSetFolder
                        // nothing to do either on success or failure, including possible timeout,
                        // or the user canceling. This is because the "result" comes back to browser-land
                        // via a websocket that sets the new result. This approach is needed because otherwise
                        // the browser would time out while waiting for the user to finish using the system folder-choosing dialog.
                    )
                }
            >
                Choose Folder
            </BloomButton>
        </div>
    </div>
);

export const DialogFolderChooser: React.FunctionComponent<{
    path: string;
    setPath: (path: string) => void;
    description?: string;
    forOutput?: boolean;
}> = props => {
    // Since a user will have as much time as they want to deal with the dialog,
    // we can't just wait for the api call to return. Instead we get called back
    // via web socket iff they select a folder and close the dialog.
    useSubscribeToWebSocketForObject<{ success: boolean; path: string }>(
        "fileIO",
        "chooseFolder-results",
        results => {
            if (results.success) {
                props.setPath(results.path);
            }
        }
    );
    const params = new URLSearchParams({
        path: props.path,
        description: props.description || "",
        forOutput: props.forOutput ? "true" : "false"
    }).toString();
    return (
        <DialogFolderChooserWithApi
            {...props}
            apiCommandToChooseAndSetFolder={"fileIO/chooseFolder?" + params}
        />
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

export const DialogOkButton: React.FunctionComponent<{
    onClick: () => void;
    enabled?: boolean;
    default?: boolean;
}> = props => (
    <BloomButton
        l10nKey="Common.OK"
        hasText={true}
        enabled={props.enabled === undefined ? true : props.enabled}
        variant={props.default === true ? "contained" : "outlined"}
        onClick={props.onClick}
    >
        OK
    </BloomButton>
);

export const DialogCancelButton: React.FunctionComponent<{
    // Use of onClick is deprecated. Instead, use the onCancel function on BloomDialog, so that
    // we get consistent behavior with Escape, upper-right close button, etc.
    // Note that if the context onCancel is set, onClick_DEPRECATED will not be called,
    // even if one is passed.
    onClick_DEPRECATED?: () => void;
    default?: boolean;
}> = props => {
    const context = React.useContext(BloomDialogContext);
    return (
        <BloomButton
            l10nKey="Common.Cancel"
            hasText={true}
            enabled={true}
            // by default, Cancel is NOT the default button
            variant={props.default === true ? "contained" : "outlined"}
            onClick={() => {
                if (context.onCancel) {
                    context.onCancel("cancelClicked");
                } else {
                    props.onClick_DEPRECATED?.();
                }
            }}
        >
            Cancel
        </BloomButton>
    );
};
export const DialogReportButton: React.FunctionComponent<{
    className?: string; // also supports Emotion CSS
    buttonText?: string; // defaults to 'Report'
    l10nKey?: string; // MUST replace this if you change buttonText
    temporarilyDisableI18nWarning?: boolean; // may use this if the passed L10nKey is temporarily disabled
    shortMessage: string;
    messageGenerator: () => string;
}> = props => (
    <BloomButton
        className={props.className}
        l10nKey={props.l10nKey ?? "ErrorReport.Report"}
        hasText={true}
        enabled={true}
        variant="text"
        temporarilyDisableI18nWarning={props.temporarilyDisableI18nWarning}
        onClick={() =>
            postJson("problemReport/showDialog", {
                shortMessage: props.shortMessage,
                message: "DialogReportButton: " + props.messageGenerator()
            })
        }
        css={css`
            span {
                color: ${kErrorBoxColor};
            }
        `}
    >
        {props.buttonText ?? "Report"}
    </BloomButton>
);
