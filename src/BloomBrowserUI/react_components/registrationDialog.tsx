import { css } from "@emotion/react";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle,
    IBloomDialogProps
} from "./BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    useEventLaunchedBloomDialog,
    useSetupBloomDialog
} from "./BloomDialog/BloomDialogPlumbing";
import { DialogCancelButton } from "./BloomDialog/commonDialogComponents";
import { useL10n } from "./l10nHooks";
import BloomButton from "./bloomButton";
import { useEffect, useState } from "react";
import { H1 } from "./l10nComponents";
import { TextFieldProps } from "@mui/material";
import { MuiTextField } from "./muiTextField";
import { get, postJson } from "../utils/bloomApi";
import { ShowEditViewDialog } from "../bookEdit/editViewFrame";
import { WireUpForWinforms } from "../utils/WireUpWinform";

export const RegistrationDialogLauncher: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
    mayChangeEmail?: boolean;
    registrationIsOptional?: boolean;
    emailRequiredForTeamCollection?: boolean;
    onSave?: (isValidEmail: boolean) => void;
}> = props => {
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
            // it needs precedence over props because CollectionsTabPane uses props for the sake of
            // Check Out Book, so the event has to overwrite those props
            mayChangeEmail={
                openingEvent?.mayChangeEmail ?? props.mayChangeEmail
            }
            registrationIsOptional={
                openingEvent?.registrationIsOptional ??
                props.registrationIsOptional
            }
            emailRequiredForTeamCollection={
                openingEvent?.emailRequiredForTeamCollection ??
                props.emailRequiredForTeamCollection
            }
            onSave={openingEvent ? openingEvent.onSave : props.onSave}
        />
    ) : null;
};

export const RegistrationDialog: React.FunctionComponent<{
    closeDialog: () => void;
    showDialog: () => void;
    propsForBloomDialog: IBloomDialogProps;
    mayChangeEmail?: boolean;
    registrationIsOptional?: boolean;
    emailRequiredForTeamCollection?: boolean;
    onSave?: (isValidEmail: boolean) => void;
}> = props => {
    const mayChangeEmail = props.mayChangeEmail ?? true;
    const registrationIsOptional = props.registrationIsOptional ?? true;
    const emailRequiredForTeamCollection =
        props.emailRequiredForTeamCollection ?? false;
    const [isValidEmail, setIsValidEmail] = useState(true);
    const [formIsFilled, setFormIsFilled] = useState(false);
    const [info, setInfo] = useState({
        firstName: "",
        surname: "",
        email: "",
        organization: "",
        usingFor: "",
        hadEmailAlready: false
    });

    // Update the fields every time the dialog is opened
    useEffect(() => {
        if (!props.propsForBloomDialog.open) return;
        get("registration/userInfo", userInfo => {
            if (userInfo?.data) {
                setInfo({
                    firstName: userInfo.data.FirstName,
                    surname: userInfo.data.LastName,
                    email: userInfo.data.Email,
                    organization: userInfo.data.OtherProperties.Organization,
                    usingFor: userInfo.data.OtherProperties.HowUsing,
                    hadEmailAlready: userInfo.data.Email !== ""
                });
            }
        });
    }, [props.propsForBloomDialog.open]);

    // from https://github.com/angular/angular.js/blob/65f800e19ec669ab7d5abbd2f6b82bf60110651a/src/ng/directive/input.js
    const email_regex = /^(?=.{1,254}$)(?=.{1,64}@)[-!#$%&'*+/0-9=?A-Z^_`a-z{|}~]+(\.[-!#$%&'*+/0-9=?A-Z^_`a-z{|}~]+)*@[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$/;
    useEffect(() => {
        setIsValidEmail(email_regex.test(info.email.trim()));
    }, [info.email]);

    useEffect(() => {
        let emailIsProvided = info.email.trim() !== "";
        setFormIsFilled(
            !!info.firstName.trim() &&
                !!info.surname.trim() &&
                (!emailRequiredForTeamCollection || emailIsProvided) &&
                (isValidEmail || !emailIsProvided) &&
                !!info.organization.trim() &&
                !!info.usingFor.trim()
        );
    }, [info, isValidEmail]);

    const textFieldProps: TextFieldProps = {
        autoFocus: false,
        margin: "normal",
        multiline: false,
        fullWidth: true,
        css: css`
            width: 188px;
        `
    };

    function tryToSave() {
        // close dialog after data save is attempted
        postJson("registration/tryToSave", info, closeDialog);
    }

    const closeDialog = () => {
        props.onSave?.(isValidEmail);
        props.closeDialog();
    };

    return (
        <BloomDialog
            {...props.propsForBloomDialog}
            onCancel={reason => {
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
                    "Dialog Title",
                    "Bloom"
                )}
                preventCloseButton={true}
            />
            <DialogMiddle
                css={css`
                    width: 400px;

                    h1 {
                        font-size: 14px;
                    }
                    .row {
                        display: flex;
                        justify-content: space-between;
                    }
                    .MuiInputBase-input {
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
                <H1 l10nKey="RegisterDialog.Heading" l10nParam0="Bloom">
                    Please take a minute to register {0}.
                </H1>
                {emailRequiredForTeamCollection ? (
                    <div>
                        You will need to register this copy of Bloom with an
                        email address before participating in a Team Collection
                    </div>
                ) : null}
                <div className="row">
                    <MuiTextField
                        {...textFieldProps}
                        autoFocus={true}
                        label="First Name"
                        l10nKey="RegisterDialog.FirstName"
                        value={info.firstName}
                        onClick={undefined}
                        onChange={e =>
                            setInfo({ ...info, firstName: e.target.value })
                        }
                    />
                    <MuiTextField
                        {...textFieldProps}
                        label="Surname"
                        l10nKey="RegisterDialog.Surname"
                        value={info.surname}
                        onClick={undefined}
                        onChange={e =>
                            setInfo({ ...info, surname: e.target.value })
                        }
                    />
                </div>
                <div className="row">
                    <MuiTextField
                        {...textFieldProps}
                        label={
                            mayChangeEmail
                                ? "Email Address"
                                : "Check in to change email"
                        }
                        l10nKey={mayChangeEmail ? "RegisterDialog.Email" : ""}
                        value={info.email}
                        disabled={!mayChangeEmail}
                        onClick={undefined}
                        onChange={e =>
                            setInfo({ ...info, email: e.target.value })
                        }
                    />
                    <MuiTextField
                        {...textFieldProps}
                        label="Organization"
                        l10nKey="RegisterDialog.Organization"
                        value={info.organization}
                        onClick={undefined}
                        onChange={e =>
                            setInfo({ ...info, organization: e.target.value })
                        }
                    />
                </div>
                <MuiTextField
                    {...textFieldProps}
                    label="How are you using {0}?"
                    l10nKey="RegisterDialog.HowAreYouUsing"
                    l10nParam0="Bloom"
                    value={info.usingFor}
                    onClick={undefined}
                    onChange={e =>
                        setInfo({ ...info, usingFor: e.target.value })
                    }
                    multiline={true}
                    rows="3"
                    css={css`
                        width: 400px;
                    `}
                />
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogBottomLeftButtons>
                    <BloomButton
                        l10nKey="RegisterDialog.IAmStuckLabel"
                        enabled={true}
                        variant="text"
                        onClick={tryToSave}
                    >
                        I'm stuck, I'll finish this later.
                    </BloomButton>
                </DialogBottomLeftButtons>
                <BloomButton
                    l10nKey="RegisterDialog.RegisterButton"
                    enabled={formIsFilled}
                    onClick={tryToSave}
                >
                    Register
                </BloomButton>
                {registrationIsOptional && <DialogCancelButton />}
            </DialogBottomButtons>
        </BloomDialog>
    );
};

let show: () => void = () => {};

export function showRegistrationDialogForEditTab(
    mayChangeEmail?: boolean,
    registrationIsOptional?: boolean,
    emailRequiredForTeamCollection?: boolean
) {
    ShowEditViewDialog(
        <RegistrationDialogLauncher
            mayChangeEmail={mayChangeEmail}
            registrationIsOptional={registrationIsOptional}
            emailRequiredForTeamCollection={emailRequiredForTeamCollection}
        />
    );
    show();
}

export function showRegistrationDialog() {
    show();
}

WireUpForWinforms(RegistrationDialogLauncher);
