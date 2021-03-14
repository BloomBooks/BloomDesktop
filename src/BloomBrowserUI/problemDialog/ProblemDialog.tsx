import * as React from "react";
import "./ProblemDialog.less";
import { NotifyDialog } from "./NotifyDialog";
import { ReportDialog } from "./ReportDialog";

export enum ProblemKind {
    Notify = "Notify",
    User = "User",
    NonFatal = "NonFatal",
    Fatal = "Fatal"
}

// Case-insensitive parsing of the problem level. (Typescript Enum parsing is case-sensitive).
function parseProblemLevel(levelStr: string | null): ProblemKind | undefined {
    switch (levelStr?.toLowerCase()) {
        case "notify":
            return ProblemKind.Notify;
        case "user":
            return ProblemKind.User;
        case "nonfatal":
            return ProblemKind.NonFatal;
        case "fatal":
            return ProblemKind.Fatal;
        default:
            return undefined;
    }
}

export const ProblemDialog: React.FunctionComponent = props => {
    const queryStringWithoutQuestionMark = window.location.search.substring(1);
    const params = new URLSearchParams(queryStringWithoutQuestionMark);
    const levelStr = params.get("level");

    let level = parseProblemLevel(levelStr);
    console.assert(level, `Level "${levelStr}" could not be parsed.`);
    level = level || ProblemKind.NonFatal; // Default to NonFatal if parsing error.

    if (level === ProblemKind.Notify) {
        // Expects ?level=notify[&reportLabel={reportLabel}][&secondaryLabel={secondaryLabel}][&msg={message}]
        // reportLabel - Optional. The localized text that goes on the Report button. Omit or pass "" to disable Report button.
        // secondaryLabel - Optional. The localized text that goes on the secondary action button. Omit or pass "" to disable the secondary action button.
        // msg - Optional. The localized message to notify the user about. If omitted, will be retrieved via the BloomAPI instead.
        return (
            <NotifyDialog
                reportLabel={params.get("reportLabel")}
                secondaryLabel={params.get("secondaryLabel")}
                messageParam={params.get("msg")}
            />
        );
    } else {
        return <ReportDialog kind={level} />;
    }
};
