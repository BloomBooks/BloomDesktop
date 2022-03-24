import * as React from "react";
import { storiesOf } from "@storybook/react";
import { SpreadsheetExportDialog } from "./SpreadsheetExportDialog";
import { StorybookDialogWrapper } from "../../react_components/BloomDialog/BloomDialogPlumbing";

storiesOf("Spreadsheet Export Dialog", module).add("Default", () => (
    <StorybookDialogWrapper
        id="SpreadsheetExportDialog"
        params={{ folderPath: "a path to somewhere" }}
    >
        <SpreadsheetExportDialog />
    </StorybookDialogWrapper>
));
