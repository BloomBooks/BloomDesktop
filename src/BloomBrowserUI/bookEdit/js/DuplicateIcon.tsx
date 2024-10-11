import * as React from "react";
import { SvgIcon, SvgIconProps } from "@mui/material";

export const DuplicateIcon: React.FunctionComponent<SvgIconProps> = props => (
    <SvgIcon {...props}>
        <path d="M0 0h24v24H0V0z" fill="none" />
        <path d="M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z" />
        <line
            x1="13.5"
            y1="10.5"
            x2="13.5"
            y2="16.5"
            stroke="currentColor"
            strokeWidth="2"
        />
        <line
            x1="10.5"
            y1="13.5"
            x2="16.5"
            y2="13.5"
            stroke="currentColor"
            strokeWidth="2"
        />
    </SvgIcon>
);
