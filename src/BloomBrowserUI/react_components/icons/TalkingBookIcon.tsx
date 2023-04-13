import React = require("react");

// The same SVG as the content of the icon file we use in Blorg; but because this is not a
// Create React App there is apparently no easy way to adapt an SVG into a color-controllable
// component.
// Different from the toolbox Talking Book icon
export const TalkingBookIcon: React.FunctionComponent<{
    className?: string;
    color?: string;
}> = props => {
    return (
        <svg
            className={props.className}
            // width="19"
            // height="18"
            viewBox="0 0 19 18"
            fill={props.color}
            xmlns="http://www.w3.org/2000/svg"
        >
            <path d="M0.847656 5.83079V11.8308H4.84766L9.84766 16.8308V0.830791L4.84766 5.83079H0.847656ZM14.3477 8.83079C14.3477 7.06079 13.3277 5.54079 11.8477 4.80079V12.8508C13.3277 12.1208 14.3477 10.6008 14.3477 8.83079ZM11.8477 0.060791V2.12079C14.7377 2.98079 16.8477 5.66079 16.8477 8.83079C16.8477 12.0008 14.7377 14.6808 11.8477 15.5408V17.6008C15.8577 16.6908 18.8477 13.1108 18.8477 8.83079C18.8477 4.55079 15.8577 0.970791 11.8477 0.060791Z" />
        </svg>
    );
};
