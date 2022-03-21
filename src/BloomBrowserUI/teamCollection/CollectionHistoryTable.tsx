/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { BloomApi } from "../utils/bloomApi";
import { BloomAvatar } from "../react_components/bloomAvatar";

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
}> = props => {
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
}> = props => {
    return (
        <td
            className={props.className}
            colSpan={props.colSpan}
            css={css`
                vertical-align: top;
                padding-top: 6px;
            `}
        >
            {props.children}
        </td>
    );
};

const kEventTypes = [
    "Check In",
    "Created",
    "Renamed",
    "Uploaded",
    "Force Unlock"
]; // REVIEW maybe better to do this in c# and just send it over?

export const CollectionHistoryTable: React.FunctionComponent = props => {
    const events = BloomApi.useApiData<IBookHistoryEvent[]>(
        "teamCollection/getHistory",
        []
    );

    return (
        // The grand plan: https://www.figma.com/file/IlNPkoMn4Y8nlHMTCZrXfQSZ/Bloom-Collection-Tab?node-id=2707%3A6882

        <table
            css={css`
                flex-grow: 1;
                td {
                    padding-right: 15px;
                }
            `}
        >
            <thead
                css={css`
                    //display: block;
                `}
            >
                <tr
                    css={css`
                        font-weight: 900;
                        margin-top: 10px;
                        margin-bottom: 5px;
                    `}
                >
                    <HeaderCell>When</HeaderCell>
                    <HeaderCell colSpan={2}>Title</HeaderCell>
                    <HeaderCell colSpan={2}>Who</HeaderCell>
                    <HeaderCell>What</HeaderCell>
                    <HeaderCell>Comment</HeaderCell>
                </tr>
            </thead>
            <tbody
                css={css`
                    //display: block;
                    //max-height: 71vh;
                    overflow-y: auto;
                    overflow-x: hidden;
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
                        <TextCell>
                            {/* Review: can we get away with this? I do want the 2021-11-01 format, and this gives that */}
                            {e.When.substring(0, 10)}
                        </TextCell>
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
                        <TextCell>{e.Title}</TextCell>
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
                        <TextCell>
                            <div
                                css={css`
                                    overflow-wrap: break-word;
                                    max-width: 5em;
                                `}
                            >
                                {e.UserName || e.UserId}
                            </div>
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
    );
};
