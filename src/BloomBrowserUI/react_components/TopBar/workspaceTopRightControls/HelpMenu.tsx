/** @jsxImportSource @emotion/react */
import { css } from "@emotion/react";
import * as React from "react";
import BloomButton from "../../bloomButton";
import { BloomTooltip } from "../../BloomToolTip";
import { HelpOutline } from "@mui/icons-material";
import { ListItemText, Menu, MenuItem } from "@mui/material";

interface HelpMenuItemModel {
    id: string;
    text: string;
    isSeparator: boolean;
    enabled: boolean;
}

interface HelpMenuProps {
    helpItems: HelpMenuItemModel[];
    anchorRef: React.RefObject<HTMLDivElement | null>;
    helpAnchor: HTMLElement | null;
    onOpen: (target?: HTMLElement | null) => void;
    onClose: () => void;
    onRunCommand: (id: string) => void;
}

export const HelpMenu: React.FunctionComponent<HelpMenuProps> = (props) => {
    return (
        <>
            <BloomTooltip tip={{ l10nKey: "HelpMenu.Help Menu" }}>
                <BloomButton
                    l10nKey="HelpMenu.HelpButton"
                    enabled={true}
                    transparent={true}
                    onClick={() => props.onOpen(props.anchorRef.current)}
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

            <Menu
                anchorEl={props.helpAnchor}
                open={Boolean(props.helpAnchor)}
                onClose={props.onClose}
            >
                {props.helpItems.map((item, index) => {
                    if (item.isSeparator) {
                        return (
                            <MenuItem key={`sep-${index}`} divider disabled />
                        );
                    }
                    return (
                        <MenuItem
                            key={item.id}
                            onClick={() => props.onRunCommand(item.id)}
                            disabled={!item.enabled}
                        >
                            <ListItemText primary={item.text} />
                        </MenuItem>
                    );
                })}
            </Menu>
        </>
    );
};
