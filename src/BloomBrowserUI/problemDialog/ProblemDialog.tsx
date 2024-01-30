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
    OneDriveNonFatal = "onedrivenonfatal",
    OneDriveFatal = "onedrivefatal"
}

export const ProblemDialog: React.FunctionComponent<{
    level: ProblemKind;
    message: string; // The localized message to notify the user about.
    reportLabel?: string; // The localized text that goes on the Report button. Omit or pass "" to disable Report button.
    secondaryLabel?: string; // The localized text that goes on the secondary action button. Omit or pass "" to disable the secondary action button.
}> = props => {
    if ([ProblemKind.Notify, ProblemKind.OneDriveNonFatal, ProblemKind.OneDriveFatal].includes(props.level)) { // TODO? is there a better way to do this?
        return <NotifyDialog {...props} />;
    } else {
        return <ReportDialog kind={props.level} />;
    }
};

WireUpForWinforms(ProblemDialog);
