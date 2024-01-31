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
    message: string; // The localized message to notify the user about.
    reportLabel?: string; // The localized text that goes on the Report button. Omit or pass "" to disable Report button.
    secondaryLabel?: string; // The localized text that goes on the secondary action button. Omit or pass "" to disable the secondary action button.
    notifyOnly?: boolean; // If true, use the NotifyDialog with no report button regardless of the level.
}> = props => {
    if (props.level === ProblemKind.Notify || props.notifyOnly) {
        return <NotifyDialog {...props} />;
    } else {
        return <ReportDialog kind={props.level} />;
    }
};

WireUpForWinforms(ProblemDialog);
