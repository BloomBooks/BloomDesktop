import { css } from "@emotion/react";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle,
    IBloomDialogProps,
} from "./BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    useEventLaunchedBloomDialog,
    useSetupBloomDialog,
} from "./BloomDialog/BloomDialogPlumbing";
import { DialogCancelButton } from "./BloomDialog/commonDialogComponents";
import { useL10n } from "./l10nHooks";
import BloomButton from "./bloomButton";
import { useEffect, useRef, useState } from "react";
import { H1 } from "./l10nComponents";
import { TextFieldProps } from "@mui/material";
import { MuiTextField } from "./muiTextField";
import { get, getBoolean, postJson } from "../utils/bloomApi";
import { ShowEditViewDialog } from "../bookEdit/editViewFrame";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { isValidEmail } from "../utils/emailUtils";
import { useIsTeamCollection } from "../teamCollection/teamCollectionApi";
import { AttentionTextField } from "./AttentionTextField";

interface IRegistrationDialogProps {
    registrationIsOptional?: boolean;
    emailRequiredForTeamCollection?: boolean;
    onSave?: (isValidEmail: boolean) => void;
}

export const RegistrationDialogLauncher: React.FunctionComponent<
    IRegistrationDialogProps & {
        dialogEnvironment?: IBloomDialogEnvironmentParams;
    }
> = (props) => {
    // eslint needed useSetup and useEvent to be in the same order on every render
    const useSetup = useSetupBloomDialog(props.dialogEnvironment);
    const useEventLaunched = useEventLaunchedBloomDialog("RegistrationDialog");
    const { openingEvent, showDialog, closeDialog, propsForBloomDialog } =
        // use the environment in useSetup if env.dialogFrameExternal (WinForms) exists, else tell useEvent the dialog's name for showDialog()
        props.dialogEnvironment?.dialogFrameProvidedExternally
            ? // for WinForms Wrapped things (eg Join Team Collection) env = dialogFrame:true, Open:true (initially Open inside of frame)
              useSetup
            : // for React (all other times) env = undef -> propsForBlDialog = dialogFrame:false, Open:false (will open when show() is called)
              useEventLaunched;

    show = showDialog;

    return propsForBloomDialog.open ? (
        <RegistrationDialog
            closeDialog={closeDialog}
            showDialog={showDialog}
            propsForBloomDialog={propsForBloomDialog}
            // openingEvent only exists when using the Get Help... menu from Collections and Publish
            // props are used from WinForms (Join, the On Opening Program Registration) and Edit Tab
            registrationIsOptional={
                openingEvent?.registrationIsOptional ??
                props.registrationIsOptional
            }
        />
    ) : null;
};

export const RegistrationDialog: React.FunctionComponent<
    IRegistrationDialogProps & {
        closeDialog: () => void;
        showDialog: () => void;
        propsForBloomDialog: IBloomDialogProps;
    }
> = (props) => {
    const [mayChangeEmail, setMayChangeEmail] = useState(true);
    const registrationIsOptional =
        props.registrationIsOptional ??
        externallySetRegistrationDialogProps?.registrationIsOptional ??
        true;
    // externalProp - emailRequired only needs to be used when creating a team collection
    const inTeamCollection = useIsTeamCollection();
    const emailRequiredForTeamCollection =
        externallySetRegistrationDialogProps?.emailRequiredForTeamCollection ??
        inTeamCollection;

    const [info, setInfo] = useState({
        firstName: "",
        surname: "",
        email: "",
        organization: "",
        usingFor: "",
        hadEmailAlready: false,
    });

    const [submitAttempts, setSubmitAttempts] = useState(0);

    // Show the "I'm stuck" opt-out button after 10 seconds
    const [showOptOut, setShowOptOut] = useState(false);
    useEffect(() => {
        const timer = setTimeout(() => {
            setShowOptOut(true);
        }, 10000);
        return () => clearTimeout(timer);
    }, []);

    // Every time the dialog is opened, set MayChangeEmail and User Info fields
    useEffect(() => {
        if (!props.propsForBloomDialog.open) return;
        getBoolean("teamCollection/mayChangeRegistrationEmail", (mayChange) => {
            setMayChangeEmail(mayChange);
        });

        get("registration/userInfo", (userInfo) => {
            if (userInfo?.data) {
                setInfo(userInfo?.data);
            }
        });
    }, [props.propsForBloomDialog.open]);

    const isFirstNameValid = (v) => !!v?.trim();
    const isSurnameValid = (v) => !!v?.trim();
    const isOrganizationValid = (v) => !!v?.trim();
    const isUsingForValid = (v) => !!v?.trim();
    const isEmailValid = (v) => {
        const emailIsProvided = !!v?.trim();
        return (
            (!emailRequiredForTeamCollection || emailIsProvided) &&
            (isValidEmail(v) || !emailIsProvided)
        );
    };

    function isFormFilled() {
        if (!info) return false;
        return (
            isFirstNameValid(info.firstName) &&
            isSurnameValid(info.surname) &&
            isOrganizationValid(info.organization) &&
            isUsingForValid(info.usingFor) &&
            isEmailValid(info.email)
        );
    }

    // lets the registration text expand if the buttons are wider than DialogMiddle's initial width
    // seems to be dependent on the translations loading faster than this rewrites the width, though it does finish expanding when we start typing in a field
    const mustRegisterText = useRef<HTMLDivElement>(null);
    const bottomButtons = $("#bottomButtons");
    useEffect(() => {
        if (mustRegisterText.current)
            mustRegisterText.current.style.width =
                bottomButtons[0]?.offsetWidth + "px";
    }, [mustRegisterText, bottomButtons[0]?.offsetWidth]);

    const textFieldProps: TextFieldProps = {
        autoFocus: false,
        margin: "normal",
        multiline: false,
        fullWidth: true,
    };

    function tryToSave() {
        // wait for data save attempt to finish before continuing
        postJson("registration/userInfo", info, () => {
            const onSave =
                props.onSave ?? externallySetRegistrationDialogProps?.onSave;
            onSave?.(isValidEmail(info.email));
            closeDialog();
        });
    }

    const closeDialog = () => {
        props.closeDialog();
    };

    return (
        <BloomDialog
            {...props.propsForBloomDialog}
            onCancel={(reason) => {
                // If registration is not optional, don't close if they try to escape or click out
                if (
                    registrationIsOptional ||
                    (reason !== "escapeKeyDown" &&
                        reason !== "backdropClick" &&
                        reason !== "titleCloseClick")
                ) {
                    closeDialog();
                }
            }}
        >
            <DialogTitle
                title={useL10n(
                    "Register {0}",
                    "RegisterDialog.WindowTitle",
                    "Place a {0} where the name of the program goes.",
                    "Bloom",
                )}
                preventCloseButton={true}
            />
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
                    <div id="mustRegisterText" ref={mustRegisterText}>
                        You will need to register this copy of Bloom with an
                        email address before participating in a Team Collection
                    </div>
                ) : null}
                <div className="row">
                    {/* wrap the attention fields so they can jiggle within messing up the layout */}
                    <div
                        css={css`
                            width: 50%;
                            // Using margin instead of flex gap so the attention field can jiggle into it
                            margin-right: 26px;
                        `}
                    >
                        <AttentionTextField
                            {...textFieldProps}
                            autoFocus={true}
                            label="First Name"
                            l10nKey="RegisterDialog.FirstName"
                            value={info.firstName}
                            onClick={undefined}
                            onChange={(v) => setInfo({ ...info, firstName: v })}
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
                            {...textFieldProps}
                            label="Surname"
                            l10nKey="RegisterDialog.Surname"
                            value={info.surname}
                            onClick={undefined}
                            onChange={(v) => setInfo({ ...info, surname: v })}
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
                            {...textFieldProps}
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
                            onClick={undefined}
                            onChange={(value) =>
                                setInfo({ ...info, email: value || "" })
                            }
                            isValid={isEmailValid}
                            submitAttempts={submitAttempts}
                        />
                    </div>
                    <div
                        css={css`
                            width: 50%;
                        `}
                    >
                        <AttentionTextField
                            {...textFieldProps}
                            label="Organization"
                            l10nKey="RegisterDialog.Organization"
                            value={info.organization}
                            onClick={undefined}
                            onChange={(v) =>
                                setInfo({ ...info, organization: v })
                            }
                            submitAttempts={submitAttempts}
                            isValid={isOrganizationValid}
                        />
                    </div>
                </div>
                <MuiTextField
                    {...textFieldProps}
                    label="How are you using {0}?"
                    l10nKey="RegisterDialog.HowAreYouUsing"
                    l10nParam0="Bloom"
                    value={info.usingFor}
                    onClick={undefined}
                    onChange={(e) =>
                        setInfo({ ...info, usingFor: e.target.value })
                    }
                    multiline={true}
                    rows="3"
                />
            </DialogMiddle>
            <div id="bottomButtons">
                <DialogBottomButtons>
                    <DialogBottomLeftButtons>
                        {showOptOut && (
                            <BloomButton
                                l10nKey="RegisterDialog.IAmStuckLabel"
                                enabled={true}
                                variant="text"
                                onClick={() => {
                                    // Save even if form is not complete/valid
                                    // if email is invalid, clear it. No point in keeping an invalid email
                                    if (!isEmailValid(info.email)) {
                                        info.email = "";
                                    }
                                    tryToSave();
                                }}
                                css={css`
                                    font-size: 10px;
                                `}
                            >
                                I'm stuck, I'll finish this later.
                            </BloomButton>
                        )}
                    </DialogBottomLeftButtons>
                    <BloomButton
                        l10nKey="RegisterDialog.RegisterButton"
                        enabled={true}
                        onClick={() => {
                            // Validate and save if valid
                            if (!isFormFilled()) {
                                setSubmitAttempts(submitAttempts + 1);
                            } else {
                                tryToSave();
                            }
                        }}
                    >
                        Register
                    </BloomButton>
                    {registrationIsOptional && <DialogCancelButton />}
                </DialogBottomButtons>
            </div>
        </BloomDialog>
    );
};

let show: () => void = () => {};

let externallySetRegistrationDialogProps: IRegistrationDialogProps | undefined;

export function showRegistrationDialogForEditTab(
    registrationIsOptional?: boolean,
) {
    ShowEditViewDialog(
        <RegistrationDialogLauncher
            registrationIsOptional={registrationIsOptional}
        />,
    );
    show();
}

export function showRegistrationDialog(
    registrationDialogProps: IRegistrationDialogProps,
) {
    externallySetRegistrationDialogProps = registrationDialogProps;
    show();
}

WireUpForWinforms(RegistrationDialogLauncher);
