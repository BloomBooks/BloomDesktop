/** @jsxImportSource @emotion/react */
import { css } from "@emotion/react";
import * as React from "react";
import BloomButton from "../../bloomButton";
import { BloomTooltip } from "../../BloomToolTip";
import { HelpOutline } from "@mui/icons-material";

interface HelpMenuProps {
    onOpen: () => void;
}

export const HelpMenu: React.FunctionComponent<HelpMenuProps> = (props) => {
    return (
        <>
            <BloomTooltip tip={{ l10nKey: "HelpMenu.Help Menu" }}>
                <BloomButton
                    l10nKey="HelpMenu.HelpButton"
                    enabled={true}
                    transparent={true}
                    onClick={props.onOpen}
                    hasText={false}
                    css={css`
                        background-color: transparent;
                        color: inherit;
                        border: hidden;
                        min-width: 36px;
                        padding: 6px;
                    `}
                >
                    <HelpOutline />
                </BloomButton>
            </BloomTooltip>
        </>
    );
};
