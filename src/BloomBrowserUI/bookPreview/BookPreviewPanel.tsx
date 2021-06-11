// Display the book preview in an iframe with an optional book information panel below it.

import * as React from "react";
import { useState } from "react";
import { BloomApi } from "../utils/bloomApi";
import "./BookPreviewPanel.less";
import { useSubscribeToWebSocketForStringMessage } from "../utils/WebSocketManager";
import { TeamCollectionBookStatusPanel } from "../teamCollection/TeamCollectionBookStatusPanel";
import { WireUpForWinforms } from "../utils/WireUpWinform";

export const BookPreviewPanel: React.FunctionComponent<{
    initialBookPreviewUrl: string;
}> = props => {
    const [isTeamCollection, setIsTeamCollection] = useState(false);
    const [currentBookPreviewUrl, setCurrentBookPreviewUrl] = useState(
        props.initialBookPreviewUrl ?? "about:blank"
    );

    React.useEffect(() => {
        BloomApi.getBoolean(
            "teamCollection/isTeamCollectionEnabled",
            teamCollection => setIsTeamCollection(teamCollection)
        );
    }, [currentBookPreviewUrl]);

    useSubscribeToWebSocketForStringMessage(
        "bookStatus",
        "changeBook",
        message => setCurrentBookPreviewUrl(message)
    );

    return (
        <div id="preview-and-status-panel">
            <div id="preview" className={isTeamCollection ? "abovePanel" : ""}>
                <iframe
                    src={currentBookPreviewUrl}
                    height="100%"
                    width="100%"
                />
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

WireUpForWinforms(BookPreviewPanel);
