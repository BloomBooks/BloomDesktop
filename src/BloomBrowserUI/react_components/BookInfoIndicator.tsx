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
        firstPossiblyLegacyCss: string;
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
                {info.firstPossiblyLegacyCss && (
                    <React.Fragment>
                        <p>
                            ⚠️{" "}
                            {`One of this book's stylesheets, "${info.firstPossiblyLegacyCss}", might not be fully
                        compatible with this version of Bloom. In order to preserve the layout, Bloom is showing this book using the "legacy" theme. See (TODO) for more
                        information.`}
                        </p>
                    </React.Fragment>
                )}
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
                {info.cssThemeWeWillActuallyUse && (
                    <p>
                        <b>Theme</b>
                        <br />
                        {info.cssThemeWeWillActuallyUse}
                    </p>
                )}
            </div>
        );

    return info === undefined || info.factoryInstalled || info.error ? null : (
        <BloomTooltip enableClickInTooltip={true} tip={tip}>
            {info.firstPossiblyLegacyCss ? (
                <WarningIcon color="warning" />
            ) : (
                <InfoOutlinedIcon color="primary" />
            )}
        </BloomTooltip>
    );
};
