import { ProblemKind } from "./ProblemDialog";
import { ReportDialog } from "./ReportDialog";

export default {
    title: "Problem Report/ReportDialog",
};

export const FatalError = () => <ReportDialog kind={ProblemKind.Fatal} />;

FatalError.story = {
    name: "FatalError",
};

export const NonFatalError = () => <ReportDialog kind={ProblemKind.NonFatal} />;

NonFatalError.story = {
    name: "NonFatalError",
};

export const UserProblem = () => <ReportDialog kind={ProblemKind.User} />;

UserProblem.story = {
    name: "UserProblem",
};
