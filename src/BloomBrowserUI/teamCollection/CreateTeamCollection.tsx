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
    useSetupBloomDialog
} from "../react_components/BloomDialog/BloomDialog";
import { useL10n } from "../react_components/l10nHooks";
import { Checkbox } from "../react_components/checkbox";

// Contents of a dialog launched from TeamCollectionSettingsPanel Create Team Collection button.

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

    const [collectionName] = BloomApi.useApiString(
        "teamCollection/getCollectionName",
        ""
    );
    const [boxesChecked, setBoxesChecked] = useState(0);
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    const checkChanged = (newVal: boolean) => {
        setBoxesChecked(oldCount => (newVal ? oldCount + 1 : oldCount - 1));
    };
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
                <Checkbox
                    l10nKey="TeamCollection.NameWillWork"
                    l10nParam0={collectionName}
                    onCheckChanged={checkChanged}
                    temporarilyDisableI18nWarning={true}
                    css={css`
                        margin-top: 5px;
                    `}
                >
                    The name, "%0", will be a good one for the whole team, and
                    will not be confused with other collections. I understand
                    that I will not be able to change this name, later.
                </Checkbox>
                <Checkbox
                    l10nKey="TeamCollection.OnlyOneCreator"
                    onCheckChanged={checkChanged}
                    temporarilyDisableI18nWarning={true}
                    css={css`
                        margin-top: 20px;
                    `}
                >
                    I am the only person on the team creating this Team
                    Collection. The rest of the team will join this collection.
                </Checkbox>
                <Checkbox
                    l10nKey="TeamCollection.OnlyCreatorCanChangeSettings"
                    onCheckChanged={checkChanged}
                    temporarilyDisableI18nWarning={true}
                    css={css`
                        margin-top: 20px;
                        margin-bottom: 15px;
                    `}
                >
                    I understand that as the creator of the project, I will be
                    the only person who can change Collection Settings.
                </Checkbox>
                {errorMessage && <ErrorBox>{errorMessage}</ErrorBox>}
            </DialogMiddle>
            <DialogBottomButtons>
                <BloomButton
                    id="create-and-restart"
                    l10nKey="TeamCollection.CreateAndRestart"
                    hasText={true}
                    enabled={
                        !!repoFolderPath && !errorMessage && boxesChecked == 3
                    }
                    temporarilyDisableI18nWarning={true}
                    onClick={() => {
                        BloomApi.post("teamCollection/createTeamCollection");
                    }}
                >
                    Create &amp; Restart
                </BloomButton>
                <DialogCancelButton
                    onClick={() => BloomApi.post("common/closeReactDialog")}
                />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
