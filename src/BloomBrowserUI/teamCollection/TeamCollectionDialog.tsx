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
    DialogBottom,
    DialogMiddle,
    DialogTitle
} from "../react_components/BloomDialog/BloomDialog";
import { useState } from "react";

export let showTeamCollectionDialog: () => void;
export const TeamCollectionDialog: React.FunctionComponent<{
    omitOuterFrame: boolean;
}> = props => {
    const [open, setOpen] = useState(true);
    const dialogTitle = useL10n(
        "Team Collection",
        "TeamCollection.TeamCollection"
    );

    React.useEffect(() => (showTeamCollectionDialog = () => setOpen(true)), []);

    const [events] = BloomApi.useApiObject<IBloomWebSocketProgressEvent[]>(
        "teamCollection/getLog",
        []
    );

    const urlParams = new URLSearchParams(window.location.search);
    const showReloadButton = !!urlParams.get("showReloadButton");

    function close() {
        if (props.omitOuterFrame) BloomApi.post("common/closeReactDialog");
        else setOpen(false);
    }
    return (
        <BloomDialog
            open={open}
            omitOuterFrame={props.omitOuterFrame}
            onClose={close}
        >
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
                        height: ${props.omitOuterFrame ? "100%" : "400px"};
                        min-width: 540px;
                    `}
                />
            </DialogMiddle>

            <DialogBottom>
                {showReloadButton && (
                    <BloomButton
                        id="reload"
                        l10nKey="TeamCollection.Reload"
                        temporarilyDisableI18nWarning={true}
                        //variant="text"
                        enabled={true}
                        hasText={true}
                        onClick={() => BloomApi.post("common/reloadCollection")}
                    >
                        Reload Collection
                    </BloomButton>
                )}
                <BloomButton
                    l10nKey="Common.Close"
                    hasText={true}
                    enabled={true}
                    variant={showReloadButton ? "outlined" : "contained"}
                    temporarilyDisableI18nWarning={true}
                    onClick={close}
                    css={css`
                        float: right;
                    `}
                >
                    Close
                </BloomButton>
            </DialogBottom>
        </BloomDialog>
    );
};
