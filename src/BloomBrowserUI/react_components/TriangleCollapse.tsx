import { css } from "@emotion/react";
import { Button, Collapse } from "@mui/material";
import React = require("react");
import { Span } from "./l10nComponents";

// Shows a triangle that can be clicked to expand a section
// Currently hardwired to "Advanced" because that's all we envision using it for
export const TriangleCollapse: React.FC<{
    initiallyOpen: boolean;
    children: React.ReactNode;
}> = props => {
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
        >
            <Button
                onClick={handleClick}
                css={css`
                    justify-content: start;
                    text-transform: none;
                    padding-left: 0;
                `}
            >
                <svg
                    width="10"
                    height="10"
                    css={css`
                        transition: transform 0.2s ease-in-out;
                        transform: ${open ? "rotate(135deg)" : "rotate(90deg)"};
                    `}
                >
                    <path d="M 0 10 L 5 0 L 10 10 Z" fill="white" />
                </svg>
                <Span
                    css={css`
                        margin-left: 5px;
                    `}
                    l10nKey="Common.Advanced"
                ></Span>
            </Button>
            <Collapse
                in={open}
                css={css`
                    .MuiCollapse-wrapperInner {
                        display: flex;
                        flex-direction: column;
                        gap: 5px;
                    }
                `}
            >
                {props.children}
            </Collapse>
        </div>
    );
};
