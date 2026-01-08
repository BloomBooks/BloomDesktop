import { css } from "@emotion/react";

import * as React from "react";
import { Link } from "@mui/material";
import InfoOutlinedIcon from "@mui/icons-material/InfoOutlined";
import WarningIcon from "@mui/icons-material/Warning";
import InfoIcon from "@mui/icons-material/Info";
import { BloomTooltip } from "./BloomToolTip";
import { postJson, useWatchApiData } from "../utils/bloomApi";
import {
    MessageIgnoringIncompatibleCss,
    MessageUsingMigratedThemeInsteadOfIncompatibleCss,
    MessageUsingLegacyThemeWithIncompatibleCss,
    MessageIgnoringIncompatibleCssCanDelete,
} from "../bookEdit/bookSettings/BookSettingsDialog";

export const BookInfoIndicator: React.FunctionComponent<{
    bookId: string;
}> = (props) => {
    type IndicatorInfo = {
        id: string;
        factoryInstalled: boolean;
        path: string;
        cssThemeName: string;
        firstPossiblyOffendingCssFile: string;
        migratedTheme: string;
        error: string;
    };
    const info = useWatchApiData<IndicatorInfo | undefined>(
        `book/indicatorInfo?id=${props.bookId}`,
        undefined,
        "book",
        "indicatorInfo",
    );
    const firstPossiblyConflictingCss =
        info?.firstPossiblyOffendingCssFile ?? "";
    const migratedTheme = info?.migratedTheme ?? "";
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
                            JSON.stringify({ folderPath: info.path }),
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

            {theme !== "none" && (
                <p>
                    <b>Theme</b>
                    <br />
                    {info.cssThemeName}
                </p>
            )}
            {
                // The logic that shows one or none of these four messages is similar to that in BookSettingsDialog.
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
                    <MessageUsingLegacyThemeWithIncompatibleCss
                        css={css`
                            display: inline;
                        `}
                        fileName={firstPossiblyConflictingCss}
                    />
                </div>
            )}
            {firstPossiblyConflictingCss === "customBookStyles.css" &&
                theme !== "legacy-5-6" && (
                    <div>
                        <InfoIcon
                            css={css`
                                position: relative;
                                top: 2px;
                                margin-right: 5px;
                            `}
                            fontSize="small"
                        />
                        {migratedTheme ? (
                            <MessageUsingMigratedThemeInsteadOfIncompatibleCss
                                css={css`
                                    display: inline;
                                `}
                                fileName={firstPossiblyConflictingCss}
                            />
                        ) : (
                            <MessageIgnoringIncompatibleCssCanDelete
                                css={css`
                                    display: inline;
                                `}
                                fileName={firstPossiblyConflictingCss}
                            />
                        )}
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
                        <MessageIgnoringIncompatibleCss
                            css={css`
                                display: inline;
                            `}
                            fileName={firstPossiblyConflictingCss}
                        />
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
