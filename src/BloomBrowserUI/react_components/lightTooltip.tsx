import * as React from "react";
import { styled } from "@mui/material/styles";
import Tooltip, { TooltipProps, tooltipClasses } from "@mui/material/Tooltip";

// A tooltip which looks more like the default tooltips we get when we just put
// a title on an element. It's probably not a perfect match but hopefully close
// enough not to be noticeable.
// I copied this from a material-ui example. I tried to convert it to emotion
// but there is so much magic in it that I found it too difficult. The main
// difficulty is that the tooltip is inserted at the top level and is not
// a child of the element that has the tooltip (or anything near it), and the
// classes that MUI puts on it have hashes in and are not predictable.
// The combination of these problems makes it very hard to style the actual
// tooltip content. I think somehow the tooltipClasses must provide access to
// the magic class name.
export const LightTooltip = styled(({ className, ...props }: TooltipProps) => (
    <Tooltip
        // These delays seem to be helpful in allowing the pointer to move from the
        // wrapped control into the tooltip without activating a different tooltip from another
        // control that we might cross over on the way.
        // They are common to the two current clients, TickableBox nd MuiCheckBox,
        // but can be overridden by callers since props passed in will take precedence.
        enterDelay={500}
        enterNextDelay={300}
        leaveDelay={200}
        {...props}
        classes={{ popper: className }}
    />
))(({ theme }) => ({
    [`& .${tooltipClasses.tooltip}`]: {
        backgroundColor: theme.palette.common.white,
        color: "rgba(0, 0, 0, 0.87)",
        border: "solid 1px rgba(0, 0, 0, 0.87)",
        fontSize: 11,
        // The default is to put 14px on the side towards the thing labeled.
        // But we are not currently using this with arrows that we need space for,
        // and we ARE using them with links inside. If there is a gap between the
        // thing described and the tooltip, it is hard to move the pointer to the
        // link without the tooltip going away.
        margin: "1px !important"
    }
}));
