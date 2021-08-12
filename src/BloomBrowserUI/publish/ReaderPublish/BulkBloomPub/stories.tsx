import * as React from "react";

import { storiesOf } from "@storybook/react";
import { BulkBloomPubDialog } from "./BulkBloomPubDialog";
import { normalDialogEnvironmentForStorybook } from "../../../react_components/BloomDialog/BloomDialog";

storiesOf("Bulk Bloompub Dialog", module).add(
    "NotifyUser, Report & Retry",
    () => (
        <BulkBloomPubDialog
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    )
);
