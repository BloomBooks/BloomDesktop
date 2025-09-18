import { BulkBloomPubDialog } from "./BulkBloomPubDialog";
import { normalDialogEnvironmentForStorybook } from "../../../react_components/BloomDialog/BloomDialogPlumbing";

export default {
    title: "Bulk Bloompub Dialog",
};

export const NotifyUserReportRetry = () => (
    <BulkBloomPubDialog
        dialogEnvironment={normalDialogEnvironmentForStorybook}
    />
);

NotifyUserReportRetry.story = {
    name: "NotifyUser, Report & Retry",
};
