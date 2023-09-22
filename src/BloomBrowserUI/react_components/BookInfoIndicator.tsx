/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { Link } from "@mui/material";
import InfoOutlinedIcon from "@mui/icons-material/InfoOutlined";
import WarningIcon from "@mui/icons-material/Warning";
import { BloomTooltip } from "./BloomToolTip";
import { postJson, useApiObject } from "../utils/bloomApi";

export const BookInfoIndicator: React.FunctionComponent<{
    bookId: string;
}> = props => {
    type IInfo = {
        id: string;
        factoryInstalled: boolean;
        path: string;
        cssThemeWeWillActuallyUse: string;
        firstPossiblyOffendingCssFile: string;
        offendingCss: string;
        error: string;
    };
    const info = useApiObject<IInfo | undefined>(
        `book/otherInfo?id=${props.bookId}`,
        undefined
    );

    const tip =
        info === undefined ? (
            ""
        ) : (
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
                    {info.cssThemeWeWillActuallyUse}
                </p>

                {info.firstPossiblyOffendingCssFile && (
                    <React.Fragment>
                        <p>
                            ⚠️{" "}
                            {`One of this book's stylesheets, "${info.firstPossiblyOffendingCssFile}", might not be fully
                        compatible with this version of Bloom. In order to preserve the layout, Bloom is showing this book using the "legacy" theme. See (TODO) for more
                        information.`}
                        </p>
                        <div
                            css={css`
                                font-family: "Courier New", Courier, monospace;
                                max-height: 200px;
                                overflow-x: auto;
                                overflow-y: auto;
                                white-space: pre;
                            `}
                        >
                            {info.offendingCss}
                        </div>
                    </React.Fragment>
                )}
            </div>
        );

    return info === undefined ||
        info.factoryInstalled ||
        info.error ||
        // we don't show if we don't have this because it is misleading to see info (instead of a warning) if we don't actually know
        !info.cssThemeWeWillActuallyUse ? null : (
        <BloomTooltip enableClickInTooltip={true} tip={tip}>
            {info.firstPossiblyOffendingCssFile ? (
                <WarningIcon color="warning" />
            ) : (
                <InfoOutlinedIcon color="primary" />
            )}
        </BloomTooltip>
    );
};
