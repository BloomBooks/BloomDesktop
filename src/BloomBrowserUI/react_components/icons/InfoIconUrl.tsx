import * as React from "react";
import InfoOutlinedIcon from "@mui/icons-material/InfoOutlined";
import { BloomTooltip } from "../BloomToolTip";
import { TooltipProps } from "@mui/material";

// Shows an info icon that, when hovered, shows an anchor that the user can click
// to go to the page indicated.
export const InfoIconUrl: React.FunctionComponent<{
    href: string;
    className?: string; // carry in the css props from the caller
    placement?: TooltipProps["placement"];
    slotProps?: TooltipProps["slotProps"];
}> = props => {
    const tip = (
        <a href={props.href} target="_blank" rel="noopener noreferrer">
            {props.href}
        </a>
    );

    return (
        <BloomTooltip
            enableClickInTooltip={true}
            tip={tip}
            className={props.className}
            placement={props.placement}
            slotProps={props.slotProps}
        >
            <InfoOutlinedIcon color="primary" />
        </BloomTooltip>
    );
};
