/** @jsxImportSource @emotion/react */
import { css } from "@emotion/react";
import * as React from "react";
import BloomButton from "../../bloomButton";
import { BloomTooltip } from "../../BloomToolTip";
import { ArrowDropDown } from "@mui/icons-material";
import { ListItemText, Menu, MenuItem } from "@mui/material";

interface LanguageMenuItem {
    langTag: string;
    menuText: string;
    tooltip: string;
    isCurrent: boolean;
}

interface LanguageMenuState {
    uiLanguageLabel: string;
    showUnapprovedText: string;
    showUnapprovedChecked: boolean;
}

interface LanguageMenuProps {
    state: LanguageMenuState;
    languages: LanguageMenuItem[];
    anchorRef: React.RefObject<HTMLDivElement | null>;
    languageAnchor: HTMLElement | null;
    onOpen: (target?: HTMLElement | null) => void;
    onClose: () => void;
    onApplyLanguage: (langTag: string) => void;
    onToggleShowUnapproved: () => void;
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
                    onClick={() => props.onOpen(props.anchorRef.current)}
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

            <Menu
                anchorEl={props.languageAnchor}
                open={Boolean(props.languageAnchor)}
                onClose={props.onClose}
            >
                {props.languages.map((lang) => (
                    <MenuItem
                        key={lang.langTag}
                        selected={lang.isCurrent}
                        onClick={() => props.onApplyLanguage(lang.langTag)}
                    >
                        <ListItemText primary={lang.menuText} />
                    </MenuItem>
                ))}
                <MenuItem onClick={props.onToggleShowUnapproved}>
                    <ListItemText
                        primary={props.state.showUnapprovedText}
                        sx={{
                            fontStyle: props.state.showUnapprovedChecked
                                ? "italic"
                                : "normal",
                        }}
                    />
                </MenuItem>
            </Menu>
        </>
    );
};
