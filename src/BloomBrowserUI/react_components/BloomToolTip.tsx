/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import React = require("react");
import { default as InfoIcon } from "@mui/icons-material/InfoOutlined";
import { Popover, PopoverOrigin, Typography } from "@mui/material";

// This class supports adding a tooltip to its children.
// We use this for a more controllable tooltip than we get with title and similar properties.
// We use Popover rather than various more obvious React components partly because of some
// dependency incompatibilities with our version of Storybook.
// BloomTooltip basically displays its children. When hovered over (including when clicked)
// it displays the message indicated by tooltipContent
// (currently at a fixed position below the root, but we may want to make this more
// configurable).
export const BloomTooltip: React.FunctionComponent<{
    // The color to use for the background of the tooltip, and, crucially, also for the
    // arrow that points up at the thing described. This should usually be somewhat
    // contrastive with the background of the children, but it also needs to contrast
    // with the content, currently set to white unless the tooltipContent element overrides.)
    tooltipBackColor: string;
    // The Children which this primarily displays may (when hovered over) display a popup.
    // Usually the BloomTooltip controls for itself whether this is shown; but we also permit
    // this behavior to work in the controlled component mode, where the client controls
    // its visibility. Technically, the appearance of the popup is controlled by keeping
    // track of which component it is anchored to (if visible), or storing a null if it
    // isn't. If the component is controlled, the client provides changePopupAnchor to
    // receive notification that the control wishes to change this (because it is hovered over),
    // and popupAnchorElement to actually control the presence (and placement) of the popup.
    // The client should normally change popupAnchorElement to whatever changePopupAnchor
    // tells it to, but may also set it to null to force the popup closed. It probably doesn't
    // make sense to set it to anything other than a value received from changePopupAnchor or null.
    changePopupAnchor?: (anchor: HTMLElement | null) => void;
    popupAnchorElement?: HTMLElement | null;
    tooltipContent: React.ReactNode;
    // Good practice with a popup calls for some aria attributes to indicate the relationship
    // of the popup to its parent, so we need a unique ID. The supplied ID will be applied to the popup.
    // Enhance: there's probably some way we could come up with a safe default for this.
    id: string;
    side?: "bottom" | "left";
}> = props => {
    // controls visibility and placement of the 'tooltip' (if props.changePopupAnchor is null).
    const [
        localAnchorEl,
        setLocalAnchorEl
    ] = React.useState<HTMLElement | null>(null);

    // Handle an event that should open the popover.
    const handlePopoverOpen = (event: React.MouseEvent<HTMLElement>) => {
        if (props.changePopupAnchor) {
            props.changePopupAnchor(event.currentTarget);
        } else {
            setLocalAnchorEl(event.currentTarget);
        }
    };

    const anchorEl = props.changePopupAnchor
        ? props.popupAnchorElement
        : localAnchorEl;
    const tooltipOpen = Boolean(anchorEl);

    const anchorOrigin: PopoverOrigin =
        props.side == "left"
            ? {
                  vertical: "top",
                  // 10 pixels left of the anchor; leaves room for arrow and a little margin.
                  horizontal: -20
              }
            : {
                  vertical: "bottom",
                  horizontal: "center"
              };

    const transformOrigin: PopoverOrigin =
        props.side == "left"
            ? {
                  vertical: "top",
                  horizontal: "right"
              }
            : {
                  // 15 pixels below the bottom (based on anchorOrigin) of the anchor;
                  // leaves room for arrow and a bit of margin.
                  vertical: -15,
                  horizontal: "center"
              };

    const arrowSize = 8;
    // This css is used for the div that makes the arrow on the popover.
    // I have not made it smart enough to move around if popover
    // gets smart and places the popover in an unexpected place
    // (e.g., so it fits in the window). This control is currently used in small
    // enough screen regions that I don't think this is likely to happen.
    const arrowCss =
        props.side === "left"
            ? css`
                  border: solid ${arrowSize}px;
                  position: absolute;
                  border-color: transparent;
                  right: -${arrowSize * 2}px;
                  top: 5px;
                  border-left-color: ${props.tooltipBackColor};
              `
            : css`
                  border: solid ${arrowSize}px;
                  position: absolute;
                  border-color: transparent;
                  top: ${1 - 2 * arrowSize}px;
                  left: calc(50% - ${arrowSize / 2}px);
                  border-bottom-color: ${props.tooltipBackColor};
              `;

    // Handle an event that should close the popover.
    const handlePopoverClose = () => {
        if (props.changePopupAnchor) {
            props.changePopupAnchor(null);
        } else {
            setLocalAnchorEl(null);
        }
    };

    return (
        <div
            aria-owns={tooltipOpen ? props.id : undefined}
            aria-haspopup="true"
            onMouseEnter={handlePopoverOpen}
            onMouseLeave={handlePopoverClose}
        >
            {props.children}
            <Popover
                id={"popover-info-tooltip"}
                css={css`
                    // This is just an informational popover, we don't need to suppress events outside it.
                    // Even more importantly, we don't want to prevent the parent control from receiving
                    // the mouse-move events that would indicate the mouse is no longer over the anchor
                    // and so the popover should be removed!
                    pointer-events: none;
                    .MuiPopover-paper {
                        // This allows the arrow to be seen. (If instead we try to make the arrow be
                        // inside the main content area of the popover, it is impossible to get the
                        // right background color to make the area either side of the arrow look right.
                        // The popover div is added at the root level so that the whole thing doesn't
                        // get clipped; therefore, a transparent background doesn't 'see' the thing that
                        // it seems, visibly, to be on top of. And the background is very variable, as it
                        // might be over a selected item, an unselected item, the shadow that gets created
                        // around the popover, a combination of the above...)
                        overflow: visible !important;
                    }
                `}
                // This might be a better way to do it in material-ui 5? Not in V4 API, but in MUI examples.
                // sx={{
                //     pointerEvents: 'none',
                //   }}
                open={tooltipOpen}
                anchorEl={anchorEl}
                anchorOrigin={anchorOrigin}
                transformOrigin={transformOrigin}
                onClose={handlePopoverClose}
                disableRestoreFocus // most MUI examples have this, not sure what it does.
            >
                <Typography
                    // We need our standard Typography here because when rendered it's not an actual
                    // child (in the DOM) of the thing its a (React) child of. It gets put in a popover
                    // at the root level. So we need to pull in our standard text appearance.
                    component="div"
                    css={css`
                        position: relative;
                    `}
                >
                    <div css={arrowCss}></div>
                    <div
                        css={css`
                            background-color: ${props.tooltipBackColor};
                            color: white;
                            border-radius: 4px;
                            padding: 4px 8px;
                            position: relative;
                        `}
                    >
                        {props.tooltipContent}
                    </div>
                </Typography>
            </Popover>
        </div>
    );
};
