import * as React from "react";
import Button from "@mui/material/Button";
import ButtonGroup from "@mui/material/ButtonGroup";
import ArrowDropDownIcon from "@mui/icons-material/ArrowDropDown";
import ClickAwayListener from "@mui/material/ClickAwayListener";
import Grow from "@mui/material/Grow";
import Paper from "@mui/material/Paper";
import Popper from "@mui/material/Popper";
import MenuList from "@mui/material/MenuList";
import {
    ILocalizableMenuItemProps,
    LocalizableMenuItem
} from "./localizableMenuItem";

// Creates a button which has a drop-down menu of options.
// Copied and modified from https://mui.com/components/buttons/#split-button.
// Notably, we are using our LocalizableMenuItem instead of MenuItem, to make use of enterprise badges.
export const BloomSplitButton: React.FunctionComponent<{
    options: ILocalizableMenuItemProps[];
    disabled?: boolean;
    hideArrow?: boolean;
}> = props => {
    const [open, setOpen] = React.useState(false);
    const anchorRef = React.useRef<HTMLDivElement>(null);
    const [selectedIndex, setSelectedIndex] = React.useState(0);

    const handleButtonClick = event => {
        props.options[selectedIndex].onClick(event);
    };

    const handleMenuItemClick = (_event, index: number) => {
        setSelectedIndex(index);
        setOpen(false);
    };

    const handleToggle = () => {
        setOpen(prevOpen => !prevOpen);
    };

    const handleClose = (event: Event) => {
        if (
            anchorRef.current &&
            anchorRef.current.contains(event.target as HTMLElement)
        ) {
            return;
        }

        setOpen(false);
    };

    return (
        <React.Fragment>
            <ButtonGroup
                disabled={props.disabled}
                variant="contained"
                ref={anchorRef}
                aria-label="split button"
            >
                <Button onClick={handleButtonClick}>
                    {props.options[selectedIndex].english}
                </Button>
                {!props.hideArrow && (
                    <Button
                        size="small"
                        aria-controls={open ? "split-button-menu" : undefined}
                        aria-expanded={open ? "true" : undefined}
                        aria-label="select upload source"
                        aria-haspopup="menu"
                        onClick={handleToggle}
                    >
                        <ArrowDropDownIcon />
                    </Button>
                )}
            </ButtonGroup>
            <Popper
                sx={{
                    zIndex: 1
                }}
                open={open}
                anchorEl={anchorRef.current}
                role={undefined}
                transition
                disablePortal
            >
                {({ TransitionProps, placement }) => (
                    <Grow
                        {...TransitionProps}
                        style={{
                            transformOrigin:
                                placement === "bottom"
                                    ? "center top"
                                    : "center bottom"
                        }}
                    >
                        <Paper>
                            <ClickAwayListener onClickAway={handleClose}>
                                <MenuList id="split-button-menu" autoFocusItem>
                                    {props.options.map((option, index) => (
                                        <LocalizableMenuItem
                                            key={option.english}
                                            dontGiveAffordanceForCheckbox={true}
                                            {...option}
                                            // Override option's onClick here.
                                            // The real one will be called in handleButtonClick.
                                            onClick={event =>
                                                handleMenuItemClick(
                                                    event,
                                                    index
                                                )
                                            }
                                        ></LocalizableMenuItem>
                                    ))}
                                </MenuList>
                            </ClickAwayListener>
                        </Paper>
                    </Grow>
                )}
            </Popper>
        </React.Fragment>
    );
};
