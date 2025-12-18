import { css } from "@emotion/react";
import * as React from "react";
import BloomButton from "../../bloomButton";

interface TopRightMenuButtonProps {
    text: string;
    onClick: () => void;
    startIcon?: React.ReactNode;
    endIcon?: React.ReactNode;
    hasText?: boolean;
}

export const topRightMenuArrowCss = css`
    font-size: 14px !important;
`;

export const TopRightMenuButton: React.FunctionComponent<
    TopRightMenuButtonProps
> = (props) => {
    return (
        <BloomButton
            l10nKey=""
            alreadyLocalized={true}
            enabled={true}
            onClick={props.onClick}
            startIcon={props.startIcon}
            endIcon={props.endIcon}
            hasText={props.hasText === undefined ? true : props.hasText}
            variant="text"
            css={css`
                font-size: 12px;
                padding-top: 0px;
                padding-bottom: 0px;
                text-transform: none;
                display: inline-flex;
                align-items: center;
                justify-content: end;
                width: 100%;
            `}
        >
            {props.text}
        </BloomButton>
    );
};
