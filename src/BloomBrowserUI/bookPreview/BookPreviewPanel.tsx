// Display the book preview in an iframe with an optional book information panel below it.

import * as React from "react";
import { useState } from "react";
import { BloomApi } from "../utils/bloomApi";
import "./BookPreviewPanel.less";
import { useSubscribeToWebSocketForStringMessage } from "../utils/WebSocketManager";
import { TeamCollectionBookStatusPanel } from "../teamCollection/TeamCollectionBookStatusPanel";

const urlParams = new URLSearchParams(window.location.search);
const urlPreview = urlParams.get("urlPreview") ?? "about:blank";

export const BookPreviewPanel: React.FunctionComponent = props => {
    const [isTeamCollection, setIsTeamCollection] = useState(false);
    const [previewUrl, setPreviewUrl] = useState(urlPreview);

    React.useEffect(() => {
        BloomApi.getBoolean(
            "teamCollection/isTeamCollectionEnabled",
            teamCollection => setIsTeamCollection(teamCollection)
        );
    }, [previewUrl]);

    useSubscribeToWebSocketForStringMessage(
        "bookStatus",
        "changeBook",
        message => setPreviewUrl(message)
    );

    return (
        <div>
            <div id="preview" className={isTeamCollection ? "abovePanel" : ""}>
                <iframe src={previewUrl} height="100%" width="100%" />
            </div>
            {isTeamCollection ? (
                <div id="teamCollection">
                    <TeamCollectionBookStatusPanel />
                </div>
            ) : (
                <div />
            )}
        </div>
    );
};
