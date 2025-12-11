/** @jsxImportSource @emotion/react */
import { css } from "@emotion/react";
import * as React from "react";
import BloomButton from "../../bloomButton";
import { BloomTooltip } from "../../BloomToolTip";
import { ArrowDropDown } from "@mui/icons-material";

interface LanguageMenuState {
    uiLanguageLabel: string;
}

interface LanguageMenuProps {
    state: LanguageMenuState;
    onOpen: () => void;
}

export const UiLanguageMenu: React.FunctionComponent<LanguageMenuProps> = (
    props,
) => {
    return (
        <>
            <BloomTooltip
                tip={{ l10nKey: "CollectionTab.LanguageMenu.Tooltip" }}
            >
                <BloomButton
                    l10nKey="CollectionTab.LanguageMenu"
                    enabled={true}
                    hasText={true}
                    transparent={true}
                    onClick={props.onOpen}
                    css={css`
                        background-color: transparent;
                        color: inherit;
                        padding-inline: 8px;
                        text-transform: none;
                        border: hidden;
                        font-size: 12px;
                    `}
                >
                    <span
                        css={css`
                            display: inline-flex;
                            align-items: center;
                            gap: 6px;
                        `}
                    >
                        <span>{props.state.uiLanguageLabel}</span>
                        <ArrowDropDown />
                    </span>
                </BloomButton>
            </BloomTooltip>
        </>
    );
};
