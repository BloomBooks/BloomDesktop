import * as React from "react";
import { storiesOf } from "@storybook/react";
import { SpreadsheetExportDialogLauncher } from "./SpreadsheetExportDialog";
import { StorybookDialogWrapper } from "../../react_components/BloomDialog/BloomDialogPlumbing";

storiesOf("Spreadsheet Export Dialog", module).add("Default", () => (
    <StorybookDialogWrapper
        id="SpreadsheetExportDialog"
        params={{ folderPath: "a path to somewhere" }}
    >
        <SpreadsheetExportDialogLauncher />
    </StorybookDialogWrapper>
));
