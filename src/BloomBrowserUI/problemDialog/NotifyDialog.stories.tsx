import { NotifyDialog } from "./NotifyDialog";

const message =
    "Fake error with a line break<br> and <b>bold</b> and <a href='https://google.com'>link</a>...";

export default {
    title: "Problem Report/NotifyDialog",
};

export const NotifyUserNonReportable = () => (
    <NotifyDialog reportLabel={null} secondaryLabel={null} message={message} />
);

NotifyUserNonReportable.story = {
    name: "NotifyUser, Non-Reportable",
};

export const NotifyUserReportable = () => (
    <NotifyDialog
        reportLabel="Report"
        secondaryLabel={null}
        message={message}
    />
);

NotifyUserReportable.story = {
    name: "NotifyUser, Reportable",
};

export const NotifyUserReportRetry = () => (
    <NotifyDialog
        reportLabel="Report"
        secondaryLabel="Retry"
        message={message}
    />
);

NotifyUserReportRetry.story = {
    name: "NotifyUser, Report & Retry",
};
