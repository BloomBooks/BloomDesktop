/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { useState } from "react";

import { BloomApi } from "../utils/bloomApi";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";
import BloomButton from "../react_components/bloomButton";
import { Div, P } from "../react_components/l10nComponents";

import {
    BloomDialog,
    CautionBox,
    DialogBottomButtons,
    DialogCancelButton,
    DialogMiddle,
    DialogTitle,
    ErrorBox,
    IBloomDialogEnvironmentParams,
    useMakeBloomDialog
} from "../react_components/BloomDialog/BloomDialog";
import { useL10n } from "../react_components/l10nHooks";

// Contents of a dialog launched from TeamCollectionSettingsPanel Create Team Collection button.

export let showCreateTeamCollectionDialog: () => void;

export const CreateTeamCollectionDialog: React.FunctionComponent<{
    errorForTesting?: string;
    repoFolderForTesting?: string;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const [repoFolderPath, setRepoFolderPath] = useState(
        props.repoFolderForTesting ?? ""
    );
    const [errorMessage, setErrorMessage] = useState<string>(
        props.errorForTesting ?? ""
    );
    // This listener is waiting for results that are sent when the user clicks "Choose  shared folder"
    // and then selects a folder. We use a listener rather than having the API request return the
    // results to guard against a browser timeout on the request.
    const listener = e => {
        setRepoFolderPath(e.repoFolderPath);
        setErrorMessage(e.problem);
    };
    useSubscribeToWebSocketForEvent(
        "teamCollectionCreate",
        "shared-folder-path",
        listener,
        false
    );

    const dialogTitle = useL10n(
        "Create a Team Collection",
        "TeamCollection.CreateTeamCollection"
    );
    const { showDialog, closeDialog, propsForBloomDialog } = useMakeBloomDialog(
        props.dialogEnvironment
    );
    showCreateTeamCollectionDialog = showDialog;
    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle title={`${dialogTitle}`} />
            <DialogMiddle>
                <P
                    l10nKey="TeamCollection.HowTeamCollectionsWork"
                    temporarilyDisableI18nWarning={true}
                >
                    Team Collections work by using a shared folder from a LAN
                    server, Dropbox, or other cloud provider.
                </P>
                <P
                    l10nKey="TeamCollection.StorageFolderLabel"
                    temporarilyDisableI18nWarning={true}
                >
                    Cloud Storage Folder location (for example, your Dropbox
                    folder):
                </P>
                <div
                    css={css`
                        min-height: 2em;
                        padding: 10px;
                        box-sizing: border-box;
                        background-color: lightgrey;
                        width: 100%;
                    `}
                >
                    {repoFolderPath}
                </div>

                <div
                    css={css`
                        margin-left: auto;
                    `}
                >
                    <BloomButton
                        l10nKey="TeamCollection.ChooseFolder"
                        className="teamCollection-heading"
                        enabled={true}
                        hasText={true}
                        variant="text"
                        temporarilyDisableI18nWarning={true}
                        // This will eventually timeout if the user doesn't choose a folder or cancel
                        // It doesn't matter because we ignore the result and are notified of the folder
                        // through the websocket.
                        onClick={() =>
                            BloomApi.post(
                                "teamCollection/chooseFolderLocation",
                                // nothing to do either on success or failure, including possible timeout,
                                // or the user canceling.
                                () => {},
                                () => {}
                            )
                        }
                    >
                        Choose shared folder
                    </BloomButton>
                </div>

                {errorMessage ? (
                    <ErrorBox>{errorMessage}</ErrorBox>
                ) : (
                    <CautionBox>
                        <Div
                            l10nKey="TeamCollection.MustBeDoneOnce"
                            temporarilyDisableI18nWarning={true}
                        >
                            This can only be done once, by a single member of
                            the team. Bloom will remember that you are the one
                            who created it. When other people join this Team
                            Collection, you will be the only one allowed to
                            modify collection settings.
                        </Div>
                    </CautionBox>
                )}
            </DialogMiddle>
            <DialogBottomButtons>
                <BloomButton
                    id="create-and-restart"
                    l10nKey="TeamCollection.CreateAndRestart"
                    hasText={true}
                    enabled={!!repoFolderPath && !errorMessage}
                    temporarilyDisableI18nWarning={true}
                    onClick={() => {
                        BloomApi.post("teamCollection/createTeamCollection");
                    }}
                >
                    Create &amp; Restart
                </BloomButton>
                <DialogCancelButton onClick={closeDialog} />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
