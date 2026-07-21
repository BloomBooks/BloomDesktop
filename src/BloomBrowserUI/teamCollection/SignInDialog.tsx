import { css } from "@emotion/react";
import * as React from "react";
import { post } from "../utils/bloomApi";
import BloomButton from "../react_components/bloomButton";
import { P } from "../react_components/l10nComponents";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
} from "../react_components/BloomDialog/BloomDialog";
import { DialogCancelButton } from "../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../react_components/l10nHooks";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog,
} from "../react_components/BloomDialog/BloomDialogPlumbing";
import {
    ISharingLoginState,
    openBrowserSignIn,
    useSharingLoginState,
} from "./sharingApi";
import { LocalSignInForm, useLocalSignIn } from "./LocalSignInForm";

// The dedicated sign-in dialog for cloud Team Collections, opened by `sharing/showSignIn`
// (see SharingApi.cs). Replaces the earlier placeholder, which reused the cloud
// create-collection dialog's sign-in step even in contexts that had nothing to do with
// creating a collection (e.g. signing in to see "Get my Team Collections", or to join one).
// In local-auth mode this is a plain email/password form; in "cloud" mode (Option A, decided
// 8 Jul 2026) it is a single button that opens the real BloomLibrary browser-based sign-in
// flow (see onOpenBrowserSignIn/CONTRACTS.md's "Auth (Option A)" section) -- the dialog closes
// itself once that flow completes and useSharingLoginState() picks up the resulting
// "sharing"/"loginState" event, same as the local-mode form below.

// Presentational: a pure function of its props, so both modes can be unit-tested without any
// network layer (same approach as CreateCloudTeamCollectionBody).
export const SignInDialogBody: React.FunctionComponent<{
    loginState: ISharingLoginState;
    email: string;
    password: string;
    onEmailChange: (value: string) => void;
    onPasswordChange: (value: string) => void;
    onSignIn: () => void;
    onOpenBrowserSignIn: () => void;
    submitAttempts: number;
    signInError?: string;
}> = (props) => {
    if (props.loginState.mode === "cloud") {
        return (
            <div data-testid="signin-cloud-browser">
                <P
                    l10nKey="TeamCollection.Sharing.SignInViaBrowser"
                    temporarilyDisableI18nWarning={true}
                >
                    Click &quot;Sign In&quot; to sign in with your Bloom account
                    in your web browser. Come back to this window when you're
                    done.
                </P>
                <BloomButton
                    enabled={true}
                    hasText={true}
                    l10nKey="TeamCollection.Sharing.SignIn"
                    temporarilyDisableI18nWarning={true}
                    data-testid="signin-open-browser-button"
                    onClick={props.onOpenBrowserSignIn}
                    css={css`
                        margin-top: 10px;
                    `}
                >
                    Sign In
                </BloomButton>
            </div>
        );
    }

    if (props.loginState.mode !== "local") {
        // Defensive fallback: SharingLoginMode only ever declares "local" | "cloud" today, so
        // this is unreachable in practice, but keeps this component total over its prop type
        // rather than silently rendering nothing if a third mode is ever added.
        return (
            <div data-testid="signin-not-available">
                <P
                    l10nKey="TeamCollection.Sharing.SignInNotYetAvailable"
                    temporarilyDisableI18nWarning={true}
                >
                    Signing in with your Bloom account isn't available yet.
                    Check back in a future version of Bloom.
                </P>
            </div>
        );
    }

    return (
        <div data-testid="signin-local-form">
            <LocalSignInForm
                testIdPrefix="signin"
                email={props.email}
                password={props.password}
                onEmailChange={props.onEmailChange}
                onPasswordChange={props.onPasswordChange}
                onSignIn={props.onSignIn}
                submitAttempts={props.submitAttempts}
                signInError={props.signInError}
            />
        </div>
    );
};

// Container: wires SignInDialogBody up to sharingApi and the BloomDialog frame. Closes itself
// automatically once sign-in succeeds (useSharingLoginState picks up the "sharing"/"loginState"
// websocket event SharingApi.HandleLogin raises).
export const SignInDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = (props) => {
    const loginState = useSharingLoginState();
    const localSignIn = useLocalSignIn();
    const { closeDialog, propsForBloomDialog } = useSetupBloomDialog(
        props.dialogEnvironment,
    );

    const dialogTitle = useL10n(
        "Sign In",
        "TeamCollection.Sharing.SignIn",
        undefined,
        undefined,
        undefined,
        true,
    );

    React.useEffect(() => {
        if (loginState.signedIn) closeDialog();
    }, [loginState.signedIn, closeDialog]);

    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle title={dialogTitle} />
            <DialogMiddle>
                <SignInDialogBody
                    loginState={loginState}
                    email={localSignIn.email}
                    password={localSignIn.password}
                    onEmailChange={localSignIn.setEmail}
                    onPasswordChange={localSignIn.setPassword}
                    submitAttempts={localSignIn.submitAttempts}
                    signInError={localSignIn.signInError}
                    onSignIn={localSignIn.onSignIn}
                    onOpenBrowserSignIn={() => openBrowserSignIn()}
                />
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogCancelButton
                    onClick_DEPRECATED={() => post("common/closeReactDialog")}
                />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
