import * as React from "react";
import { IBloomDialogEnvironmentParams } from "../react_components/BloomDialog/BloomDialogPlumbing";
import { useL10n } from "../react_components/l10nHooks";
import { NumberChooserDialog } from "../react_components/numberChooserDialog";
import { postData } from "../utils/bloomApi";
import { ShowEditViewDialog } from "./workspaceRoot";

export const DuplicateManyDialog: React.FunctionComponent<{
    // The page the dialog was opened for. C# duplicates only if it is still the current page.
    pageId: string;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = (props) => {
    const title = useL10n(
        "Duplicate Page Many Times",
        "EditTab.DuplicatePageMultiple.Title",
        "Title of dialog that Bloom uses to ask the user how many times to duplicate the currently selected page.",
    );

    const promptString = useL10n(
        "How many more of this page? (2-999)",
        "EditTab.DuplicatePageMultiple.Prompt",
        "Used in the window that asks how many times to duplicate the selected page.",
    );

    const min = 2;
    const max = 999;

    const clickHandler = (value: number) => {
        postData("editView/duplicatePageMany", {
            numberOfTimes: value,
            pageId: props.pageId,
        });
    };
    return (
        <NumberChooserDialog
            min={min}
            max={max}
            title={title}
            prompt={promptString}
            onClick={clickHandler}
            dialogEnvironment={props.dialogEnvironment}
        ></NumberChooserDialog>
    );
};

// Shown in the Edit tab, from the page list's context menu, so nothing reaches C# until the user
// clicks OK. (A C# modal opened from the page list's request would hold the API lock.)
export function showDuplicateManyDialog(pageId: string) {
    ShowEditViewDialog(
        <DuplicateManyDialog
            pageId={pageId}
            dialogEnvironment={{
                initiallyOpen: true,
                dialogFrameProvidedExternally: false,
            }}
        />,
    );
}
