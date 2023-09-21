/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { Card, CardContent, IconButton, Typography } from "@mui/material";
import InfoIcon from "@mui/icons-material/Info";
import WarningIcon from "@mui/icons-material/Warning";
import { BloomTooltip } from "./BloomToolTip";
import { useApiObject } from "../utils/bloomApi";

export const BookInfoIndicator: React.FunctionComponent<{
    bookId: string;
}> = props => {
    type IInfo = {
        id: string;
        path: string;
        cssThemeWeWillActuallyUse: string;
        firstPossiblyLegacyCss: string;
    };
    const info = useApiObject<IInfo | undefined>(
        `book/otherInfo?id=${props.bookId}`,
        undefined
    );

    const tip =
        info === undefined ? (
            ""
        ) : (
            // <Card sx={{ minWidth: 400 }}>
            //     <CardContent>
            //         {info.firstPossiblyLegacyCss && (
            //             <React.Fragment>
            //                 <Typography>
            //                     ⚠️{" "}
            //                     {`One of this book's stylesheets, "${info.firstPossiblyLegacyCss}", might not be fully
            //                 compatible with this version of Bloom. In order to preserve the layout, Bloom is showing this book using the "legacy" theme. See (TODO) for more
            //                 information.`}
            //                 </Typography>
            //                 <br />
            //             </React.Fragment>
            //         )}
            //         <Typography>
            //             {`Theme: ${info.cssThemeWeWillActuallyUse}`}
            //         </Typography>
            //         <Typography color="text.secondary">{`Book ID: ${info.id}`}</Typography>
            //     </CardContent>
            // </Card>
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
                <p>{`Path on disk: ${info.path}`}</p>
                <p>{`Book ID: ${info.id}`}</p>
                <p>{`Theme: ${info.cssThemeWeWillActuallyUse}`}</p>
            </div>
        );
    return info === undefined ? null : (
        <BloomTooltip tip={tip}>
            {info.firstPossiblyLegacyCss ? (
                <WarningIcon color="warning" />
            ) : (
                <InfoIcon color="primary" />
            )}
        </BloomTooltip>
    );
};
