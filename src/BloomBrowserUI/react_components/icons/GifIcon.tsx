import React = require("react");

interface ImagePlaceholderIconProps extends React.SVGProps<SVGSVGElement> {
    color?: string;
    strokeColor?: string;
}

// An icon that looks like our image placeholder.
export const GifIcon: React.FunctionComponent<ImagePlaceholderIconProps> = props => {
    const { color, strokeColor, ...rest } = props;
    return (
        <svg
            width="121.94573mm"
            height="116.45483mm"
            viewBox="0 0 121.94573 116.45483"
            version="1.1"
            id="svg1"
            xmlns="http://www.w3.org/2000/svg"
            {...rest}
        >
            <g id="layer1" transform="translate(-1.38124,-2.1065131)">
                <ellipse
                    fill={color ?? "transparent"}
                    stroke={strokeColor ?? "black"}
                    strokeWidth={2.71467}
                    cx="62.354107"
                    cy="60.333931"
                    rx="59.615532"
                    ry="56.870083"
                />
                <text
                    fontSize={50.8}
                    fontFamily="Segoe UI"
                    fill={strokeColor ?? "black"}
                    //style={fontSize:50.8px,font-family:'Segoe UI';-inkscape-font-specification:'Segoe UI';fill:#000000;fill-opacity:0.00414937;stroke:#000000;stroke-width:0;stroke-dasharray:none;stroke-opacity:1}
                    x="25.453087"
                    y="75.806305"
                >
                    <tspan x="25.453087" y="75.806305">
                        GIF
                    </tspan>
                </text>
            </g>
        </svg>
    );
};
