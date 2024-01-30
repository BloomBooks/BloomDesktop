import * as React from "react";
import "./ProblemDialog.less";
import { NotifyDialog } from "./NotifyDialog";
import { ReportDialog } from "./ReportDialog";
import { WireUpForWinforms } from "../utils/WireUpWinform";

// Matches values in Bloom.ErrorReporter.ProblemLevel
export enum ProblemKind {
    Notify = "notify",
    User = "user",
    NonFatal = "nonfatal",
    Fatal = "fatal",
}

export const ProblemDialog: React.FunctionComponent<{
    level: ProblemKind;
    alwaysUseNotify?: boolean; // If true, use the NotifyDialog regardless of the level.

    //Props used only by the NotifyDialog:
    message: string; // The localized message to notify the user about.
    reportLabel?: string; // The localized text that goes on the Report button. Omit or pass "" to disable Report button.
    secondaryLabel?: string; // The localized text that goes on the secondary action button. Omit or pass "" to disable the secondary action button.
    detailsBoxText?: string; // Localized text to go into a grey details box under the message. Omit or pass "" to not show a details box.
    titleOverride?: string; // If present, wil be used in place of the dialog title defined for this level in themes.ts
    titleL10nKeyOverride?: string; // The L10nKey for the titleOverride, if present.
}> = props => {
    if (props.level === ProblemKind.Notify || props.alwaysUseNotify) {
        return <NotifyDialog {...props} />;
    } else {
        return <ReportDialog kind={props.level} />;
    }
};

WireUpForWinforms(ProblemDialog);
