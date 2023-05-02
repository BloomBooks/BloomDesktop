/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useL10n } from "../react_components/l10nHooks";
import { Div } from "../react_components/l10nComponents";
import BloomButton from "../react_components/bloomButton";
import { post } from "../utils/bloomApi";
import { DialogCancelButton } from "../react_components/BloomDialog/commonDialogComponents";
import {
    BloomDialog,
    DialogTitle,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle
} from "../react_components/BloomDialog/BloomDialog";
import { kBloomRed } from "../utils/colorUtils";
import { WarningBox } from "../react_components/boxes";

// Dialog shown (when props.open is true) in response to the "Force Unlock (Administrator Only)..." menu item
// in the TeamCollectionBookStatusPanel.
export const ForceUnlockDialog: React.FunctionComponent<{
    open: boolean;
    close: () => void;
}> = props => {
    const title = useL10n(
        "Force Unlock (Administrator Only)",
        "TeamCollection.ForceUnlockTitle",
        undefined,
        undefined,
        undefined,
        true
    );
    return (
        <BloomDialog
            open={props.open}
            onClose={props.close}
            // This somewhat arbitrary limit makes the dialog more compact and looking more like the mock-up.
            css={css`
                max-width: 400px;
            `}
        >
            <DialogTitle title={title} />
            <DialogMiddle>
                <Div
                    css={css`
                        margin-bottom: 20px;
                    `}
                    l10nKey="TeamCollection.ForceUnlockDescription"
                    temporarilyDisableI18nWarning={true}
                >
                    This will make the book available for people to check out
                </Div>
                <Div
                    css={css`
                        margin-bottom: 20px;
                    `}
                    l10nKey="TeamCollection.ForceUnlockUseWhen"
                    temporarilyDisableI18nWarning={true}
                >
                    Use this when a teammate checks out a book but then loses a
                    computer or for some other reason cannot check in the book.
                </Div>
                <WarningBox>
                    <div>
                        <Div
                            css={css`
                                font-style: italic;
                                font-weight: bold;
                            `}
                            l10nKey="Warning"
                            temporarilyDisableI18nWarning={true}
                        >
                            Warning
                        </Div>
                        <Div
                            l10nKey="TeamCollection.ForceUnlockWarning"
                            temporarilyDisableI18nWarning={true}
                        >
                            If the teammate later tries to sync, their version
                            of the book will be moved to the "Lost &amp; Found"
                            folder. It will be inconvenient to retrieve any work
                            they did on that book.
                        </Div>
                    </div>
                </WarningBox>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogBottomLeftButtons>
                    <BloomButton
                        css={css`
                            color: ${kBloomRed} !important;
                            border-color: ${kBloomRed} !important;
                        `}
                        id="forceUnlock"
                        variant="outlined"
                        enabled={true}
                        l10nKey="TeamCollection.Unlock"
                        temporarilyDisableI18nWarning={true}
                        onClick={() => {
                            props.close();
                            // Do nothing here on either success or failure. (C# code will have already reported failure).
                            post("teamCollection/forceUnlock");
                        }}
                        hasText={true}
                    >
                        Unlock
                    </BloomButton>
                </DialogBottomLeftButtons>
                <DialogCancelButton
                    default={true}
                    onClick_DEPRECATED={props.close}
                />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
