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
    When: string;
    Message: string;
    Type: number;
}

const kEventTypes = ["Check In"]; // REVIEW maybe better to do this in c# and just send it over?

export const CollectionHistoryTable: React.FunctionComponent = props => {
    const [events] = BloomApi.useApiObject<IBookHistoryEvent[]>(
        "teamCollection/getHistory",
        []
    );

    return (
        // The grand plan: https://www.figma.com/file/IlNPkoMn4Y8nlHMTCZrXfQSZ/Bloom-Collection-Tab?node-id=2707%3A6882
        // TODO: switch to use the same grid as blorg
        // TODO: get thumbnail in there
        // TODO: get user name
        // TODO: get user avatar

        <table>
            <tr>
                <td>Title</td> <td>When</td>
                <td>What</td>
                <td>Comment</td>
            </tr>
            {events.map((e, index) => (
                <tr key={index}>
                    <td>{e.Title}</td>

                    {/* Review: can we get away with this? I do want the 2021-11-01 format, and this gives that */}
                    <td>{e.When.substring(0, 10)}</td>
                    <td>{kEventTypes[e.Type]}</td>
                    <td>{e.Message}</td>
                </tr>
            ))}
        </table>
    );
};
