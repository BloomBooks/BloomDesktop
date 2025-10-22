/**
 * Registration Dialog Launcher - Imperative API Bridge
 *
 * This file exists to bridge between two different ways of opening the registration dialog:
 *
 * 1. React-style: Render <RegistrationDialogLauncher /> with props directly
 *    - Used by storybook and some React components
 *    - Props flow through React's normal component tree
 *
 * 2. Imperative-style: Call showRegistrationDialog({ ... }) or showRegistrationDialogForEditTab()
 *    - Used by C# code (via WireUpForWinforms) and some React components
 *    - These functions can't pass props directly into an already-mounted React component
 *    - Instead, they store props in module-level variables (show, setOverrides)
 *    - The launcher component picks them up when it renders
 *
 * The complexity here comes from supporting both patterns while preventing bugs:
 * - We capture overrides in component state when the dialog opens
 * - We clear them immediately to prevent leaking into the next dialog open
 * - This ensures showRegistrationDialog({ emailRequired: true }) doesn't affect
 *   a later showRegistrationDialogForEditTab() call that expects the default behavior
 */
import { useState } from "react";
import {
    IBloomDialogEnvironmentParams,
    useEventLaunchedBloomDialog,
    useSetupBloomDialog,
} from "../BloomDialog/BloomDialogPlumbing";
import { RegistrationDialog } from "./registrationDialog";
import { ShowEditViewDialog } from "../../bookEdit/editViewFrame";

export interface IRegistrationDialogProps {
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

    // Store pending overrides from imperative showRegistrationDialog() calls
    const [pendingOverrides, setPendingOverrides] = useState<
        IRegistrationDialogProps | undefined
    >(undefined);

    show = showDialog;
    setOverrides = setPendingOverrides;

    return propsForBloomDialog.open ? (
        <RegistrationDialog
            closeDialog={closeDialog}
            showDialog={showDialog}
            propsForBloomDialog={propsForBloomDialog}
            emailRequiredForTeamCollection={
                props.emailRequiredForTeamCollection ??
                pendingOverrides?.emailRequiredForTeamCollection
            }
            onSave={props.onSave ?? pendingOverrides?.onSave}
        />
    ) : null;
};

let show: () => void = () => {};
let setOverrides: (
    props: IRegistrationDialogProps | undefined,
) => void = () => {};

export function showRegistrationDialogForEditTab() {
    ShowEditViewDialog(<RegistrationDialogLauncher />);
    setOverrides(undefined); // Clear any previous overrides
    show();
}

export function showRegistrationDialog(
    registrationDialogProps: IRegistrationDialogProps,
) {
    setOverrides(registrationDialogProps);
    show();
}
