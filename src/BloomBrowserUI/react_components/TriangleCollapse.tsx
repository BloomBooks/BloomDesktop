import { css } from "@emotion/react";
import { Button, Collapse } from "@mui/material";
import * as React from "react";
import { Span } from "./l10nComponents";
import { InfoIconHelp } from "./icons/InfoIconHelp";

// Shows a triangle that can be clicked to expand a section
// Defaults to "Advanced" as the label.
export const TriangleCollapse: React.FC<{
    initiallyOpen: boolean;
    labelL10nKey?: string;
    indented?: boolean;
    children: React.ReactNode;
    className?: string;
    buttonColor?: string;
    extraButtonCss?: string;
    helpId?: string;
}> = (props) => {
    const [open, setOpen] = React.useState(props.initiallyOpen);

    const handleClick = () => {
        setOpen(!open);
    };

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                gap: 5px;
            `}
            className={props.className}
        >
            <div css="display: flex; ">
                <Button
                    onClick={handleClick}
                    css={css`
                        justify-content: start;
                        text-transform: none;
                        padding-left: 0;
                        padding-right: 0;
                        ${props.indented ? "padding-bottom: 0;" : ""}
                        ${props.extraButtonCss ?? ""}
                        & .MuiTouchRipple-root {
                            display: none;
                        }
                        min-width: auto;
                        width: auto;
                    `}
                >
                    <svg
                        width="10"
                        height="10"
                        css={css`
                            transition: transform 0.2s ease-in-out;
                            transform: ${open
                                ? "rotate(135deg)"
                                : "rotate(90deg)"};
                        `}
                    >
                        <path
                            d="M 0 10 L 5 0 L 10 10 Z"
                            fill={props.buttonColor ?? "white"}
                        />
                    </svg>
                    <Span
                        css={css`
                            margin-left: 5px;
                            // maybe the alternative should be white; but before I added
                            // the buttonColor prop, it was unspecified, so I'll leave it that way for now
                            ${props.buttonColor
                                ? "color:" + props.buttonColor + ";"
                                : ""}
                        `}
                        l10nKey={props.labelL10nKey ?? "Common.Advanced"}
                    ></Span>
                </Button>
                {props.helpId && (
                    <InfoIconHelp
                        helpId={props.helpId}
                        css={css`
                            margin-left: 5px;
                        `}
                    />
                )}
            </div>
            <Collapse
                in={open}
                css={css`
                    .MuiCollapse-wrapperInner {
                        display: flex;
                        flex-direction: column;
                        gap: ${props.indented ? "0" : "5px"};
                        ${props.indented ? "padding-left: 14px;" : ""}
                    }
                `}
            >
                {props.children}
            </Collapse>
        </div>
    );
};
