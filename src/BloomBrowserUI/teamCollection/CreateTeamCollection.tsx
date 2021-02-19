import { DialogTitle, Typography } from "@material-ui/core";
import * as React from "react";
import { useRef, useState } from "react";
import ReactDOM = require("react-dom");
import { BloomApi } from "../utils/bloomApi";
import WebSocketManager, {
    useWebSocketListenerForOneEvent
} from "../utils/WebSocketManager";
import BloomButton from "../react_components/bloomButton";
import "./CreateTeamCollection.less";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import { Div } from "../react_components/l10nComponents";
import { ExclaimTriangle } from "../react_components/ExclaimTriangle";

const kBloomBlue = "#1d94a4";

// Contents of a dialog launched from TeamCollectionSettingsPanel Creat Team Collection button.

interface IProps {
    closeDlg: () => void;
}

export const CreateTeamCollection: React.FunctionComponent<IProps> = props => {
    const [repoFolderPath, setRepoFolderPath] = useState("");
    const [problemReport, setProblemReport] = useState("");
    // This listener is waiting for results that are sent when the user clicks "Choose  shared folder"
    // and then selects a folder. We use a listener rather than having the API request return the
    // results to guard against a browser timeout on the request.
    const listener = e => {
        setRepoFolderPath(e.repoFolderPath);
        setProblemReport(e.problem);
    };
    useWebSocketListenerForOneEvent(
        "teamCollectionCreate",
        "shared-folder-path",
        listener,
        false
    );

    return (
        <ThemeProvider theme={theme}>
            <div id="create-team-collection-root">
                <div className="grow">
                    <Div
                        className="heading"
                        l10nKey="TeamCollection.CreateTeamCollection" // review: reuse same ID as button in main settings screen?
                        temporarilyDisableI18nWarning={true}
                    >
                        Create a Team Collection
                    </Div>
                    <Div
                        l10nKey="TeamCollection.HowTeamCollectionsWork"
                        temporarilyDisableI18nWarning={true}
                    >
                        Team Collections work by using a shared folder from a
                        LAN server, Dropbox, or other cloud provider.
                    </Div>
                    <Div
                        l10nKey="TeamCollection.StorageFolderLabel"
                        temporarilyDisableI18nWarning={true}
                    >
                        Cloud Storage Folder location (for example, your Dropbox
                        folder):
                    </Div>
                    <div>{repoFolderPath}</div>
                    <div className="align-right">
                        <BloomButton
                            l10nKey="TeamCollection.ChooseFolder"
                            className="teamCollection-heading"
                            enabled={true}
                            hasText={true}
                            variant="text"
                            temporarilyDisableI18nWarning={true}
                            // This will eventually timeout if the user doesn't choose a folder or cancel
                            // It doesn't matter because we ignore the result and are notified of the folder
                            // through the websocket.
                            onClick={() =>
                                BloomApi.post(
                                    "teamCollection/chooseFolderLocation",
                                    // nothing to do either on success or failure, including possible timeout,
                                    // or the user canceling.
                                    () => {},
                                    () => {}
                                )
                            }
                        >
                            Choose shared folder
                        </BloomButton>
                    </div>

                    {problemReport ? (
                        <div className="problem-report">{problemReport}</div>
                    ) : (
                        <div className="icon-row">
                            <ExclaimTriangle
                                triangleColor={kBloomBlue}
                                exclaimColor="white"
                            />
                            <Div
                                l10nKey="TeamCollection.MustBeDoneOnce"
                                temporarilyDisableI18nWarning={true}
                            >
                                This must only be done by one person in your
                                team. If instead you want to Join a Team
                                Collection that someone else has made, click
                                "Cancel"
                            </Div>
                        </div>
                    )}
                </div>

                <div className="align-right no-space-below">
                    <BloomButton
                        id="create-and-restart"
                        l10nKey="TeamCollection.CreateAndRestart"
                        hasText={true}
                        enabled={!!repoFolderPath && !problemReport}
                        temporarilyDisableI18nWarning={true}
                        onClick={() => {
                            BloomApi.post(
                                "teamCollection/createTeamCollection"
                            );
                        }}
                    >
                        Create &amp; Restart
                    </BloomButton>
                    <BloomButton
                        l10nKey="Common.Cancel"
                        hasText={true}
                        enabled={true}
                        variant="outlined"
                        temporarilyDisableI18nWarning={true}
                        onClick={() => props.closeDlg()}
                    >
                        Cancel
                    </BloomButton>
                </div>
            </div>
        </ThemeProvider>
    );
};
