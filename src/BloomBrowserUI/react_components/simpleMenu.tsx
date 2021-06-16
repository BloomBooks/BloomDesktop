/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import Button from "@material-ui/core/Button";
import Menu from "@material-ui/core/Menu";
import MenuItem from "@material-ui/core/MenuItem";
import { useL10n } from "./l10nHooks";

export class SimpleMenuItem {
    text: string;
    l10nKey: string;
    temporarilyDisableI18nWarning?: boolean;
    action: () => void; // in addition to closing the menu
}

// This class defines a simple menu. Currently it's optimized to be the pull-down from the "..." button
// in the TeamCollectionBookStatusPanel. The idea was to have something pretty general for a localizable
// button that brings up a menu with localizable items. It handles showing the menu and hiding it again;
// you just provide the various bits of text, their keys, and the action to perform when each item
// is chosen. It would be more flexible to let the items be children...but then you're almost back to the
// Menu class that this is built around. The idea was to make something simpler.
// The main non-general bit is placing the up-arrow on the top of the menu. For a general solution, we
// probably want props to control where it pops up relative to the button, and to arrange for the arrow
// to point the right way. Leaving this as YAGNI for now.
// Also, it's currently hard-coded to be white text (for a dark background).
// As needed we could also add l10nParam and comment fields and props.
export const SimpleMenu: React.FunctionComponent<{
    text: string;
    l10nKey: string;
    temporarilyDisableI18nWarning?: boolean;
    items: SimpleMenuItem[];
}> = props => {
    // When this is not null, the menu appears, positioned relative to the anchorEl (which here is always the
    // main button).
    const [anchorEl, setAnchorEl] = React.useState(null);

    const handleClick = event => {
        setAnchorEl(event.currentTarget);
    };

    const handleClose = () => {
        setAnchorEl(null);
    };

    const buttonText = useL10n(
        props.text,
        props.l10nKey,
        undefined,
        undefined,
        undefined,
        props.temporarilyDisableI18nWarning
    );

    const items = props.items.map(item => {
        const itemText = useL10n(
            item.text,
            item.l10nKey,
            undefined,
            undefined,
            undefined,
            item.temporarilyDisableI18nWarning ||
                props.temporarilyDisableI18nWarning
        );
        return (
            <MenuItem
                key={item.text}
                onClick={() => {
                    handleClose();
                    item.action();
                }}
            >
                {itemText}
            </MenuItem>
        );
    });

    return (
        <div
            css={css`
                .MuiButton-label {
                    color: white;
                    font-size: 20pt;
                    height: 10px;
                }
            `}
        >
            <Button
                aria-controls="simple-menu"
                aria-haspopup="true"
                onClick={handleClick}
            >
                {buttonText}
            </Button>
            <Menu
                css={css`
                    .MuiPaper-root {
                        margin-top: 45px; // the origins below SHOULD position it below the button, but don't. This corrects.
                        margin-left: -20px;
                        overflow: visible; // allows the :after to produce the up arrow effect
                        :after {
                            // A trick for making the up-arrow pointing to the button.
                            content: "";
                            width: 0;
                            height: 0;
                            position: absolute;
                            top: -8px;
                            right: 4px; // aligns it with the button
                            border-left: 8px solid transparent;
                            border-right: 8px solid transparent;
                            border-bottom: 8px solid white;
                        }
                    }
                `}
                anchorEl={anchorEl}
                keepMounted
                open={Boolean(anchorEl)}
                onClose={handleClose}
                anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
                transformOrigin={{ vertical: "top", horizontal: "right" }}
            >
                {items}
            </Menu>
        </div>
    );
};
