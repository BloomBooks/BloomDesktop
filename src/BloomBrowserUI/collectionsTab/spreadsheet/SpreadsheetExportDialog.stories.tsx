import { SpreadsheetExportDialogLauncher } from "./SpreadsheetExportDialog";
import { StorybookDialogWrapper } from "../../react_components/BloomDialog/BloomDialogPlumbing";

export default {
    title: "Spreadsheet Export Dialog",
};

export const Default = () => (
    <StorybookDialogWrapper
        id="SpreadsheetExportDialog"
        params={{ folderPath: "a path to somewhere" }}
    >
        <SpreadsheetExportDialogLauncher />
    </StorybookDialogWrapper>
);
