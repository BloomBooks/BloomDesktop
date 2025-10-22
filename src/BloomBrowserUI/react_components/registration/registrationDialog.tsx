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
import { useEffect, useState } from "react";
import { get, getBoolean } from "../../utils/bloomApi";
import { ShowEditViewDialog } from "../../bookEdit/editViewFrame";
import { WireUpForWinforms } from "../../utils/WireUpWinform";
import { useIsTeamCollection } from "../../teamCollection/teamCollectionApi";
import {
    RegistrationContents,
    RegistrationInfo,
    createEmptyRegistrationInfo,
} from "./registrationContents";

interface IRegistrationDialogProps {
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
    const { showDialog, closeDialog, propsForBloomDialog } =
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
    // externalProp - emailRequired only needs to be used when creating a team collection
    const inTeamCollection = useIsTeamCollection();
    const emailRequiredForTeamCollection =
        externallySetRegistrationDialogProps?.emailRequiredForTeamCollection ??
        inTeamCollection;

    const [info, setInfo] = useState<RegistrationInfo>(
        createEmptyRegistrationInfo,
    );

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

    return (
        <BloomDialog
            {...props.propsForBloomDialog}
            onCancel={(reason) => {
                // Registration is required, so don't close if they try to escape or click out
                if (
                    reason !== "escapeKeyDown" &&
                    reason !== "backdropClick" &&
                    reason !== "titleCloseClick"
                ) {
                    props.closeDialog();
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
                initialInfo={info}
                mayChangeEmail={mayChangeEmail}
                emailRequiredForTeamCollection={emailRequiredForTeamCollection}
                onClose={props.closeDialog}
            />
        </BloomDialog>
    );
};

let show: () => void = () => {};

let externallySetRegistrationDialogProps: IRegistrationDialogProps | undefined;

export function showRegistrationDialogForEditTab() {
    ShowEditViewDialog(<RegistrationDialogLauncher />);
    show();
}

export function showRegistrationDialog(
    registrationDialogProps: IRegistrationDialogProps,
) {
    externallySetRegistrationDialogProps = registrationDialogProps;
    show();
}

WireUpForWinforms(RegistrationDialogLauncher);
