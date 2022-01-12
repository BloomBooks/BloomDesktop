import * as React from "react";
import { storiesOf } from "@storybook/react";
import { SpreadsheetExportDialog } from "./SpreadsheetExportDialog";

storiesOf("Spreadsheet Export Dialog", module).add("Default", () => (
    <SpreadsheetExportDialog
        dialogEnvironment={{
            initiallyOpen: true,
            dialogFrameProvidedExternally: false
        }}
        folder={"somewhere"}
    />
));
