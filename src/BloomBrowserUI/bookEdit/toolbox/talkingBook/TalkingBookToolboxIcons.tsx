import React = require("react");
import SvgIcon, { SvgIconProps } from "@mui/material/SvgIcon";

export const ImportIcon = (props: SvgIconProps) => (
    <SvgIcon {...props}>
        <path
            d="m4.8 1e-6c-1.332 0-2.4 1.08-2.4 2.4v19.2c0 0.63648 0.25286 1.2469 0.70294 1.697 0.45009 0.45012 1.0605 0.70296 1.6971 0.70296h14.4c0.63648 0 1.2469-0.25284 1.697-0.70296s0.70296-1.0606 0.70296-1.697v-14.4l-7.2-7.2zm8.4 1.8 6.6 6.6h-6.6zm-3.5763 12.129 3.3891-3.3844 3.4056 3.3766-3.3799 3.3799 2.5125 2.5628h-8.4725v-8.4709"
            fill="currentColor"
        />
    </SvgIcon>
);

export const UseTimingsFileIcon = (props: SvgIconProps) => (
    <SvgIcon width="15" height="16" viewBox="0 0 15 16" {...props}>
        <path fill="currentColor" />
    </SvgIcon>
);

export const InsertSegmentMarkerIcon = (props: SvgIconProps) => (
    <SvgIcon width="2" height="12" viewBox="0 0 2 12" fill="none" {...props}>
        <line
            x1="1"
            y1="12"
            x2="0.999999"
            y2="4.37115e-08"
            stroke="currentColor"
            strokeWidth="2"
        />
    </SvgIcon>
);
