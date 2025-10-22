import { css } from "@emotion/react";
import * as React from "react";
import {
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
} from "../BloomDialog/BloomDialog";
import { H1 } from "../l10nComponents";
import { AttentionTextField } from "../AttentionTextField";
import BloomButton from "../bloomButton";
import { isValidEmail } from "../../utils/emailUtils";
import { postJson } from "../../utils/bloomApi";
import {
    kInactivitySecondsBeforeShowingOptOut,
    type RegistrationInfo,
    type IRegistrationContentsProps,
} from "./registrationTypes";

export { kInactivitySecondsBeforeShowingOptOut };
export type { RegistrationInfo, IRegistrationContentsProps };

export const createEmptyRegistrationInfo = (): RegistrationInfo => ({
    firstName: "",
    surname: "",
    email: "",
    organization: "",
    usingFor: "",
    hadEmailAlready: false,
});

const trimRegistrationInfo = (info: RegistrationInfo): RegistrationInfo => ({
    ...info,
    firstName: info.firstName.trim(),
    surname: info.surname.trim(),
    email: info.email.trim(),
    organization: info.organization.trim(),
    usingFor: info.usingFor.trim(),
});

export const isFirstNameValid = (value: string): boolean => !!value?.trim();
export const isSurnameValid = (value: string): boolean => !!value?.trim();
export const isOrganizationValid = (value: string): boolean => !!value?.trim();
export const isUsingForValid = (value: string): boolean => !!value?.trim();
export const isEmailFieldValid = (
    value: string,
    emailRequired: boolean,
): boolean => {
    const trimmedValue = value?.trim();
    const emailIsProvided = !!trimmedValue;
    return (
        (!emailRequired || emailIsProvided) &&
        (isValidEmail(trimmedValue) || !emailIsProvided)
    );
};

export const isRegistrationInfoComplete = (
    info: RegistrationInfo,
    emailRequired: boolean,
): boolean => {
    return (
        isFirstNameValid(info.firstName) &&
        isSurnameValid(info.surname) &&
        isOrganizationValid(info.organization) &&
        isUsingForValid(info.usingFor) &&
        isEmailFieldValid(info.email, emailRequired)
    );
};

export const RegistrationContents: React.FunctionComponent<
    IRegistrationContentsProps
> = (props) => {
    const mustRegisterTextRef = React.useRef<HTMLDivElement>(null);
    const bottomButtonsRef = React.useRef<HTMLDivElement>(null);
    const [submitAttempts, setSubmitAttempts] = React.useState(0);
    const [info, setInfo] = React.useState<RegistrationInfo>(props.initialInfo);

    const mayChangeEmail = props.mayChangeEmail ?? true;
    const emailRequiredForTeamCollection =
        props.emailRequiredForTeamCollection ?? false;

    // Update local state when initialInfo changes
    React.useEffect(() => {
        setInfo(props.initialInfo);
    }, [props.initialInfo]);

    const updateInfo = React.useCallback(
        (changes: Partial<RegistrationInfo>) => {
            setInfo((previous) => ({ ...previous, ...changes }));
        },
        [],
    );

    // Show the "I'm stuck" opt-out button after 10 seconds
    // Reset the timer whenever the user types anything
    const [showOptOut, setShowOptOut] = React.useState(false);
    React.useEffect(() => {
        setShowOptOut(false);
        const timer = setTimeout(() => {
            setShowOptOut(true);
        }, kInactivitySecondsBeforeShowingOptOut * 1000);
        return () => clearTimeout(timer);
    }, [info]);

    React.useEffect(() => {
        if (mustRegisterTextRef.current && bottomButtonsRef.current) {
            mustRegisterTextRef.current.style.width =
                bottomButtonsRef.current.offsetWidth + "px";
        }
    });

    const handleSubmit = () => {
        if (!isRegistrationInfoComplete(info, emailRequiredForTeamCollection)) {
            setSubmitAttempts((previous) => previous + 1);
            return;
        }
        setSubmitAttempts(0);
        const trimmedInfo = trimRegistrationInfo(info);

        // Call the API to save registration info
        postJson("registration/userInfo", trimmedInfo);

        // Also call the callback if provided (for backward compatibility during migration)
        props.onClose?.();
    };

    const handleOptOutClick = () => {
        const trimmedInfo = trimRegistrationInfo(info);
        const sanitizedInfo = isEmailFieldValid(
            trimmedInfo.email,
            emailRequiredForTeamCollection,
        )
            ? trimmedInfo
            : { ...trimmedInfo, email: "" };

        // Call the API to save registration info
        postJson("registration/userInfo", sanitizedInfo);

        // Also call the callback if provided (for backward compatibility during migration)
        props.onClose?.();
    };

    return (
        <>
            <DialogMiddle
                css={css`
                    width: 400px;

                    // Make room for the attention fields to jiggle without overflowing
                    margin-right: -11px;
                    padding-right: 11px;

                    h1 {
                        font-size: 14px;
                    }

                    #mustRegisterText {
                        width: 400px;
                    }

                    .row {
                        display: flex;
                        // We'll use margins on the children instead of gap so the attention field can jiggle into it
                        column-gap: 0px;
                    }

                    .MuiInputLabel-root {
                        font-size: 18px;
                        font-weight: 500;
                    }

                    .MuiOutlinedInput-notchedOutline legend {
                        font-size: 14px;
                    }
                `}
            >
                <H1
                    l10nKey="RegisterDialog.Heading"
                    l10nParam0="Bloom"
                    l10nComment="Place a {0} where the name of the program goes."
                >
                    Please take a minute to register {0}.
                </H1>
                {emailRequiredForTeamCollection ? (
                    <div id="mustRegisterText" ref={mustRegisterTextRef}>
                        You will need to register this copy of Bloom with an
                        email address before participating in a Team Collection
                    </div>
                ) : null}
                <div className="row">
                    <div
                        css={css`
                            width: 50%;
                            // Using margin instead of flex gap so the attention field can jiggle into it
                            margin-right: 26px;
                        `}
                    >
                        <AttentionTextField
                            autoFocus={true}
                            margin="normal"
                            fullWidth={true}
                            label="First Name"
                            l10nKey="RegisterDialog.FirstName"
                            value={info.firstName}
                            onChange={(value) =>
                                updateInfo({ firstName: value })
                            }
                            submitAttempts={submitAttempts}
                            isValid={isFirstNameValid}
                        />
                    </div>
                    <div
                        css={css`
                            width: 50%;
                        `}
                    >
                        <AttentionTextField
                            margin="normal"
                            fullWidth={true}
                            label="Surname"
                            l10nKey="RegisterDialog.Surname"
                            value={info.surname}
                            onChange={(value) => updateInfo({ surname: value })}
                            submitAttempts={submitAttempts}
                            isValid={isSurnameValid}
                        />
                    </div>
                </div>
                <div className="row">
                    <div
                        css={css`
                            width: 50%;
                            // Using margin instead of flex gap so the attention field can jiggle into it
                            margin-right: 26px;
                        `}
                    >
                        <AttentionTextField
                            margin="normal"
                            fullWidth={true}
                            label={
                                mayChangeEmail
                                    ? "Email Address"
                                    : "Check in to change email"
                            }
                            l10nKey={
                                mayChangeEmail ? "RegisterDialog.Email" : ""
                            }
                            value={info.email}
                            disabled={!mayChangeEmail}
                            onChange={(value) =>
                                updateInfo({ email: value || "" })
                            }
                            isValid={(value) =>
                                isEmailFieldValid(
                                    value,
                                    emailRequiredForTeamCollection,
                                )
                            }
                            submitAttempts={submitAttempts}
                        />
                    </div>
                    <div
                        css={css`
                            width: 50%;
                        `}
                    >
                        <AttentionTextField
                            margin="normal"
                            fullWidth={true}
                            label="Organization"
                            l10nKey="RegisterDialog.Organization"
                            value={info.organization}
                            onChange={(value) =>
                                updateInfo({ organization: value })
                            }
                            submitAttempts={submitAttempts}
                            isValid={isOrganizationValid}
                        />
                    </div>
                </div>
                <AttentionTextField
                    margin="normal"
                    fullWidth={true}
                    label="How are you using {0}?"
                    l10nKey="RegisterDialog.HowAreYouUsing"
                    l10nParam0="Bloom"
                    value={info.usingFor}
                    onChange={(value) => updateInfo({ usingFor: value })}
                    submitAttempts={submitAttempts}
                    isValid={isUsingForValid}
                    multiline={true}
                    rows="3"
                />
            </DialogMiddle>
            <div id="bottomButtons" ref={bottomButtonsRef}>
                <DialogBottomButtons>
                    <DialogBottomLeftButtons>
                        {showOptOut && (
                            <BloomButton
                                l10nKey="RegisterDialog.IAmStuckLabel"
                                enabled={true}
                                variant="text"
                                onClick={handleOptOutClick}
                                css={css`
                                    font-size: 10px;
                                    animation: fadeIn 1s ease-in;
                                    @keyframes fadeIn {
                                        from {
                                            opacity: 0;
                                        }
                                        to {
                                            opacity: 1;
                                        }
                                    }
                                `}
                            >
                                I'm stuck, I'll finish this later.
                            </BloomButton>
                        )}
                    </DialogBottomLeftButtons>
                    <BloomButton
                        l10nKey="RegisterDialog.RegisterButton"
                        enabled={true}
                        onClick={handleSubmit}
                    >
                        Register
                    </BloomButton>
                </DialogBottomButtons>
            </div>
        </>
    );
};
