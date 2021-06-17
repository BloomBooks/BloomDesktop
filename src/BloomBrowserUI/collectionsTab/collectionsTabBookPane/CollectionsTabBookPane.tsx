/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { useState } from "react";
import { BloomApi } from "../../utils/bloomApi";
import { TeamCollectionBookStatusPanel } from "../../teamCollection/TeamCollectionBookStatusPanel";
import { useSelectedBookId } from "../../utils/WebSocketManager";
import { Button } from "@material-ui/core";

export const CollectionsTabBookPane: React.FunctionComponent<{}> = props => {
    const [isTeamCollection, setIsTeamCollection] = useState(false);

    // this is just to make react refresh (and the browser not cache) since we're always asking for the same "book-preview/index.htm"
    const currentBookId = useSelectedBookId();

    React.useEffect(
        () => {
            BloomApi.getBoolean(
                "teamCollection/isTeamCollectionEnabled",
                teamCollection => setIsTeamCollection(teamCollection)
            );
        },
        [] /* means once and never again */
    );

    return (
        <div
            css={css`
                height: 100%;
                display: flex;
                flex: 1;
                flex-direction: column;
            `}
        >
            <div
                css={css`
                    margin-bottom: 10px;
                `}
            >
                <Button
                    color="primary"
                    variant="outlined"
                    // startIcon={<img src="../../images/edit.png" />}
                    css={css`
                        color: black !important;
                        background-color: white !important;
                        width: fit-content;
                    `}
                >
                    Edit this book
                </Button>
            </div>
            <div
                css={css`
                    // We want the preview to take up the available space, limiting the Team Collection panel
                    // (if present) to what it needs.
                    flex-grow: 4;
                    width: 100vw;
                    overflow-y: hidden; // inner iframe shows scrollbars as needed
                    overflow-x: hidden;
                `}
            >
                <iframe
                    src={`/book-preview/index.htm?dummy=${currentBookId}`}
                    height="100%"
                    width="100%"
                />
            </div>
            {isTeamCollection ? (
                <div id="teamCollection">
                    <TeamCollectionBookStatusPanel />
                </div>
            ) : null}
        </div>
    );
};
