import {
    BloomDialog,
    DialogTitle,
    IBloomDialogProps,
} from "../BloomDialog/BloomDialog";
import $ from "jquery";
import { useL10n } from "../l10nHooks";
import { useEffect, useState } from "react";
import { get, getBoolean } from "../../utils/bloomApi";
import { WireUpForWinforms } from "../../utils/WireUpWinform";
import { useIsTeamCollection } from "../../teamCollection/teamCollectionApi";
import {
    RegistrationContents,
    RegistrationInfo,
    createEmptyRegistrationInfo,
} from "./registrationContents";
import {
    IRegistrationDialogProps,
    RegistrationDialogLauncher,
    RegistrationDialogEventLauncher,
    showRegistrationDialog,
    showRegistrationDialogForEditTab,
} from "./registrationDialogLauncher";

// Re-export everything that external code needs
export type { IRegistrationDialogProps };
export {
    RegistrationDialogLauncher,
    RegistrationDialogEventLauncher,
    showRegistrationDialog,
    showRegistrationDialogForEditTab,
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
        props.emailRequiredForTeamCollection ?? inTeamCollection;

    const [info, setInfo] = useState<RegistrationInfo>(
        createEmptyRegistrationInfo,
    );

    // Every time the dialog is opened, set MayChangeEmail and User Info fields
    useEffect(() => {
        if (!props.propsForBloomDialog.open) {
            return;
        }

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
                onClose={(hasValidEmail: boolean) => {
                    props.onSave?.(hasValidEmail);
                    props.closeDialog();
                }}
            />
        </BloomDialog>
    );
};

// Wire up for WinForms - this must be in the webpack entry point file
WireUpForWinforms(RegistrationDialogLauncher);
