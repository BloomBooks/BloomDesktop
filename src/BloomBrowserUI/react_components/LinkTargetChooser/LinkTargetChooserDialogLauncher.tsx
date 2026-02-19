import * as React from "react";
import { ShowEditViewDialog } from "../../bookEdit/editViewFrame";
import { LinkTargetChooserDialog } from "./LinkTargetChooserDialog";

export const showLinkTargetChooserDialog = (
    currentUrl: string,
    onSetUrl: (url: string) => void,
): void => {
    ShowEditViewDialog(
        <LinkTargetChooserDialog currentURL={currentUrl} onSetUrl={onSetUrl} />,
    );
};
