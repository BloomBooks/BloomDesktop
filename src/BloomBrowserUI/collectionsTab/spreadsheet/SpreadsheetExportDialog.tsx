/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { useL10n } from "../../react_components/l10nHooks";
import BloomButton from "../../react_components/bloomButton";
import { BloomApi } from "../../utils/bloomApi";
import {
    BloomDialog,
    DialogMiddle,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogTitle,
    useSetupBloomDialog,
    IBloomDialogEnvironmentParams
} from "../../react_components/BloomDialog/BloomDialog";
import HelpLink from "../../react_components/helpLink";
import {
    DialogCancelButton,
    DialogFolderChooser
} from "../../react_components/BloomDialog/commonDialogComponents";
import { WireUpForWinforms } from "../../utils/WireUpWinform";

import { kVerticalSpacingBetweenDialogSections } from "../../bloomMaterialUITheme";
import { ExperimentalBadge } from "../../react_components/experimentalBadge";

export const SpreadsheetExportDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    var title = useL10n(
        "Export to spreadsheet",
        "Spreadsheet.ExportDialogTitle"
    );

    const { closeDialog, propsForBloomDialog } = useSetupBloomDialog(
        props.dialogEnvironment
    );

    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle title={title}>
                <ExperimentalBadge />
            </DialogTitle>
            <DialogMiddle
                css={css`
                    gap: ${kVerticalSpacingBetweenDialogSections};
                `}
            >
                <div>
                    Bloom will create a spreadsheet containing the text and
                    images of this book. You can then make changes, like making
                    new translations. Finally, use the “Import &amp; Update from
                    Spreadsheet...” command to bring your changes back in.
                </div>
                <div>
                    <div>Target folder for the spreadsheet and images:</div>
                    <DialogFolderChooser
                        // TODO: wire this up
                        apiCommandToChooseAndSetFolder=""
                        // TODO: should DialogFolderChooser also be getting this from the same API?
                        // I.e. /spreadsheet-folder  endpoint would have a get and put
                        path=""
                    />
                </div>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogBottomLeftButtons>
                    <HelpLink
                        helpId="Tasks/Basic_tasks/Export_to_Spreadsheet.htm"
                        l10nKey="Common.Help"
                    >
                        Help
                    </HelpLink>
                </DialogBottomLeftButtons>
                <BloomButton
                    enabled={true}
                    variant="contained"
                    l10nKey="Spreadsheet.Export"
                    hasText={true}
                    size="medium"
                    // TODO: make it do something, then close
                    onClick={closeDialog}
                >
                    Export
                </BloomButton>
                <DialogCancelButton
                    onClick={() => {
                        // TODO: If we can have a generic cancel api, then we can also move this onClick to the DialogCancelButton Class
                        BloomApi.post("common/cancel");
                        closeDialog();
                    }}
                />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

WireUpForWinforms(SpreadsheetExportDialog);
