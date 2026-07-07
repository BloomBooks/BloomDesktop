import { css } from "@emotion/react";
import * as React from "react";
import { useState } from "react";
import { post } from "../utils/bloomApi";
import BloomButton from "../react_components/bloomButton";
import { P } from "../react_components/l10nComponents";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
} from "../react_components/BloomDialog/BloomDialog";
import {
    DialogCancelButton,
    DialogControlGroup,
} from "../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../react_components/l10nHooks";
import { AttentionTextField } from "../react_components/AttentionTextField";
import { ErrorBox } from "../react_components/boxes";
import { isValidEmail } from "../utils/emailUtils";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog,
} from "../react_components/BloomDialog/BloomDialogPlumbing";
import {
    ISharingLoginState,
    signIn as sharingSignIn,
    useSharingLoginState,
} from "./sharingApi";

// The dedicated sign-in dialog for cloud Team Collections, opened by `sharing/showSignIn`
// (see SharingApi.cs). Replaces the earlier placeholder, which reused the cloud
// create-collection dialog's sign-in step even in contexts that had nothing to do with
// creating a collection (e.g. signing in to see "Get my Team Collections", or to join one).
// In dev-auth mode this is a plain email/password form; in the eventual production ("cloud")
// mode, the real BloomLibrary browser-based sign-in flow slots in later (task 06's note) --
// for now this just explains that it isn't available yet.

// Presentational: a pure function of its props, so both modes can be unit-tested without any
// network layer (same approach as CreateCloudTeamCollectionBody).
export const SignInDialogBody: React.FunctionComponent<{
    loginState: ISharingLoginState;
    email: string;
    password: string;
    onEmailChange: (value: string) => void;
    onPasswordChange: (value: string) => void;
    onSignIn: () => void;
    submitAttempts: number;
    signInError?: string;
}> = (props) => {
    if (props.loginState.mode !== "dev") {
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
        <div data-testid="signin-dev-form">
            <DialogControlGroup>
                <AttentionTextField
                    label="Email address"
                    l10nKey="TeamCollection.Sharing.EmailAddress"
                    // Note: unlike Div/P/BloomButton, AttentionTextField's underlying
                    // MuiTextField treats temporarilyDisableI18nWarning as "skip the XLF
                    // lookup entirely" (see muiTextField.tsx), not just "suppress the
                    // warning" -- so it must be omitted here for this label to actually
                    // be localized.
                    value={props.email}
                    onChange={props.onEmailChange}
                    isValid={(value) => isValidEmail(value.trim())}
                    submitAttempts={props.submitAttempts}
                    data-testid="signin-email"
                    css={css`
                        margin-top: 5px;
                    `}
                />
                <AttentionTextField
                    label="Password"
                    l10nKey="TeamCollection.Sharing.Password"
                    type="password"
                    value={props.password}
                    onChange={props.onPasswordChange}
                    isValid={(value) => value.length > 0}
                    submitAttempts={props.submitAttempts}
                    data-testid="signin-password"
                    css={css`
                        margin-top: 5px;
                    `}
                />
                {props.signInError && (
                    <div data-testid="signin-error">
                        <ErrorBox>{props.signInError}</ErrorBox>
                    </div>
                )}
                <BloomButton
                    enabled={true}
                    hasText={true}
                    l10nKey="TeamCollection.Sharing.SignIn"
                    temporarilyDisableI18nWarning={true}
                    data-testid="signin-button"
                    onClick={props.onSignIn}
                    css={css`
                        margin-top: 10px;
                    `}
                >
                    Sign In
                </BloomButton>
            </DialogControlGroup>
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
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [submitAttempts, setSubmitAttempts] = useState(0);
    const [signInError, setSignInError] = useState<string | undefined>(
        undefined,
    );
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
                    email={email}
                    password={password}
                    onEmailChange={setEmail}
                    onPasswordChange={setPassword}
                    submitAttempts={submitAttempts}
                    signInError={signInError}
                    onSignIn={() => {
                        if (
                            !isValidEmail(email.trim()) ||
                            password.length === 0
                        ) {
                            setSubmitAttempts((old) => old + 1);
                            return;
                        }
                        setSignInError(undefined);
                        sharingSignIn(email.trim(), password).then(
                            undefined,
                            (error) =>
                                setSignInError(String(error?.message ?? error)),
                        );
                    }}
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
