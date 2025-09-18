import * as React from "react";
import { IBloomDialogEnvironmentParams } from "../react_components/BloomDialog/BloomDialogPlumbing";
import { useL10n } from "../react_components/l10nHooks";
import { NumberChooserDialog } from "../react_components/numberChooserDialog";
import { postData } from "../utils/bloomApi";
import { WireUpForWinforms } from "../utils/WireUpWinform";

export const DuplicateManyDialog: React.FunctionComponent<{
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

WireUpForWinforms(DuplicateManyDialog);
