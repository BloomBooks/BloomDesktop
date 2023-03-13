/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import {
    kDialogPadding,
    kBloomBlue,
    kBorderRadiusForSpecialBlocks,
    kBloomBlue50Transparent
} from "../../bloomMaterialUITheme";
import { post, postJson } from "../../utils/bloomApi";
import BloomButton from "../bloomButton";

import InfoIcon from "@mui/icons-material/Info";
import WarningIcon from "@mui/icons-material/Warning";
import ErrorIcon from "@mui/icons-material/Error";
import WaitIcon from "@mui/icons-material/HourglassEmpty";
import { useSubscribeToWebSocketForObject } from "../../utils/WebSocketManager";
import {
    kBloomDarkTextOverWarning,
    kBloomWarning
} from "../../utils/colorUtils";
import { BloomDialogContext } from "./BloomDialog";

export const kErrorBoxColor = "#eb3941";
const kLightBlueBackground = "#F0FDFE";

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
        "common",
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
            apiCommandToChooseAndSetFolder={"common/chooseFolder?" + params}
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
            onClick={context.onCancel ?? props.onClick_DEPRECATED}
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
                message: props.messageGenerator()
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

export const BoxWithIconAndText: React.FunctionComponent<{
    hasBorder?: boolean;
    color?: string;
    borderColor?: string;
    backgroundColor?: string;
    icon?: JSX.Element;
}> = props => {
    let border = css``;
    if (props.hasBorder) {
        border = css`
            border: solid 1px ${props.borderColor || kBloomBlue50Transparent};
        `;
    }
    const {
        hasBorder,
        color,
        borderColor,
        backgroundColor,
        icon,
        ...propsToPass
    } = props;
    const cssForIcon = css`
        margin-right: ${kDialogPadding};
    `;
    // React's cloneElement doesn't work with Emotion's css prop, so we have to do this.
    // See https://github.com/emotion-js/emotion/issues/1102.
    const cloneElement = (element, props) =>
        jsx(element.type, {
            key: element.key,
            ref: element.ref,
            ...element.props,
            ...props
        });
    return (
        <div
            css={css`
                display: flex;
                background-color: ${props.backgroundColor ||
                    kLightBlueBackground};
                border-radius: ${kBorderRadiusForSpecialBlocks};
                padding: ${kDialogPadding};
                color: ${props.color || kBloomBlue};
                // The original version of this used p instead of div to get this spacing below.
                // But we want div so we have more flexibility with adding children.
                margin-block-end: 1em;
                ${border};
                a {
                    color: ${props.color || kBloomBlue};
                }
            `}
            {...propsToPass} // allows defining more css rules from container
        >
            {props.icon ? (
                cloneElement(props.icon, { css: cssForIcon })
            ) : (
                <InfoIcon color="primary" css={cssForIcon} />
            )}
            {props.children}
        </div>
    );
};

export const NoteBoxSansBorder: React.FunctionComponent<{}> = props => {
    return <BoxWithIconAndText {...props}>{props.children}</BoxWithIconAndText>;
};

export const NoteBox: React.FunctionComponent<{}> = props => {
    return (
        <BoxWithIconAndText hasBorder={true} {...props}>
            {props.children}
        </BoxWithIconAndText>
    );
};

export const WaitBox: React.FunctionComponent<{}> = props => {
    return (
        <BoxWithIconAndText
            hasBorder={true}
            color="#629E16"
            borderColor="#629E16"
            backgroundColor="#F2FCE4"
            icon={<WaitIcon />}
            {...props}
        >
            {props.children}
        </BoxWithIconAndText>
    );
};

export const WarningBox: React.FunctionComponent<{}> = props => {
    return (
        <BoxWithIconAndText
            color={kBloomDarkTextOverWarning}
            backgroundColor={kBloomWarning}
            icon={<WarningIcon />}
            css={css`
                font-weight: 500;
            `}
            {...props}
        >
            {props.children}
        </BoxWithIconAndText>
    );
};

export const ErrorBox: React.FunctionComponent<{}> = props => {
    return (
        <BoxWithIconAndText
            color="white"
            backgroundColor={kErrorBoxColor}
            icon={<ErrorIcon />}
            {...props}
        >
            {props.children}
        </BoxWithIconAndText>
    );
};
