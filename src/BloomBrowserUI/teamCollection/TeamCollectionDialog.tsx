/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import BloomButton from "../react_components/bloomButton";
import { BloomApi } from "../utils/bloomApi";
import "./TeamCollectionDialog.less";
import { useL10n } from "../react_components/l10nHooks";
import { ProgressBox } from "../react_components/Progress/progressBox";
import { IBloomWebSocketProgressEvent } from "../utils/WebSocketManager";
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
export let showTeamCollectionDialog: () => void;

export const TeamCollectionDialog: React.FunctionComponent<{
    showReloadButton: boolean;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    // hoist this up to the window level so that any code that imports showTeamCollectionDialog can show it
    // (It will still have to be declared once at the app level when it is no longer launched in its own winforms dialog.)
    showTeamCollectionDialog = showDialog;

    const dialogTitle = useL10n(
        "Team Collection",
        "TeamCollection.TeamCollection"
    );

    const [events] = BloomApi.useApiObject<IBloomWebSocketProgressEvent[]>(
        "teamCollection/getLog",
        []
    );

    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle
                title={`${dialogTitle} (experimental)`}
                icon={"Team Collection.svg"}
                backgroundColor={kBloomBlue}
                color={"white"}
            />
            <DialogMiddle>
                <ProgressBox
                    preloadedProgressEvents={events}
                    css={css`
                        // If we have omitOuterFrame that means the dialog height is controlled by c#, so let the progress grow to fit it.
                        // Maybe we could have that approach *all* the time?
                        height: ${props.dialogEnvironment?.omitOuterFrame
                            ? "100%"
                            : "350px"};
                        // enhance: there is a bug I haven't found where, if this is > 530px, then it overflows. Instead, the BloomDialog should keep growing.
                        min-width: 530px;
                    `}
                />
            </DialogMiddle>

            <DialogBottomButtons>
                {props.showReloadButton && (
                    <DialogBottomLeftButtons>
                        <BloomButton
                            id="reload"
                            l10nKey="TeamCollection.Reload"
                            temporarilyDisableI18nWarning={true}
                            //variant="text"
                            enabled={true}
                            hasText={true}
                            onClick={() =>
                                BloomApi.post("common/reloadCollection")
                            }
                        >
                            Reload Collection
                        </BloomButton>
                    </DialogBottomLeftButtons>
                )}
                <DialogCloseButton
                    onClick={closeDialog}
                    // default action is to close *unless* we're showing reload
                    default={!props.showReloadButton}
                />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
