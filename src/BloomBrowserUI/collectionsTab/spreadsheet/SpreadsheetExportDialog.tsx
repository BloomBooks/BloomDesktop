/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useL10n } from "../../react_components/l10nHooks";
import BloomButton from "../../react_components/bloomButton";
import { postData } from "../../utils/bloomApi";
import {
    BloomDialog,
    DialogMiddle,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogTitle,
    IBloomDialogProps
} from "../../react_components/BloomDialog/BloomDialog";
import {
    DialogCancelButton,
    DialogFolderChooser
} from "../../react_components/BloomDialog/commonDialogComponents";

import { kVerticalSpacingBetweenDialogSections } from "../../bloomMaterialUITheme";
import { ExperimentalBadge } from "../../react_components/experimentalBadge";
import { Div } from "../../react_components/l10nComponents";
import { useEventLaunchedBloomDialog } from "../../react_components/BloomDialog/BloomDialogPlumbing";
import { WarningBox } from "../../react_components/boxes";

export const SpreadsheetExportDialogLauncher: React.FunctionComponent<{}> = () => {
    const {
        openingEvent,
        closeDialog,
        propsForBloomDialog
    } = useEventLaunchedBloomDialog("SpreadsheetExportDialog");

    // We extract the core here so that we can avoid running most of the hook code when this dialog is not visible.
    return propsForBloomDialog.open ? (
        <SpreadsheetExportDialog
            closeDialog={closeDialog}
            propsForBloomDialog={propsForBloomDialog}
            folderPath={openingEvent.folderPath}
        />
    ) : null;
};

const SpreadsheetExportDialog: React.FunctionComponent<{
    closeDialog: () => void;
    propsForBloomDialog: IBloomDialogProps;
    folderPath: string;
}> = props => {
    const title = useL10n(
        "Export to Spreadsheet...",
        "CollectionTab.BookMenu.ExportToSpreadsheet" // same as the menu
    );
    const chooseFolderDescription = useL10n(
        "Target folder for the spreadsheet and images:",
        "Spreadsheet.ExportDialog.folderLabel"
    );
    const [folderPath, setFolderPath] = React.useState(props.folderPath);

    return (
        <BloomDialog {...props.propsForBloomDialog}>
            <DialogTitle title={title}>
                <ExperimentalBadge />
            </DialogTitle>
            <DialogMiddle
                css={css`
                    // our geckofx60 doesn't support this. So there is a <p></p> below instead
                    // gap: ${kVerticalSpacingBetweenDialogSections};
                `}
            >
                <Div l10nKey="Spreadsheet.ExportDialog.Description">
                    Bloom will create a spreadsheet containing the text and
                    images of this book. You can then make changes, like making
                    new translations. Finally, use the “Import &amp; Update from
                    Spreadsheet...” command to bring your changes back in.
                </Div>
                <p></p>
                <WarningBox>
                    <span>
                        This feature is still a work in progress. If you change
                        the text after recording audio and before exporting, the
                        audio may not import correctly. There may be other
                        export and import limitations we are not aware of.
                    </span>
                </WarningBox>
                <p></p>
                <div>{chooseFolderDescription}</div>

                <DialogFolderChooser
                    path={folderPath}
                    setPath={setFolderPath}
                    description={chooseFolderDescription}
                    forOutput={true}
                />
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogBottomLeftButtons>
                    {/* Currently we don't really have any help for this.

                    <HelpLink
                        helpId="Tasks/Basic_tasks/Export_to_Spreadsheet.htm"
                        l10nKey="Common.Help"
                    >
                        Help
                    </HelpLink> */}
                </DialogBottomLeftButtons>
                <BloomButton
                    enabled={true}
                    variant="contained"
                    l10nKey="Spreadsheet.ExportDialog.ExportButton"
                    hasText={true}
                    size="medium"
                    onClick={() => {
                        postData("spreadsheet/export", {
                            parentFolderPath: folderPath
                        });
                        props.closeDialog();
                    }}
                >
                    Export
                </BloomButton>
                <DialogCancelButton onClick_DEPRECATED={props.closeDialog} />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
