import { css } from "@emotion/react";

import * as React from "react";
import { useState } from "react";

import { get, post, useApiStringState } from "../utils/bloomApi";
import BloomButton from "../react_components/bloomButton";
import { P, Span } from "../react_components/l10nComponents";
import LinearProgress from "@mui/material/LinearProgress";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
} from "../react_components/BloomDialog/BloomDialog";
import { DialogCancelButton } from "../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../react_components/l10nHooks";
import { Checkbox } from "../react_components/checkbox";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog,
} from "../react_components/BloomDialog/BloomDialogPlumbing";
import { ErrorBox } from "../react_components/boxes";
import { showRegistrationDialog } from "../react_components/registration/registrationDialog";
import {
    ISharingLoginState,
    createCloudTeamCollection,
    useSharingLoginState,
} from "./sharingApi";
import { LocalSignInForm, useLocalSignIn } from "./LocalSignInForm";

// -----------------------------------------------------------------------------------------
// Cloud Team Collection creation; see sharingApi.ts for the real SharingApi/TeamCollectionApi
// endpoints this drives. Unlike the folder-TC CreateTeamCollectionDialog
// (CreateTeamCollection.tsx), there is no folder chooser, no Dropbox checkboxes, and no
// restart: sign in, acknowledge the immutable name, then Bloom uploads (Sends) the current
// collection as the initial version of the new cloud Team Collection.
// This dialog is hosted by CreateTeamCollection.tsx's CreateTeamCollectionBundleDispatcher
// (the bundle's single WireUpForWinforms entry point); do NOT call WireUpForWinforms here.
// -----------------------------------------------------------------------------------------

export type CloudSendState = "notStarted" | "sending" | "done" | "error";

// Presentational: a pure function of its props, so the sign-in/acknowledge/send gating can be
// unit-tested without any network layer (same approach as SharingMembersList).
export const CreateCloudTeamCollectionBody: React.FunctionComponent<{
    loginState: ISharingLoginState;
    collectionName: string;
    localEmail: string;
    localPassword: string;
    onLocalEmailChange: (value: string) => void;
    onLocalPasswordChange: (value: string) => void;
    onLocalSignIn: () => void;
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
                {props.loginState.mode === "local" ? (
                    <LocalSignInForm
                        testIdPrefix="cloud-create-signin"
                        email={props.localEmail}
                        password={props.localPassword}
                        onEmailChange={props.onLocalEmailChange}
                        onPasswordChange={props.onLocalPasswordChange}
                        onSignIn={props.onLocalSignIn}
                        submitAttempts={props.signInSubmitAttempts}
                        signInError={props.signInError}
                    />
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
    const localSignIn = useLocalSignIn();
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
                    localEmail={localSignIn.email}
                    localPassword={localSignIn.password}
                    onLocalEmailChange={localSignIn.setEmail}
                    onLocalPasswordChange={localSignIn.setPassword}
                    onLocalSignIn={localSignIn.onSignIn}
                    signInSubmitAttempts={localSignIn.submitAttempts}
                    signInError={localSignIn.signInError}
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
