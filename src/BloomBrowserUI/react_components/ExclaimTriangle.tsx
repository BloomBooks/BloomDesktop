import * as React from "react";

interface IProps {
    triangleColor: string;
    exclaimColor: string;
}

export const ExclaimTriangle: React.FunctionComponent<IProps> = props => {
    return (
        <svg
            xmlns="http://www.w3.org/2000/svg"
            height="550"
            width="628"
            viewBox="0 0 627.8 550.5"
        >
            <path
                style={{
                    fill: props.triangleColor,
                    fillOpacity: 1,
                    stroke: props.triangleColor,
                    strokeWidth: 134.496,
                    strokeLinecap: "round",
                    strokeLinejoin: "round",
                    strokeMiterlimit: 4,
                    strokeDasharray: "none",
                    strokeOpacity: 1
                }}
                d="m 314.7913,33.70932 148.12211,256.55505 148.12213,256.55505 -296.24425,-1e-5 -296.244254,-10e-6 148.122124,-256.55503 z"
                transform="matrix(0.8402744,0,0,0.82490495,53.231396,34.635543)"
                id="path857"
            />
            <path
                fill={props.exclaimColor}
                d="m291.9 343.4c1.2 11.5 3.2 20 6 25.7 2.8 5.6 7.8 8.4 15 8.4h2c7.2 0 12.2-2.8 15-8.4 2.8-5.6 4.8-14.2 6-25.7l6.4-88.7c1.2-17.3 1.8-29.7 1.8-37.2 0-10.2-2.9-18.2-8.7-24-5.5-5.5-13.4-8.6-21.6-8.6s-16 3.1-21.6 8.6c-5.8 5.7-8.7 13.7-8.7 24 0 7.5 0.6 20 1.8 37.3l6.4 88.8z"
            />
            <circle fill={props.exclaimColor} cy="430.8" cx="313.9" r="30.7" />
        </svg>
    );
};
