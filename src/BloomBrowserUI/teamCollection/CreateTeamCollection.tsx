import { css } from "@emotion/react";

import * as React from "react";
import { useEffect, useState } from "react";

import { get, post, postString, useApiStringState } from "../utils/bloomApi";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";
import BloomButton from "../react_components/bloomButton";
import { Div, P, Span } from "../react_components/l10nComponents";
import { kDialogPadding } from "../bloomMaterialUITheme";
import LinearProgress from "@mui/material/LinearProgress";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
} from "../react_components/BloomDialog/BloomDialog";
import {
    DialogCancelButton,
    DialogControlGroup,
    DialogFolderChooserWithApi,
} from "../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../react_components/l10nHooks";
import { Checkbox } from "../react_components/checkbox";
import { AttentionTextField } from "../react_components/AttentionTextField";
import { TextWithEmbeddedLink } from "../react_components/link";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog,
} from "../react_components/BloomDialog/BloomDialogPlumbing";
import { ErrorBox } from "../react_components/boxes";
import { showRegistrationDialog } from "../react_components/registration/registrationDialog";
import { isValidEmail } from "../utils/emailUtils";
import {
    ISharingLoginState,
    createCloudTeamCollection,
    signIn as sharingSignIn,
    useSharingLoginState,
} from "./sharingApi";
import { SignInDialog } from "./SignInDialog";

// Contents of a dialog launched from TeamCollectionSettingsPanel Create Team Collection button.

export const CreateTeamCollectionDialog: React.FunctionComponent<{
    errorForTesting?: string;
    defaultRepoFolder?: string;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = (props) => {
    const [repoFolderPath, setRepoFolderPath] = useState(
        props.defaultRepoFolder ?? "",
    );
    const [errorMessage, setErrorMessage] = useState<string>(
        props.errorForTesting ?? "",
    );
    // This listener is waiting for results that are sent when the user clicks "Choose Folder"
    // and then selects a folder. We use a listener rather than having the API request return the
    // results to guard against a browser timeout on the request.
    const listener = (e) => {
        setRepoFolderPath(e.repoFolderPath);
        setErrorMessage(e.problem);
    };
    useSubscribeToWebSocketForEvent(
        "teamCollectionCreate",
        "shared-folder-path",
        listener,
        false,
    );

    const dialogTitle = useL10n(
        "Create a Team Collection",
        "TeamCollection.CreateTeamCollection",
        undefined,
        undefined,
        undefined,
        true,
    );

    const [collectionName] = useApiStringState(
        "teamCollection/getCollectionName",
        "",
    );
    const [boxesChecked, setBoxesChecked] = useState(0);
    const { showDialog, closeDialog, propsForBloomDialog } =
        useSetupBloomDialog(props.dialogEnvironment);

    const checkChanged = (newVal: boolean) => {
        setBoxesChecked((oldCount) => (newVal ? oldCount + 1 : oldCount - 1));
    };

    const [emailExists, setEmailExists] = useState(false);
    useEffect(() => {
        get("registration/userInfo", (userInfo) => {
            if (userInfo?.data) {
                setEmailExists(userInfo.data.email ? true : false);
            }
        });
    }, []);

    function create() {
        postString("teamCollection/createTeamCollection", repoFolderPath);
    }

    function tryToCreate() {
        if (emailExists) create();
        else
            showRegistrationDialog({
                emailRequiredForTeamCollection: true,
                onSave: (hasValidEmail: boolean) => {
                    if (hasValidEmail) create();
                },
            });
    }

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
                            `}
                        >
                            I understand that as the creator of the project, I
                            will be the only person who can change Collection
                            Settings.
                        </Checkbox>
                        <Checkbox
                            l10nKey="TeamCollection.DropboxSettingsAcknowledgement"
                            onCheckChanged={checkChanged}
                            temporarilyDisableI18nWarning={true}
                            css={css`
                                margin-top: 20px;
                                margin-bottom: 15px;
                            `}
                        >
                            I will ensure that all team members have properly
                            configured [Critical Dropbox
                            Settings](https://docs.bloomlibrary.org/critical-dropbox-settings/).
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
                        !!repoFolderPath && !errorMessage && boxesChecked == 4
                    }
                    temporarilyDisableI18nWarning={true}
                    onClick={() => {
                        tryToCreate();
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

// -----------------------------------------------------------------------------------------
// Cloud Team Collection creation; see sharingApi.ts for the real SharingApi/TeamCollectionApi
// endpoints this drives. Unlike CreateTeamCollectionDialog above, there is no folder chooser,
// no Dropbox checkboxes, and no restart: sign in, acknowledge the immutable name, then Bloom
// uploads (Sends) the current collection as the initial version of the new cloud Team
// Collection.
// -----------------------------------------------------------------------------------------

export type CloudSendState = "notStarted" | "sending" | "done" | "error";

// Presentational: a pure function of its props, so the sign-in/acknowledge/send gating can be
// unit-tested without any network layer (same approach as SharingMembersList).
export const CreateCloudTeamCollectionBody: React.FunctionComponent<{
    loginState: ISharingLoginState;
    collectionName: string;
    devEmail: string;
    devPassword: string;
    onDevEmailChange: (value: string) => void;
    onDevPasswordChange: (value: string) => void;
    onDevSignIn: () => void;
    signInSubmitAttempts: number;
    signInError?: string;
    onCloudSignInClick: () => void;
    nameAcknowledged: boolean;
    onAcknowledgeNameChange: (checked: boolean) => void;
    sendState: CloudSendState;
    sendError?: string;
    onStartSend: () => void;
    onRetrySend: () => void;
}> = (props) => {
    if (!props.loginState.signedIn) {
        return (
            <div data-testid="cloud-create-signin-step">
                <P
                    l10nKey="TeamCollection.Sharing.SignInIntro"
                    temporarilyDisableI18nWarning={true}
                >
                    Sign in with your Bloom account to share this collection.
                </P>
                {props.loginState.mode === "dev" ? (
                    <DialogControlGroup>
                        <AttentionTextField
                            label="Email address"
                            l10nKey="TeamCollection.Sharing.EmailAddress"
                            // Note: unlike Div/P/BloomButton, AttentionTextField's underlying
                            // MuiTextField treats temporarilyDisableI18nWarning as "skip the XLF
                            // lookup entirely" (see muiTextField.tsx), not just "suppress the
                            // warning" — so it must be omitted here for this label to actually
                            // be localized.
                            value={props.devEmail}
                            onChange={props.onDevEmailChange}
                            isValid={(value) => isValidEmail(value.trim())}
                            submitAttempts={props.signInSubmitAttempts}
                            data-testid="cloud-create-signin-email"
                            css={css`
                                margin-top: 5px;
                            `}
                        />
                        <AttentionTextField
                            label="Password"
                            l10nKey="TeamCollection.Sharing.Password"
                            type="password"
                            value={props.devPassword}
                            onChange={props.onDevPasswordChange}
                            isValid={(value) => value.length > 0}
                            submitAttempts={props.signInSubmitAttempts}
                            data-testid="cloud-create-signin-password"
                            css={css`
                                margin-top: 5px;
                            `}
                        />
                        {props.signInError && (
                            <div data-testid="cloud-create-signin-error">
                                <ErrorBox>{props.signInError}</ErrorBox>
                            </div>
                        )}
                        <BloomButton
                            enabled={true}
                            hasText={true}
                            l10nKey="TeamCollection.Sharing.SignIn"
                            temporarilyDisableI18nWarning={true}
                            data-testid="cloud-create-signin-button"
                            onClick={props.onDevSignIn}
                            css={css`
                                margin-top: 10px;
                            `}
                        >
                            Sign In
                        </BloomButton>
                    </DialogControlGroup>
                ) : (
                    // Production ("cloud") mode: the real BloomLibrary browser-based sign-in
                    // flow slots in later (task 06); for now this button just requests it.
                    <BloomButton
                        enabled={true}
                        hasText={true}
                        l10nKey="TeamCollection.Sharing.SignInWithBloomAccount"
                        temporarilyDisableI18nWarning={true}
                        data-testid="cloud-create-cloud-signin-button"
                        onClick={props.onCloudSignInClick}
                    >
                        Sign in with your Bloom account
                    </BloomButton>
                )}
            </div>
        );
    }

    if (props.sendState === "notStarted") {
        return (
            <div data-testid="cloud-create-confirm-step">
                <Checkbox
                    id="cloud-create-name-ack-checkbox"
                    l10nKey="TeamCollection.Sharing.CloudNameWillWork"
                    l10nParam0={props.collectionName}
                    checked={props.nameAcknowledged}
                    onCheckChanged={props.onAcknowledgeNameChange}
                    temporarilyDisableI18nWarning={true}
                >
                    I think the name, "%0", will be a good one for the whole
                    team. I understand that I will not be able to change this
                    name once this becomes a Team Collection.
                </Checkbox>
                <BloomButton
                    enabled={props.nameAcknowledged}
                    hasText={true}
                    l10nKey="TeamCollection.Sharing.ShareCollection"
                    temporarilyDisableI18nWarning={true}
                    data-testid="cloud-create-share-button"
                    onClick={props.onStartSend}
                    css={css`
                        margin-top: 15px;
                    `}
                >
                    Share Collection
                </BloomButton>
            </div>
        );
    }

    if (props.sendState === "sending") {
        return (
            <div data-testid="cloud-create-sending-step">
                <P
                    l10nKey="TeamCollection.Sharing.SendingInitialCollection"
                    temporarilyDisableI18nWarning={true}
                >
                    Sending your collection to the cloud sharing server. This
                    may take a while depending on the size of your collection.
                </P>
                <LinearProgress data-testid="cloud-create-progress" />
            </div>
        );
    }

    if (props.sendState === "error") {
        return (
            <div data-testid="cloud-create-error-step">
                <div data-testid="cloud-create-error">
                    <ErrorBox>{props.sendError}</ErrorBox>
                </div>
                <BloomButton
                    enabled={true}
                    hasText={true}
                    l10nKey="TeamCollection.Sharing.TryAgain"
                    temporarilyDisableI18nWarning={true}
                    data-testid="cloud-create-retry-button"
                    onClick={props.onRetrySend}
                    css={css`
                        margin-top: 10px;
                    `}
                >
                    Try Again
                </BloomButton>
            </div>
        );
    }

    // sendState === "done"
    return (
        <div data-testid="cloud-create-done-step">
            <Span
                l10nKey="TeamCollection.Sharing.InitialSendComplete"
                temporarilyDisableI18nWarning={true}
            >
                Your Team Collection is ready. Invite your team from the Team
                Collection panel in Collection Settings.
            </Span>
        </div>
    );
};

// Container: wires CreateCloudTeamCollectionBody up to sharingApi and the BloomDialog frame.
export const CreateCloudTeamCollectionDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = (props) => {
    const loginState = useSharingLoginState();
    const [collectionName] = useApiStringState(
        "teamCollection/getCollectionName",
        "",
    );
    const [devEmail, setDevEmail] = useState("");
    const [devPassword, setDevPassword] = useState("");
    const [signInSubmitAttempts, setSignInSubmitAttempts] = useState(0);
    const [signInError, setSignInError] = useState<string | undefined>(
        undefined,
    );
    const [nameAcknowledged, setNameAcknowledged] = useState(false);
    const [sendState, setSendState] = useState<CloudSendState>("notStarted");
    const [sendError, setSendError] = useState<string | undefined>(undefined);
    const { propsForBloomDialog } = useSetupBloomDialog(
        props.dialogEnvironment,
    );

    const dialogTitle = useL10n(
        "Share this Collection",
        "TeamCollection.Sharing.ShareThisCollection",
        undefined,
        undefined,
        undefined,
        true,
    );

    const doSend = () => {
        setSendState("sending");
        setSendError(undefined);
        createCloudTeamCollection().then(
            () => setSendState("done"),
            (error) => {
                setSendError(String(error?.message ?? error));
                setSendState("error");
            },
        );
    };

    // Cloud TCs tie registration identity to the signed-in account (see registrationTypes.ts'
    // cloudAccountEmail), so make sure this copy of Bloom is registered under that email before
    // sending, same as the folder-TC dialog's tryToCreate() does for its own registration check.
    const startSend = () => {
        get("registration/userInfo", (userInfo) => {
            if (userInfo?.data?.email) {
                doSend();
            } else {
                showRegistrationDialog({
                    emailRequiredForTeamCollection: true,
                    cloudAccountEmail: loginState.email,
                    onSave: (hasValidEmail: boolean) => {
                        if (hasValidEmail) doSend();
                    },
                });
            }
        });
    };

    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle title={dialogTitle} />
            <DialogMiddle>
                <CreateCloudTeamCollectionBody
                    loginState={loginState}
                    collectionName={collectionName}
                    devEmail={devEmail}
                    devPassword={devPassword}
                    onDevEmailChange={setDevEmail}
                    onDevPasswordChange={setDevPassword}
                    onDevSignIn={() => {
                        if (
                            !isValidEmail(devEmail.trim()) ||
                            devPassword.length === 0
                        ) {
                            setSignInSubmitAttempts((old) => old + 1);
                            return;
                        }
                        setSignInError(undefined);
                        sharingSignIn(devEmail.trim(), devPassword).then(
                            undefined,
                            (error) =>
                                setSignInError(String(error?.message ?? error)),
                        );
                    }}
                    signInSubmitAttempts={signInSubmitAttempts}
                    signInError={signInError}
                    onCloudSignInClick={() => post("sharing/showSignIn")}
                    nameAcknowledged={nameAcknowledged}
                    onAcknowledgeNameChange={setNameAcknowledged}
                    sendState={sendState}
                    sendError={sendError}
                    onStartSend={startSend}
                    onRetrySend={startSend}
                />
            </DialogMiddle>
            <DialogBottomButtons>
                {sendState === "done" ? (
                    <BloomButton
                        enabled={true}
                        hasText={true}
                        l10nKey="Common.Close"
                        onClick={() => post("common/closeReactDialog")}
                    >
                        Close
                    </BloomButton>
                ) : (
                    <DialogCancelButton
                        onClick_DEPRECATED={() =>
                            post("common/closeReactDialog")
                        }
                    />
                )}
            </DialogBottomButtons>
        </BloomDialog>
    );
};

// -----------------------------------------------------------------------------------------
// This one file/bundle ("createTeamCollectionDialogBundle") hosts three distinct top-level
// dialogs -- the folder-TC create dialog above, the cloud-TC create dialog above, and the
// dedicated sign-in dialog (SignInDialog.tsx) -- because `WireUpForWinforms` sets a single
// global (`window.wireUpRootComponentFromWinforms`), so at most ONE component per bundle can
// ever call it: whichever call ran last at module load silently wins, breaking every other
// dialog in the file (this used to be a live bug -- the cloud dialog's own
// `WireUpForWinforms` call always overwrote the folder dialog's, so the folder-TC "Create Team
// Collection" dialog could no longer open). CreateTeamCollectionBundleDispatcher is the ONLY
// component in this file that may call WireUpForWinforms; C# selects which of the three to
// show via the `dialogKind` prop it now always passes (TeamCollectionApi.cs's
// HandleShowCreateTeamCollectionDialog/HandleShowCreateCloudTeamCollectionDialog and
// SharingApi.cs's HandleShowSignIn).
// -----------------------------------------------------------------------------------------

export type CreateTeamCollectionBundleDialogKind =
    | "folder"
    | "cloud"
    | "signIn";

export const CreateTeamCollectionBundleDispatcher: React.FunctionComponent<{
    dialogKind?: CreateTeamCollectionBundleDialogKind;
    errorForTesting?: string;
    defaultRepoFolder?: string;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = (props) => {
    switch (props.dialogKind) {
        case "cloud":
            return (
                <CreateCloudTeamCollectionDialog
                    dialogEnvironment={props.dialogEnvironment}
                />
            );
        case "signIn":
            return <SignInDialog dialogEnvironment={props.dialogEnvironment} />;
        case "folder":
        default:
            // Defaults to the folder dialog (today's only caller that predates the
            // `dialogKind` prop existing) so this stays byte-identical for folder TCs even
            // if some future caller forgets to pass it.
            return <CreateTeamCollectionDialog {...props} />;
    }
};

WireUpForWinforms(CreateTeamCollectionBundleDispatcher);
