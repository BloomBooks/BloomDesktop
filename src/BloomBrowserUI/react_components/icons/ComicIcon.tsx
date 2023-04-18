import React = require("react");

// The same SVG as the content of the icon file we use in Blorg; but because this is not a
// Create React App there is apparently no easy way to adapt an SVG into a color-controllable
// component.
// Differernt from the Toolbox Overlay icon.
export const ComicIcon: React.FunctionComponent<{
    className?: string;
    color: string;
}> = props => {
    return (
        <svg
            className={props.className}
            fill={props.color}
            // width="16"
            // height="13"
            viewBox="0 0 16 13"
            xmlns="http://www.w3.org/2000/svg"
        >
            <path d="M7.73323 12.0258L6.20999 9.07883L3.0655 10.6755L4.24901 8.28283L0.0741272 8.94245L3.368 6.4883L0.326077 4.88834L3.77957 4.81031L2.44424 1.60357L5.87449 3.28545L6.41285 3.05176e-05L8.03313 2.98084L9.92389 0.897621L10.3161 3.77683L14.7415 2.38429L11.5017 5.68629L15.6609 7.25682L11.4944 7.61508L13.5615 10.6687L9.07035 8.84801L7.73323 12.0258ZM6.50892 8.15181L7.66902 10.3963L8.69779 7.95123L11.7155 9.17457L10.262 7.02731L12.571 6.82884L10.2664 5.95855L12.3124 3.87322L9.742 4.68223L9.43933 2.46003L7.89041 4.1663L6.76851 2.10226L6.40586 4.31601L3.7877 3.0322L4.80639 5.47855L3.01007 5.51919L4.66074 6.38735L2.73439 7.82233L5.46081 7.39158L4.60727 9.11751L6.50892 8.15181Z" />
        </svg>
    );
};
