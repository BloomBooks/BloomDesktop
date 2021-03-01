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
        "TeamCollection.DialogTitle"
    );
    const [messages, setMessages] = useState([""]);
    useEffect(() => {
        BloomApi.get("teamCollection/getLog", result => {
            setMessages(result.data.messages);
        });
    }, []);
    return (
        <ThemeProvider theme={theme}>
            <div id="team-collection-dialog">
                <DialogTitle className="dialog-title">
                    <Typography variant="h6">{dialogTitle}</Typography>
                </DialogTitle>
                <div id="messages">
                    {messages.map((m, index) => (
                        <div key={index}>{m}</div>
                    ))}
                </div>
                <div className="align-right no-space-below">
                    <BloomButton
                        id="reload"
                        l10nKey="TeamCollection.Reload"
                        temporarilyDisableI18nWarning={true}
                        //variant="text"
                        enabled={true}
                        hasText={true}
                        onClick={() => BloomApi.post("teamCollection/reload")}
                    >
                        Reload Collection
                    </BloomButton>
                    <BloomButton
                        l10nKey="Common.Cancel"
                        hasText={true}
                        enabled={true}
                        variant="outlined"
                        temporarilyDisableI18nWarning={true}
                        onClick={() =>
                            BloomApi.post("teamCollection/closeDialog")
                        }
                    >
                        Cancel
                    </BloomButton>
                </div>
            </div>
        </ThemeProvider>
    );
};
