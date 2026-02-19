import * as React from "react";
import InfoOutlinedIcon from "@mui/icons-material/InfoOutlined";
import { TooltipProps } from "@mui/material";

// Shows an info icon that, when hovered, displays a tooltip users can click to open Bloom help content.
export const InfoIconHelp: React.FunctionComponent<{
    helpId: string;
    className?: string;
    placement?: TooltipProps["placement"];
    slotProps?: TooltipProps["slotProps"];
}> = (props) => {
    const helpUrl = React.useMemo(
        () => `/bloom/api/help?topic=${encodeURIComponent(props.helpId)}`,
        [props.helpId],
    );

    return (
        // Note that we do NOT set target="_blank" here. This is not because we want the
        // help to open in the same tab, but because the BloomServer will open a
        // completely different tool (not a browser at all). If we set target="_blank",
        // a new browser window opens and attempts to load the help URL, which results
        // in help opening, but the blank browser window remains.
        <a href={helpUrl} rel="noopener noreferrer" className={props.className}>
            <InfoOutlinedIcon color="primary" />
        </a>
    );
};
