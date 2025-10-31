/**
 * Registration Dialog Launcher
 *
 * Follows the same pattern as ConfirmDialog and BloomMessageBox.
 * The dialog can be shown from TypeScript using showRegistrationDialog(props),
 * or from C# via WireUpForWinforms.
 */
import * as React from "react";
import * as ReactDOM from "react-dom";
import {
    IBloomDialogEnvironmentParams,
    Mode,
    useSetupBloomDialog,
    useEventLaunchedBloomDialog,
} from "../BloomDialog/BloomDialogPlumbing";
import { RegistrationDialog } from "./registrationDialog";
import { getEditTabBundleExports } from "../../bookEdit/js/bloomFrames";
import { postBoolean } from "../../utils/bloomApi";

export interface IRegistrationDialogProps {
    emailRequiredForTeamCollection?: boolean;
    onSave?: (hasValidEmail: boolean) => void;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}

// Module-level function that can be called from anywhere to show the dialog
export let showRegistrationDialogFn: () => void = () => {
    console.error("showRegistrationDialog is not set up yet.");
};

export const RegistrationDialogLauncher: React.FunctionComponent<
    IRegistrationDialogProps
> = (props) => {
    const { showDialog, closeDialog, propsForBloomDialog } =
        useSetupBloomDialog(props.dialogEnvironment);

    showRegistrationDialogFn = showDialog;

    React.useEffect(() => {
        if (props.dialogEnvironment?.mode === Mode.Edit) {
            // Tell edit tab to disable everything when the dialog is up
            postBoolean("editView/setModalState", propsForBloomDialog.open);
        }
    }, [props.dialogEnvironment?.mode, propsForBloomDialog.open]);

    return (
        <RegistrationDialog
            closeDialog={closeDialog}
            showDialog={showDialog}
            propsForBloomDialog={propsForBloomDialog}
            emailRequiredForTeamCollection={
                props.emailRequiredForTeamCollection
            }
            onSave={props.onSave}
        />
    );
};

// This sits in the react tree doing nothing until it gets
// a websocket event from C# LaunchDialog calls.
export const RegistrationDialogEventLauncher: React.FunctionComponent = () => {
    const { openingEvent, closeDialog, propsForBloomDialog } =
        useEventLaunchedBloomDialog("RegistrationDialog");

    // Extract props from the event if available
    const eventProps: IRegistrationDialogProps = {
        emailRequiredForTeamCollection:
            openingEvent?.emailRequiredForTeamCollection,
        onSave: openingEvent?.onSave,
    };

    return propsForBloomDialog.open ? (
        <RegistrationDialog
            closeDialog={closeDialog}
            showDialog={() => {}} // Not used in event-based pattern
            propsForBloomDialog={propsForBloomDialog}
            emailRequiredForTeamCollection={
                eventProps.emailRequiredForTeamCollection
            }
            onSave={eventProps.onSave}
        />
    ) : null;
};

// Render the dialog with props and show it (used from TypeScript/React contexts)
export function showRegistrationDialog(
    registrationDialogProps: IRegistrationDialogProps,
    container?: Element | null,
) {
    doRender(registrationDialogProps, container);
    showRegistrationDialogFn();
}

// Special function for Edit Tab that uses the edit tab's modal container
export function showRegistrationDialogForEditTab(
    registrationDialogProps: IRegistrationDialogProps = {},
) {
    const modalContainer = getEditTabBundleExports().getModalDialogContainer();
    if (!registrationDialogProps.dialogEnvironment) {
        registrationDialogProps.dialogEnvironment = {
            dialogFrameProvidedExternally: false,
            initiallyOpen: false,
        };
    }
    registrationDialogProps.dialogEnvironment.mode = Mode.Edit;

    doRender(registrationDialogProps, modalContainer);
    showRegistrationDialogFn();
}

const doRender = (
    props: IRegistrationDialogProps,
    container?: Element | null,
) => {
    let modalContainer = container;

    // If no container specified, try to get one from the current context
    if (!modalContainer) {
        // Try to find a modal container in the current document
        modalContainer = document.getElementById("modal-dialog-container");

        // If still no container, we'll let React create the dialog at the body level
        if (!modalContainer) {
            modalContainer = document.createElement("div");
            document.body.appendChild(modalContainer);
        }

        if (!props.dialogEnvironment) {
            props.dialogEnvironment = {
                dialogFrameProvidedExternally: false,
                initiallyOpen: false,
            };
        }
    }

    try {
        ReactDOM.render(
            <RegistrationDialogLauncher {...props} />,
            modalContainer,
        );
    } catch (error) {
        console.error(error);
    }
};
