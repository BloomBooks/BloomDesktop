/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useState } from "react";

import { post, postString, useApiStringState } from "../utils/bloomApi";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";
import BloomButton from "../react_components/bloomButton";
import { Div, P } from "../react_components/l10nComponents";
import { kDialogPadding } from "../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle
} from "../react_components/BloomDialog/BloomDialog";
import {
    DialogCancelButton,
    DialogControlGroup,
    DialogFolderChooserWithApi
} from "../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../react_components/l10nHooks";
import { Checkbox } from "../react_components/checkbox";
import { TextWithEmbeddedLink } from "../react_components/link";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../react_components/BloomDialog/BloomDialogPlumbing";
import { ErrorBox } from "../react_components/boxes";

// Contents of a dialog launched from TeamCollectionSettingsPanel Create Team Collection button.

export const CreateTeamCollectionDialog: React.FunctionComponent<{
    errorForTesting?: string;
    defaultRepoFolder?: string;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const [repoFolderPath, setRepoFolderPath] = useState(
        props.defaultRepoFolder ?? ""
    );
    const [errorMessage, setErrorMessage] = useState<string>(
        props.errorForTesting ?? ""
    );
    // This listener is waiting for results that are sent when the user clicks "Choose Folder"
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
        "TeamCollection.CreateTeamCollection",
        undefined,
        undefined,
        undefined,
        true
    );

    const [collectionName] = useApiStringState(
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
                <TextWithEmbeddedLink
                    l10nKey="TeamCollection.CreateTeamCollection.Intro"
                    temporarilyDisableI18nWarning={true}
                    href="https://docs.bloomlibrary.org/team-collections-sharing-services"
                >
                    Team Collections work by sharing files between team members
                    using Dropbox or a LAN server. Read [this note] about other
                    file sync services.
                </TextWithEmbeddedLink>

                <DialogControlGroup
                    css={css`
                        margin-top: ${kDialogPadding};
                    `}
                >
                    <Div
                        l10nKey="TeamCollection.StorageFolderLabel"
                        temporarilyDisableI18nWarning={true}
                        css={css`
                            margin-top: 1em;
                        `}
                    >
                        LAN or Dropbox Folder:
                    </Div>
                    <DialogFolderChooserWithApi
                        path={repoFolderPath}
                        apiCommandToChooseAndSetFolder="teamCollection/chooseFolderLocation"
                    />
                </DialogControlGroup>
                {!errorMessage && (
                    <DialogControlGroup>
                        <P
                            l10nKey="TeamCollection.ChecklistIntro"
                            temporarilyDisableI18nWarning={true}
                        >
                            Please read and check each of these items:
                        </P>
                        <Checkbox
                            l10nKey="TeamCollection.NameWillWork"
                            l10nParam0={collectionName}
                            onCheckChanged={checkChanged}
                            temporarilyDisableI18nWarning={true}
                            css={css`
                                margin-top: 5px;
                            `}
                        >
                            I think the name, "%0", will be a good one for the
                            whole team, and will not be confused with other
                            collections. I understand that I will not be able to
                            change this name once this becomes a Team
                            Collection.
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
                            Collection. The rest of the team will join it.
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
                            I understand that as the creator of the project, I
                            will be the only person who can change Collection
                            Settings.
                        </Checkbox>
                    </DialogControlGroup>
                )}
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
                        postString(
                            "teamCollection/createTeamCollection",
                            repoFolderPath
                        );
                    }}
                >
                    Create &amp; Restart
                </BloomButton>
                <DialogCancelButton
                    onClick_DEPRECATED={() => post("common/closeReactDialog")}
                />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

WireUpForWinforms(CreateTeamCollectionDialog);
