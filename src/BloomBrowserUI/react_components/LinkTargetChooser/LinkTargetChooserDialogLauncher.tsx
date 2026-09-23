import * as React from "react";
import { ShowEditViewDialog } from "../../bookEdit/workspaceRoot";
import { LinkTargetChooserDialog } from "./LinkTargetChooserDialog";

// Call this only from the workspace root (from the page iframe, go through
// getWorkspaceBundleExports().showLinkTargetChooserDialog): ShowEditViewDialog renders in the
// calling frame's document, and only in the root does the dialog's backdrop cover the page list.
export const showLinkTargetChooserDialog = (
    currentUrl: string,
    onSetUrl: (url: string) => void,
): void => {
    ShowEditViewDialog(
        <LinkTargetChooserDialog currentURL={currentUrl} onSetUrl={onSetUrl} />,
    );
};
