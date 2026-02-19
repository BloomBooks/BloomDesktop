import { ProblemDialog, ProblemKind } from "./ProblemDialog";

const message =
    "Fake error with a line break<br> and <b>bold</b> and <a href='https://google.com'>link</a>...";

export default {
    title: "Problem Report/ProblemDialog",
};

export const ProblemDialogProblemKindFatal = () => (
    <ProblemDialog level={ProblemKind.Fatal} message={message} />
);

ProblemDialogProblemKindFatal.story = {
    name: "ProblemDialog ProblemKind.Fatal",
};

export const ProblemDialogNotifyWithReport = () => (
    <ProblemDialog
        level={ProblemKind.Notify}
        message={message}
        reportLabel="Report"
    />
);

ProblemDialogNotifyWithReport.story = {
    name: "ProblemDialog notify with Report",
};

export const ProblemDialogNotifyWithSecondaryButton = () => (
    <ProblemDialog
        level={ProblemKind.Notify}
        message={message}
        secondaryLabel="Secondary"
    />
);

ProblemDialogNotifyWithSecondaryButton.story = {
    name: "ProblemDialog notify with secondary button",
};

export const ProblemDialogNotifyWithBoth = () => (
    <ProblemDialog
        level={ProblemKind.Notify}
        message={message}
        reportLabel="Report"
        secondaryLabel="Secondary"
    />
);

ProblemDialogNotifyWithBoth.story = {
    name: "ProblemDialog notify with both",
};
