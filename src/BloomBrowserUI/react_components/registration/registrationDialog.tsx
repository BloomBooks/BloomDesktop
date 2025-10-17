import {
    BloomDialog,
    DialogTitle,
    IBloomDialogProps,
} from "../BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    useEventLaunchedBloomDialog,
    useSetupBloomDialog,
} from "../BloomDialog/BloomDialogPlumbing";
import { useL10n } from "../l10nHooks";
import { useCallback, useEffect, useState } from "react";
import { get, getBoolean, postJson } from "../../utils/bloomApi";
import { ShowEditViewDialog } from "../../bookEdit/editViewFrame";
import { WireUpForWinforms } from "../../utils/WireUpWinform";
import { isValidEmail } from "../../utils/emailUtils";
import { useIsTeamCollection } from "../../teamCollection/teamCollectionApi";
import {
    RegistrationContents,
    RegistrationInfo,
    createEmptyRegistrationInfo,
} from "./registrationContents";

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
    const closeDialogFunc = props.closeDialog;
    const onSaveFunc = props.onSave;
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

    const [info, setInfo] = useState<RegistrationInfo>(
        createEmptyRegistrationInfo,
    );
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

    const closeDialog = useCallback(() => {
        closeDialogFunc();
    }, [closeDialogFunc]);

    const saveInfo = useCallback(
        (nextInfo: RegistrationInfo) => {
            postJson("registration/userInfo", nextInfo, () => {
                const onSave =
                    onSaveFunc ?? externallySetRegistrationDialogProps?.onSave;
                onSave?.(isValidEmail(nextInfo.email));
                closeDialog();
            });
        },
        [onSaveFunc, closeDialog],
    );

    const updateInfo = useCallback((changes: Partial<RegistrationInfo>) => {
        setInfo((previous) => ({ ...previous, ...changes }));
    }, []);

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
            <RegistrationContents
                info={info}
                onInfoChange={updateInfo}
                mayChangeEmail={mayChangeEmail}
                emailRequiredForTeamCollection={emailRequiredForTeamCollection}
                registrationIsOptional={registrationIsOptional}
                showOptOut={showOptOut}
                onSubmit={saveInfo}
                onOptOut={saveInfo}
            />
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
