import { ProblemKind } from "./ProblemDialog";
import { NotifyDialog } from "./NotifyDialog";
import { ReportDialog } from "./ReportDialog";

const message =
    "Fake error with a line break<br> and <b>bold</b> and <a href='https://google.com'>link</a>...";

export default {
    title: "Problem Report"
};

export const FatalError = () => <ReportDialog kind={ProblemKind.Fatal} />;

FatalError.story = {
    name: "FatalError"
};

export const NonFatalError = () => <ReportDialog kind={ProblemKind.NonFatal} />;

NonFatalError.story = {
    name: "NonFatalError"
};

export const UserProblem = () => <ReportDialog kind={ProblemKind.User} />;

UserProblem.story = {
    name: "UserProblem"
};

export const NotifyUserNonReportable = () => (
    <NotifyDialog reportLabel={null} secondaryLabel={null} message={message} />
);

NotifyUserNonReportable.story = {
    name: "NotifyUser, Non-Reportable"
};

export const NotifyUserReportable = () => (
    <NotifyDialog
        reportLabel="Report"
        secondaryLabel={null}
        message={message}
    />
);

NotifyUserReportable.story = {
    name: "NotifyUser, Reportable"
};

export const NotifyUserReportRetry = () => (
    <NotifyDialog
        reportLabel="Report"
        secondaryLabel="Retry"
        message={message}
    />
);

NotifyUserReportRetry.story = {
    name: "NotifyUser, Report & Retry"
};
