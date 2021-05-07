/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { useState } from "react";

import { BloomApi } from "../utils/bloomApi";
import WebSocketManager, {
    useSubscribeToWebSocketForEvent
} from "../utils/WebSocketManager";
import BloomButton from "../react_components/bloomButton";
import { Div } from "../react_components/l10nComponents";

import {
    BloomDialog,
    CautionBox,
    DialogBottomButtons,
    DialogCancelButton,
    DialogMiddle,
    DialogTitle,
    useMakeBloomDialog
} from "../react_components/BloomDialog/BloomDialog";
import { useL10n } from "../react_components/l10nHooks";

const kBloomBlue = "#1d94a4";

// Contents of a dialog launched from TeamCollectionSettingsPanel Creat Team Collection button.

// interface IProps {
//     closeDlg: () => void;
// }

export let showCreateTeamCollectionDialog: () => void;

export const CreateTeamCollectionDialog: React.FunctionComponent = props => {
    const [repoFolderPath, setRepoFolderPath] = useState("");
    const [problemReport, setProblemReport] = useState("");
    // This listener is waiting for results that are sent when the user clicks "Choose  shared folder"
    // and then selects a folder. We use a listener rather than having the API request return the
    // results to guard against a browser timeout on the request.
    const listener = e => {
        setRepoFolderPath(e.repoFolderPath);
        setProblemReport(e.problem);
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
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useMakeBloomDialog();
    showCreateTeamCollectionDialog = showDialog;
    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle title={`${dialogTitle}`} />
            <DialogMiddle>
                <Div
                    l10nKey="TeamCollection.HowTeamCollectionsWork"
                    temporarilyDisableI18nWarning={true}
                >
                    Team Collections work by using a shared folder from a LAN
                    server, Dropbox, or other cloud provider.
                </Div>
                <Div
                    l10nKey="TeamCollection.StorageFolderLabel"
                    temporarilyDisableI18nWarning={true}
                >
                    Cloud Storage Folder location (for example, your Dropbox
                    folder):
                </Div>
                <div>{repoFolderPath}</div>

                <div className="align-right">
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

                {problemReport ? (
                    <div
                        css={css`
                            color: red;
                        `}
                    >
                        {problemReport}
                    </div>
                ) : (
                    <CautionBox>
                        <Div
                            l10nKey="TeamCollection.MustBeDoneOnce"
                            temporarilyDisableI18nWarning={true}
                        >
                            This must only be done by one person in your team.
                            If instead you want to Join a Team Collection that
                            someone else has made, click "Cancel".
                        </Div>
                    </CautionBox>
                )}
            </DialogMiddle>
            <DialogBottomButtons>
                <BloomButton
                    id="create-and-restart"
                    l10nKey="TeamCollection.CreateAndRestart"
                    hasText={true}
                    enabled={!!repoFolderPath && !problemReport}
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
