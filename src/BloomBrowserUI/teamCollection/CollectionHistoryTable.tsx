import { css } from "@emotion/react";

import * as React from "react";
import { get, getBoolean, useApiData } from "../utils/bloomApi";
import { BloomAvatar } from "../react_components/bloomAvatar";
import { BloomTooltip } from "../react_components/BloomToolTip";
import WarningIcon from "@mui/icons-material/Warning";
import { kBloomRed } from "../utils/colorUtils";
import { useEffect, useState } from "react";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";
import {
    isCloudTeamCollection,
    useCloudCollectionId,
    useTeamCollectionCapabilities,
} from "./teamCollectionApi";

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
    children?: React.ReactNode;
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
    children?: React.ReactNode;
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

// Event types that represent an incident an admin should notice (per CONTRACTS.md: "recorded
// as a server-side incident event admins can see") rather than routine activity. Indices into
// kEventTypes above / BookHistoryEventType.cs: ForcedUnlock (5), SyncProblem (7) — the latter
// also covers the "repo won, local work saved to Lost & Found" case from the design doc.
const kIncidentEventTypes = new Set<number>([5, 7]);

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

    // Folder Team Collections: unchanged from before this task — same endpoint, same hook, same
    // behavior. This hook call itself is unconditional (React's rules of hooks), but its result
    // is only used below when !isCloud, so cloud Team Collections don't depend on it.
    const folderEvents = useApiData<IBookHistoryEvent[]>(
        "teamCollection/getHistory" +
            (currentBookOnly
                ? "?currentBookOnly=true&generation=" + generation
                : ""),
        [],
    );

    // Cloud Team Collections: history comes from the server events feed (CONTRACTS.md's
    // `get_changes` RPC, surfaced here as the mocked "sharing/history" endpoint) instead of the
    // local-file-derived endpoint folder Team Collections use above. While disconnected, fall
    // back to a local cache of the last-known events (mocked "sharing/historyCache" for now;
    // Wave 3 backs it with a real on-disk cache) rather than a live call that would just fail
    // offline. Branches on capability, never on concrete backend type.
    const capabilities = useTeamCollectionCapabilities();
    const isCloud = isCloudTeamCollection(capabilities);
    const cloudCollectionId = useCloudCollectionId();
    // Folder Team Collections must make zero extra requests, so (unlike TeamCollectionDialog.tsx,
    // which already called "teamCollection/isDisconnected" for folder Team Collections long
    // before this project) this only queries it when isCloud, following the same
    // guard-inside-the-effect pattern as teamCollectionApi.tsx's other Wave-2 hooks.
    // `undefined` means "not yet known" so the cloud fetch below can wait for it, rather than
    // firing an initial request against the wrong (live vs. cache) endpoint.
    const [disconnected, setDisconnected] = useState<boolean | undefined>(
        undefined,
    );
    useEffect(() => {
        if (!isCloud) return;
        getBoolean("teamCollection/isDisconnected", setDisconnected);
    }, [isCloud]);

    const [cloudEvents, setCloudEvents] = useState<IBookHistoryEvent[]>([]);
    useEffect(() => {
        if (!isCloud || disconnected === undefined) return;
        const cloudQuery =
            (currentBookOnly ? "currentBookOnly=true&" : "") +
            "collectionId=" +
            encodeURIComponent(cloudCollectionId) +
            "&generation=" +
            generation;
        get(
            (disconnected ? "sharing/historyCache?" : "sharing/history?") +
                cloudQuery,
            (result) =>
                setCloudEvents((result.data as IBookHistoryEvent[]) ?? []),
        );
    }, [isCloud, disconnected, cloudCollectionId, currentBookOnly, generation]);

    const events = isCloud ? cloudEvents : folderEvents;

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
                                {isCloud && kIncidentEventTypes.has(e.Type) && (
                                    <BloomTooltip
                                        tip={{
                                            l10nKey: "Warning",
                                            english: "Warning",
                                        }}
                                    >
                                        <WarningIcon
                                            data-testid="history-incident-icon"
                                            fontSize="small"
                                            css={css`
                                                color: ${kBloomRed};
                                                vertical-align: text-bottom;
                                                margin-right: 4px;
                                            `}
                                        />
                                    </BloomTooltip>
                                )}
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
