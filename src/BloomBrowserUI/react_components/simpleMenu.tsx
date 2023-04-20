/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import Button from "@mui/material/Button";
import Menu from "@mui/material/Menu";
import MenuItem from "@mui/material/MenuItem";
import { useL10n } from "./l10nHooks";
import { Divider, ListItemIcon, ListItemText } from "@mui/material";
import { makeStyles } from "@mui/styles";

export class SimpleMenuItem {
    text: string;
    l10nKey: string;
    temporarilyDisableI18nWarning?: boolean;
    action: () => void; // in addition to closing the menu
    disabled?: boolean;
    icon?: React.ReactNode;
}

const useButtonStyles = makeStyles({
    label: {
        color: "white",
        fontSize: "20pt",
        height: "10px"
    }
});

// A more commented version of this, in Emotion, is below, with explanation of why
// we can't currently use Emotion here.
const useMenuStyles = makeStyles({
    paper: {
        marginTop: "10px",
        marginLeft: "-20px",
        overflow: "visible",
        "&:after": {
            content: "''",
            width: "0",
            height: "0",
            position: "absolute",
            top: "-8px",
            right: "4px",
            borderLeft: "8px solid transparent",
            borderRight: "8px solid transparent",
            borderBottom: "8px solid white"
        }
    }
});

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
    items: (SimpleMenuItem | "-")[];
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

    const items = props.items.map((item, index) =>
        item === "-" ? (
            <Divider key={index} />
        ) : (
            <SimpleMenuRow
                key={index}
                handleClose={handleClose}
                item={item}
                temporarilyDisableI18nWarning={
                    props.temporarilyDisableI18nWarning
                }
            />
        )
    );
    const buttonClasses = useButtonStyles();
    const menuClasses = useMenuStyles();

    return (
        <div
        // How we'e like to do this. See comment below.
        // css={css`
        //     .MuiButton-label {
        //         color: white;
        //         font-size: 20pt;
        //         height: 10px;
        //     }
        // `}
        >
            <Button
                classes={{ root: buttonClasses.label }}
                aria-controls="simple-menu"
                aria-haspopup="true"
                onClick={handleClick}
            >
                {buttonText}
            </Button>
            <Menu
                // We'd prefer to style things with emotion, like this.
                // But somewhere around when we merged the react collection tab (July 2021),
                // Material-UI started generating unpredictble class names. So for example
                // instead of the popup having class MuiPaper-root it has ones like
                // MuiPaper-root-23 (where the 23 is unpredictable). This might relate
                // to using more than one theme (as we do to make the checkin button
                // orange, for example). Or some build setting may have changed. For now,
                // I'm having to do it with the react style system.
                // Version 5 of Material-UI allows more colors and may remove the
                // need for multiple themes, which might let us go back to Emotion...
                // or something else may help, as V5 is supposed to be more friendly to Emotion.
                // css={css`
                //     .MuiPaper-root {
                //         margin-top: 10px; // the origins below SHOULD position it below the button, but don't. This corrects.
                //         margin-left: -20px;
                //         overflow: visible; // allows the :after to produce the up arrow effect
                //         :after {
                //             // A trick for making the up-arrow pointing to the button.
                //             content: "";
                //             width: 0;
                //             height: 0;
                //             position: absolute;
                //             top: -8px;
                //             right: 4px; // aligns it with the button
                //             border-left: 8px solid transparent;
                //             border-right: 8px solid transparent;
                //             border-bottom: 8px solid white;
                //         }
                //     }
                // `}
                classes={{ paper: menuClasses.paper }}
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

const SimpleMenuRow: React.FunctionComponent<{
    handleClose: () => void;
    item: SimpleMenuItem;
    temporarilyDisableI18nWarning?: boolean;
}> = props => {
    const itemText = useL10n(
        props.item.text,
        props.item.l10nKey,
        undefined,
        undefined,
        undefined,
        props.item.temporarilyDisableI18nWarning ||
            props.temporarilyDisableI18nWarning
    );
    return (
        <MenuItem
            key={props.item.text}
            onClick={() => {
                props.handleClose();
                props.item.action();
            }}
            disabled={props.item.disabled}
        >
            {props.item.icon ? (
                <React.Fragment>
                    <ListItemIcon
                        css={css`
                            min-width: 30px !important; // overrides MUI default that leaves way too much space
                        `}
                    >
                        {props.item.icon}
                    </ListItemIcon>
                    <ListItemText>{itemText}</ListItemText>
                </React.Fragment>
            ) : (
                itemText
            )}
        </MenuItem>
    );
};
