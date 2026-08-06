import { getWorkspaceBundleExports } from "../js/workspaceFrames";
import { DialogResult } from "../../react_components/confirmDialog";

// Ask the user to confirm that they really want to remove the current page,
// and call onConfirm if so. The dialog is shown in the workspace root window
// so it isn't confined to the narrow page-list iframe. This replaces the old
// C#-side WinForms ConfirmRemovePageDialog, which laid out badly on scaled
// monitors (BL-16421).
export const confirmRemovePage = (onConfirm: () => void) => {
    getWorkspaceBundleExports().showConfirmDialog({
        title: "Really Remove Page?",
        titleL10nKey:
            "EditTab.ConfirmRemovePageDialog.ConformRemovePageWindowTitle",
        message: "This page will be permanently removed.",
        messageL10nKey: "EditTab.ConfirmRemovePageDialog._messageLabel",
        // The XLF value of this key is "&Remove"; the leading mnemonic ampersand
        // (a holdover from the WinForms button) is stripped automatically by our
        // localization layer (see getLocalization in react_components/l10n.ts).
        confirmButtonLabel: "&Remove",
        confirmButtonLabelL10nKey:
            "EditTab.ConfirmRemovePageDialog.DeleteButton",
        onDialogClose: (result) => {
            if (result === DialogResult.Confirm) onConfirm();
        },
    });
};
