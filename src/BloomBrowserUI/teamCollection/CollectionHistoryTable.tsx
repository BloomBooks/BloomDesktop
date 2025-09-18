import { css } from "@emotion/react";

import * as React from "react";
import { useApiData } from "../utils/bloomApi";
import { BloomAvatar } from "../react_components/bloomAvatar";
import { useEffect, useState } from "react";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";

interface IBookHistoryEvent {
    Title: string;
    ThumbnailPath: string;
    When: string;
    Message: string;
    Type: number;
    UserId: string;
    UserName: string;
}

const HeaderCell: React.FunctionComponent<{
    colSpan?: number;
}> = (props) => {
    return (
        <td
            colSpan={props.colSpan}
            // This would be more natural on the row, but padding <tr> has no effect.
            css={css`
                padding-top: 10px;
                padding-bottom: 5px;
            `}
        >
            {props.children}
        </td>
    );
};

const TextCell: React.FunctionComponent<{
    className?: string;
    colSpan?: number;
}> = (props) => {
    return (
        <td
            className={props.className}
            colSpan={props.colSpan}
            css={css`
                vertical-align: top;
                padding-top: 6px;
            `}
            {...props}
        >
            {props.children}
        </td>
    );
};

// Keep this list in sync with the server-side enum in History\HistoryEvent.cs.
const kEventTypes = [
    "Check Out",
    "Check In",
    "Created",
    "Renamed",
    "Uploaded",
    "Forced Unlock",
    "Import Spreadsheet",
    "Sync Problem",
    "Deleted",
    "Moved",
]; // REVIEW maybe better to do this in c# and just send it over?

export const CollectionHistoryTable: React.FunctionComponent<{
    selectedBook?: string;
}> = (props) => {
    const currentBookOnly = !!props.selectedBook;
    // This is a trick to force the API call to run again when the selected book changes.
    const [generation, setGeneration] = useState(0);
    useEffect(() => setGeneration((gen) => gen + 1), [props.selectedBook]);
    // Likewise force a re-run when there is a new event.
    useSubscribeToWebSocketForEvent("bookHistory", "eventAdded", () =>
        setGeneration((gen) => gen + 1),
    );
    const events = useApiData<IBookHistoryEvent[]>(
        "teamCollection/getHistory" +
            (currentBookOnly
                ? "?currentBookOnly=true&generation=" + generation
                : ""),
        [],
    );

    return (
        // The grand plan: https://www.figma.com/file/IlNPkoMn4Y8nlHMTCZrXfQSZ/Bloom-Collection-Tab?node-id=2707%3A6882
        <div
            css={css`
                background-color: white;
                color: black;
                height: 100%;
                padding-left: 20px;
                padding-right: 20px;
                overflow: auto; // have a scrollbar if we need it
            `}
        >
            <table
                css={css`
                    td {
                        padding-right: 15px;
                    }
                `}
            >
                <thead>
                    <tr
                        css={css`
                            font-weight: 900;
                            margin-top: 10px;
                            margin-bottom: 5px;
                        `}
                    >
                        <HeaderCell>When</HeaderCell>
                        {currentBookOnly || (
                            <HeaderCell colSpan={2}>Title</HeaderCell>
                        )}
                        <HeaderCell colSpan={2}>Who</HeaderCell>
                        <HeaderCell>What</HeaderCell>
                        <HeaderCell>Comment</HeaderCell>
                    </tr>
                </thead>
                <tbody
                    css={css`
                        td {
                            vertical-align: top;
                        }
                    `}
                >
                    {events.map((e, index) => (
                        <tr
                            css={css`
                                margin-bottom: 5px;
                            `}
                            key={index}
                        >
                            <TextCell>{e.When.substring(0, 10)}</TextCell>
                            {currentBookOnly || (
                                <td
                                    css={css`
                                        padding-right: 4px !important;
                                    `}
                                >
                                    <img
                                        css={css`
                                            height: 2em;
                                        `}
                                        src={e.ThumbnailPath}
                                    />
                                </td>
                            )}
                            {currentBookOnly || <TextCell>{e.Title}</TextCell>}
                            <td
                                css={css`
                                    padding-right: 2px !important;
                                    // This is usually the highest element on the row. So it's a good place to put some
                                    // padding to separate the rows. Fine tuning the padding above in TextCell and the
                                    // padding here (currently all below) controls the alignment; we aim to have single-line
                                    // text centered on the avatar.
                                    padding-top: 0px;
                                    padding-bottom: 8px;
                                `}
                            >
                                <BloomAvatar
                                    email={e.UserId}
                                    name={e.UserName}
                                    avatarSizeInt={30}
                                />
                            </td>
                            <TextCell
                                css={css`
                                    overflow-wrap: break-word;
                                    max-width: 5em;
                                `}
                            >
                                {e.UserName || e.UserId}
                            </TextCell>
                            <TextCell
                                css={css`
                                    min-width: 4em;
                                `}
                            >
                                {kEventTypes[e.Type]}
                            </TextCell>
                            <TextCell>{e.Message}</TextCell>
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
};
