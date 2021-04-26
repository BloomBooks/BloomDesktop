import * as React from "react";
import BloomButton from "../react_components/bloomButton";
import { BloomApi } from "../utils/bloomApi";
import "./TeamCollectionDialog.less";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import { Typography } from "@material-ui/core";
import { useL10n } from "../react_components/l10nHooks";
import CloseOnEscape from "react-close-on-escape";
import { ProgressBox } from "../react_components/Progress/progressBox";
import { IBloomWebSocketProgressEvent } from "../utils/WebSocketManager";

export const TeamCollectionDialog: React.FunctionComponent<{}> = props => {
    const dialogTitle = useL10n(
        "Team Collection",
        "TeamCollection.TeamCollection"
    );

    const [events] = BloomApi.useApiObject<IBloomWebSocketProgressEvent[]>(
        "teamCollection/getLog",
        []
    );

    const urlParams = new URLSearchParams(window.location.search);
    const showReloadButton = !!urlParams.get("showReloadButton");
    return (
        <CloseOnEscape
            onEscape={() => {
                CloseDialog();
            }}
        >
            <ThemeProvider theme={theme}>
                <div id="team-collection-dialog">
                    <div className="title-bar">
                        <img
                            src={"Team Collection.svg"}
                            alt="Team Collection Icon"
                        />
                        <Typography variant="h4">{dialogTitle}</Typography>
                    </div>
                    <ProgressBox preloadedProgressEvents={events} />

                    <div className="align-right no-space-below">
                        {showReloadButton && (
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
                        )}
                        <BloomButton
                            l10nKey="Common.Close"
                            hasText={true}
                            enabled={true}
                            variant={
                                showReloadButton ? "outlined" : "contained"
                            }
                            temporarilyDisableI18nWarning={true}
                            onClick={() => CloseDialog()}
                        >
                            Close
                        </BloomButton>
                    </div>
                </div>
            </ThemeProvider>
        </CloseOnEscape>
    );
};

function CloseDialog() {
    BloomApi.post("common/closeReactDialog");
}
