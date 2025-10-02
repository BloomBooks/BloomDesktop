import { css } from "@emotion/react";
import $ from "jquery";
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
import { isValidEmail } from "../problemDialog/EmailField";
import { useIsTeamCollection } from "../teamCollection/teamCollectionApi";

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

    const [formIsFilled, setFormIsFilled] = useState(false);
    const [info, setInfo] = useState({
        firstName: "",
        surname: "",
        email: "",
        organization: "",
        usingFor: "",
        hadEmailAlready: false,
    });

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

    useEffect(() => {
        if (!info) setFormIsFilled(false);

        const emailIsProvided = !!info.email?.trim();
        setFormIsFilled(
            !!info.firstName?.trim() &&
                !!info.surname?.trim() &&
                (!emailRequiredForTeamCollection || emailIsProvided) &&
                (isValidEmail(info.email) || !emailIsProvided) &&
                !!info.organization?.trim() &&
                !!info.usingFor?.trim(),
        );
    }, [info, emailRequiredForTeamCollection]);

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
                    min-width: 400px;

                    h1 {
                        font-size: 14px;
                    }

                    #mustRegisterText {
                        width: 400px;
                    }

                    .row {
                        display: flex;
                        column-gap: 26px;
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
                    <MuiTextField
                        {...textFieldProps}
                        autoFocus={true}
                        label="First Name"
                        l10nKey="RegisterDialog.FirstName"
                        value={info.firstName}
                        onClick={undefined}
                        onChange={(e) =>
                            setInfo({ ...info, firstName: e.target.value })
                        }
                    />
                    <MuiTextField
                        {...textFieldProps}
                        label="Surname"
                        l10nKey="RegisterDialog.Surname"
                        value={info.surname}
                        onClick={undefined}
                        onChange={(e) =>
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
                        onChange={(e) =>
                            setInfo({ ...info, email: e.target.value })
                        }
                    />
                    <MuiTextField
                        {...textFieldProps}
                        label="Organization"
                        l10nKey="RegisterDialog.Organization"
                        value={info.organization}
                        onClick={undefined}
                        onChange={(e) =>
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
                        <BloomButton
                            l10nKey="RegisterDialog.IAmStuckLabel"
                            enabled={true}
                            variant="text"
                            onClick={tryToSave}
                            css={css`
                                font-size: 10px;
                            `}
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
