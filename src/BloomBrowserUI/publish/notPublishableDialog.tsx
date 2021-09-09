// /** @jsx jsx **/
//import { jsx, css } from "@emotion/core";

import * as React from "react";
import { useL10n } from "../react_components/l10nHooks";
import { kBloomBlue } from "../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle,
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../react_components/BloomDialog/BloomDialog";
import { DialogCloseButton } from "../react_components/BloomDialog/commonDialogComponents";
import { ThemeProvider } from "@material-ui/styles";
import theme from "../bloomMaterialUITheme";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import BloomButton from "../react_components/bloomButton";
import { BloomApi } from "../utils/bloomApi";

export const NotPublishableDialog: React.FunctionComponent<{
    firstOverlayPage: string;
    bookTitle: string;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    const l10nPrefix = "PublishTab.NotPublishableDialog.";

    const dialogTitle = useL10n(
        "Not Publishable",
        l10nPrefix + "NotPublishableTitle"
    );

    const problemExplanationMessage = useL10n(
        "The book titled '{0}' uses Overlay elements. Overlay elements are a Bloom Enterprise feature.",
        l10nPrefix + "ProblemExplanation",
        "{0} will be replaced with the book's title. Everything inside the ' marks will be in bold.",
        props.bookTitle
    );

    // For some reason, this doesn't work. Apparently the bit that applies formatting,
    // happens before l10n returns. Putting the <b>s directly in the dialog works.
    // const getFinalProblemMessage = (): string => {
    //     const parts = initialProblemExplanationMessage.split("'");
    //     if (parts.length !== 3) {
    //         return initialProblemExplanationMessage;
    //     }
    //     return parts[0] + "<b>'" + parts[1] + "'</b>" + parts[2];
    // };

    const optionsMessage = useL10n(
        "In order to publish your book, you need to either activate Bloom Enterprise, or remove the Overlay elements from your book.",
        l10nPrefix + "Options"
    );

    const firstOverlayPageMessage = useL10n(
        "Page {0} is the first page that uses Overlay elements.",
        l10nPrefix + "FirstOverlayPage",
        "",
        props.firstOverlayPage
    );

    return (
        <BloomDialog {...propsForBloomDialog}>
            <ThemeProvider theme={theme}>
                <DialogTitle
                    title={dialogTitle}
                    icon={"../images/bloom-enterprise-badge.svg"}
                    backgroundColor={kBloomBlue}
                    color={"white"}
                />
                <DialogMiddle>
                    <p>
                        {problemExplanationMessage}
                        <br />
                        <br />
                        {optionsMessage}
                    </p>
                    <br />
                    {firstOverlayPageMessage}
                </DialogMiddle>

                <DialogBottomButtons>
                    <DialogBottomLeftButtons>
                        <BloomButton
                            id="settings"
                            l10nKey={l10nPrefix + "openSettingsButton"}
                            temporarilyDisableI18nWarning={true}
                            variant="outlined"
                            enabled={true}
                            hasText={true}
                            onClick={() =>
                                BloomApi.post(
                                    "common/showSettingsDialog?tab=enterprise"
                                )
                            }
                        >
                            Open Settings
                        </BloomButton>
                    </DialogBottomLeftButtons>
                    <DialogCloseButton onClick={closeDialog} />
                </DialogBottomButtons>
            </ThemeProvider>
        </BloomDialog>
    );
};

WireUpForWinforms(NotPublishableDialog);
