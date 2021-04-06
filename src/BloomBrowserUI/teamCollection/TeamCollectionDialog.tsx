import * as React from "react";
import BloomButton from "../react_components/bloomButton";
import { BloomApi } from "../utils/bloomApi";
import "./TeamCollectionDialog.less";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import { DialogTitle, Typography } from "@material-ui/core";
import { useL10n } from "../react_components/l10nHooks";
import { useEffect, useState } from "react";

export const TeamCollectionDialog: React.FunctionComponent = props => {
    const dialogTitle = useL10n(
        "Team Collection",
        "TeamCollection.TeamCollection"
    );
    const [messages, setMessages] = useState([{ type: "", message: "" }]);
    useEffect(() => {
        BloomApi.get("teamCollection/getLog", result => {
            setMessages(result.data.messages);
        });
    }, []);
    const urlParams = new URLSearchParams(window.location.search);
    const showReloadButton = !!urlParams.get("showReloadButton");
    return (
        <ThemeProvider theme={theme}>
            <div id="team-collection-dialog">
                <DialogTitle className="dialog-title">
                    <Typography variant="h6">{dialogTitle}</Typography>
                </DialogTitle>
                <div id="messages">
                    {messages.map((m, index) => (
                        <div key={index}>
                            {<div className={m.type}>{m.message}</div>}
                        </div>
                    ))}
                </div>
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
                        variant={showReloadButton ? "outlined" : "contained"}
                        temporarilyDisableI18nWarning={true}
                        onClick={() => BloomApi.post("common/closeReactDialog")}
                    >
                        Close
                    </BloomButton>
                </div>
            </div>
        </ThemeProvider>
    );
};
