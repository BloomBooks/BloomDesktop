import * as React from "react";
import InfoOutlinedIcon from "@mui/icons-material/InfoOutlined";
import { BloomTooltip } from "../BloomToolTip";
import { TooltipProps } from "@mui/material";
import { useL10n } from "../l10nHooks";

// Shows an info icon that, when hovered, shows an anchor that the user can click
// to go to the page indicated.
export const InfoIconUrl: React.FunctionComponent<{
    href: string;
    className?: string; // carry in the css props from the caller
    placement?: TooltipProps["placement"];
    slotProps?: TooltipProps["slotProps"];
    // optional, specifies the tooltip text. If not provided, it should just show the href,
    // but I haven't tested that.
    l10nKey?: string;
}> = (props) => {
    // English is deliberately empty. If we don't get an l10nKey, localizedTip will be empty,
    // so we should get the href below.
    const localizedTip = useL10n("", props.l10nKey ?? null);
    const tip = (
        <a href={props.href} target="_blank" rel="noopener noreferrer">
            {localizedTip || props.href}
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
