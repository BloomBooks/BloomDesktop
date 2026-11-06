import {
    getEditablePageBundleExports,
    getWorkspaceBundleExports,
} from "../js/workspaceFrames";
import { DialogResult } from "../../react_components/confirmDialog";

// Ask the user to confirm that they really want to remove the current page,
// and call onConfirm if so. While they decide, the page frame shows which page
// that is with a red X across it. The dialog is shown in the workspace root
// window so it isn't confined to the narrow page-list iframe. This replaces the
// old C#-side WinForms ConfirmRemovePageDialog, which laid out badly on scaled
// monitors (BL-16421).
export const confirmRemovePage = (onConfirm: () => void) => {
    // The page frame has no exports while it is loading a page; then there is nothing to mark.
    const removeMark =
        getEditablePageBundleExports()?.markPageForRemoval() ??
        (() => undefined);
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
            removeMark();
            if (result === DialogResult.Confirm) onConfirm();
        },
    });
};
