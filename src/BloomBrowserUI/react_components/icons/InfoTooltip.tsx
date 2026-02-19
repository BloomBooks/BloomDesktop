import { css } from "@emotion/react";
import * as React from "react";
import {
    ILocalizationProps,
    Span,
} from "../../react_components/l10nComponents";
import { default as InfoIcon } from "@mui/icons-material/InfoOutlined";
import { Popover } from "@mui/material";

interface IProps extends ILocalizationProps {
    color?: string; // of the info icon
    size?: string; // of the info icon
}

// Displays an info icon. When hovered over (or clicked) it displays the message indicated by its props
// (as a Span, centered below the icon, if there is room).
// Enhance: eventually we may want additional props to control things like placement, or another version
// that is more flexible about what children may be.
export const InfoTooltip: React.FunctionComponent<IProps> = (props) => {
    // controls visibility and placement of the 'tooltip' on the info icon when bookdata is disabled.
    const [anchorEl, setAnchorEl] = React.useState<SVGSVGElement | null>(null);
    const tooltipOpen = Boolean(anchorEl);
    const handlePopoverOpen = (event: React.MouseEvent<SVGSVGElement>) => {
        setAnchorEl(event.currentTarget);
    };
    const { color, size, className, ...localizationProps } = props;
    return (
        <div>
            <InfoIcon
                className={className} // pick up any css from the emotion props of the caller
                css={css`
                    ${color ? "color: " + color : ""};
                    ${size ? "font-size: " + size : ""};
                `}
                onMouseEnter={handlePopoverOpen}
                onMouseLeave={() => setAnchorEl(null)}
            ></InfoIcon>
            <Popover
                // We use this for a more controllable tooltip than we get with titleAccess on the icon.
                id={"popover-info-tooltip"}
                css={css`
                    // This is just an informational popover, we don't need to suppress events outside it.
                    // Even more importantly, we don't want to prevent the parent control from receiving
                    // the mouse-move events that would indicate the mouse is no longer over the anchor
                    // and so the popover should be removed!
                    pointer-events: none;
                `}
                // This might be a better way to do it in material-ui 5? Not in V4 API, but in MUI examples.
                // sx={{
                //     pointerEvents: 'none',
                //   }}
                open={tooltipOpen}
                anchorEl={anchorEl}
                anchorOrigin={{
                    vertical: "bottom",
                    horizontal: "center",
                }}
                transformOrigin={{
                    // 15 pixels below the bottom (based on anchorOrigin) of the anchor;
                    // leaves room for arrow and a bit of margin.
                    vertical: -15,
                    horizontal: "center",
                }}
                onClose={() => setAnchorEl(null)}
                disableRestoreFocus // most MUI examples have this, not sure what it does.
            >
                <div
                    css={css`
                        padding: 2px 4px;
                        max-width: 150px;
                        font-size: smaller;
                    `}
                >
                    <Span {...localizationProps}></Span>
                </div>
            </Popover>
        </div>
    );
};
