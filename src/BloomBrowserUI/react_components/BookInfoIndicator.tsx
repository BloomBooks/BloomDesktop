/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { Link } from "@mui/material";
import InfoOutlinedIcon from "@mui/icons-material/InfoOutlined";
import WarningIcon from "@mui/icons-material/Warning";
import InfoIcon from "@mui/icons-material/Info";
import { BloomTooltip } from "./BloomToolTip";
import { postJson, useApiObject, useWatchApiData } from "../utils/bloomApi";

export const BookInfoIndicator: React.FunctionComponent<{
    bookId: string;
}> = props => {
    type IndicatorInfo = {
        id: string;
        factoryInstalled: boolean;
        path: string;
        cssThemeName: string;
        firstPossiblyOffendingCssFile: string;
        substitutedCssFile: string;
        error: string;
    };
    const info = useWatchApiData<IndicatorInfo | undefined>(
        `book/indicatorInfo?id=${props.bookId}`,
        undefined,
        "book",
        "indicatorInfo"
    );
    const firstPossiblyConflictingCss =
        info?.firstPossiblyOffendingCssFile ?? "";
    const theme = info?.cssThemeName ?? "";

    const tip = info && (
        <div>
            <p>
                <b>Path on disk</b>
                <br />
                <Link
                    onClick={() => {
                        postJson(
                            "fileIO/showInFolder",
                            JSON.stringify({ folderPath: info.path })
                        );
                    }}
                >
                    {info.path}
                </Link>
            </p>
            <p>
                <b>Book ID</b>
                <br />
                {info.id}
            </p>

            <p>
                <b>Theme</b>
                <br />
                {info.cssThemeName}
            </p>
            {
                // The logic that shows one or none of these three messages is similar to that in BookSettingsDialog.
                // See the comment there.
            }
            {firstPossiblyConflictingCss && theme === "legacy-5-6" && (
                <div>
                    <WarningIcon
                        css={css`
                            position: relative;
                            top: 2px; // to align with text
                            margin-right: 5px;
                        `}
                        color="warning"
                        fontSize="small"
                    />
                    <span>
                        {`"The ${firstPossiblyConflictingCss}" stylesheet of this book is incompatible with
                                    modern themes. Bloom is using it because the book is using the Legacy-5-6 theme. Click (TODO) for more information.`}
                    </span>
                </div>
            )}
            {firstPossiblyConflictingCss === "customBookStyles.css" &&
                theme !== "legacy-5-6" && (
                    <div>
                        <span>
                            <InfoIcon
                                css={css`
                                    position: relative;
                                    top: 2px;
                                    margin-right: 5px;
                                `}
                                fontSize="small"
                            />
                            {`"The ${firstPossiblyConflictingCss}" stylesheet of this book is incompatible with
                                    modern themes. Bloom is currently ignoring it. If you don't need those
                                    customizations any more, you can delete your customBookStyles.css. Click (TODO) for more information.`}
                        </span>
                    </div>
                )}
            {firstPossiblyConflictingCss &&
                firstPossiblyConflictingCss !== "customBookStyles.css" &&
                theme !== "legacy-5-6" && (
                    <span>
                        <InfoIcon
                            css={css`
                                position: relative;
                                top: 2px;
                                margin-right: 5px;
                            `}
                            fontSize="small"
                        />
                        {`"The ${firstPossiblyConflictingCss}" stylesheet of this book is incompatible with
                                    modern themes. Bloom is currently ignoring it. Click (TODO) for more information.`}
                    </span>
                )}
        </div>
    );

    return info === undefined ||
        info.factoryInstalled ||
        info.error ||
        // we don't show if we don't have this because it is misleading to see info (instead of a warning) if we don't actually know
        !info.cssThemeName ? null : (
        <BloomTooltip enableClickInTooltip={true} tip={tip}>
            {firstPossiblyConflictingCss && theme === "legacy-5-6" ? (
                <WarningIcon color="warning" />
            ) : (
                <InfoOutlinedIcon color="primary" />
            )}
        </BloomTooltip>
    );
};
