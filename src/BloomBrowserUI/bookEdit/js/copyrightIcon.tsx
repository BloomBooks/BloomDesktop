import * as React from "react";

const CopyrightIcon: React.FunctionComponent<{
    color: string;
}> = props => {
    return (
        <svg
            xmlns="http://www.w3.org/2000/svg"
            // width="65.551"
            // height="66.108"
            version="1.1"
            viewBox="0 0 65.551 66.108"
        >
            <g transform="translate(1.436 2.252)">
                <path
                    fill="none"
                    stroke={props.color}
                    strokeDasharray="none"
                    strokeMiterlimit="4"
                    strokeOpacity="1"
                    strokeWidth="59.969"
                    d="M482.99 257.99c0 115.519-93.645 209.165-209.163 209.165-115.518 0-209.164-93.646-209.164-209.164 0-115.518 93.646-209.164 209.164-209.164 115.518 0 209.163 93.646 209.163 209.164z"
                    transform="matrix(.11615 0 0 .11731 -.464 .537)"
                ></path>
                <g
                    transform="matrix(.99163 0 0 1.00844 -18.545 -1.455)"
                    style={{
                        lineHeight: "125%"
                    }}
                    fill={props.color}
                    fillOpacity="1"
                    stroke="none"
                    fontFamily="Sneakerhead BTN"
                    fontSize="12.623"
                    fontStretch="normal"
                    fontStyle="normal"
                    fontVariant="normal"
                    fontWeight="bold"
                    letterSpacing="0"
                    wordSpacing="0"
                >
                    <path
                        d="M62.88 42.945c0 .236-.155.547-.465.931-2.722 3.24-6.339 4.86-10.85 4.86-4.616 0-8.358-1.576-11.228-4.726-2.795-3.048-4.193-6.894-4.193-11.538 0-4.497 1.368-8.313 4.105-11.45 2.855-3.283 6.486-4.925 10.894-4.925 4.793 0 8.55 1.472 11.272 4.415.295.326.443.614.443.866 0 .28-.514 1.324-1.542 3.128-1.028 1.805-1.675 2.818-1.941 3.04a.602.602 0 01-.444.177c-.074 0-.444-.295-1.11-.887-.798-.71-1.582-1.265-2.351-1.664a7.96 7.96 0 00-3.64-.888c-2.174 0-3.912.814-5.213 2.441-1.199 1.509-1.798 3.373-1.798 5.591 0 2.249.6 4.135 1.798 5.658 1.301 1.657 3.04 2.485 5.214 2.485a8.253 8.253 0 003.66-.843c.755-.37 1.524-.887 2.308-1.553.651-.562 1.014-.843 1.088-.843.148 0 .296.081.443.244.237.266.88 1.19 1.93 2.774 1.08 1.642 1.62 2.544 1.62 2.707z"
                        style={{
                            textAlign: "start",
                            lineHeight: "125%"
                        }}
                        fill={props.color}
                        fontFamily="Berlin Sans FB Demi"
                        fontSize="45.441"
                        textAnchor="start"
                        writingMode="lr-tb"
                    ></path>
                </g>
                <text
                    xmlSpace="preserve"
                    style={{ lineHeight: "125%" }}
                    x="30.545"
                    y="-57.818"
                    fill={props.color}
                    fillOpacity="1"
                    stroke="none"
                    fontFamily="Sans"
                    fontSize="40"
                    fontStyle="normal"
                    fontWeight="normal"
                    letterSpacing="0"
                    wordSpacing="0"
                ></text>
            </g>
        </svg>
    );
};

export default CopyrightIcon;
