/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { BloomApi } from "../utils/bloomApi";
import theme, { kBloomYellow } from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import { useState } from "react";
import { useL10n } from "../react_components/l10nHooks";

import { BloomAvatar } from "../react_components/bloomAvatar";
import { string } from "prop-types";

interface IBookHistoryEvent {
    Title: string;
    ThumbnailPath: string;
    When: string;
    Message: string;
    Type: number;
    UserId: string;
    UserName: string;
}

const kEventTypes = ["Check In"]; // REVIEW maybe better to do this in c# and just send it over?

export const CollectionHistoryTable: React.FunctionComponent = props => {
    const events = BloomApi.useApiData<IBookHistoryEvent[]>(
        "teamCollection/getHistory",
        []
    );

    return (
        // The grand plan: https://www.figma.com/file/IlNPkoMn4Y8nlHMTCZrXfQSZ/Bloom-Collection-Tab?node-id=2707%3A6882
        // TODO: switch to use the same grid as blorg
        // TODO: get thumbnail in there
        // TODO: get user name
        // TODO: get user avatar

        <table
            css={css`
                td {
                    padding-right: 15px;
                }
            `}
        >
            <tr
                css={css`
                    font-weight: 900;
                    margin-top: 10px;
                    margin-bottom: 5px;
                `}
            >
                <td
                    colSpan={2}
                    // This would be more natural on the row, but padding <tr> has no effect.
                    css={css`
                        padding-top: 10px;
                        padding-bottom: 5px;
                    `}
                >
                    Title
                </td>{" "}
                <td>When</td>
                <td colSpan={2}>Who</td>
                <td>What</td>
                <td>Comment</td>
            </tr>
            {events.map((e, index) => (
                <tr
                    css={css`
                        margin-bottom: 5px;
                    `}
                    key={index}
                >
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
                    <td>{e.Title}</td>

                    {/* Review: can we get away with this? I do want the 2021-11-01 format, and this gives that */}
                    <td>{e.When.substring(0, 10)}</td>
                    <td
                        css={css`
                            padding-right: 2px !important;
                            padding-bottom: 8px; // this is to help separate rows
                        `}
                    >
                        <BloomAvatar
                            email={e.UserId}
                            name={e.UserName}
                            avatarSizeInt={30}
                        />
                    </td>
                    <td>
                        <div
                            css={css`
                                overflow-wrap: break-word;
                                max-width: 7em;
                            `}
                        >
                            {e.UserName || e.UserId}
                        </div>
                    </td>
                    <td>{kEventTypes[e.Type]}</td>
                    <td>{e.Message}</td>
                </tr>
            ))}
        </table>
    );
};
