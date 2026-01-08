import * as React from "react";
import "./ProblemDialog.less";
import { INotifyDialogProps, NotifyDialog } from "./NotifyDialog";
import { ReportDialog } from "./ReportDialog";
import { WireUpForWinforms } from "../utils/WireUpWinform";

// Matches values in Bloom.ErrorReporter.ProblemLevel
export enum ProblemKind {
    Notify = "notify",
    User = "user",
    NonFatal = "nonfatal",
    Fatal = "fatal",
}

export const ProblemDialog: React.FC<
    { level: ProblemKind } & Partial<INotifyDialogProps>
> = (props) => {
    if (props.level === ProblemKind.Notify) {
        return <NotifyDialog {...props} />;
    } else {
        return <ReportDialog kind={props.level} />;
    }
};

WireUpForWinforms(ProblemDialog);
