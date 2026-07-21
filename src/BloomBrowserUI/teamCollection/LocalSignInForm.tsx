import { css } from "@emotion/react";
import * as React from "react";
import { useState } from "react";
import BloomButton from "../react_components/bloomButton";
import { DialogControlGroup } from "../react_components/BloomDialog/commonDialogComponents";
import { AttentionTextField } from "../react_components/AttentionTextField";
import { ErrorBox } from "../react_components/boxes";
import { isValidEmail } from "../utils/emailUtils";
import { signIn as sharingSignIn } from "./sharingApi";

// The local-auth-mode email/password sign-in form shared by the dedicated sign-in dialog
// (SignInDialog.tsx) and the cloud create-collection dialog's sign-in step
// (CreateCloudTeamCollection.tsx). Purely presentational (controlled by its props, no network
// layer) so both hosts stay unit-testable the same way; each host supplies its own
// data-testid prefix so existing tests keep their distinct ids. The matching container-side
// state + submit/validation logic the two hosts also shared lives in useLocalSignIn() below.

export const LocalSignInForm: React.FunctionComponent<{
    // data-testids rendered are `${testIdPrefix}-email`, `-password`, `-error`, `-button`.
    testIdPrefix: string;
    email: string;
    password: string;
    onEmailChange: (value: string) => void;
    onPasswordChange: (value: string) => void;
    onSignIn: () => void;
    submitAttempts: number;
    signInError?: string;
}> = (props) => {
    return (
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
                data-testid={`${props.testIdPrefix}-email`}
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
                data-testid={`${props.testIdPrefix}-password`}
                css={css`
                    margin-top: 5px;
                `}
            />
            {props.signInError && (
                <div data-testid={`${props.testIdPrefix}-error`}>
                    <ErrorBox>{props.signInError}</ErrorBox>
                </div>
            )}
            <BloomButton
                enabled={true}
                hasText={true}
                l10nKey="TeamCollection.Sharing.SignIn"
                temporarilyDisableI18nWarning={true}
                data-testid={`${props.testIdPrefix}-button`}
                onClick={props.onSignIn}
                css={css`
                    margin-top: 10px;
                `}
            >
                Sign In
            </BloomButton>
        </DialogControlGroup>
    );
};

// Container-side half of the shared local sign-in flow: owns the email/password/submit-attempt/
// error state and the validate-then-sharing/login submit handler that SignInDialog and
// CreateCloudTeamCollectionDialog previously duplicated. The returned values plug straight
// into LocalSignInForm's props (usually via a presentational Body component in between).
// Note: onSignIn resolves via the "sharing"/"loginState" websocket event (watched by
// useSharingLoginState), not a return value; only a rejection is surfaced, as signInError.
export function useLocalSignIn(): {
    email: string;
    setEmail: (value: string) => void;
    password: string;
    setPassword: (value: string) => void;
    submitAttempts: number;
    signInError?: string;
    onSignIn: () => void;
} {
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [submitAttempts, setSubmitAttempts] = useState(0);
    const [signInError, setSignInError] = useState<string | undefined>(
        undefined,
    );
    const onSignIn = () => {
        if (!isValidEmail(email.trim()) || password.length === 0) {
            setSubmitAttempts((old) => old + 1);
            return;
        }
        setSignInError(undefined);
        sharingSignIn(email.trim(), password).then(undefined, (error) =>
            setSignInError(String(error?.message ?? error)),
        );
    };
    return {
        email,
        setEmail,
        password,
        setPassword,
        submitAttempts,
        signInError,
        onSignIn,
    };
}
