import * as React from "react";
import { SvgIcon, SvgIconProps } from "@mui/material";

// Icons for the four commands in the canvas element "Layer" submenu (BL-15992). Each shows a
// stack of layers with an arrow: one arrow moves one step, an arrow plus a second layer moves
// all the way. They are stroke drawings in currentColor, so they take the menu text color like
// the MUI icons beside them.

const strokeProps = {
    stroke: "currentColor",
    strokeWidth: 1.5,
    strokeLinecap: "round" as const,
    strokeLinejoin: "round" as const,
    fill: "none",
};

export const BringForwardIcon: React.FunctionComponent<SvgIconProps> = (
    props,
) => (
    <SvgIcon viewBox="0 0 24 24" {...props}>
        <path
            d="M15.89 11.5L19.287 13.06C21.096 13.892 22 14.308 22 15C22 15.692 21.096 16.109 19.287 16.94L14.394 19.187C13.214 19.729 12.624 20 12 20C11.376 20 10.786 19.729 9.606 19.187L4.713 16.939C2.904 16.11 2 15.693 2 15C2 14.307 2.904 13.891 4.713 13.06L8.11 11.5M12 4.5V15M15 7C14.41 6.393 12.84 4 12 4C11.16 4 9.59 6.393 9 7"
            {...strokeProps}
        />
    </SvgIcon>
);

export const BringToFrontIcon: React.FunctionComponent<SvgIconProps> = (
    props,
) => (
    <SvgIcon viewBox="0 0 24 24" {...props}>
        <path
            d="M16.978 8L19.288 9.06C21.095 9.892 22 10.308 22 11C22 11.692 21.096 12.109 19.287 12.94L14.394 15.187C13.214 15.729 12.624 16 12 16C11.376 16 10.786 15.729 9.606 15.187L4.713 12.939C2.904 12.11 2 11.693 2 11C2 10.307 2.904 9.891 4.713 9.06L7.022 8M12 2.5V10M15 5C14.41 4.393 12.84 2 12 2C11.16 2 9.59 4.393 9 5"
            {...strokeProps}
        />
        <path
            d="M20.233 15.5C21.41 16.062 22 16.44 22 17C22 17.693 21.096 18.109 19.287 18.94L14.394 21.187C13.214 21.729 12.624 22 12 22C11.376 22 10.786 21.73 9.606 21.187L4.713 18.94C2.904 18.11 2 17.694 2 17C2 16.44 2.59 16.062 3.767 15.5"
            {...strokeProps}
        />
    </SvgIcon>
);

export const SendBackwardIcon: React.FunctionComponent<SvgIconProps> = (
    props,
) => (
    <SvgIcon viewBox="0 0 24 24" {...props}>
        <path
            d="M15.89 12.5L19.287 10.94C21.096 10.108 22 9.692 22 9C22 8.308 21.096 7.891 19.287 7.06L14.394 4.813C13.214 4.271 12.624 4 12 4C11.376 4 10.786 4.271 9.606 4.813L4.713 7.061C2.904 7.89 2 8.307 2 9C2 9.693 2.904 10.109 4.713 10.94L8.11 12.5M12 19.5V9M15 17C14.41 17.607 12.84 20 12 20C11.16 20 9.59 17.607 9 17"
            {...strokeProps}
        />
    </SvgIcon>
);

export const SendToBackIcon: React.FunctionComponent<SvgIconProps> = (
    props,
) => (
    <SvgIcon viewBox="0 0 24 24" {...props}>
        <path
            d="M12 21.5V7M15 19C14.41 19.607 12.84 22 12 22C11.16 22 9.59 19.607 9 19M20.233 11.5C21.41 12.062 22 12.44 22 13C22 13.693 21.096 14.109 19.287 14.94L15.89 16.5M3.767 11.5C2.59 12.062 2 12.44 2 13C2 13.693 2.904 14.109 4.713 14.94L8.11 16.5M8.11 10.5L4.713 8.94C2.904 8.108 2 7.692 2 7C2 6.308 2.904 5.891 4.713 5.06L9.606 2.813C10.786 2.271 11.376 2 12 2C12.624 2 13.214 2.271 14.394 2.813L19.287 5.061C21.096 5.89 22 6.307 22 7C22 7.693 21.096 8.109 19.287 8.94L15.89 10.5"
            {...strokeProps}
        />
    </SvgIcon>
);
